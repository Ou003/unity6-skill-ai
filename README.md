# Unity6 Skill AI

一个可以**直接装进 Unity 6 工程**的 BYOK AI 助手窗口：用户在窗口里填自己主流大模型的 API Key，项目内置 **38 张 Unity 6 版本化知识卡** 与 **35 个 Unity 官方 Skills 原文**，会按自然语言需求命中对应知识，再把「该用哪些 API、哪些工具、有哪些版本坑」注入 System Prompt，让模型真正按 Unity 6 的方式动手创作。

窗口可停靠，也可一键浮动成独立窗口。纯 C#、零外部进程、零第三方 DLL、不需要 Python / Node / MCP 服务。

---

## 一、它解决什么问题

普通的 Unity MCP 桥只提供「工具菜单」，模型靠预训练里那点 Unity 知识自己点菜，容易写出 Unity 2021 时代的 API。本项目多做了三层：

| 层 | 做法 |
|---|---|
| 工程实测信息 | 自动读取 Unity 版本、渲染管线、输入处理模式、已安装包、当前场景，注入 System Prompt，不靠模型猜版本 |
| 版本化知识卡 | 38 张自撰的 Unity 6 知识卡，带适用版本范围、推荐 API、已弃用 API、约束、常见坑、参数模板、参考写法 |
| 官方 Skills 参考层 | 35 个 Unity 官方 SKILL.md 原文随包附带，按中文/英文关键词命中后按需注入，只作知识参考，不参与工具授权 |
| 知识驱动的工具白名单 | 只有命中的知识卡里列出的工具才会暴露给模型。写操作默认不会凭空开放，先给知识再给权限 |

一次请求的完整链路：

```text
用户自然语言
  -> SkillRouter 按关键词 + Unity 小版本打分，选 1 张常驻卡 + 最多 3 张主题卡
  -> OfficialSkillLibrary 按中文/英文关键词选最多 3 个官方技能，按需读原文并截断
  -> PromptBuilder 组装 System Prompt（工程实测 + 通用规则 + 知识卡 + 工具白名单 + 官方参考层）
  -> LlmClient 携带白名单工具调用自备大模型（OpenAI 兼容 / Anthropic / Gemini）
  -> 模型返回 tool_calls
  -> 只读工具自动执行；写工具弹审批
  -> 工具结果回灌模型，最多 N 轮
  -> 脚本写入后读 Console 编译错误并让模型自行修正
```

---

## 二、安装（本地测试用）

要求 Unity 6000.0 或更高（开发与验证环境：Unity 6000.3.26f1）。

**这个仓库根目录本身就是 UPM 包根**（根目录带 `package.json`），所以不需要打包、不需要构建，把文件夹放进你已有的 Unity 6 工程即可。以下三种任选一种：

方式一，直接拖进 Assets（最快，推荐先这样测）：

```text
把 D:\unity6-agent 整个文件夹拖到 <你的Unity6工程>\Assets\ 下面
例如 <工程>\Assets\unity6-agent\
Unity 自动导入 Editor/ 下的脚本，编译完出现菜单 Tools > Unity6 Skill AI
```

方式二，作为 embedded package 放进 Packages（更规范）：

```text
把 D:\unity6-agent 整个文件夹复制到 <工程>\Packages\unity6-agent\
Unity 读取 package.json 后自动注册为本地包，无需改 manifest.json
```

方式三，从磁盘挂载（开发调试最方便，不复制文件）：

```text
Unity > Window > Package Manager > + > Add package from disk
选择 D:\unity6-agent\package.json
改本仓库的代码会直接在你工程里生效，适合边测边改
```

三种方式都会自动定位 `Editor/Skills/Cards/` 下的知识卡与 `Official/` 下的官方技能索引（代码里做了 UPM 包路径、AssetDatabase、磁盘扫描三重回退）。仓库已随附全部 `.meta` 文件，从 Git URL 安装的 immutable 包也不会丢资源。

注意事项：

