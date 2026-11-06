# -*- coding: utf-8 -*-
"""离线复刻 C# 官方技能路由评分, 不启动 Unity 即可验收命中率。"""

import hashlib
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OFFICIAL = os.path.join(ROOT, "Official")

CASES = [
    ("用 NavMesh 做敌人巡逻寻路", "initialize-ai-navigation"),
    ("UI Toolkit 的 UXML 布局怎么写", "ui-uitk"),
    ("两个 Trigger 不触发 OnTriggerEnter", "physics-3d-collision"),
    ("把 Built-in 管线迁移到 URP 后材质变粉", "migrate-birp-to-urp"),
    ("像素风游戏画面模糊抖动", "2d-pixel-perfect"),
    ("Sprite Atlas 图集怎么打包", "manage-sprite-atlas"),
    ("TextMeshPro 字体图集太大怎么优化", "optimize-text-mesh-pro"),
    ("用 AudioMixer 做音量分组", "audio-setup-mixers"),
    ("Shader Graph 自定义节点怎么写", "shader-graph-create-custom-node"),
    ("本地化多语言 String Table 配置", "localization"),
]


def score(skill, text):
    """与 OfficialSkillLibrary.Score 保持一致。"""
    total = 0
    for kw in skill.get("keywords", []):
        if kw and kw.lower() in text:
            total += 3 if len(kw) >= 4 else 2
    for topic in skill.get("topics", []):
        if topic and topic.lower() in text:
            total += 1
    name = skill.get("name", "").lower()
    if len(name) >= 4 and name in text:
        total += 5
    return max(0, total)


def pick(index, text, min_score=3, core_only=True):
    text = text.lower()
    scored = []
    for s in index["skills"]:
        if core_only and not s.get("enabledByDefault", True):
            continue
        v = score(s, text)
        if v >= min_score:
            scored.append((v, s.get("priority", 0), s["name"], s))
    scored.sort(key=lambda x: (-x[0], -x[1], x[2]))
    return scored


def main():
    path = os.path.join(OFFICIAL, "official_index.json")
    with open(path, encoding="utf-8") as f:
        idx = json.load(f)

    min_score = idx.get("defaults", {}).get("minScore", 3)
    print("上游提交 :", idx["upstream"]["commit"], idx["upstream"]["commitDate"])
    print("许可     :", idx["upstream"]["license"])
    print("技能总数 :", len(idx["skills"]),
          "| core", sum(1 for s in idx["skills"] if s.get("enabledByDefault")),
          "| minScore", min_score)
    print()

    # 许可证与原文完整性
    missing = [n for n in ("LICENSE-UCL-v1.4.md", "UPSTREAM-LICENSE.md", "NOTICE.md", "SOURCE.md")
               if not os.path.isfile(os.path.join(OFFICIAL, n))]
    bad_hash = []
    for s in idx["skills"]:
        p = os.path.join(OFFICIAL, s["file"].replace("/", os.sep))
        if not os.path.isfile(p):
            bad_hash.append(s["name"] + " (缺失)")
            continue
        raw = open(p, "rb").read()
        if hashlib.sha256(raw).hexdigest() != s["sha256"]:
            bad_hash.append(s["name"] + " (SHA 不符)")
    print("许可证文件缺失 :", missing or "无")
    print("原文校验失败   :", bad_hash or "无")
    print()

    passed = 0
    for prompt, expected in CASES:
        hits = pick(idx, prompt, min_score)
        got = hits[0][2] if hits else "(无)"
        top_score = hits[0][0] if hits else 0
        ok = got == expected
        passed += ok
        print(("PASS" if ok else "FAIL"), "|", prompt, "->", got,
              "(", top_score, ")", "" if ok else "期望 " + expected)
    print()
    print("命中率 :", str(passed) + "/" + str(len(CASES)))
    print("结果   :", "PASS" if passed == len(CASES) and not missing and not bad_hash else "FAIL")
    return 0 if passed == len(CASES) and not missing and not bad_hash else 1


if __name__ == "__main__":
    sys.exit(main())
