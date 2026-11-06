# -*- coding: utf-8 -*-
"""
把 Unity-Technologies/skills 的 SKILL.md 原文打包进本 UPM 包。

用法:
    python tools/build_official_skills.py <官方仓库解压目录> [输出目录]

示例:
    curl -L -o skills-main.zip https://codeload.github.com/Unity-Technologies/skills/zip/refs/heads/main
    python -c "import zipfile; zipfile.ZipFile('skills-main.zip').extractall('.')"
    python tools/build_official_skills.py skills-main

约束:
    - SKILL.md 逐字节原样复制, 不改动一字 (Unity Companion License v1.4 第 3.2 条
      规定对 Work 的演绎作品权利归 Unity, 因此不做任何改写)。
    - 只复制 SKILL.md 主文件。官方仓库 references/ 与 resources/ 下的 .cs
      文件是示例代码, 放进 UPM 包会被 Unity 当脚本编译, 故一律不打包。
    - 必须随附官方 LICENSE.md 与 UCL v1.4 全文 (第 5 条 Notices)。
"""

import hashlib
import json
import os
import re
import shutil
import sys
from datetime import date

UPSTREAM_REPO = "Unity-Technologies/skills"
UPSTREAM_URL = "https://github.com/Unity-Technologies/skills"
UPSTREAM_COMMIT = "ba28c24fc7e569fa0fd5b6bf16ab2bb0acef0594"
UPSTREAM_COMMIT_DATE = "2026-11-04T14:38:05Z"

# 中文关键词是本项目自撰的路由元数据, 不属于官方原文。
# 官方 description 全为英文, 不补中文关键词的话中文提问几乎无法命中。
KEYWORDS = {
    "2d-pixel-perfect": ["像素完美", "像素风", "像素画", "pixel perfect", "pixelperfectcamera", "模糊", "抖动", "锯齿", "2d 相机", "2d相机", "retro", "精灵模糊"],
    "asset-transformer-toolkit": ["资源转换", "asset transformer", "导入器", "importer", "lod", "规则集", "ruleset", "模型优化", "资产处理"],
    "audio-setup-mixers": ["混音器", "audiomixer", "音频分组", "音量控制", "snapshot", "母线", "音效管理", "音频设置", "audio mixer"],
    "build-gtk": ["graph toolkit", "gtk", "状态机", "节点图", "状态图", "graph api", "可视化脚本", "编辑器图表"],
    "build-live-game": ["ugs", "live game", "云服务", "战令", "battlepass", "成就", "云存档", "cloud save", "remote config"],
    "craft-game-floors": ["关卡", "场景搭建", "地板", "游戏感", "game feel", "美术方向", "占位资源", "卡通渲染", "镜头", "场景灯光", "vfx"],
    "create-2d-physics": ["2d 物理", "2d物理", "rigidbody2d", "碰撞体2d", "boxcollider2d", "2d 碰撞", "物理2d", "2d 刚体"],
    "create-tile-palette-or-rule-tiles": ["tilemap", "瓦片", "调色板", "ruletile", "规则瓦片", "六边形", "地图编辑", "tile palette"],
    "generate-editor-search-query": ["编辑器搜索", "search query", "层级搜索", "资源搜索", "find assets", "查询语法", "搜索窗口"],
    "implement-in-app-purchases": ["内购", "iap", "应用内购买", "支付", "商店", "购买", "receipt"],
    "initialize-ai-navigation": ["导航", "寻路", "navmesh", "navmeshagent", "navmeshsurface", "navmeshobstacle", "navmeshlink", "巡逻", "避障", "pathfinding", "导航网格"],
    "levelplay-unity-integration": ["广告", "levelplay", "激励视频", "插屏", "banner", "变现", "ironsource"],
    "localization": ["本地化", "多语言", "localization", "翻译", "locale", "string table", "语言切换", "l10n"],
    "manage-sprite-atlas": ["图集", "sprite atlas", "精灵图集", "atlas", "打包图集", "packing", "图集变体"],
    "migrate-birp-to-urp": ["渲染管线迁移", "birp", "built-in", "urp", "升级 urp", "shader 迁移", "材质丢失", "粉色材质", "管线升级"],
    "new-unity-project": ["新建工程", "创建项目", "工程模板", "安装编辑器", "unity hub", "项目初始化"],
    "optimize-audio": ["音频优化", "音频导入", "压缩格式", "audio clip", "音频内存", "加载方式", "声音优化"],
    "optimize-text-mesh-pro": ["textmeshpro", "tmp", "文本优化", "字体图集", "富文本", "字体资源", "文字渲染"],
    "optimize-web": ["webgl", "网页", "web 优化", "构建体积", "toktx", "浏览器", "web 平台"],
    "physics-3d-collision": ["碰撞", "trigger", "ontriggerenter", "oncollisionenter", "射线检测", "raycast", "穿透", "meshcollider", "physx", "触发器", "碰撞检测"],
    "project-auditor-fixes": ["性能分析", "project auditor", "性能问题", "优化建议", "诊断报告", "性能诊断"],
    "setup-multiplayer-services": ["多人", "联机", "netcode", "lobby", "relay", "多人服务", "dedicated server"],
    "setup-vivox-voice-chat": ["语音", "vivox", "语音聊天", "文字聊天", "voice chat", "语音频道"],
    "shader-graph-create-custom-node": ["shader graph", "自定义节点", "shader", "hlsl", "着色器", "节点", "shadergraph"],
    "sprite-editor": ["精灵编辑器", "切图", "sprite editor", "切片", "pivot", "精灵轴心", "自动切片"],
    "sprite-segment-3x3grid": ["九宫格", "3x3", "sprite 分割", "边框拉伸", "切图", "九宫切图"],
    "ui-imgui": ["imgui", "ongui", "编辑器窗口", "自定义检视面板", "inspector", "编辑器 ui"],
    "ui-ugui": ["ugui", "canvas", "scrollview", "button", "画布", "ui 界面", "界面布局", "scroll rect"],
    "ui-uitk": ["ui toolkit", "uitk", "uxml", "uss", "uidocument", "可视化元素", "运行时绑定", "uielements"],
    "ui": ["ui", "界面", "hud", "菜单", "按钮", "界面布局", "用户界"],
    "unity-cli": ["unity cli", "命令行", "批处理", "构建命令", "script eval", "自动化测试", "cli"],
    "unity-package-management": ["包管理", "package manager", "manifest", "添加包", "包版本", "upm"],
    "unity-pipeline": ["ci", "流水线", "构建流水线", "自动化", "devops", "持续集成"],
    "urp-postprocessing": ["后处理", "post processing", "volume", "bloom", "色调映射", "抗锯齿", "urp 后处理"],
    "validate-urp-render-graph-renderer-feature": ["render graph", "renderer feature", "scriptable renderer", "urp 扩展", "渲染图", "rendergraph"],
}