- 不要保留或删除 `.meta` 文件；从本仓库复制时请整个文件夹一起复制。
- 不要把另一个完整 Unity 工程（含 `Library/`、`ProjectSettings/`）塞进包目录内部，否则 Unity 会把它当包资源扫描并产生程序集冲突。
- 仓库里的 `.git` 是点开头目录，Unity 会忽略，可以直接带过去。包目录里不含任何 Unity 工程、`Library/` 或临时验证产物，整个文件夹约 1 MB（含 35 个官方 SKILL.md 原文）。
- 放好后等 Unity 右下角编译转圈结束，再开菜单。若没出现菜单，看 Console 是否有 `error CS`。

---

## 三、五分钟跑通

### 第一轮验收顺序（前 3 步完全不花 Token）

```text
1. 打开 Unity 6 工程，等编译结束，确认菜单 Tools > Unity6 Skill AI 出现且 Console 无 error CS
2. 点 Tools > Unity6 Skill AI > Chat (Floating)，确认窗口能独立浮动
3. 点窗口「离线路由评测」，Console 应打印 30/30 命中；报告在 Library/Unity6SkillAI/eval-report.md
4. 状态栏应显示「官方技能 35 个」；输入「两个 Trigger 不触发 OnTriggerEnter」，点「预览注入」
   应看到命中官方技能 physics-3d-collision，System Prompt 里有官方参考层与 5 条适配声明
5. 输入「用新输入系统做一个 WASD 移动脚本」，点「预览注入」
   应看到命中 u6-input-system，且工具白名单只开放该卡列出的工具，不是全量 17 个
6. 点「设置」填自备 API Key → 「测试连接」→ 正式发一句自然语言
7. 有问题把 Console 原文或窗口内容发我，我按报错改
```

### 正常对话

1. 菜单 `Tools > Unity6 Skill AI > Chat (Floating)` 打开独立浮动窗口（或 `Chat (Dockable)` 停靠）。
2. 点顶部「设置」，选服务商预设，填入自备 API Key，点「测试连接」。
   - 国内中转站选「自定义 OpenAI 兼容中转」，把中转站的 base URL 填进去即可。
   - Key 存在 `<工程>/Library/Unity6SkillAI/secrets.json`，不进版本库；也可用环境变量 `UNITY6_SKILL_AI_API_KEY` 覆盖。
3. 不想花 Token 也能先验知识库：点「预览注入」，会显示本次命中的知识卡、命中的官方技能、开放的工具和完整 System Prompt；或点「离线路由评测」跑 58 条固定用例。
4. 输入一句自然语言，例如：

```text
用新输入系统做一个 WASD 移动并挂到 Player 上
```

```text
在原点创建一个立方体并加上 Rigidbody
```

```text
FindObjectOfType 在 Unity 6 报错怎么办
```

`Ctrl + Enter` 发送。写操作会弹审批，可点「执行」或「跳过」。

---

## 四、支持的模型接入

| 协议 | 默认 Base URL | 说明 |
|---|---|---|
| OpenAI 兼容 | `https://api.openai.com/v1` | 覆盖 OpenAI、DeepSeek、SiliconFlow、Moonshot 及各类国内中转站，只要支持 `/chat/completions` 与 function calling |
| Anthropic | `https://api.anthropic.com` | 走 `/v1/messages`，`x-api-key` 头 |
| Gemini | `https://generativelanguage.googleapis.com/v1beta` | 走 `generateContent`，`x-goog-api-key` 头 |

模型名、Base URL、温度、最大 Token、最大工具轮次都可在窗口里改。预设里的模型名只是常见默认值，以服务商当前文档为准。

如果中转站不支持 function calling，模型仍能正常对话，但工具调用不可用；错误信息会带上服务端原始响应前 800 字符，方便定位是 HTML 错误页还是协议不兼容。

---

## 五、内置工具

只读（默认始终开放）：

```text
get_project_info      工程实测信息（版本/管线/输入模式/包/场景）
get_scene_hierarchy   场景层级树与组件
get_selection         选中对象与 Transform
get_console_logs      Unity Console 最近日志
get_compile_errors    编译错误与异常
list_scripts          Assets 下 C# 脚本列表
read_script           读取指定脚本
find_assets           按类型或名称搜索资源
get_packages          已安装 UPM 包及版本
```

