#!/usr/bin/env python3
"""Offline twin of SkillRouter.cs.

Contributors can add/adjust a knowledge card and immediately see the routing
hit rate without opening Unity or spending a single LLM token. The scoring
rules here MUST stay in sync with Editor/Core/SkillRouter.cs.

Usage:  python tools/verify_router.py
"""

import json
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
CARD_DIR = os.path.join(ROOT, "Editor", "Skills", "Cards")
EVAL_FILE = os.path.join(ROOT, "evals", "prompts.json")

BASELINE_READ_TOOLS = [
    "get_project_info", "get_scene_hierarchy", "get_selection",
    "get_console_logs", "get_compile_errors", "list_scripts",
    "read_script", "find_assets", "get_packages",
]
MAX_TOPIC_CARDS = 3
MIN_SCORE = 2
TITLE_SPLIT = re.compile(r"[ /\u002d_()\u3001\uff0c,]")


def load_cards():
    cards = []
    for name in sorted(os.listdir(CARD_DIR)):
        if not (name.startswith("u6skill_") and name.endswith(".json")):
            continue
        with open(os.path.join(CARD_DIR, name), "r", encoding="utf-8") as handle:
            cards.append(json.load(handle))
    return cards


def title_words(title):
    return [w.strip().lower() for w in TITLE_SPLIT.split((title or "").lower()) if w.strip()]


def score(card, text):
    total = 0
    for kw in card.get("keywords", []):
        if kw and kw.lower() in text:
            total += 3
    for topic in card.get("topics", []):
        if topic and topic.lower() in text:
            total += 2
    for package in card.get("packages", []):
        if package and package.lower() in text:
            total += 2
    for word in title_words(card.get("title", "")):
        if len(word) >= 3 and word in text:
            total += 1
    return total


def route(cards, user_text):
    text = (user_text or "").lower()
    scored = []
    details = []
    for card in cards:
        if card.get("alwaysInclude"):
            continue
        value = score(card, text)
        details.append("[{0}] {1}".format(value, card["id"]))
        if value >= MIN_SCORE:
            scored.append((value, card))
    scored.sort(key=lambda item: (-item[0], -item[1].get("priority", 0), item[1]["id"]))

    picked = [card for card in cards if card.get("alwaysInclude")]
    picked += [card for _, card in scored[:MAX_TOPIC_CARDS]]
    if not scored:
        fallback = next((c for c in cards if c["id"] == "u6-general-workflow"), None)
        if fallback and fallback not in picked:
            picked.append(fallback)

    tools = set(BASELINE_READ_TOOLS)
    for card in picked:
        tools.update(card.get("tools", []))
    return [c["id"] for c in picked], tools, details


def main():
    cards = load_cards()
    with open(EVAL_FILE, "r", encoding="utf-8") as handle:
        cases = json.load(handle)["cases"]

    card_pass = 0
    tool_pass = 0
    failures = []
    for case in cases:
        card_ids, tools, details = route(cards, case["prompt"])
        cards_ok = all(expected in card_ids for expected in case["expectCards"])
        tools_ok = all(expected in tools for expected in case["expectTools"])
        card_pass += int(cards_ok)
        tool_pass += int(tools_ok)
        if not (cards_ok and tools_ok):
            failures.append(
                '  "{0}"\n    期望卡 {1} | 实际 {2}\n    期望工具 {3} | 命中 {4}\n    评分 {5}'.format(
                    case["prompt"], case["expectCards"], card_ids,
                    case["expectTools"], tools_ok, " ".join(details)))

    total = len(cases)
    print("知识卡数量: {0}".format(len(cards)))
    print("评测用例数: {0}".format(total))
    print("知识卡命中率: {0}/{1} ({2}%)".format(card_pass, total, card_pass * 100 // total))
    print("工具命中率: {0}/{1} ({2}%)".format(tool_pass, total, tool_pass * 100 // total))
    if failures:
        print("\n未通过用例 ({0}):".format(len(failures)))
        for failure in failures:
            print(failure)
    else:
        print("\n全部用例通过。")
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main())