TOPICS = {
    "2d-pixel-perfect": ["2D", "渲染"],
    "asset-transformer-toolkit": ["资源管线"],
    "audio-setup-mixers": ["音频"],
    "build-gtk": ["编辑器扩展"],
    "build-live-game": ["云服务"],
    "craft-game-floors": ["关卡", "场景"],
    "create-2d-physics": ["2D", "物理"],
    "create-tile-palette-or-rule-tiles": ["2D", "Tilemap"],
    "generate-editor-search-query": ["编辑器"],
    "implement-in-app-purchases": ["商业化"],
    "initialize-ai-navigation": ["AI", "导航"],
    "levelplay-unity-integration": ["商业化"],
    "localization": ["本地化"],
    "manage-sprite-atlas": ["2D", "资源"],
    "migrate-birp-to-urp": ["渲染管线", "URP"],
    "new-unity-project": ["工程"],
    "optimize-audio": ["音频", "性能"],
    "optimize-text-mesh-pro": ["UI", "文本"],
    "optimize-web": ["平台", "性能"],
    "physics-3d-collision": ["物理", "诊断"],
    "project-auditor-fixes": ["性能", "诊断"],
    "setup-multiplayer-services": ["网络"],
    "setup-vivox-voice-chat": ["网络"],
    "shader-graph-create-custom-node": ["Shader", "渲染"],
    "sprite-editor": ["2D", "资源"],
    "sprite-segment-3x3grid": ["2D", "资源"],
    "ui-imgui": ["UI", "编辑器"],
    "ui-ugui": ["UI", "uGUI"],
    "ui-uitk": ["UI", "UI Toolkit"],
    "ui": ["UI"],
    "unity-cli": ["命令行"],
    "unity-package-management": ["包管理"],
    "unity-pipeline": ["CI"],
    "urp-postprocessing": ["渲染管线", "URP"],
    "validate-urp-render-graph-renderer-feature": ["渲染管线", "URP"],
}

# core     编辑器内创作/诊断知识, 默认注入
# cli      依赖 Unity CLI / Hub / 命令行, 本窗口不具备
# service  第三方或 UGS 服务集成, 与编辑器创作关系弱
TIERS = {
    "build-live-game": "service",
    "implement-in-app-purchases": "service",
    "levelplay-unity-integration": "service",
    "setup-multiplayer-services": "service",
    "setup-vivox-voice-chat": "service",
    "new-unity-project": "cli",
    "unity-cli": "cli",
    "unity-package-management": "cli",
    "unity-pipeline": "cli",
}

