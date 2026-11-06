#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""为 official_index.json 的每个 skill 补充 references 字段。

只读取磁盘上 Official/skills/<name>/references/*.md，计算每个 reference 的
相对路径、字符数与 sha256，写回索引的对应 skill 条目。其它字段（id/name/
keywords/sha256 等）一律保留，不重新生成，避免破坏已有校验。

用法:  python tools/update_references_index.py
"""
import os
import json
import hashlib

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
OFFICIAL = os.path.join(ROOT, "Official")
IDX_PATH = os.path.join(OFFICIAL, "official_index.json")


def main():
    with open(IDX_PATH, encoding="utf-8") as f:
        idx = json.load(f)

    total_refs = 0
    for s in idx.get("skills", []):
        name = s.get("name", "")
        ref_dir = os.path.join(OFFICIAL, "skills", name, "references")
        refs = []
        if os.path.isdir(ref_dir):
            for fn in sorted(os.listdir(ref_dir)):
                if not fn.lower().endswith(".md"):
                    continue
                p = os.path.join(ref_dir, fn)
                try:
                    raw = open(p, "rb").read()
                    text = raw.decode("utf-8", "ignore")
                    refs.append({
                        "file": "skills/{0}/references/{1}".format(name, fn),
                        "chars": len(text),
                        "sha256": hashlib.sha256(raw).hexdigest(),
                    })
                except Exception as e:
                    print("跳过 {0}: {1}".format(p, e))
        s["references"] = refs
        total_refs += len(refs)

    with open(IDX_PATH, "w", encoding="utf-8", newline="\n") as f:
        json.dump(idx, f, ensure_ascii=False, indent=2)
        f.write("\n")

    print("已更新索引: {0} 个 skill, 共 {1} 个 reference 条目".format(
        len(idx.get("skills", [])), total_refs))


if __name__ == "__main__":
    main()