写操作（只在命中的知识卡列出时开放，且默认需审批）：

```text
create_gameobject     创建物体/几何体/组件/坐标
add_component         添加组件
set_transform         修改 Transform
delete_gameobject     删除物体
create_script         写入 C# 脚本并触发导入
create_material       创建 URP/Lit 或 Standard 材质
save_scene            保存场景
refresh_assets        刷新资源并触发编译
```

所有场景写操作都登记为一个 Unity Undo 分组，一次 AI 动作对应一次 Ctrl+Z。脚本写入无法被 Unity Undo 覆盖，因此单独记了 `Library/Unity6SkillAI/undo.json`，窗口有「撤销脚本写入」。

---

## 六、知识库

知识卡是 `Editor/Skills/Cards/u6skill_*.json`，纯数据，改完点窗口「重载知识卡」即可生效，不用改 C#。

```json
{
  "id": "u6-example",
  "title": "示例知识卡",
  "unity": ["6000.0+"],
  "packages": ["com.unity.inputsystem"],
  "topics": ["输入"],
  "keywords": ["wasd", "输入系统"],
  "tools": ["create_script", "get_compile_errors"],
  "summary": "一句话说明该怎么做",
  "preferred": ["推荐 API"],
  "deprecated": ["禁用或已弃用 API"],
  "constraints": ["必须满足的条件"],
  "pitfalls": ["常见错误"],
  "parameterTemplate": "工具参数示例",
  "snippet": "可编译的 C# 参考写法",
  "examplePrompts": ["示例用户指令"],
  "source": "参考来源",
  "alwaysInclude": false,
  "priority": 10
}
```

`unity` 支持 `any`、`6000.0+`（最低版本）、`6000.3`（精确小版本）、`6000.0-6000.2`（区间）。当前 Unity 小版本由 `Application.unityVersion` 解析，不匹配的卡会被直接排除。

评分规则：keyword 命中 +3，topic +2，package +2，title 词 +1；取分数最高的 3 张主题卡，另有 1 张 `alwaysInclude` 常驻卡；一张都没命中时注入通用工作流卡兜底。

离线评测（不需要 API Key、不消耗 Token）：

```text
命令行：python tools/verify_router.py
Unity 菜单：Tools > Unity6 Skill AI > Run Routing Eval (No API Key)
窗口按钮：离线路由评测
```

评测集在 `evals/prompts.json`，报告写到 `Library/Unity6SkillAI/eval-report.md`。当前基线：**38 张知识卡，58 条用例，知识卡命中率 100%，工具白名单命中率 100%**。

另有独立的官方 Skills 参考层，见 `docs/OFFICIAL-SKILLS.md`：

```text
python tools/verify_official_router.py    离线复刻官方技能路由，10 条中英文用例
```

官方技能带自撰中文关键词、按 tier 分级、默认只注入 1 篇且截断到 3000 字符，**不参与工具白名单**。

新增一张卡的流程：复制一份 `u6skill_*.json` 改内容，运行 `python tools/gen_meta.py` 补 `.meta`，在 `evals/prompts.json` 加一条用例，再跑 `python tools/verify_router.py` 看命中率有没有回退。

---

## 七、目录结构

```text
package.json                     UPM 包描述，仓库根即包根
Editor/
  Unity6SkillAI.Editor.asmdef    Editor-only 程序集
  Core/
    MiniJson.cs                  自带轻量 JSON 解析（不依赖 Newtonsoft）
    AgentSettings.cs             设置与密钥存储、服务商预设
    LlmClient.cs                 三家协议 + UnityWebRequest 主线程轮询
    SkillLibrary.cs              知识卡加载、版本匹配、包路径定位
    OfficialSkillLibrary.cs      官方技能索引、中文关键词路由、正文截断与适配声明
    SkillRouter.cs               路由、评分、System Prompt 组装
    ToolRegistry.cs              工具注册表与白名单
    UnityReadTools.cs            只读工具、Console 读取、UndoLog、工程实测
    UnityWriteTools.cs           写工具、Undo 分组、类型解析
    AgentController.cs           Agent 循环、审批门、会话持久化
    SkillRouterEval.cs           离线评测
  UI/SkillChatWindow.cs          可浮动聊天窗口（IMGUI）
  Skills/Cards/                  38 张 Unity 6 知识卡
Official/                        35 个 Unity 官方 SKILL.md 原文 + 索引 + 许可证与声明
evals/prompts.json               30 条固定评测用例
tools/gen_meta.py                生成 .meta（Git URL 安装必需）
tools/verify_router.py           自研知识卡离线路由评测
tools/verify_official_router.py  官方技能离线路由评测
tools/build_official_skills.py   从上游仓库重建 Official/ 快照
tools/UCL-v1.4.txt               Unity Companion License v1.4 全文
docs/                            架构、知识卡规范、官方技能层说明
```

