using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Unity6SkillAI.Editor
{
    internal sealed class RouteResult
    {
        public readonly List<SkillCard> Cards = new List<SkillCard>();
        public readonly List<string> AllowedTools = new List<string>();
        public readonly List<string> ScoreDetails = new List<string>();
        public string SystemPrompt = "";
        public string ProjectContext = "";
        public int EstimatedChars;

        public string CardIds
        {
            get
            {
                var ids = new List<string>();
                foreach (var card in Cards)
                    ids.Add(card.Id);
                return string.Join(", ", ids.ToArray());
            }
        }
    }

    /// <summary>
    /// Progressive disclosure: a small always-on core card, plus at most three
    /// task-specific cards selected by keyword score and Unity version. The
    /// union of the cards' tool lists is the tool whitelist for this turn.
    /// Write-capable tools are therefore never exposed unless a knowledge card
    /// that describes the correct Unity 6 API was matched first.
    /// </summary>
    internal static class SkillRouter
    {
        public static readonly string[] BaselineReadTools =
        {
            "get_project_info",
            "get_scene_hierarchy",
            "get_selection",
            "get_console_logs",
            "get_compile_errors",
            "list_scripts",
            "read_script",
            "find_assets",
            "get_packages"
        };

        private const int MaxTopicCards = 3;
        private const int MinScore = 2;
        private const int MaxSnippetChars = 1200;

        public static RouteResult Route(string userText)
        {
            var result = new RouteResult();
            string input = (userText ?? "").ToLowerInvariant();
            double currentVersion = UnityVersionInfo.Numeric;

            var scored = new List<KeyValuePair<int, SkillCard>>();
            foreach (var card in SkillLibrary.All)
            {
                if (!UnityVersionInfo.Matches(card.Unity, currentVersion))
                {
                    result.ScoreDetails.Add("[版本不符] " + card.Id + " (" + string.Join(",", card.Unity.ToArray()) + ")");
                    continue;
                }
                if (card.AlwaysInclude)
                    continue;
                int score = Score(card, input);
                result.ScoreDetails.Add("[" + score + "] " + card.Id + " " + card.Title);
                if (score >= MinScore)
                    scored.Add(new KeyValuePair<int, SkillCard>(score, card));
            }

            scored.Sort((a, b) =>
            {
                int byScore = b.Key.CompareTo(a.Key);
                if (byScore != 0)
                    return byScore;
                int byPriority = b.Value.Priority.CompareTo(a.Value.Priority);
                if (byPriority != 0)
                    return byPriority;
                return string.CompareOrdinal(a.Value.Id, b.Value.Id);
            });

            var core = SkillLibrary.CoreCard;
            if (core != null)
                result.Cards.Add(core);

            int picked = 0;
            foreach (var entry in scored)
            {
                if (picked >= MaxTopicCards)
                    break;
                result.Cards.Add(entry.Value);
                picked++;
            }
            if (picked == 0)
            {
                var fallback = SkillLibrary.Find("u6-general-workflow");
                if (fallback != null && fallback != core)
                {
                    result.Cards.Add(fallback);
                    result.ScoreDetails.Add("[兜底] 未命中主题卡，注入通用工作流");
                }
            }

            var toolSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tool in BaselineReadTools)
                toolSet.Add(tool);
            foreach (var card in result.Cards)
            {
                foreach (var tool in card.Tools)
                    toolSet.Add(tool);
            }
            foreach (var tool in toolSet)
                result.AllowedTools.Add(tool);
            result.AllowedTools.Sort(StringComparer.Ordinal);

            result.ProjectContext = AgentSettings.Current.autoInjectProjectContext
                ? ProjectInspector.BuildContextText()
                : "(已关闭自动注入工程上下文，可调用 get_project_info 获取)";
            result.SystemPrompt = BuildSystemPrompt(result);
            result.EstimatedChars = result.SystemPrompt.Length;
            return result;
        }

        private static int Score(SkillCard card, string input)
        {
            if (string.IsNullOrEmpty(input))
                return 0;
            int score = 0;
            foreach (var keyword in card.Keywords)
            {
                if (!string.IsNullOrEmpty(keyword) && input.Contains(keyword.ToLowerInvariant()))
                    score += 3;
            }
            foreach (var topic in card.Topics)
            {
                if (!string.IsNullOrEmpty(topic) && input.Contains(topic.ToLowerInvariant()))
                    score += 2;
            }
            foreach (var package in card.Packages)
            {
                if (!string.IsNullOrEmpty(package) && input.Contains(package.ToLowerInvariant()))
                    score += 2;
            }
            string title = (card.Title ?? "").ToLowerInvariant();
            foreach (var word in SplitWords(title))
            {
                if (word.Length >= 3 && input.Contains(word))
                    score += 1;
            }
            return score;
        }

        private static IEnumerable<string> SplitWords(string text)
        {
            var parts = text.Split(new[] { ' ', '/', '-', '_', '(', ')', '、', '，', ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
                yield return part.Trim().ToLowerInvariant();
        }

        private static string BuildSystemPrompt(RouteResult route)
        {
            var sb = new StringBuilder();
            sb.AppendLine("你是内嵌在 Unity 编辑器窗口里的开发助手，版本定位为 Unity 6（6000.x）。");
            sb.AppendLine("用户使用简体中文，回答也使用简体中文；给出可执行的操作，不要只讲理论。");
            sb.AppendLine();
            sb.AppendLine("=== 工程实测信息（优先依据，勿凭记忆猜测版本） ===");
            sb.AppendLine(route.ProjectContext);
            sb.AppendLine();
            sb.AppendLine("=== 通用规则 ===");
            sb.AppendLine("1. 只能调用本次提供给你的工具，不要编造工具名；需要的能力不在白名单里时，说明限制并给出手工步骤。");
            sb.AppendLine("2. 写 C# 前先用 read_script / list_scripts / get_project_info 了解现有代码与包版本。");
            sb.AppendLine("3. 生成或修改脚本后，用 get_compile_errors / get_console_logs 复查编译结果，有错就自行修正。");
            sb.AppendLine("4. 严格遵守知识卡里的「禁用或已弃用」与「必须满足」，优先使用「优先使用」列出的 API。");
            sb.AppendLine("5. 修改场景前先读层级，避免重名与误改；一次只做用户要求的事，不要顺手重构无关内容。");
            sb.AppendLine("6. 输出代码片段时标注应保存的相对路径（Assets/ 下，.cs 结尾）。");
            sb.AppendLine();
            sb.AppendLine("=== 本次命中的 Unity 6 知识卡 ===");
            foreach (var card in route.Cards)
            {
                string rendered = card.RenderForPrompt();
                if (rendered.Length > MaxSnippetChars + 800)
                    rendered = rendered.Substring(0, MaxSnippetChars + 800) + "\n[...已截断...]";
                sb.AppendLine(rendered);
            }
            sb.AppendLine();
            sb.AppendLine("=== 本次可用工具 ===");
            sb.AppendLine(string.Join(", ", route.AllowedTools.ToArray()));
            sb.AppendLine();
            string extra = AgentSettings.Current.extraSystemPrompt;
            if (!string.IsNullOrEmpty(extra))
            {
                sb.AppendLine("=== 用户补充规则 ===");
                sb.AppendLine(extra);
            }
            return sb.ToString();
        }
    }
}
