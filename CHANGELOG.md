# 更新日志

## 0.3.0 (2026-11-06)

扩充自研知识卡，让自然语言能覆盖更多 Unity 6 创作主题，降低系统学习引擎的成本。

新增 14 张知识卡（共 24 -> 38 张）：

- 脚本生命周期与执行顺序（`u6-scripting-lifecycle`）：Awake/Start/Update/FixedUpdate/LateUpdate 职责分工，相机跟随应放 LateUpdate。
- 2D 物理（`u6-2d-physics`）：Rigidbody2D、Collider2D、PhysicsMaterial2D、2D 碰撞/触发事件。
- 2D 精灵渲染（`u6-sprite-2d`）：SpriteRenderer、Sorting Layer/Order、URP 2D 光照、像素图设置。
- TextMeshPro 文本（`u6-tmp-text`）：TMP 文本组件、Essential Resources、中文方块解决方案、富文本。
- URP 后处理（`u6-urp-postprocessing`）：Volume、Bloom、Vignette、Color Adjustments，Renderer 启用开关。
- 渲染管线（`u6-render-pipeline`）：URP vs BIRP、Renderer Feature、材质变粉色排查。
- 常见运行时报错（`u6-common-runtime-errors`）：NullReference、SerializeField 可见性、控制台排错顺序。
- 事件系统（`u6-event-system`）：UnityEvent、Button.onClick、event/delegate、订阅清理。
- 脚本架构模式（`u6-scripting-patterns`）：单例、状态机、事件总线、职责拆分。
- 相机与多相机（`u6-camera-rendering`）：Culling Mask、Depth 叠加、Render Texture 小地图。
- Timeline 过场（`u6-timeline-playable`）：PlayableDirector、TimelineAsset、运行时播放。
- 地形系统（`u6-terrain-basics`）：Terrain、TerrainCollider、Terrain Tools 笔刷、运行时改高度。
- 移动端触摸（`u6-mobile-touch-input`）：Input System EnhancedTouch、多点触控、Pointer 拖拽。
- 打包发布（`u6-build-export`）：Unity 6 Build Profiles、Player Settings、自定义 BuildPipeline 编辑器脚本。

其他：

- 评测集由 30 条扩展到 58 条，新增卡片各配 1-2 条用例；离线路由评测 58/58（知识卡命中率与工具白名单命中率均 100%）。
- 新增卡片均严格使用现有 17 个工具名，写入前已做 JSON 合法性、工具名白名单、字段完整性、重复 id 校验。
- 修正一处评测期望：私有字段在 Inspector 不显示本应命中序列化卡，原期望误写为报错卡。

说明：本次仍是自撰结构化卡片，未引入第三方知识；`source` 字段仍为自撰声明，暂无真实文档 URL。版本化（按 6000.0/6000.1/6000.2/6000.3 分卡组）尚未真正落地。

## 0.2.0 (2026-11-06)

新增「Unity 官方 Skills 参考层」，把官方权威知识接进窗口，同时不破坏原有工具授权逻辑。

新增：

- `Official/` 随包附带 Unity-Technologies/skills 固定提交 `ba28c24` 的 35 个 `SKILL.md` 原文（54 万字符），逐字节复制、未改写；只复制主文件，不打包 `references/` `resources/` `scripts/` 下的示例代码 `.cs`，避免被 Unity 当脚本编译。
- 附 UCL v1.4 全文、`UPSTREAM-LICENSE.md`、`NOTICE.md`、`SOURCE.md`，来源与许可边界可追溯。
- `official_index.json`：为 35 个技能自撰中文 + 英文关键词、主题、能力分级（core 26 / cli 4 / service 5）、真实上游 URL、字符数与 SHA-256。官方 `description` 全为英文，不补中文关键词中文提问几乎无法命中。
- `OfficialSkillLibrary.cs`：独立参考层，按关键词评分选 top N、按需读正文、按字符预算截断；注入前置 5 条适配声明，明确忽略 Unity CLI / script eval / 外部工具与官方原文的强制流程指令。
- `tools/build_official_skills.py` + `tools/UCL-v1.4.txt`：可从上游仓库重建快照并重算校验和。
- `tools/verify_official_router.py`：离线复刻 C# 评分，10 条中英文用例。
- 设置页新增 4 个开关（启用 / 包含 CLI 与服务类 / 最多注入数 / 单技能字符上限），状态栏、路由日志、注入预览均显示命中的官方技能。
- `BatchSelfTest` 增加 8 条中文官方技能用例、许可证文件存在性、适配声明与「开启官方层不改变工具白名单」检查。
- 新增 `docs/OFFICIAL-SKILLS.md`；修正 README 与知识卡规范里关于 `source` 字段的错误口径（此前写「只引用官方文档链接」，实际 24 张卡均为自撰声明、无真实 URL）。

