using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Unity6SkillAI.Editor
{
    public enum ProviderKind
    {
        OpenAICompatible = 0,
        Anthropic = 1,
        Gemini = 2
    }

    internal sealed class ProviderPreset
    {
        public readonly string Label;
        public readonly ProviderKind Kind;
        public readonly string BaseUrl;
        public readonly string Model;

        public ProviderPreset(string label, ProviderKind kind, string baseUrl, string model)
        {
            Label = label;
            Kind = kind;
            BaseUrl = baseUrl;
            Model = model;
        }
    }

    internal static class ProviderPresets
    {
        // Model identifiers change over time; these are editable defaults, not
        // a hard-coded list. Users can type any model name their provider offers.
        public static readonly ProviderPreset[] Items =
        {
            new ProviderPreset("OpenAI", ProviderKind.OpenAICompatible, "https://api.openai.com/v1", "gpt-4o-mini"),
            new ProviderPreset("DeepSeek", ProviderKind.OpenAICompatible, "https://api.deepseek.com/v1", "deepseek-chat"),
            new ProviderPreset("SiliconFlow / 硅基流动", ProviderKind.OpenAICompatible, "https://api.siliconflow.cn/v1", "deepseek-ai/DeepSeek-V3"),
            new ProviderPreset("Moonshot / 月之暗面", ProviderKind.OpenAICompatible, "https://api.moonshot.cn/v1", "moonshot-v1-8k"),
            new ProviderPreset("Anthropic", ProviderKind.Anthropic, "https://api.anthropic.com", "claude-3-5-sonnet-latest"),
            new ProviderPreset("Google Gemini", ProviderKind.Gemini, "https://generativelanguage.googleapis.com/v1beta", "gemini-2.0-flash"),
            new ProviderPreset("自定义 OpenAI 兼容中转", ProviderKind.OpenAICompatible, "", "")
        };
    }

    [Serializable]
    internal sealed class AgentSettingsData
    {
        public int provider = (int)ProviderKind.OpenAICompatible;
        public string baseUrl = "https://api.openai.com/v1";
        public string model = "gpt-4o-mini";
        public double temperature = 0.2;
        public int maxTokens = 2048;
        public int maxToolRounds = 6;
        public bool requireApprovalForWrite = true;
        public bool autoInjectProjectContext = true;
        public bool showRawToolResults = false;
        public string extraSystemPrompt = "";
        public int presetIndex = 0;
        public bool enableOfficialSkills = true;
        public bool officialSkillsIncludeAllTiers = false;
        public int officialSkillsMaxCount = 1;
        public int officialSkillsMaxChars = 3000;
    }

    [Serializable]
    internal sealed class SecretsData
    {
        public string apiKey = "";
    }

    /// <summary>
    /// Settings live in UserSettings/ and secrets in Library/, both outside
    /// version control. An environment variable always wins over the stored key
    /// so CI and shared machines never need to persist a key on disk.
    /// </summary>
    internal static class AgentSettings
    {
        public const string ApiKeyEnvironmentVariable = "UNITY6_SKILL_AI_API_KEY";

        private static AgentSettingsData _current;

        public static AgentSettingsData Current
        {
            get
            {
                if (_current == null)
                    Load();
                return _current;
            }
        }

        public static string ProjectRoot
        {
            get
            {
                // Application.dataPath is <project>/Assets in the Editor.
                return Directory.GetParent(Application.dataPath).FullName;
            }
        }

        private static string SettingsPath
        {
            get { return Path.Combine(ProjectRoot, "UserSettings", "Unity6SkillAI", "settings.json"); }
        }

        private static string SecretsPath
        {
            get { return Path.Combine(ProjectRoot, "Library", "Unity6SkillAI", "secrets.json"); }
        }

        public static string LocalDataDirectory
        {
            get { return Path.Combine(ProjectRoot, "Library", "Unity6SkillAI"); }
        }

        public static void Load()
        {
            _current = new AgentSettingsData();
            try
            {
                if (File.Exists(SettingsPath))
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(SettingsPath), _current);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Unity6 Skill AI] Failed to read settings: " + e.Message);
            }
        }

        public static void Save()
        {
            try
            {
                EnsureDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllText(SettingsPath, JsonUtility.ToJson(_current, true), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Unity6 Skill AI] Failed to save settings: " + e.Message);
            }
        }

        public static string GetApiKey()
        {
            string fromEnv = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
            if (!string.IsNullOrEmpty(fromEnv))
                return fromEnv.Trim();
            try
            {
                if (File.Exists(SecretsPath))
                {
                    var data = JsonUtility.FromJson<SecretsData>(File.ReadAllText(SecretsPath));
                    if (data != null && !string.IsNullOrEmpty(data.apiKey))
                        return data.apiKey;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Unity6 Skill AI] Failed to read local key: " + e.Message);
            }
            return "";
        }

        public static bool HasApiKey()
        {
            return !string.IsNullOrEmpty(GetApiKey());
        }

        public static bool ApiKeyComesFromEnvironment()
        {
            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable));
        }

        public static void SetApiKey(string key)
        {
            try
            {
                EnsureDirectory(Path.GetDirectoryName(SecretsPath));
                var data = new SecretsData { apiKey = key ?? "" };
                File.WriteAllText(SecretsPath, JsonUtility.ToJson(data, true), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Unity6 Skill AI] Failed to store local key: " + e.Message);
            }
        }

        public static void ApplyPreset(int index)
        {
            if (index < 0 || index >= ProviderPresets.Items.Length)
                return;
            var preset = ProviderPresets.Items[index];
            _current.presetIndex = index;
            _current.provider = (int)preset.Kind;
            if (!string.IsNullOrEmpty(preset.BaseUrl))
                _current.baseUrl = preset.BaseUrl;
            if (!string.IsNullOrEmpty(preset.Model))
                _current.model = preset.Model;
            Save();
        }

        public static void EnsureDirectory(string path)
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }
    }
}
