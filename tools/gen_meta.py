#!/usr/bin/env python3
"""Generate .meta files for a UPM package that is installed from a Git URL.

Unity treats packages in Library/PackageCache as immutable: every file and
folder needs a .meta file, otherwise Unity reports "has no meta file, but it's
in an immutable folder" and silently drops the asset. Local packages under
Packages/ or Assets/ do not need this, but shipping the .meta files makes both
installation paths work.

Usage:  python tools/gen_meta.py [package_root]
Safe to re-run: existing .meta files are never overwritten.
"""

import os
import sys
import uuid

SKIP_DIRS = {".git", ".verify", "Library", "Temp", "Logs", "obj", "node_modules"}
SKIP_FILES = {".gitignore", ".gitattributes", ".DS_Store"}

FILE_META = "fileFormatVersion: 2\nguid: {guid}\n"
FOLDER_META = "fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n"


def ensure_meta(path, is_dir):
    if os.path.basename(path).startswith("."):
        return 0
    if not is_dir and os.path.basename(path) in SKIP_FILES:
        return 0
    meta = path + ".meta"
    if os.path.exists(meta):
        return 0
    template = FOLDER_META if is_dir else FILE_META
    with open(meta, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(template.format(guid=uuid.uuid4().hex))
    return 1


def main():
    root = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), ".."))
    created = 0
    for current, dirs, files in os.walk(root):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS and not d.startswith(".")]
        for d in dirs:
            created += ensure_meta(os.path.join(current, d), True)
        for f in files:
            if f.endswith(".meta"):
                continue
            created += ensure_meta(os.path.join(current, f), False)
    print("created {0} .meta file(s) under {1}".format(created, root))


if __name__ == "__main__":
    main()
