using System;
using System.Collections.Generic;

namespace Unity6SkillAI.Editor
{
    internal sealed class ToolResult
    {
        public bool Ok;
        public string Content = "";
        public bool MayTriggerReload;

        public static ToolResult Success(string content, bool mayTriggerReload = false)
        {
            return new ToolResult { Ok = true, Content = content ?? "", MayTriggerReload = mayTriggerReload };
        }

        public static ToolResult Failure(string content)
        {
            return new ToolResult { Ok = false, Content = content ?? "" };
        }

        public string AsToolMessage()
        {
            return (Ok ? "" : "[工具执行失败] ") + Content;
        }
    }

    internal sealed class ToolDefinition
    {
        public string Name;
        public string Description;
        public string ParametersJson;
        public bool IsWrite;
        public Func<Dictionary<string, object>, ToolResult> Execute;
    }

    internal static class ToolRegistry
    {
        private static List<ToolDefinition> _all;
        private static Dictionary<string, ToolDefinition> _byName;

        public static List<ToolDefinition> All
        {
            get
            {
                if (_all == null)
                    Build();
                return _all;
            }
        }

        public static ToolDefinition Get(string name)
        {
            if (_byName == null)
                Build();
            ToolDefinition definition;
            return _byName.TryGetValue(name ?? "", out definition) ? definition : null;
        }

        public static bool IsWrite(string name)
        {
            var definition = Get(name);
            return definition != null && definition.IsWrite;
        }

        /// <summary>Tool menus exposed to the model, restricted by the router whitelist.</summary>
        public static List<ToolSpec> SpecsFor(List<string> whitelist)
        {
            var result = new List<ToolSpec>();
            if (whitelist == null)
                return result;
            foreach (var definition in All)
            {
                if (!whitelist.Contains(definition.Name))
                    continue;
                result.Add(new ToolSpec
                {
                    Name = definition.Name,
                    Description = definition.Description,
                    ParametersJson = definition.ParametersJson
                });
            }
            return result;
        }

        public static string Schema(Dictionary<string, object> properties, params string[] required)
        {
            var requiredList = new List<object>();
            if (required != null)
            {
                foreach (var name in required)
                    requiredList.Add(name);
            }
            return MiniJson.Serialize(MiniJson.Dict(
                "type", "object",
                "properties", properties,
                "required", requiredList));
        }

        public static Dictionary<string, object> Prop(string type, string description)
        {
            return MiniJson.Dict("type", type, "description", description);
        }

        public static Dictionary<string, object> ArrayProp(string itemType, string description)
        {
            return MiniJson.Dict("type", "array", "description", description,
                "items", MiniJson.Dict("type", itemType));
        }

        private static void Build()
        {
            _all = new List<ToolDefinition>();
            UnityReadTools.Register(_all);
            UnityWriteTools.Register(_all);
            _byName = new Dictionary<string, ToolDefinition>(StringComparer.Ordinal);
            foreach (var definition in _all)
                _byName[definition.Name] = definition;
        }

        internal static void Add(List<ToolDefinition> list, string name, string description,
            string parametersJson, bool isWrite, Func<Dictionary<string, object>, ToolResult> execute)
        {
            list.Add(new ToolDefinition
            {
                Name = name,
                Description = description,
                ParametersJson = parametersJson,
                IsWrite = isWrite,
                Execute = execute
            });
        }
    }
}