设计约束：官方层是只读知识，**不参与工具白名单**。工具授权仍完全由 38 张自研知识卡决定，原有 58/58 评测不退化。

合规：官方原文按 Unity Companion License v1.4 在 Unity-dependent project 范围内使用与分发；该许可禁止用于竞争分析或开发竞争性产品/服务。自用与内部使用按此方式，商业化分发前需做法务复核。MIT 不覆盖 `Official/`。

验证：

- 离线：`python tools/verify_official_router.py` 10/10；35 个原文 SHA-256 全部与索引一致，与上游逐字节一致。
- Unity 6000.3.26f1 批处理：编译零错误、零 `.meta` 警告；无头自测 PASS（24 张卡、35 个官方技能、17 个工具、原评测 30/30、官方技能 8/8、许可证与适配声明齐全、开启官方层后工具白名单保持 12 -> 12 不变）。
- 实机已确认 Assets 拖入方式可用（用户 Unity 6 工程截图：窗口浮动、知识卡 24 张、工具 17 个）。
- 仓库仍为纯本地 Git，无 remote、无 push。

## 0.1.1 (2026-11-06)

本轮只做仓库收敛与安装体验，不改功能代码。

- 移除误入仓库的测试工程脚手架与 `.meta`，仓库收敛为纯 UPM 包：仓库根目录即包根，拖进任意 Unity 6 工程即可用。
- 临时验证产物移出包目录，交付文件夹约 450 KB，不含任何 Unity 工程、`Library/` 或缓存。
- README 安装章节改为面向「已有一个 Unity 6 工程，把文件夹拖进去」：Assets 拖入、Packages 内嵌、Package Manager 从磁盘挂载三种方式；补第一轮验收清单（前 3 步不消耗 Token）。
- 核查全部 54 个 `.meta` 完整，无缺失。

验证状态：

- Unity 6000.3.26f1 批处理：本地包方式编译零错误零警告，无头自测 PASS（24 张卡、17 个工具、评测 30/30）。
- Assets 拖入与 Packages 内嵌两种方式的路径回退已实现（UPM 包路径、AssetDatabase、磁盘扫描三重回退）。因本机同时运行着图形界面 Unity 编辑器，追加的无头批处理实例卡在 Bee 编译阶段，未能独立完成复验，交由实机验收第一步确认。
- 仓库仍为纯本地 Git，无 remote、无 push。

## 0.1.0 (2026-11-06)

首个可用版本，目标是把「BYOK 大模型客户端 + Unity 6 版本化知识库 + 编辑器内执行」合成一个可以直接拖进 Unity 6 工程的文件夹。

新增：

- 可浮动 / 可停靠的 `EditorWindow` 聊天窗口，IMGUI 实现，支持会话恢复、复制、展开折叠、Ctrl+Enter 发送。
- BYOK 设置页：7 个服务商预设（OpenAI、DeepSeek、SiliconFlow、Moonshot、Anthropic、Gemini、自定义中转）、三家协议适配、连通性自检、环境变量覆盖密钥。
- 自带 `MiniJson`，零第三方 DLL；`UnityWebRequest` 主线程轮询，无外部进程。
- 24 张 Unity 6 版本化知识卡，含适用版本、推荐 API、已弃用 API、约束、坑、参数模板与可编译片段。
- 知识路由：关键词评分 + Unity 小版本过滤 + top3 渐进式披露 + 常驻卡 + 零命中兜底；工具白名单由命中卡决定。
- 17 个纯 C# 编辑器工具（9 只读 / 8 写入），写操作审批门、Undo 分组、脚本写入撤销日志、Console 编译错误回灌。
- 「注入预览」可离线查看完整 System Prompt；「离线路由评测」30 条固定用例；`BatchSelfTest` 无头 CI 入口。
- 合规与文档：MIT 许可、商标声明、Unity 文档引用边界、密钥存储说明、架构与知识卡规范。

验证：

- Unity 6000.3.26f1 批处理编译零错误零警告，`Unity6SkillAI.Editor.dll` 生成。
- 无头自测 PASS：知识卡目录定位成功、24 张卡加载、17 个工具注册、路由命中正确。
- 离线评测：知识卡命中率 30/30，工具白名单命中率 30/30。

已知限制：

- 写 C# 触发域重载会中断进行中的模型请求，需点「检查编译结果」继续。
- 非流式响应；Gemini / Anthropic 分支主要在 OpenAI 兼容中转环境下验证。
- 知识卡为人工维护的要点库，不是 Unity 官方文档全文 RAG。