---

## 八、安全与合规

- API Key 只存在本机 `Library/Unity6SkillAI/secrets.json` 或环境变量，**不会写进 Assets，不会进版本库**。该文件是明文，公共机器请改用环境变量。不要用会提交 `Library/` 的配置。
- 模型请求只发往你在窗口里填的 Base URL，本项目没有自建服务端、没有遥测、没有埋点。
- 写操作默认需人工审批；建议先在空工程或 Git 分支上试用，别直接让 AI 改主工程。
- 38 张知识卡由本项目逐张自撰，`source` 字段目前是来源声明文字，**没有真实 URL，也没有复制 Unity 文档全文**。Unity 文档主体是 CC BY-NC-ND 4.0，代码片段适用 Unity Companion License；贡献知识卡时请自行撰写，不要写爬虫抓取官网全文。
- `Official/` 另附 35 个 Unity 官方 `SKILL.md` 原文，来自 Unity-Technologies/skills，按 **Unity Companion License v1.4** 在 Unity-dependent project 范围内使用与分发，许可证全文、上游版权声明与 NOTICE 随包附带。该许可禁止将内容用于竞争分析或开发竞争性产品/服务；商业化分发或公开服务前请先做法务复核。MIT 不覆盖这部分第三方内容，详见 `docs/OFFICIAL-SKILLS.md`。
- 本项目是社区作品，与 Unity Technologies 无隶属、无背书关系。Unity、Unity 标志及相关商标归 Unity Technologies 或其关联方所有。项目定位是帮助用户更好地使用 Unity 编辑器，不替代也不复制 Unity 官方 AI 产品。

---

## 九、已知限制

1. 写 C# 会触发 Unity 编译与域重载，进行中的模型请求无法跨重载续接。窗口会自动恢复聊天记录；脚本写入后点「检查编译结果」即可把 Console 错误回灌给模型继续修正。
2. 目前是非流式请求，长回答要等模型完整返回；部分中转站的 function calling 稳定性需自行验证。
3. 工具是纯 C# 编辑器 API 子集，不覆盖资源导入设置、动画状态机连线、Shader Graph 图编辑等深度操作，可按 `docs/ARCHITECTURE.md` 扩展。
4. 知识库是人工维护的知识卡 + 按需注入的官方 Skills 单篇，不是 Unity 官方文档全文 RAG；官方技能默认只取 1 篇并截断到 3000 字符，覆盖不到的场景模型仍可能给出过时写法，此时应补一张卡而不是反复改 Prompt。
5. Gemini 与 Anthropic 分支已实现，但主要验证环境是 OpenAI 兼容中转，若遇到协议细节差异请看窗口里带原始响应的报错。

---

## 十、后续计划

```text
v0.2  工具结果自动折叠/编译自检闭环、知识卡按包版本自动裁剪、更多 Provider
v0.3  跨域重载自动续接、脚本写入 diff 预览、多轮会话导出
v0.4  评测集扩充到 100 条并公开命中率报告、按 6000.0/6000.1/6000.2/6000.3 分版本卡组
v0.5  官方 Skills 参考层按小版本分组、references/ 按需加载、评测集扩充到 100 条
```

---

## License

本项目自身代码为 MIT，见 `LICENSE`。`Official/` 内的第三方内容不适用 MIT，按 Unity Companion License v1.4 授权，见 `Official/NOTICE.md`。欢迎提 Issue 与 PR，尤其是新增知识卡和评测用例。