# 高频且容易被泛词误命中的技能, 给一点优先级修正
PRIORITY = {
    "ui": -2,
    "physics-3d-collision": 2,
    "initialize-ai-navigation": 2,
    "ui-uitk": 1,
    "ui-ugui": 1,
    "migrate-birp-to-urp": 1,
    "urp-postprocessing": 1,
}

def _load_ucl():
    """UCL v1.4 全文单独存放, 取自 Unity 官网, 避免手抄误差。"""
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "UCL-v1.4.txt")
    with open(path, encoding="utf-8") as f:
        return f.read()


UCL_V14 = _load_ucl()

NOTICE = """# 第三方内容声明 / Third-Party Notices

本目录 `Official/` 内的 `skills/*/SKILL.md` 来自 Unity Technologies 的公开仓库
Unity-Technologies/skills, 版权归 Unity Technologies 所有, 按
Unity Companion License v1.4 (Unity-dependent projects) 授权使用。

- 上游仓库: https://github.com/Unity-Technologies/skills
- 固定提交: ba28c24fc7e569fa0fd5b6bf16ab2bb0acef0594 (2026-11-04)
- 上游许可声明原文: UPSTREAM-LICENSE.md
- 许可证全文: LICENSE-UCL-v1.4.md
- 许可在线地址: https://unity.com/legal/licenses/unity-companion-license

处理方式:

1. 35 个 SKILL.md 逐字节原样复制, 未修改、未翻译、未删节。
   其中包括原文自带的符号与格式, 均属第三方内容。
2. 官方仓库 references/ resources/ scripts/ 下的文件未打包。
   其中的 .cs 是示例代码, 放进 UPM 包会被 Unity 当作脚本编译。
   SKILL.md 正文里指向这些相对路径的链接需到上游仓库查看。
3. official_index.json 里的中文关键词、主题、能力分级由本项目自撰,
   仅用于本地路由, 不是 Unity 官方内容, 也不代表 Unity 的推荐。
4. 本包自身代码仍按仓库根目录 LICENSE (MIT) 授权; MIT 不覆盖
   Official/ 内的第三方内容。
5. Unity Companion License v1.4 第 1 条禁止将 Work 用于竞争分析或
   开发竞争性产品/服务。本项目定位为 Unity 编辑器内的开发辅助工具,
   仅供学习与内部使用; 若用于商业化分发或公开服务, 请先做法务复核。
6. 未提供任何 Unity 商标授权; 文中提及 Unity 仅为描述性使用。

如认为本目录内容不应被再分发, 请联系本项目维护者, 我们会立即移除。
"""

SOURCE = """# 官方技能库更新方法

Official/ 是固定版本的快照, 不随 Unity 官方仓库自动更新。
需要升级时由维护者在本机重跑构建脚本, 再提交到本仓库。

```text
1 下载上游仓库压缩包并解压 (在包目录之外的临时目录操作)
   curl -L -o skills-main.zip https://codeload.github.com/Unity-Technologies/skills/zip/refs/heads/main
   python -c "import zipfile; zipfile.ZipFile('skills-main.zip').extractall('.')"

2 用 API 取当前 main 的 commit sha 与日期
   https://api.github.com/repos/Unity-Technologies/skills/commits/main

3 编辑 tools/build_official_skills.py 顶部的
   UPSTREAM_COMMIT / UPSTREAM_COMMIT_DATE, 必要时补充 KEYWORDS / TIERS

4 运行
   python tools/build_official_skills.py skills-main

5 运行 python tools/gen_meta.py 补齐 .meta, 再用 Unity 6 跑一次无头自测
   Unity.exe -batchmode -nographics -quit -projectPath <验证工程> \\
     -executeMethod Unity6SkillAI.Editor.BatchSelfTest.Run

6 更新 CHANGELOG.md 与 docs/OFFICIAL-SKILLS.md, 本地 commit
```

注意: 脚本只复制 SKILL.md 主文件, 不会把官方仓库的 .cs 示例带进包内。
"""


