# Unity 官方 Skills 参考层

本目录说明 `Official/` 里那套第三方知识库从哪来、怎么用、边界在哪。

## 一、它是什么

`Official/` 是 Unity Technologies 公开仓库 [Unity-Technologies/skills](https://github.com/Unity-Technologies/skills) 在固定提交上的快照：

```text
仓库      Unity-Technologies/skills
提交      ba28c24fc7e569fa0fd5b6bf16ab2bb0acef0594 (2026-11-04)
文件      35 个 SKILL.md，共 544,740 字符
许可      Unity Companion License v1.4 (Unity-dependent projects)
许可全文  Official/LICENSE-UCL-v1.4.md
上游声明  Official/UPSTREAM-LICENSE.md
第三方声明 Official/NOTICE.md
```

每个 `SKILL.md` 由 `name` / `description` 的 YAML frontmatter 加正文组成，正文是 Unity 官方写给 AI 编码代理的工作流知识（例如 NavMesh 配置流程、UI Toolkit 的 UXML/USS 写法、PhysX 碰撞矩阵诊断树）。

**逐字节原样复制，未翻译、未删节、未改写。** UCL v1.4 第 3.2 条规定对 Work 的演绎作品权利归 Unity，所以本项目一个字都不改，只在外面加路由元数据。

## 二、为什么不让它们直接当知识卡用

三个现实问题：

1. **体量**。54 万字符全塞进 System Prompt 会直接撑爆上下文。实测单个技能最长 69,015 字符（`unity-cli`），最短 1,176 字符。
2. **语言**。官方 `description` 全是英文，用户输入中文时按英文关键词打分几乎不命中。
3. **能力错位**。官方技能是给「有 shell、能跑 Unity CLI 的编码代理」写的，正文里大量出现 `script eval`、`Bash`、装编辑器、联网抓文档等指令。本窗口是纯 C# 编辑器窗口，没有这些能力，直接注入会让模型去调根本不存在的工具。

对应的处理：

| 问题 | 处理 |
|---|---|
| 体量 | 渐进式披露：默认只取命中分最高的 1 个技能，正文按 3000 字符预算截断，截断处标注不得臆造 |
| 语言 | `official_index.json` 里为 35 个技能自撰中文 + 英文关键词与主题词，由 `OfficialSkillLibrary.Score` 按长度加权评分 |
| 能力错位 | 注入段落前置 5 条适配声明，明确忽略 CLI / 外部工具 / 强制流程指令，工具白名单以自研知识卡为准 |
| 官方正文含 emoji | 仅在内存渲染时移除非 BMP 字符与 U+FFFD，磁盘文件保持原样 |

## 三、两层知识库的分工

```text
自研知识卡 (Editor/Skills/Cards/, 24 张)
  职责：决定「用哪个 API、禁用哪个 API、开放哪些工具、参数怎么填」
  产出：工具白名单的唯一来源，参与 30 条固定评测

官方 Skills 参考层 (Official/, 35 个)
  职责：按需补充该主题的完整工作流、诊断树、易错点
  产出：System Prompt 里的一个参考段落，不增加、不删除任何工具
```

这是刻意做的隔离。`BatchSelfTest` 里有一条检查：同一句输入分别关闭/开启官方层，两次 `AllowedTools` 必须完全一致。官方层只读，不改权限。

冲突时的优先级也写进了适配声明：官方原文与自研知识卡冲突，以知识卡的「必须满足」「禁用或已弃用」为准。

## 四、分级

`official_index.json` 给每个技能打了 `tier`：

| tier | 数量 | 默认 | 说明 |
|---|---|---|---|
| `core` | 26 | 启用 | 编辑器内创作与诊断知识，如物理碰撞、UI、NavMesh、图集、URP 迁移、Shader Graph |
| `cli` | 4 | 关闭 | 依赖 Unity CLI / Hub / 命令行，本窗口不具备 |
| `service` | 5 | 关闭 | UGS、IAP、广告、多人、语音等第三方服务集成 |

设置页的「包含 CLI / 服务类技能」可以临时放开后 9 个，但它们正文里的命令行指令依然被适配声明屏蔽，只作背景知识。

## 五、怎么用

窗口 `设置 > Unity 官方 Skills 参考层`：

```text
注入官方技能知识        默认开
包含 CLI / 服务类技能   默认关
最多注入技能数          默认 1，可调 1 到 3
单技能最大字符          默认 3000，可调 1000 到 8000
```

`注入预览` 会显示本次命中的官方技能名；完整 System Prompt 里能看到参考层原文与 5 条适配声明。状态栏会显示官方技能总数。

离线验收（不花 Token）：

```text
python tools/verify_official_router.py    复刻 C# 评分，10 条中英文用例
Unity 无头自测                            8 条中文用例 + 许可证存在性 + 白名单稳定性
```

## 六、来源与合规边界

UCL v1.4 第 1 条允许在与有效 Unity Engine License 相关的内容创作/分发中使用、复制、演绎和分发这些文档，但明确禁止「用于竞争分析或开发竞争性产品/服务」；第 5 条要求随实质部分提供许可证与版权声明；第 4 条不授予商标权。本项目据此：

- 随包附带 UCL v1.4 全文、上游 LICENSE.md 原文和 NOTICE.md；
- 只复制 `SKILL.md` 主文件，**不打包** `references/` `resources/` `scripts/`。其中 `.cs` 是示例代码，放进 UPM 包会被 Unity 当脚本编译；因此正文里的 `references/xxx.md` 相对链接需到上游仓库查看，适配声明已要求模型不要尝试读取或据此编造 API；
- 项目自身代码仍是 MIT，**MIT 不覆盖 `Official/` 内的第三方内容**；
- 不使用 Unity 商标做背书，项目定位为帮助使用 Unity 编辑器的开发辅助工具。

需要提醒的一点：Unity 自己也提供 AI Assistant / Agent 类产品，UCL 的「不得开发竞争性产品」如何界定，本项目不作法律判断。**自用、学习、内部工具按上述方式使用；若要商业化分发或公开提供服务，请先做法务复核，或改为不随包附带官方原文、只保留索引和链接、由使用者自行拉取。**

若 Unity 认为不应再分发这些文件，移除 `Official/` 即可，路由层会自动降级为「官方技能参考层不可用」，其余功能不受影响。

## 七、更新方法

见 `Official/SOURCE.md`。简要流程：下载上游 zip 到包目录之外 → 在 `tools/build_official_skills.py` 顶部更新 commit sha 与日期、必要时补中文关键词 → 重跑脚本 → `python tools/gen_meta.py` → Unity 6 无头自测 → 更新 CHANGELOG。

脚本会重新生成 `official_index.json` 并校验每个 `SKILL.md` 的 SHA-256；`SKILL.md` 本身始终逐字节复制。
