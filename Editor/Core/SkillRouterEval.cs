using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Unity6SkillAI.Editor
{
    /// <summary>
    /// Offline evaluation of the knowledge router. No API key and no LLM call
    /// are needed, so contributors can verify "does my prompt hit the right
    /// Unity 6 card / tool" before touching a paid endpoint.
    /// </summary>
    internal static class SkillRouterEval
    {
        private const string PackageJsonAssetPath = "Packages/com.hongjing.unity6-skill-ai/package.json";

        [MenuItem("Tools/Unity6 Skill AI/Run Routing Eval (No API Key)")]
        public static void RunFromMenu()
        {
            string report = Run();
            Debug.Log(report);
        }

        public static string Run()
        {
            string path = FindEvalFile();
            var sb = new StringBuilder();
            sb.AppendLine("# Unity6 Skill AI 路由评测报告");
            sb.AppendLine();
            sb.Append("生成时间: ").AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.Append("Unity 版本: ").AppendLine(UnityVersionInfo.Raw);
            sb.Append("知识卡数量: ").AppendLine(SkillLibrary.All.Count.ToString());
            sb.Append("知识卡目录: ").AppendLine(string.IsNullOrEmpty(SkillLibrary.CardsDirectory) ? "(未找到)" : SkillLibrary.CardsDirectory);
            if (!string.IsNullOrEmpty(SkillLibrary.Diagnostics))
                sb.AppendLine("加载诊断: " + SkillLibrary.Diagnostics);
            sb.AppendLine();

            if (string.IsNullOrEmpty(path))
            {
                sb.AppendLine("未找到 evals/prompts.json，无法执行评测。");
                WriteReport(sb.ToString());
                return sb.ToString();
            }
            sb.Append("评测集: ").AppendLine(path);
            sb.AppendLine();

            object parsed;
            List<object> cases;
            try
            {
                parsed = MiniJson.Parse(File.ReadAllText(path));
                var root = MiniJson.Obj(parsed);
                cases = MiniJson.Arr(root != null && root.ContainsKey("cases") ? root["cases"] : null);
            }
            catch (Exception e)
            {
                sb.AppendLine("评测集解析失败: " + e.Message);
                WriteReport(sb.ToString());
                return sb.ToString();
            }
            if (cases == null || cases.Count == 0)
            {
                sb.AppendLine("评测集为空。");
                WriteReport(sb.ToString());
                return sb.ToString();
            }

            int cardPass = 0;
            int toolPass = 0;
            var failures = new List<string>();
            foreach (var raw in cases)
            {
                var item = MiniJson.Obj(raw);
                if (item == null)
                    continue;
                string prompt = MiniJson.Get(item, "prompt");
                var expectedCards = MiniJson.GetStrings(item, "expectCards");
                var expectedTools = MiniJson.GetStrings(item, "expectTools");
                var route = SkillRouter.Route(prompt);
                var actualCards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var card in route.Cards)
                    actualCards.Add(card.Id);
                var actualTools = new HashSet<string>(route.AllowedTools, StringComparer.OrdinalIgnoreCase);

                bool cardsOk = true;
                foreach (var expected in expectedCards)
                {
                    if (!actualCards.Contains(expected))
                    {
                        cardsOk = false;
                        break;
                    }
                }
                bool toolsOk = true;
                foreach (var expected in expectedTools)
                {
                    if (!actualTools.Contains(expected))
                    {
                        toolsOk = false;
                        break;
                    }
                }
                if (cardsOk) cardPass++;
                if (toolsOk) toolPass++;
                if (!cardsOk || !toolsOk)
                {
                    failures.Add("- \"" + prompt + "\"\n  期望卡: " + string.Join(",", expectedCards.ToArray()) +
                        " | 实际: " + route.CardIds +
                        "\n  期望工具: " + string.Join(",", expectedTools.ToArray()) +
                        " | 实际含: " + (toolsOk ? "是" : "否") +
                        "\n  评分明细: " + string.Join(" | ", route.ScoreDetails.ToArray()));
                }
            }

            sb.Append("用例总数: ").AppendLine(cases.Count.ToString());
            sb.Append("知识卡命中率: ").Append(cardPass).Append("/").Append(cases.Count)
                .Append(" (").Append(Percent(cardPass, cases.Count)).AppendLine("%)");
            sb.Append("工具白名单命中率: ").Append(toolPass).Append("/").Append(cases.Count)
                .Append(" (").Append(Percent(toolPass, cases.Count)).AppendLine("%)");
            sb.AppendLine();
            if (failures.Count == 0)
            {
                sb.AppendLine("全部用例通过。");
            }
            else
            {
                sb.AppendLine("## 未通过用例 (" + failures.Count + ")");
                sb.AppendLine();
                foreach (var failure in failures)
                    sb.AppendLine(failure);
            }
            string report = sb.ToString();
            WriteReport(report);
            return report;
        }

        private static string Percent(int value, int total)
        {
            return total == 0 ? "0" : (value * 100 / total).ToString();
        }

        private static void WriteReport(string report)
        {
            try
            {
                string path = Path.Combine(AgentSettings.LocalDataDirectory, "eval-report.md");
                AgentSettings.EnsureDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, report, new UTF8Encoding(false));
                Debug.Log("[Unity6 Skill AI] 评测报告已写入: " + path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Unity6 Skill AI] 评测报告写入失败: " + e.Message);
            }
        }

        public static string FindEvalFile()
        {
            try
            {
                var info = global::UnityEditor.PackageManager.PackageInfo.FindForAssetPath(PackageJsonAssetPath);
                if (info != null && !string.IsNullOrEmpty(info.resolvedPath))
                {
                    string candidate = Path.Combine(info.resolvedPath, "evals", "prompts.json");
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
            catch (Exception) { /* fall through */ }

            try
            {
                var guids = AssetDatabase.FindAssets("t:TextAsset prompts");
                foreach (var guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (assetPath.IndexOf("evals", StringComparison.OrdinalIgnoreCase) < 0 ||
                        !assetPath.EndsWith("prompts.json", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string absolute = Path.GetFullPath(Path.Combine(AgentSettings.ProjectRoot,
                        assetPath.Replace('/', Path.DirectorySeparatorChar)));
                    if (File.Exists(absolute))
                        return absolute;
                }
            }
            catch (Exception) { /* fall through */ }

            var roots = new List<string> { Application.dataPath, Path.Combine(AgentSettings.ProjectRoot, "Packages") };
            foreach (var root in roots)
            {
                if (!Directory.Exists(root))
                    continue;
                try
                {
                    var found = Directory.GetFiles(root, "prompts.json", SearchOption.AllDirectories);
                    foreach (var file in found)
                    {
                        if (file.Replace('\\', '/').IndexOf("/evals/", StringComparison.OrdinalIgnoreCase) >= 0)
                            return file;
                    }
                }
                catch (Exception) { /* unreadable folder */ }
            }
            return "";
        }
    }
}
