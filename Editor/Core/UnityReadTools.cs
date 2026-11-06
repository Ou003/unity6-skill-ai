using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Unity6SkillAI.Editor
{
    internal static class ToolArgs
    {
        public static Dictionary<string, object> Parse(string json)
        {
            try
            {
                var result = MiniJson.Obj(MiniJson.Parse(json ?? ""));
                return result ?? new Dictionary<string, object>(StringComparer.Ordinal);
            }
            catch (Exception)
            {
                return new Dictionary<string, object>(StringComparer.Ordinal);
            }
        }

        public static string S(Dictionary<string, object> args, string key, string fallback = "")
        {
            return MiniJson.Get(args, key, fallback);
        }

        public static int I(Dictionary<string, object> args, string key, int fallback)
        {
            return (int)MiniJson.GetNum(args, key, fallback);
        }

        public static float F(Dictionary<string, object> args, string key, float fallback)
        {
            return (float)MiniJson.GetNum(args, key, fallback);
        }

        public static bool B(Dictionary<string, object> args, string key, bool fallback)
        {
            return MiniJson.GetBool(args, key, fallback);
        }

        public static List<string> Strings(Dictionary<string, object> args, string key)
        {
            return MiniJson.GetStrings(args, key);
        }

        public static float[] Vec3(Dictionary<string, object> args, string key, float[] fallback)
        {
            object raw;
            if (args == null || !args.TryGetValue(key, out raw))
                return fallback;
            var list = raw as List<object>;
            if (list == null || list.Count < 3)
                return fallback;
            var result = new float[3];
            for (int i = 0; i < 3; i++)
                result[i] = ToFloat(list[i]);
            return result;
        }

        public static float[] Vec4(Dictionary<string, object> args, string key, float[] fallback)
        {
            object raw;
            if (args == null || !args.TryGetValue(key, out raw))
                return fallback;
            var list = raw as List<object>;
            if (list == null || list.Count < 3)
                return fallback;
            var result = new float[4] { 0f, 0f, 0f, 1f };
            for (int i = 0; i < Math.Min(4, list.Count); i++)
                result[i] = ToFloat(list[i]);
            return result;
        }

        private static float ToFloat(object value)
        {
            try { return (float)Convert.ToDouble(value, CultureInfo.InvariantCulture); }
            catch (Exception) { return 0f; }
        }
    }

    internal static class GameObjectLookup
    {
        public static string PathOf(Transform transform)
        {
            var stack = new List<string>();
            var current = transform;
            while (current != null)
            {
                stack.Add(current.name);
                current = current.parent;
            }
            stack.Reverse();
            return string.Join("/", stack.ToArray());
        }

        public static GameObject Find(string target)
        {
            if (string.IsNullOrEmpty(target))
                return null;
            var direct = GameObject.Find(target);
            if (direct != null)
                return direct;
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t == null || !t.gameObject.scene.IsValid())
                    continue;
                if (string.Equals(t.name, target, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(PathOf(t), target, StringComparison.OrdinalIgnoreCase))
                    return t.gameObject;
            }
            return null;
        }
    }

    /// <summary>
    /// Reads Unity's real Console history through LogEntries, which survives
    /// script recompilation and contains compile errors. Falls back to an
    /// in-memory ring buffer if Unity changes the internal API.
    /// </summary>
    internal sealed class ConsoleEntry
    {
        public string Message = "";
        public int Mode;
        public string File = "";
        public int Line;
    }

    internal static class ConsoleLogBuffer
    {
        private const int Capacity = 300;
        private static readonly List<ConsoleEntry> Memory = new List<ConsoleEntry>();

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            Memory.Add(new ConsoleEntry
            {
                Message = condition ?? "",
                Mode = type == LogType.Error ? 1 : type == LogType.Exception ? 2 : type == LogType.Warning ? 512 : 4,
                File = "",
                Line = 0
            });
            if (Memory.Count > Capacity)
                Memory.RemoveAt(0);
        }

        public static List<ConsoleEntry> ReadRecent(int count, bool errorsOnly)
        {
            if (count <= 0)
                count = 30;
            count = Math.Min(count, 200);
            var source = ReadFromEditorConsole(count) ?? Tail(Memory, count);
            var result = new List<ConsoleEntry>();
            foreach (var entry in source)
            {
                if (errorsOnly && !LooksLikeError(entry))
                    continue;
                result.Add(entry);
            }
            return result;
        }

        private static List<ConsoleEntry> Tail(List<ConsoleEntry> source, int count)
        {
            var result = new List<ConsoleEntry>();
            int start = Math.Max(0, source.Count - count);
            for (int i = start; i < source.Count; i++)
                result.Add(source[i]);
            return result;
        }

        private static bool LooksLikeError(ConsoleEntry entry)
        {
            const int errorFlags = 1 | 2 | 16 | 64 | 256 | 2048;
            if ((entry.Mode & errorFlags) != 0)
                return true;
            string message = entry.Message ?? "";
            return message.IndexOf("error CS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("error:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("Exception", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("Compilation failed", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<ConsoleEntry> ReadFromEditorConsole(int count)
        {
            try
            {
                Assembly editorAssembly = typeof(EditorWindow).Assembly;
                Type logEntries = editorAssembly.GetType("UnityEditor.LogEntries");
                Type logEntry = editorAssembly.GetType("UnityEditor.LogEntry");
                if (logEntries == null || logEntry == null)
                    return null;
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                MethodInfo getCount = logEntries.GetMethod("GetCount", flags, null, Type.EmptyTypes, null);
                MethodInfo getEntry = logEntries.GetMethod("GetEntryInternal", flags);
                if (getCount == null || getEntry == null)
                    return null;
                FieldInfo messageField = Field(logEntry, "message", "Message");
                FieldInfo modeField = Field(logEntry, "mode", "Mode");
                FieldInfo fileField = Field(logEntry, "file", "File", "conditionFile");
                FieldInfo lineField = Field(logEntry, "line", "Line");
                if (messageField == null || modeField == null)
                    return null;

                object totalObj = getCount.Invoke(null, null);
                int total = Convert.ToInt32(totalObj, CultureInfo.InvariantCulture);
                int start = Math.Max(0, total - Math.Min(count * 3, 400));
                object instance = Activator.CreateInstance(logEntry);
                var result = new List<ConsoleEntry>();
                for (int row = start; row < total; row++)
                {
                    object[] parameters = { row, instance };
                    getEntry.Invoke(null, parameters);
                    var entry = new ConsoleEntry
                    {
                        Message = Convert.ToString(messageField.GetValue(instance), CultureInfo.InvariantCulture) ?? "",
                        Mode = Convert.ToInt32(modeField.GetValue(instance), CultureInfo.InvariantCulture)
                    };
                    if (fileField != null)
                        entry.File = Convert.ToString(fileField.GetValue(instance), CultureInfo.InvariantCulture) ?? "";
                    if (lineField != null)
                        entry.Line = Convert.ToInt32(lineField.GetValue(instance), CultureInfo.InvariantCulture);
                    result.Add(entry);
                }
                return Tail(result, count);
            }
            catch (Exception)
            {
                return null; // internal API changed; use the ring buffer
            }
        }

        private static FieldInfo Field(Type type, params string[] names)
        {
            foreach (var name in names)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                    return field;
            }
            return null;
        }

        public static string Format(List<ConsoleEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return "(没有匹配的 Console 记录)";
            var sb = new StringBuilder();
            foreach (var entry in entries)
            {
                string message = (entry.Message ?? "").Replace("\r", " ").Replace("\n", " ");
                if (message.Length > 500)
                    message = message.Substring(0, 500) + "...";
                sb.Append("[mode=").Append(entry.Mode).Append("] ").Append(message);
                if (!string.IsNullOrEmpty(entry.File))
                    sb.Append(" (").Append(entry.File).Append(":").Append(entry.Line).Append(")");
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }

    /// <summary>Undo for file writes, which Unity's Undo class cannot cover.</summary>
    internal static class UndoLog
    {
        [Serializable]
        private sealed class Entry
        {
            public string path;
            public string previous;
            public bool existed;
            public string label;
        }

        [Serializable]
        private sealed class Data
        {
            public List<Entry> entries = new List<Entry>();
        }

        private static string FilePath
        {
            get { return Path.Combine(AgentSettings.LocalDataDirectory, "undo.json"); }
        }

        private static Data Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var data = JsonUtility.FromJson<Data>(File.ReadAllText(FilePath));
                    if (data != null && data.entries != null)
                        return data;
                }
            }
            catch (Exception) { /* corrupted log: start fresh */ }
            return new Data();
        }

        private static void Save(Data data)
        {
            try
            {
                AgentSettings.EnsureDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonUtility.ToJson(data, true), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Unity6 Skill AI] Failed to write undo log: " + e.Message);
            }
        }

        public static void RecordScript(string assetPath, string previousContent, bool existed)
        {
            var data = Load();
            data.entries.Add(new Entry { path = assetPath, previous = previousContent ?? "", existed = existed, label = "写入脚本 " + assetPath });
            while (data.entries.Count > 20)
                data.entries.RemoveAt(0);
            Save(data);
        }

        public static bool HasEntries
        {
            get { return Load().entries.Count > 0; }
        }

        public static string UndoLast()
        {
            var data = Load();
            if (data.entries.Count == 0)
                return "没有可撤销的脚本写入。";
            var entry = data.entries[data.entries.Count - 1];
            data.entries.RemoveAt(data.entries.Count - 1);
            try
            {
                string absolute = Path.GetFullPath(Path.Combine(AgentSettings.ProjectRoot,
                    entry.path.Replace('/', Path.DirectorySeparatorChar)));
                if (entry.existed)
                    File.WriteAllText(absolute, entry.previous, new UTF8Encoding(false));
                else if (File.Exists(absolute))
                    File.Delete(absolute);
                Save(data);
                AssetDatabase.Refresh();
                return "已撤销: " + entry.label;
            }
            catch (Exception e)
            {
                return "撤销失败: " + e.Message;
            }
        }
    }

    /// <summary>Live project facts injected into the system prompt.</summary>
    internal static class ProjectInspector
    {
        public static string BuildContextText()
        {
            var sb = new StringBuilder();
            sb.Append("Unity 版本: ").Append(UnityVersionInfo.Raw).Append(" (系列 ").Append(UnityVersionInfo.Series).AppendLine(")");
            var scene = SceneManager.GetActiveScene();
            sb.Append("当前场景: ").Append(string.IsNullOrEmpty(scene.path) ? "(未保存)" : scene.path)
                .Append("，根物体数 ").Append(scene.rootCount).AppendLine();
            sb.Append("构建目标: ").Append(EditorUserBuildSettings.activeBuildTarget).AppendLine();
            sb.Append("颜色空间: ").Append(PlayerSettings.colorSpace).AppendLine();
            var pipeline = GraphicsSettings.defaultRenderPipeline;
            sb.Append("默认渲染管线: ").Append(pipeline == null ? "Built-in" : pipeline.GetType().FullName).AppendLine();
            sb.Append("输入处理模式: ").Append(ReadInputHandling()).AppendLine();
            sb.Append("已进入播放模式: ").Append(EditorApplication.isPlaying).AppendLine();
            sb.Append("关键包: ").AppendLine(KeyPackages());
            return sb.ToString();
        }

        private static string ReadInputHandling()
        {
            try
            {
                string path = Path.Combine(AgentSettings.ProjectRoot, "ProjectSettings", "ProjectSettings.asset");
                if (!File.Exists(path))
                    return "未知";
                foreach (var line in File.ReadAllLines(path))
                {
                    string trimmed = line.Trim();
                    if (!trimmed.StartsWith("activeInputHandler:", StringComparison.Ordinal))
                        continue;
                    string value = trimmed.Substring("activeInputHandler:".Length).Trim();
                    if (value == "0") return "旧 Input Manager (0)";
                    if (value == "1") return "新 Input System (1)";
                    if (value == "2") return "Both 兼容模式 (2)";
                    return value;
                }
            }
            catch (Exception) { /* project settings unavailable */ }
            return "未知（请用 get_project_info 复核）";
        }

        private static string KeyPackages()
        {
            try
            {
                var all = global::UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
                var interesting = new List<string>();
                string[] keys = { "render-pipelines", "inputsystem", "ai.navigation", "cinemachine", "ugui",
                                  "textmeshpro", "test-framework", "addressables", "localization", "netcode",
                                  "timeline", "visualscripting", "shadergraph", "vfx", "ide." };
                foreach (var package in all)
                {
                    foreach (var key in keys)
                    {
                        if (package.name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            interesting.Add(package.name + "@" + package.version);
                            break;
                        }
                    }
                }
                return interesting.Count == 0
                    ? "(共 " + all.Length + " 个包，未检测到常见功能包)"
                    : string.Join(", ", interesting.ToArray()) + " (共 " + all.Length + " 个包)";
            }
            catch (Exception e)
            {
                return "(读取包列表失败: " + e.Message + ")";
            }
        }
    }

    internal static class UnityReadTools
    {
        public static void Register(List<ToolDefinition> list)
        {
            ToolRegistry.Add(list, "get_project_info",
                "读取当前工程的实测信息：Unity 版本、渲染管线、输入处理模式、构建目标、当前场景、已安装包。写任何代码前先调用它确认版本与包。",
                ToolRegistry.Schema(new Dictionary<string, object>()), false, GetProjectInfo);

            ToolRegistry.Add(list, "get_scene_hierarchy",
                "读取当前场景的层级树，包含每个 GameObject 的组件名。修改场景前先调用。",
                ToolRegistry.Schema(MiniJson.Dict(
                    "maxDepth", ToolRegistry.Prop("integer", "最大递归深度，默认 5"),
                    "maxNodes", ToolRegistry.Prop("integer", "最多输出节点数，默认 120"))), false, GetSceneHierarchy);

            ToolRegistry.Add(list, "get_selection",
                "读取当前在 Hierarchy/Project 中选中的对象及其组件和 Transform 数值。",
                ToolRegistry.Schema(new Dictionary<string, object>()), false, GetSelection);

            ToolRegistry.Add(list, "get_console_logs",
                "读取 Unity Console 最近若干条日志（含错误与警告）。",
                ToolRegistry.Schema(MiniJson.Dict(
                    "count", ToolRegistry.Prop("integer", "读取条数，默认 30"))), false, GetConsoleLogs);

            ToolRegistry.Add(list, "get_compile_errors",
                "只读取最近的编译错误/异常，用于生成脚本后自查。若无错误返回明确提示。",
                ToolRegistry.Schema(MiniJson.Dict(
                    "count", ToolRegistry.Prop("integer", "读取条数，默认 30"))), false, GetCompileErrors);

            ToolRegistry.Add(list, "list_scripts",
                "列出 Assets 下的 C# 脚本路径，用于了解项目已有代码。",
                ToolRegistry.Schema(MiniJson.Dict(
                    "limit", ToolRegistry.Prop("integer", "最多返回条数，默认 80"))), false, ListScripts);

            ToolRegistry.Add(list, "read_script",
                "读取指定 Assets 路径下 .cs 脚本的内容，便于在既有代码风格上修改。",
                ToolRegistry.Schema(MiniJson.Dict(
                    "path", ToolRegistry.Prop("string", "Assets/ 开头的 .cs 路径"),
                    "maxChars", ToolRegistry.Prop("integer", "最多读取字符数，默认 12000"))), false, ReadScript);

            ToolRegistry.Add(list, "find_assets",
                "按名称或类型搜索工程资源，返回资源路径列表。",
                ToolRegistry.Schema(MiniJson.Dict(
                    "filter", ToolRegistry.Prop("string", "AssetDatabase.FindAssets 过滤串，如 t:Prefab 或 t:Material 名称"),
                    "limit", ToolRegistry.Prop("integer", "最多返回条数，默认 40"))), false, FindAssets);

            ToolRegistry.Add(list, "get_packages",
                "列出全部已安装 UPM 包及版本，判断某功能是否可用。",
                ToolRegistry.Schema(new Dictionary<string, object>()), false, GetPackages);
        }

        private static ToolResult GetProjectInfo(Dictionary<string, object> args)
        {
            return ToolResult.Success(ProjectInspector.BuildContextText());
        }

        private static ToolResult GetSceneHierarchy(Dictionary<string, object> args)
        {
            int maxDepth = ToolArgs.I(args, "maxDepth", 5);
            int maxNodes = ToolArgs.I(args, "maxNodes", 120);
            maxDepth = Mathf.Clamp(maxDepth, 1, 10);
            maxNodes = Mathf.Clamp(maxNodes, 1, 400);
            var scene = SceneManager.GetActiveScene();
            var sb = new StringBuilder();
            sb.Append("场景: ").Append(string.IsNullOrEmpty(scene.path) ? "(未保存)" : scene.path).AppendLine();
            int used = 0;
            foreach (var root in scene.GetRootGameObjects())
                Walk(root.transform, 0, maxDepth, maxNodes, ref used, sb);
            if (used >= maxNodes)
                sb.AppendLine("[...节点数达到上限...]");
            return ToolResult.Success(sb.ToString());
        }

        private static void Walk(Transform node, int depth, int maxDepth, int maxNodes, ref int used, StringBuilder sb)
        {
            if (used >= maxNodes || node == null)
                return;
            used++;
            sb.Append(new string(' ', depth * 2)).Append("- ").Append(node.name)
                .Append(node.gameObject.activeSelf ? "" : " [inactive]");
            var components = node.GetComponents<Component>();
            var names = new List<string>();
            foreach (var component in components)
            {
                if (component == null)
                    names.Add("Missing Script");
                else
                    names.Add(component.GetType().Name);
            }
            if (names.Count > 0)
                sb.Append(" (").Append(string.Join(", ", names.ToArray())).Append(")");
            sb.AppendLine();
            if (depth >= maxDepth)
                return;
            foreach (Transform child in node)
                Walk(child, depth + 1, maxDepth, maxNodes, ref used, sb);
        }

        private static ToolResult GetSelection(Dictionary<string, object> args)
        {
            var go = Selection.activeGameObject;
            var sb = new StringBuilder();
            if (go == null)
            {
                sb.AppendLine("当前没有选中的 GameObject。");
                if (Selection.activeObject != null)
                    sb.Append("选中的资源: ").AppendLine(AssetDatabase.GetAssetPath(Selection.activeObject));
                return ToolResult.Success(sb.ToString());
            }
            sb.Append("选中对象: ").AppendLine(GameObjectLookup.PathOf(go.transform));
            sb.Append("Tag: ").Append(go.tag).Append(" / Layer: ").AppendLine(LayerMask.LayerToName(go.layer));
            sb.Append("Transform: pos=").Append(go.transform.localPosition)
                .Append(" rot=").Append(go.transform.localEulerAngles)
                .Append(" scale=").AppendLine(go.transform.localScale.ToString());
            foreach (var component in go.GetComponents<Component>())
                sb.Append("组件: ").AppendLine(component == null ? "Missing Script" : component.GetType().FullName);
            return ToolResult.Success(sb.ToString());
        }

        private static ToolResult GetConsoleLogs(Dictionary<string, object> args)
        {
            int count = ToolArgs.I(args, "count", 30);
            return ToolResult.Success(ConsoleLogBuffer.Format(ConsoleLogBuffer.ReadRecent(count, false)));
        }

        private static ToolResult GetCompileErrors(Dictionary<string, object> args)
        {
            int count = ToolArgs.I(args, "count", 30);
            var entries = ConsoleLogBuffer.ReadRecent(count, true);
            if (entries.Count == 0)
                return ToolResult.Success("未发现编译错误或异常。（注意：若刚写入脚本，Unity 可能仍在编译，请稍后再查。）");
            return ToolResult.Success("发现 " + entries.Count + " 条错误/异常:\n" + ConsoleLogBuffer.Format(entries));
        }

        private static ToolResult ListScripts(Dictionary<string, object> args)
        {
            int limit = Mathf.Clamp(ToolArgs.I(args, "limit", 80), 1, 400);
            var guids = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" });
            var sb = new StringBuilder();
            sb.Append("共 ").Append(guids.Length).Append(" 个脚本，列出前 ").Append(Math.Min(limit, guids.Length)).AppendLine(" 个:");
            for (int i = 0; i < Math.Min(limit, guids.Length); i++)
                sb.AppendLine(AssetDatabase.GUIDToAssetPath(guids[i]));
            return ToolResult.Success(sb.ToString());
        }

        private static ToolResult ReadScript(Dictionary<string, object> args)
        {
            string path = ToolArgs.S(args, "path");
            string absolute;
            string error;
            if (!ValidateAssetPath(path, ".cs", out absolute, out error))
                return ToolResult.Failure(error);
            if (!File.Exists(absolute))
                return ToolResult.Failure("脚本不存在: " + path);
            int maxChars = Mathf.Clamp(ToolArgs.I(args, "maxChars", 12000), 200, 60000);
            string content = File.ReadAllText(absolute);
            bool truncated = content.Length > maxChars;
            if (truncated)
                content = content.Substring(0, maxChars);
            return ToolResult.Success("文件 " + path + (truncated ? " (已截断)" : "") + ":\n" + content);
        }

        private static ToolResult FindAssets(Dictionary<string, object> args)
        {
            string filter = ToolArgs.S(args, "filter");
            int limit = Mathf.Clamp(ToolArgs.I(args, "limit", 40), 1, 200);
            var guids = AssetDatabase.FindAssets(filter);
            var sb = new StringBuilder();
            sb.Append("过滤条件 \"").Append(filter).Append("\" 命中 ").Append(guids.Length).AppendLine(" 项:");
            for (int i = 0; i < Math.Min(limit, guids.Length); i++)
                sb.AppendLine(AssetDatabase.GUIDToAssetPath(guids[i]));
            return ToolResult.Success(sb.ToString());
        }

        private static ToolResult GetPackages(Dictionary<string, object> args)
        {
            try
            {
                var packages = global::UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
                var sb = new StringBuilder();
                sb.Append("已安装 ").Append(packages.Length).AppendLine(" 个包:");
                foreach (var package in packages)
                    sb.Append(package.name).Append("@").AppendLine(package.version);
                return ToolResult.Success(sb.ToString());
            }
            catch (Exception e)
            {
                return ToolResult.Failure("读取包列表失败: " + e.Message);
            }
        }

        /// <summary>Shared path guard for every file-touching tool.</summary>
        internal static bool ValidateAssetPath(string assetPath, string requiredExtension,
            out string absolute, out string error)
        {
            absolute = "";
            error = "";
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                error = "路径必须以 Assets/ 开头: " + assetPath;
                return false;
            }
            if (assetPath.IndexOf("..", StringComparison.Ordinal) >= 0 || assetPath.IndexOf('\\') >= 0)
            {
                error = "路径包含非法字符: " + assetPath;
                return false;
            }
            if (!string.IsNullOrEmpty(requiredExtension) &&
                !assetPath.EndsWith(requiredExtension, StringComparison.OrdinalIgnoreCase))
            {
                error = "路径必须以 " + requiredExtension + " 结尾: " + assetPath;
                return false;
            }
            absolute = Path.GetFullPath(Path.Combine(AgentSettings.ProjectRoot,
                assetPath.Replace('/', Path.DirectorySeparatorChar)));
            string root = Path.GetFullPath(AgentSettings.ProjectRoot + Path.DirectorySeparatorChar + "Assets");
            if (!absolute.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                error = "路径越出 Assets 目录: " + assetPath;
                return false;
            }
            return true;
        }
    }
}
