using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Unity6SkillAI.Editor
{
    /// <summary>
    /// A single versioned Unity 6 knowledge card. Cards are plain JSON files so
    /// contributors can add one without touching C# and without rebuilding.
    /// </summary>
    internal sealed class SkillCard
    {
        public string Id = "";
        public string Title = "";
        public List<string> Unity = new List<string>();
        public List<string> Packages = new List<string>();
        public List<string> Topics = new List<string>();
        public List<string> Keywords = new List<string>();
        public List<string> Tools = new List<string>();
        public List<string> Preferred = new List<string>();
        public List<string> Deprecated = new List<string>();
        public List<string> Constraints = new List<string>();
        public List<string> Pitfalls = new List<string>();
        public List<string> ExamplePrompts = new List<string>();
        public string Summary = "";
        public string ParameterTemplate = "";
        public string Snippet = "";
        public string Source = "";
        public bool AlwaysInclude;
        public int Priority;
        public string FilePath = "";

        public static SkillCard FromJson(string json, string path)
        {
            var root = MiniJson.Obj(MiniJson.Parse(json));
            if (root == null)
                throw new FormatException("知识卡顶层不是 JSON 对象: " + path);
            var card = new SkillCard
            {
                Id = MiniJson.Get(root, "id"),
                Title = MiniJson.Get(root, "title"),
                Unity = MiniJson.GetStrings(root, "unity"),
                Packages = MiniJson.GetStrings(root, "packages"),
                Topics = MiniJson.GetStrings(root, "topics"),
                Keywords = MiniJson.GetStrings(root, "keywords"),
                Tools = MiniJson.GetStrings(root, "tools"),
                Preferred = MiniJson.GetStrings(root, "preferred"),
                Deprecated = MiniJson.GetStrings(root, "deprecated"),
                Constraints = MiniJson.GetStrings(root, "constraints"),
                Pitfalls = MiniJson.GetStrings(root, "pitfalls"),
                ExamplePrompts = MiniJson.GetStrings(root, "examplePrompts"),
                Summary = MiniJson.Get(root, "summary"),
                ParameterTemplate = MiniJson.Get(root, "parameterTemplate"),
                Snippet = MiniJson.Get(root, "snippet"),
                Source = MiniJson.Get(root, "source"),
                AlwaysInclude = MiniJson.GetBool(root, "alwaysInclude", false),
                Priority = (int)MiniJson.GetNum(root, "priority", 0),
                FilePath = path
            };
            if (string.IsNullOrEmpty(card.Id))
                card.Id = Path.GetFileNameWithoutExtension(path);
            if (card.Unity.Count == 0)
                card.Unity.Add("any");
            return card;
        }

        public string RenderForPrompt()
        {
            var sb = new StringBuilder();
            sb.Append("### 知识卡 [").Append(Id).Append("] ").AppendLine(Title);
            AppendLine(sb, "适用版本", Join(Unity));
            AppendLine(sb, "相关包", Join(Packages));
            AppendLine(sb, "要点", Summary);
            AppendLine(sb, "优先使用", Join(Preferred));
            AppendLine(sb, "禁用或已弃用", Join(Deprecated));
            AppendLine(sb, "必须满足", Join(Constraints));
            AppendLine(sb, "常见错误", Join(Pitfalls));
            AppendLine(sb, "推荐工具", Join(Tools));
            if (!string.IsNullOrEmpty(ParameterTemplate))
                sb.Append("工具参数模板: ").AppendLine(ParameterTemplate);
            if (!string.IsNullOrEmpty(Snippet))
                sb.Append("参考写法: ").AppendLine(Snippet);
            if (!string.IsNullOrEmpty(Source))
                sb.Append("参考来源: ").AppendLine(Source);
            return sb.ToString();
        }

        private static void AppendLine(StringBuilder sb, string label, string value)
        {
            if (!string.IsNullOrEmpty(value))
                sb.Append(label).Append(": ").AppendLine(value);
        }

        private static string Join(List<string> items)
        {
            return items == null || items.Count == 0 ? "" : string.Join("; ", items.ToArray());
        }
    }

    internal static class UnityVersionInfo
    {
        public static string Raw
        {
            get { return Application.unityVersion; }
        }

        /// <summary>
        /// "6000.3.26f1" -> 6000003 (major * 1000 + minor). An integer-like
        /// encoding keeps exact-series specs such as "6000.3" comparable.
        /// </summary>
        public static double Numeric
        {
            get { return Encode(Raw); }
        }

        public static string Series
        {
            get
            {
                double encoded = Numeric;
                long major = (long)(encoded / 1000);
                long minor = (long)(encoded - major * 1000);
                return major.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." +
                       minor.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        private static double Encode(string text)
        {
            var parts = (text ?? "").Split('.');
            double major;
            if (parts.Length == 0 || !double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out major))
                return 6000000;
            double minor = 0;
            if (parts.Length > 1)
            {
                string minorText = parts[1];
                int cut = 0;
                while (cut < minorText.Length && char.IsDigit(minorText[cut]))
                    cut++;
                if (cut > 0)
                    double.TryParse(minorText.Substring(0, cut), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out minor);
            }
            return major * 1000 + minor;
        }

        /// <summary>
        /// Supports "any", "6000.3" (exact minor), "6000.0+" (minimum),
        /// and "6000.0-6000.2" (inclusive range).
        /// </summary>
        public static bool Matches(List<string> specs, double current)
        {
            if (specs == null || specs.Count == 0)
                return true;
            foreach (var raw in specs)
            {
                string spec = (raw ?? "").Trim();
                if (string.IsNullOrEmpty(spec) || spec.Equals("any", StringComparison.OrdinalIgnoreCase))
                    return true;
                string numbers = spec.TrimEnd('+');
                bool isMinimum = spec.EndsWith("+", StringComparison.Ordinal);
                int dash = numbers.IndexOf('-');
                if (dash > 0)
                {
                    double low = Parse(numbers.Substring(0, dash));
                    double high = Parse(numbers.Substring(dash + 1));
                    if (current >= low - 0.0001 && current <= high + 0.0001)
                        return true;
                }
                else
                {
                    double value = Parse(numbers);
                    if (isMinimum && current >= value - 0.0001)
                        return true;
                    if (!isMinimum && Math.Abs(current - value) < 0.0001)
                        return true;
                }
            }
            return false;
        }

        private static double Parse(string text)
        {
            // Specs use "6000.3" (major.minor); encode them the same way as the
            // running editor so exact-series cards actually match.
            var parts = (text ?? "").Trim().Split('.');
            double major;
            if (parts.Length == 0 || !double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out major))
                return -1;
            double minor = 0;
            if (parts.Length > 1)
                double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out minor);
            return major * 1000 + minor;
        }
    }

    internal static class SkillLibrary
    {
        private const string PackageJsonAssetPath = "Packages/com.hongjing.unity6-skill-ai/package.json";
        private const string CardPrefix = "u6skill_";

        private static List<SkillCard> _cards;
        private static string _directory = "";

        public static string CardsDirectory
        {
            get
            {
                if (_cards == null)
                    Reload();
                return _directory;
            }
        }

        public static string Diagnostics { get; private set; } = "";

        public static List<SkillCard> All
        {
            get
            {
                if (_cards == null)
                    Reload();
                return _cards;
            }
        }

        public static SkillCard Find(string id)
        {
            foreach (var card in All)
            {
                if (string.Equals(card.Id, id, StringComparison.OrdinalIgnoreCase))
                    return card;
            }
            return null;
        }

        public static void Reload()
        {
            _cards = new List<SkillCard>();
            var problems = new StringBuilder();
            _directory = ResolveCardsDirectory();
            if (string.IsNullOrEmpty(_directory))
            {
                problems.AppendLine("未找到知识卡目录，将只使用内置基础规则。");
            }
            else
            {
                string[] files;
                try { files = Directory.GetFiles(_directory, CardPrefix + "*.json", SearchOption.TopDirectoryOnly); }
                catch (Exception e)
                {
                    files = new string[0];
                    problems.AppendLine("读取知识卡目录失败: " + e.Message);
                }
                Array.Sort(files, StringComparer.Ordinal);
                foreach (var file in files)
                {
                    try
                    {
                        _cards.Add(SkillCard.FromJson(File.ReadAllText(file), file));
                    }
                    catch (Exception e)
                    {
                        problems.AppendLine("知识卡解析失败 " + Path.GetFileName(file) + ": " + e.Message);
                    }
                }
            }

            if (Find("u6-core") == null)
                _cards.Insert(0, BuildFallbackCoreCard());
            Diagnostics = problems.ToString();
            Debug.Log("[Unity6 Skill AI] 已加载 " + _cards.Count + " 张知识卡" +
                (string.IsNullOrEmpty(_directory) ? "" : "，目录: " + _directory));
        }

        public static SkillCard CoreCard
        {
            get { return Find("u6-core"); }
        }

        private static SkillCard BuildFallbackCoreCard()
        {
            var card = new SkillCard
            {
                Id = "u6-core",
                Title = "Unity 6 通用工作约定（内置兜底）",
                Summary = "只使用当前白名单内的工具；先读取工程实测信息再决定 API；不确定时先查 Console 与已有脚本；所有回答使用简体中文。",
                AlwaysInclude = true,
                Priority = 100
            };
            card.Unity.Add("any");
            card.Tools.Add("get_project_info");
            card.Tools.Add("get_scene_hierarchy");
            card.Tools.Add("get_console_logs");
            card.Tools.Add("get_compile_errors");
            card.Tools.Add("list_scripts");
            card.Tools.Add("read_script");
            card.Tools.Add("create_gameobject");
            card.Tools.Add("create_script");
            card.Deprecated.Add("Object.FindObjectOfType");
            card.Deprecated.Add("Object.FindObjectsOfType");
            card.Constraints.Add("不要臆造 Unity 6 中不存在的 API；生成 C# 后必须检查 Console 编译错误。");
            return card;
        }

        /// <summary>
        /// Works for all three installation styles: UPM git package, local
        /// Packages/ folder, and a plain folder dropped under Assets/.
        /// Card files are read from disk so immutable packages do not depend on
        /// Unity's asset importer.
        /// </summary>
        private static string ResolveCardsDirectory()
        {
            try
            {
                var info = global::UnityEditor.PackageManager.PackageInfo.FindForAssetPath(PackageJsonAssetPath);
                if (info != null && !string.IsNullOrEmpty(info.resolvedPath))
                {
                    string candidate = Path.Combine(info.resolvedPath, "Editor", "Skills", "Cards");
                    if (Directory.Exists(candidate))
                        return candidate;
                }
            }
            catch (Exception) { /* older/package-less layouts fall through */ }

            try
            {
                var guids = AssetDatabase.FindAssets("t:TextAsset " + CardPrefix);
                foreach (var guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (!Path.GetFileName(assetPath).StartsWith(CardPrefix, StringComparison.OrdinalIgnoreCase))
                        continue;
                    string absolute = Path.GetFullPath(Path.Combine(AgentSettings.ProjectRoot,
                        assetPath.Replace('/', Path.DirectorySeparatorChar)));
                    string dir = Path.GetDirectoryName(absolute);
                    if (Directory.Exists(dir))
                        return dir;
                }
            }
            catch (Exception) { /* fall through to a disk scan */ }

            var roots = new List<string> { Application.dataPath, Path.Combine(AgentSettings.ProjectRoot, "Packages") };
            foreach (var root in roots)
            {
                if (!Directory.Exists(root))
                    continue;
                try
                {
                    var found = Directory.GetFiles(root, CardPrefix + "*.json", SearchOption.AllDirectories);
                    if (found.Length > 0)
                        return Path.GetDirectoryName(found[0]);
                }
                catch (Exception) { /* unreadable folder */ }
            }
            return "";
        }
    }
}
