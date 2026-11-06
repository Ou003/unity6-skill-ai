# 架构说明

## 一、组件与依赖

```text
SkillChatWindow (EditorWindow, IMGUI)
        |
        v
AgentController  --------------------->  SessionState (聊天记录持久化)
        |  \_________________
        |                   \
        v                    v
SkillRouter / PromptBuilder   LlmClient (UnityWebRequest + EditorApplication.update)
        |                            |
        v                            v
SkillLibrary (知识卡 JSON)       OpenAI 兼容 / Anthropic / Gemini
        |                            
        v
ToolRegistry.SpecsFor(白名单)
        |
        v
UnityReadTools(9) + UnityWriteTools(8)
        |
        v
Unity Editor API / Scene / AssetDatabase / Console / Undo
```

全部代码在 `Unity6SkillAI.Editor.asmdef`，`includePlatforms: ["Editor"]`，不会打进运行时包体。没有 MCP 进程、没有外部服务、没有 NuGet/UPM 第三方依赖。

## 二、关键设计决策

| 决策 | 原因 |
|---|---|
| 自带 `MiniJson` 而不是 Newtonsoft | Unity 的 `JsonUtility` 只能映射 `[Serializable]` 类，无法解析 LLM 返回的动态 `tool_calls` / content blocks；引入 DLL 会破坏「拖个文件夹就能用」 |
| `UnityWebRequest` + `EditorApplication.update` 轮询 | 编辑器不在 Play 模式，协程不可用；`async/Task` 在域重载时状态易丢；主线程轮询最稳，且能统一处理中转站返回的 HTML 错误页 |
| 知识卡用磁盘 JSON 而非 TextAsset | UPM immutable 包缺 `.meta` 会静默丢资源；用 `PackageInfo.FindForAssetPath` + `AssetDatabase` + 磁盘扫描三重定位，兼容 Git 包 / 本地包 / Assets 三种安装方式 |
| 工具白名单由知识卡决定 | 工具越多选错率与 Token 消耗越高。写工具只有在描述正确 Unity 6 API 的知识卡被命中时才暴露，属于「先给知识再给权限」 |
| 渐进式披露五层 | 常驻 System Prompt -> 路由 -> 按需加载 1~3 张卡 -> 工具白名单 -> 模型 tool calling 决策，避免把 24 张卡全塞进上下文 |
| 每个 AI 动作一个 Undo 分组 | `Undo.SetCurrentGroupName` + `IncrementCurrentGroup`，一次工具链对应一次 Ctrl+Z；文件写入另用 `undo.json` 记录旧内容 |
| 聊天记录写 `SessionState` | 写 C# 触发域重载后静态字段全部重置，`SessionState` 是 Unity 官方支持跨域重载的键值存储 |
| 写脚本后暂停自动循环 | 编译与域重载会销毁进行中的 `UnityWebRequest`，与其做脆弱的自动续接，不如显式让用户点「检查编译结果」把 Console 回灌给模型 |

## 三、一次完整请求

```text
1. SkillChatWindow.Send
2. AgentController.Submit -> SkillRouter.Route(userText)
     - UnityVersionInfo.Numeric 解析 Application.unityVersion
     - 逐卡评分，排除版本不匹配与 alwaysInclude
     - 取 top3 主题卡 + 常驻卡；零命中注入通用工作流卡
     - AllowedTools = BaselineReadTools ∪ Cards.Tools
3. PromptBuilder 输出 System Prompt
     - 工程实测（ProjectInspector.BuildContextText）
     - 通用规则（只调白名单、先读后写、写完查 Console、遵守弃用清单）
     - 每张知识卡的结构化文本
     - 本次工具清单、用户补充规则
4. LlmClient.Send 组装三家协议各自的 messages/tools，主线程轮询
5. 解析出 ToolCall 列表
6. 只读直接执行；写操作挂起 PendingApproval，窗口显示参数与「执行/跳过」
7. ToolResult 以 tool role 回灌 Messages，进入下一轮（上限 maxToolRounds）
8. 最终文本写入聊天区，SaveSession
```

## 四、扩展点

### 新增 Provider

在 `LlmClient.cs` 增加 `ProviderKind` 枚举值，并实现：

```text
BuildRequest       -> url / headers / body
BuildXxxBody       -> 内部 ChatMessage 转成该协议的消息格式
ParseXxxResponse   -> 该协议响应转成 LlmResult.Text + ToolCalls
```

在 `AgentSettings.cs` 的 `ProviderPresets.Items` 里加预设。内部统一格式是 `ChatMessage`（user / assistant / tool）与 `ToolCall(Id, Name, ArgumentsJson)`，新协议只需做双向适配。

### 新增工具

在 `UnityReadTools.Register` 或 `UnityWriteTools.Register` 里调用 `ToolRegistry.Add`：

```csharp
ToolRegistry.Add(list, "my_tool", "给模型看的功能描述",
    ToolRegistry.Schema(MiniJson.Dict("path", ToolRegistry.Prop("string", "说明"))),
    false, /* IsWrite */
    args => ToolResult.Success("结果文本"));
```

参数用 `ToolArgs` 读取；写工具请设置 `IsWrite = true` 以便进入审批门，并用 `Undo` 系列 API 登记。最后把工具名写进相关知识卡的 `tools` 数组，否则模型看不到它。

### 新增知识卡

见 `docs/KNOWLEDGE-CARDS.md`。改完运行 `python tools/gen_meta.py` 与 `python tools/verify_router.py`。

### 新增评测用例

编辑 `evals/prompts.json`，字段 `prompt` / `expectCards` / `expectTools`。`tools/verify_router.py` 是 `SkillRouter.cs` 的离线等价实现，改评分规则时两边必须同步。

## 五、状态与文件位置

| 内容 | 位置 | 是否进版本库 |
|---|---|---|
| 设置 | `<工程>/UserSettings/Unity6SkillAI/settings.json` | 否 |
| API Key | `<工程>/Library/Unity6SkillAI/secrets.json` 或环境变量 | 否 |
| 脚本写入撤销日志 | `<工程>/Library/Unity6SkillAI/undo.json` | 否 |
| 评测报告 | `<工程>/Library/Unity6SkillAI/eval-report.md` | 否 |
| 聊天记录 | `SessionState`（编辑器会话级） | 否 |
| 知识卡 / 工具 / 评测集 | 仓库内 | 是 |

## 六、安全边界

- 文件类工具统一走 `UnityReadTools.ValidateAssetPath`：必须 `Assets/` 开头、禁止 `..`、禁止越出 Assets 目录、扩展名校验。
- 写操作默认人工审批；关闭审批等于把编辑器写权限交给模型，请只在测试工程里这么做。
- 组件类型解析 `ResolveType` 只接受 `Component` 子类，避免模型通过反射式类型名触发任意类型构造。
- 密钥不明文写入日志；HTTP 错误只打印状态码与服务端 message，不打印 Authorization 头。
- 本项目不上传任何数据到自建服务，请求终点完全由用户填写的 Base URL 决定。