def parse_frontmatter(text):
    """读取 name / description / allowed-tools, 支持单行与多行列表。"""
    if not text.startswith("---"):
        return {}
    end = text.find("\n---", 3)
    if end < 0:
        return {}
    block = text[3:end].replace("\r\n", "\n").split("\n")
    data = {}
    current = None
    for line in block:
        if line.startswith("  - ") and current:
            data.setdefault(current + "_list", []).append(line[4:].strip())
            continue
        m = re.match(r"^([A-Za-z][A-Za-z0-9_-]*):\s*(.*)$", line)
        if not m:
            continue
        key, value = m.group(1), m.group(2).strip()
        current = key
        if value:
            data[key] = value
    return data


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def main():
    src = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else "skills-main"
    repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out = os.path.abspath(sys.argv[2]) if len(sys.argv) > 2 else os.path.join(repo_root, "Official")
    skills_src = os.path.join(src, "skills")

    if not os.path.isdir(skills_src):
        raise SystemExit("找不到官方技能目录: " + skills_src)

    upstream_license_path = os.path.join(src, "LICENSE.md")
    if not os.path.isfile(upstream_license_path):
        raise SystemExit("找不到上游 LICENSE.md: " + upstream_license_path)

    if os.path.isdir(out):
        shutil.rmtree(out)
    os.makedirs(out)

    # 逐字节复制上游许可声明
    with open(upstream_license_path, "rb") as f:
        upstream_license = f.read()
    with open(os.path.join(out, "UPSTREAM-LICENSE.md"), "wb") as f:
        f.write(upstream_license)
    with open(os.path.join(out, "LICENSE-UCL-v1.4.md"), "w", encoding="utf-8", newline="\n") as f:
        f.write(UCL_V14)
    with open(os.path.join(out, "NOTICE.md"), "w", encoding="utf-8", newline="\n") as f:
        f.write(NOTICE.replace("ba28c24fc7e569fa0fd5b6bf16ab2bb0acef0594 (2026-11-04)",
                               UPSTREAM_COMMIT + " (" + UPSTREAM_COMMIT_DATE[:10] + ")"))
    with open(os.path.join(out, "SOURCE.md"), "w", encoding="utf-8", newline="\n") as f:
        f.write(SOURCE)

    entries = []
    names = sorted(d for d in os.listdir(skills_src)
                   if os.path.isfile(os.path.join(skills_src, d, "SKILL.md")))
    for name in names:
        src_file = os.path.join(skills_src, name, "SKILL.md")
        with open(src_file, "rb") as f:
            raw = f.read()
        text = raw.decode("utf-8")
        fm = parse_frontmatter(text)
        skill_id = "official-" + name
        rel = "skills/" + name + "/SKILL.md"
        dst_file = os.path.join(out, rel.replace("/", os.sep))
        os.makedirs(os.path.dirname(dst_file), exist_ok=True)
        with open(dst_file, "wb") as f:
            f.write(raw)

        if name not in KEYWORDS:
            print("WARN 未配置中文关键词: " + name)
        tier = TIERS.get(name, "core")
        entries.append({
            "id": skill_id,
            "name": fm.get("name", name),
            "description": fm.get("description", ""),
            "tier": tier,
            "enabledByDefault": tier == "core",
            "priority": PRIORITY.get(name, 0),
            "topics": TOPICS.get(name, []),
            "keywords": KEYWORDS.get(name, [name]),
            "allowedTools": fm.get("allowed-tools_list", []),
            "file": rel,
            "sourceUrl": (UPSTREAM_URL + "/blob/" + UPSTREAM_COMMIT +
                          "/skills/" + name + "/SKILL.md"),
            "chars": len(text),
            "sha256": sha256(raw),
        })

    index = {
        "schemaVersion": 1,
        "upstream": {
            "repository": UPSTREAM_REPO,
            "url": UPSTREAM_URL,
            "commit": UPSTREAM_COMMIT,
            "commitDate": UPSTREAM_COMMIT_DATE,
            "license": "Unity Companion License v1.4 (Unity-dependent projects)",
            "licenseUrl": "https://unity.com/legal/licenses/unity-companion-license",
            "upstreamLicenseFile": "UPSTREAM-LICENSE.md",
            "licenseFile": "LICENSE-UCL-v1.4.md",
            "noticeFile": "NOTICE.md",
            "fetchedAt": date.today().isoformat(),
        },
        "defaults": {
            "enabledTiers": ["core"],
            "maxSkills": 1,
            "maxCharsPerSkill": 3000,
            "minScore": 3,
        },
        "skills": entries,
    }
    with open(os.path.join(out, "official_index.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(index, f, ensure_ascii=False, indent=2)
        f.write("\n")

    tiers = {}
    for e in entries:
        tiers[e["tier"]] = tiers.get(e["tier"], 0) + 1
    print("输出目录      :", out)
    print("技能总数      :", len(entries))
    print("分级          :", tiers)
    print("正文总字符    :", sum(e["chars"] for e in entries))
    print("索引          :", os.path.join(out, "official_index.json"))
    print("未配关键词    :", [n for n in names if n not in KEYWORDS])


if __name__ == "__main__":
    main()
