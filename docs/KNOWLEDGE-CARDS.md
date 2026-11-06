# 知识卡规范

知识卡是本项目的核心资产。它不是 Unity 文档全文，而是**自己撰写的、结构化的、带版本与工具约束的决策要点**：告诉模型「这个需求在 Unity 6 里该用哪个 API、不能用哪个、调哪个工具、参数怎么填、写完检查什么」。

## 一、文件与命名

```text
Editor/Skills/Cards/u6skill_<主题>.json
```

- 文件名必须以 `u6skill_` 开头、`.json` 结尾，`SkillLibrary` 只扫描这个前缀。
- 新增文件后运行 `python tools/gen_meta.py` 补 `.meta`，否则从 Git URL 安装时 Unity 会忽略该资源。
- `id` 全局唯一，评测集 `evals/prompts.json` 用 `id` 引用。

## 二、字段

| 字段 | 类型 | 必需 | 作用 |
|---|---|---|---|
| `id` | string | 是 | 唯一标识，评测与日志都用它 |
| `title` | string | 是 | 卡片标题，也参与打分（长度 >= 3 的词） |
| `unity` | string[] | 是 | 适用版本表达式，见下节 |
| `packages` | string[] | 否 | 相关 UPM 包名，命中 +2 分，写进 System Prompt 提醒模型先确认安装 |
| `topics` | string[] | 否 | 主题词，命中 +2 分 |
| `keywords` | string[] | 是 | 触发词，命中 +3 分，中英文都写 |
| `tools` | string[] | 是 | 该卡允许开放的工具名，是工具白名单的唯一来源 |
| `summary` | string | 是 | 一句话决策结论 |
| `preferred` | string[] | 否 | 推荐 API / 做法 |
| `deprecated` | string[] | 否 | 禁用或已弃用 API，这是「版本更懂」最直接的体现 |
| `constraints` | string[] | 否 | 动手前必须满足的条件 |
| `pitfalls` | string[] | 否 | 常见错误与症状 |
| `parameterTemplate` | string | 否 | 工具参数示例，让检索结果能直接转成 tool call |
| `snippet` | string | 否 | 可编译的 C# 参考写法，会被截断到约 1200 字符 |
| `examplePrompts` | string[] | 否 | 示例指令，供贡献者理解用途 |
| `source` | string | 否 | 来源声明；当前 24 张卡均为自撰说明、尚未补真实 URL，不写文档全文 |
| `alwaysInclude` | bool | 否 | 常驻卡（全库只应有 1 张：`u6-core`） |
| `priority` | int | 否 | 同分排序权重，越大越靠前 |

## 三、版本表达式

当前版本由 `Application.unityVersion` 解析，例如 `6000.3.26f1` -> `6000.3`。

| 写法 | 含义 |
|---|---|
| `any` | 所有版本 |
| `6000.0+` | 6000.0 及以上 |
| `6000.3` | 精确匹配 6000.3 小版本 |
| `6000.0-6000.2` | 闭区间 |

不匹配的卡在路由阶段直接排除，不会注入 System Prompt。写跨版本有差异的 API 时，宁可拆成两张卡分别标注版本，也不要在一张卡里含糊带过。

## 四、路由与渐进式披露

```text
第 1 层  常驻 System Prompt：角色、工程实测信息、通用规则
第 2 层  路由：关键词 +3 / topic +2 / package +2 / title 词 +1，取 top3 主题卡
第 3 层  按需加载：把命中卡渲染成结构化文本（推荐/禁用/约束/坑/参数模板/片段）
第 4 层  工具白名单：BaselineReadTools ∪ 命中卡的 tools
第 5 层  模型 tool calling，执行后回读 Console 再迭代
```

零命中时注入 `u6-general-workflow` 兜底，保证仍能写脚本、建物体。

**写工具不要写进 `u6-core`**：常驻卡一旦包含写工具，所有请求都会开放写权限，知识驱动的意义就没了。写工具只放在主题卡和兜底卡里。

## 五、撰写准则

1. `snippet` 必须是能编译的 C#，字符串字面量要写引号。错误的示例片段会直接把错误写法教给模型。
2. `deprecated` 与 `preferred` 要成对出现，明确「用 X 不用 Y」，不要只讲理论。
3. 涉及包才有能力的 API（Addressables、Cinemachine、AI Navigation、VFX Graph、Input System），`constraints` 里必须写「先 get_packages 确认包已安装」，避免模型在没装包时生成编译不过的代码。
4. `source` 写清来源；当前 24 张卡的 `source` 是自撰声明文字，**尚无任何真实链接**，这是已知欠账，补卡时应同时补上官方页面 URL。不要复制官方文档段落：Unity 文档主体是 CC BY-NC-ND 4.0（署名、非商业、禁止演绎），代码片段适用 Unity Companion License；Unity 允许 AI 检索公开文档用于帮助用户学习使用 Unity 产品，但禁止用于开发与 Unity 竞争或复制的产品，且服务条款禁止爬虫批量抓取。官方原文另有一套独立来源，见下节。
5. 事实不确定时写「以 get_packages / get_project_info 实测为准」，不要编造 API。
6. 卡里的工具名必须真实存在于 `ToolRegistry`，写错等于白名单里混入不存在的工具。

## 六、新增卡的验收流程

```text
1. 复制一张现有卡，改 id / keywords / tools / deprecated / snippet
2. python tools/gen_meta.py                  补 .meta
3. 在 evals/prompts.json 加 1~3 条 expectCards / expectTools 用例
4. python tools/verify_router.py            离线验算命中率
5. Unity 菜单 Tools > Unity6 Skill AI > Reload Knowledge Cards
6. 窗口点「预览注入」，确认 System Prompt 里出现新卡且工具白名单正确
7. 若命中率下降，优先调 keywords，不要放宽阈值或增加 top3 数量
```

`tools/verify_router.py` 是 `SkillRouter.cs` 的离线等价实现。改动评分规则时必须同步两边，并在 README 更新基线命中率。

无头验证（CI 可用）：

```text
Unity.exe -batchmode -nographics -quit -projectPath <工程> \
  -executeMethod Unity6SkillAI.Editor.BatchSelfTest.Run -logFile selftest.log
```

日志里出现 `[SELFTEST] RESULT: PASS` 即表示知识卡定位、版本解析、路由、工具注册、评测全部正常。

## 七、当前基线

```text
知识卡 24 张（含 1 张常驻）
评测用例 30 条
知识卡命中率 30/30
工具白名单命中率 30/30
官方 Skills 35 个（core 26 / cli 4 / service 5）
官方技能离线用例 10/10
验证环境 Unity 6000.3.26f1
```

## 八、与官方 Skills 参考层的关系

`Official/` 是另一套知识库，和本目录的知识卡**不是同一层**，不要把官方 SKILL.md 改写成 `u6skill_*.json` 塞进 `Cards/`：

| | 自研知识卡 | 官方 Skills 参考层 |
|---|---|---|
| 位置 | `Editor/Skills/Cards/` | `Official/` |
| 数量 | 24 | 35 |
| 来源 | 本项目逐张自撰 | Unity-Technologies/skills 原文快照 |
| 注入方式 | 1 张常驻 + 最多 3 张主题卡，结构化字段 | 默认 1 篇，正文按字符预算截断 |
| 决定工具白名单 | 是，唯一来源 | 否，只读参考 |
| 版本过滤 | 按 `unity` 表达式 | 不做版本过滤，官方仓库未声明最低版本 |

官方技能面向能跑 Unity CLI 的编码代理，正文含命令行指令，因此注入时前置 5 条适配声明；详见 `docs/OFFICIAL-SKILLS.md`。
