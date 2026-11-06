using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Unity6SkillAI.Editor
{
    internal sealed class ToolSpec
    {
        public string Name;
        public string Description;
        public string ParametersJson;
    }

    internal sealed class ToolCall
    {
        public string Id;
        public string Name;
        public string ArgumentsJson;
    }

    internal sealed class ChatMessage
    {
        public string Role; // user | assistant | tool
        public string Content;
        public List<ToolCall> ToolCalls;
        public string ToolCallId;
        public string ToolName;

        public static ChatMessage User(string content)
        {
            return new ChatMessage { Role = "user", Content = content ?? "" };
        }

        public static ChatMessage Assistant(string content, List<ToolCall> calls)
        {
            return new ChatMessage { Role = "assistant", Content = content ?? "", ToolCalls = calls };
        }

        public static ChatMessage Tool(string id, string name, string content)
        {
            return new ChatMessage { Role = "tool", ToolCallId = id, ToolName = name, Content = content ?? "" };
        }
    }

    internal sealed class LlmRequest
    {
        public string SystemPrompt = "";
        public readonly List<ChatMessage> Messages = new List<ChatMessage>();
        public readonly List<ToolSpec> Tools = new List<ToolSpec>();
        public int MaxTokens = 2048;
        public double Temperature = 0.2;
        public bool TextOnly; // used by the connectivity check
    }

    internal sealed class LlmResult
    {
        public bool Ok;
        public string Error = "";
        public string Text = "";
        public readonly List<ToolCall> ToolCalls = new List<ToolCall>();
        public string Raw = "";
    }

    /// <summary>
    /// One HTTP client for three wire formats. Everything runs on the Editor
    /// main thread via UnityWebRequest + EditorApplication.update polling, so
    /// there is no Task/async state to lose during a domain reload, and no
    /// external Python/Node process to install.
    /// </summary>
    internal sealed class LlmClient
    {
        private const int TimeoutSeconds = 120;

        private UnityWebRequest _active;
        private EditorApplication.CallbackFunction _poll;

        public bool IsBusy { get { return _active != null; } }

        public void Cancel()
        {
            if (_poll != null)
            {
                EditorApplication.update -= _poll;
                _poll = null;
            }
            if (_active != null)
            {
                try { _active.Abort(); _active.Dispose(); }
                catch (Exception) { /* already disposed */ }
                _active = null;
            }
        }

        public void Send(LlmRequest request, Action<LlmResult> callback)
        {
            if (callback == null)
                return;
            Cancel();

            var settings = AgentSettings.Current;
            string key = AgentSettings.GetApiKey();
            if (string.IsNullOrEmpty(key))
            {
                callback(new LlmResult { Ok = false, Error = "未配置 API Key。请在设置页填写，或设置环境变量 " + AgentSettings.ApiKeyEnvironmentVariable + "。" });
                return;
            }
            if (string.IsNullOrEmpty(settings.baseUrl) || string.IsNullOrEmpty(settings.model))
            {
                callback(new LlmResult { Ok = false, Error = "Base URL 或模型名为空，请在设置页补齐。" });
                return;
            }

            string url;
            string body;
            var headers = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                BuildRequest(request, settings, key, out url, out body, headers);
            }
            catch (Exception e)
            {
                callback(new LlmResult { Ok = false, Error = "构建请求失败: " + e.Message });
                return;
            }

            var www = new UnityWebRequest(url, "POST");
            www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            www.downloadHandler = new DownloadHandlerBuffer();
            www.timeout = TimeoutSeconds;
            www.SetRequestHeader("Content-Type", "application/json");
            www.SetRequestHeader("Accept", "application/json");
            foreach (var pair in headers)
                www.SetRequestHeader(pair.Key, pair.Value);

            _active = www;
            try
            {
                www.SendWebRequest();
            }
            catch (Exception e)
            {
                Cancel();
                callback(new LlmResult { Ok = false, Error = "发起请求失败: " + e.Message });
                return;
            }

            _poll = () =>
            {
                if (_active == null || !_active.isDone)
                    return;
                var finished = _active;
                EditorApplication.update -= _poll;
                _poll = null;
                _active = null;
                string text = finished.downloadHandler != null ? finished.downloadHandler.text : "";
                bool transportOk = finished.result == UnityWebRequest.Result.Success;
                string transportError = finished.error;
                finished.Dispose();

                var result = new LlmResult { Raw = text ?? "" };
                if (!transportOk)
                {
                    result.Ok = false;
                    result.Error = "HTTP/网络错误: " + transportError + ExtractServerError(text);
                    callback(result);
                    return;
                }
                try
                {
                    ParseResponse((ProviderKind)settings.provider, text, result);
                }
                catch (Exception e)
                {
                    result.Ok = false;
                    result.Error = "响应解析失败: " + e.Message + "\n原始响应(前 800 字符):\n" + Truncate(text, 800);
                }
                callback(result);
            };
            EditorApplication.update += _poll;
        }

        private static string ExtractServerError(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            try
            {
                var root = MiniJson.Obj(MiniJson.Parse(text));
                if (root == null)
                    return "";
                object err;
                if (!root.TryGetValue("error", out err))
                    return "";
                var errObj = MiniJson.Obj(err);
                string message = errObj != null ? MiniJson.Get(errObj, "message") : MiniJson.Str(err);
                return string.IsNullOrEmpty(message) ? "" : "\n服务端返回: " + message;
            }
            catch (Exception)
            {
                return "\n原始响应(前 300 字符): " + Truncate(text, 300);
            }
        }

        private static void BuildRequest(LlmRequest request, AgentSettingsData settings, string key,
            out string url, out string body, Dictionary<string, string> headers)
        {
            var kind = (ProviderKind)settings.provider;
            string baseUrl = (settings.baseUrl ?? "").Trim().TrimEnd('/');
            if (kind == ProviderKind.OpenAICompatible)
            {
                headers["Authorization"] = "Bearer " + key;
                url = baseUrl.EndsWith("/chat/completions", StringComparison.Ordinal)
                    ? baseUrl
                    : baseUrl + "/chat/completions";
                body = MiniJson.Serialize(BuildOpenAiBody(request, settings));
            }
            else if (kind == ProviderKind.Anthropic)
            {
                headers["x-api-key"] = key;
                headers["anthropic-version"] = "2023-06-01";
                url = baseUrl.EndsWith("/v1/messages", StringComparison.Ordinal)
                    ? baseUrl
                    : baseUrl + "/v1/messages";
                body = MiniJson.Serialize(BuildAnthropicBody(request, settings));
            }
            else
            {
                headers["x-goog-api-key"] = key;
                url = baseUrl + "/models/" + settings.model.Trim() + ":generateContent";
                body = MiniJson.Serialize(BuildGeminiBody(request, settings));
            }
        }

        private static Dictionary<string, object> BuildOpenAiBody(LlmRequest request, AgentSettingsData settings)
        {
            var messages = new List<object>();
            if (!string.IsNullOrEmpty(request.SystemPrompt))
                messages.Add(MiniJson.Dict("role", "system", "content", request.SystemPrompt));
            foreach (var m in request.Messages)
            {
                if (m.Role == "user")
                {
                    messages.Add(MiniJson.Dict("role", "user", "content", m.Content ?? ""));
                }
                else if (m.Role == "tool")
                {
                    messages.Add(MiniJson.Dict("role", "tool", "tool_call_id", m.ToolCallId ?? "", "content", m.Content ?? ""));
                }
                else
                {
                    var msg = MiniJson.Dict("role", "assistant", "content", m.Content ?? "");
                    if (m.ToolCalls != null && m.ToolCalls.Count > 0)
                    {
                        var calls = new List<object>();
                        foreach (var call in m.ToolCalls)
                        {
                            calls.Add(MiniJson.Dict(
                                "id", call.Id ?? "",
                                "type", "function",
                                "function", MiniJson.Dict("name", call.Name ?? "", "arguments", call.ArgumentsJson ?? "{}")));
                        }
                        msg["tool_calls"] = calls;
                    }
                    messages.Add(msg);
                }
            }

            var body = MiniJson.Dict(
                "model", settings.model.Trim(),
                "messages", messages,
                "temperature", request.Temperature,
                "max_tokens", request.TextOnly ? 16 : request.MaxTokens,
                "stream", false);
            if (!request.TextOnly && request.Tools.Count > 0)
            {
                var tools = new List<object>();
                foreach (var t in request.Tools)
                {
                    tools.Add(MiniJson.Dict(
                        "type", "function",
                        "function", MiniJson.Dict("name", t.Name, "description", t.Description, "parameters", ParseSchema(t.ParametersJson))));
                }
                body["tools"] = tools;
                body["tool_choice"] = "auto";
            }
            return body;
        }

        private static Dictionary<string, object> BuildAnthropicBody(LlmRequest request, AgentSettingsData settings)
        {
            var messages = new List<object>();
            var pendingToolResults = new List<object>();
            for (int i = 0; i < request.Messages.Count; i++)
            {
                var m = request.Messages[i];
                if (m.Role == "tool")
                {
                    pendingToolResults.Add(MiniJson.Dict(
                        "type", "tool_result",
                        "tool_use_id", m.ToolCallId ?? "",
                        "content", m.Content ?? ""));
                    bool hasNextTool = i + 1 < request.Messages.Count && request.Messages[i + 1].Role == "tool";
                    if (!hasNextTool)
                    {
                        messages.Add(MiniJson.Dict("role", "user", "content", pendingToolResults));
                        pendingToolResults = new List<object>();
                    }
                    continue;
                }
                if (m.Role == "user")
                {
                    messages.Add(MiniJson.Dict("role", "user", "content", m.Content ?? ""));
                    continue;
                }
                var blocks = new List<object>();
                if (!string.IsNullOrEmpty(m.Content))
                    blocks.Add(MiniJson.Dict("type", "text", "text", m.Content));
                if (m.ToolCalls != null)
                {
                    foreach (var call in m.ToolCalls)
                    {
                        blocks.Add(MiniJson.Dict(
                            "type", "tool_use",
                            "id", call.Id ?? "",
                            "name", call.Name ?? "",
                            "input", ParseSchema(call.ArgumentsJson)));
                    }
                }
                if (blocks.Count > 0)
                    messages.Add(MiniJson.Dict("role", "assistant", "content", blocks));
            }

            var body = MiniJson.Dict(
                "model", settings.model.Trim(),
                "max_tokens", request.TextOnly ? 16 : Math.Max(256, request.MaxTokens),
                "temperature", request.Temperature,
                "messages", messages);
            if (!string.IsNullOrEmpty(request.SystemPrompt))
                body["system"] = request.SystemPrompt;
            if (!request.TextOnly && request.Tools.Count > 0)
            {
                var tools = new List<object>();
                foreach (var t in request.Tools)
                    tools.Add(MiniJson.Dict("name", t.Name, "description", t.Description, "input_schema", ParseSchema(t.ParametersJson)));
                body["tools"] = tools;
            }
            return body;
        }

        private static Dictionary<string, object> BuildGeminiBody(LlmRequest request, AgentSettingsData settings)
        {
            var contents = new List<object>();
            foreach (var m in request.Messages)
            {
                if (m.Role == "user")
                {
                    contents.Add(MiniJson.Dict("role", "user", "parts", MiniJson.List(MiniJson.Dict("text", m.Content ?? ""))));
                }
                else if (m.Role == "assistant")
                {
                    var parts = new List<object>();
                    if (!string.IsNullOrEmpty(m.Content))
                        parts.Add(MiniJson.Dict("text", m.Content));
                    if (m.ToolCalls != null)
                    {
                        foreach (var call in m.ToolCalls)
                        {
                            parts.Add(MiniJson.Dict("functionCall", MiniJson.Dict(
                                "name", call.Name ?? "",
                                "args", ParseSchema(call.ArgumentsJson))));
                        }
                    }
                    contents.Add(MiniJson.Dict("role", "model", "parts", parts));
                }
                else
                {
                    contents.Add(MiniJson.Dict("role", "user", "parts", MiniJson.List(MiniJson.Dict(
                        "functionResponse", MiniJson.Dict(
                            "name", m.ToolName ?? "function",
                            "response", MiniJson.Dict("content", m.Content ?? ""))))));
                }
            }

            var body = MiniJson.Dict(
                "contents", contents,
                "generationConfig", MiniJson.Dict(
                    "temperature", request.Temperature,
                    "maxOutputTokens", request.TextOnly ? 16 : request.MaxTokens));
            if (!string.IsNullOrEmpty(request.SystemPrompt))
                body["systemInstruction"] = MiniJson.Dict("parts", MiniJson.List(MiniJson.Dict("text", request.SystemPrompt)));
            if (!request.TextOnly && request.Tools.Count > 0)
            {
                var declarations = new List<object>();
                foreach (var t in request.Tools)
                    declarations.Add(MiniJson.Dict("name", t.Name, "description", t.Description, "parameters", ParseSchema(t.ParametersJson)));
                body["tools"] = MiniJson.List(MiniJson.Dict("functionDeclarations", declarations));
            }
            return body;
        }

        private static object ParseSchema(string json)
        {
            if (string.IsNullOrEmpty(json))
                return MiniJson.Dict("type", "object", "properties", new Dictionary<string, object>());
            try
            {
                object parsed = MiniJson.Parse(json);
                if (parsed != null)
                    return parsed;
            }
            catch (Exception) { /* fall through to the safe empty schema */ }
            return MiniJson.Dict("type", "object", "properties", new Dictionary<string, object>());
        }

        private static void ParseResponse(ProviderKind kind, string text, LlmResult result)
        {
            if (string.IsNullOrEmpty(text))
            {
                result.Ok = false;
                result.Error = "服务端返回空响应。";
                return;
            }
            object parsed;
            try { parsed = MiniJson.Parse(text); }
            catch (Exception e)
            {
                result.Ok = false;
                result.Error = "响应不是合法 JSON（常见于中转站返回了 HTML 错误页）: " + e.Message
                    + "\n原始响应(前 800 字符):\n" + Truncate(text, 800);
                return;
            }
            var root = MiniJson.Obj(parsed);
            if (root == null)
            {
                result.Ok = false;
                result.Error = "响应顶层不是对象。原始响应(前 500 字符):\n" + Truncate(text, 500);
                return;
            }

            object errorValue;
            if (root.TryGetValue("error", out errorValue) && errorValue != null)
            {
                var errObj = MiniJson.Obj(errorValue);
                string message = errObj != null ? MiniJson.Get(errObj, "message") : MiniJson.Str(errorValue);
                result.Ok = false;
                result.Error = "API 返回错误: " + (string.IsNullOrEmpty(message) ? MiniJson.Str(errorValue) : message);
                return;
            }

            if (kind == ProviderKind.OpenAICompatible)
                ParseOpenAiResponse(root, result);
            else if (kind == ProviderKind.Anthropic)
                ParseAnthropicResponse(root, result);
            else
                ParseGeminiResponse(root, result);

            if (!result.Ok && string.IsNullOrEmpty(result.Error))
                result.Error = "响应中未找到可解析的文本或工具调用。原始响应(前 500 字符):\n" + Truncate(text, 500);
        }

        private static void ParseOpenAiResponse(Dictionary<string, object> root, LlmResult result)
        {
            var choices = MiniJson.Arr(TryGet(root, "choices"));
            if (choices == null || choices.Count == 0)
                return;
            var message = MiniJson.Obj(MiniJson.Obj(choices[0]) != null ? TryGet(MiniJson.Obj(choices[0]), "message") : null);
            if (message == null)
                return;
            result.Ok = true;
            result.Text = MiniJson.Get(message, "content");
            var calls = MiniJson.Arr(TryGet(message, "tool_calls"));
            if (calls != null)
            {
                foreach (var raw in calls)
                {
                    var call = MiniJson.Obj(raw);
                    if (call == null)
                        continue;
                    var function = MiniJson.Obj(TryGet(call, "function"));
                    string name = function != null ? MiniJson.Get(function, "name") : MiniJson.Get(call, "name");
                    object arguments = function != null ? TryGet(function, "arguments") : TryGet(call, "arguments");
                    string argsJson = NormalizeArguments(arguments);
                    result.ToolCalls.Add(new ToolCall
                    {
                        Id = MiniJson.Get(call, "id", "call_" + result.ToolCalls.Count),
                        Name = name,
                        ArgumentsJson = argsJson
                    });
                }
            }
        }

        private static void ParseAnthropicResponse(Dictionary<string, object> root, LlmResult result)
        {
            var content = MiniJson.Arr(TryGet(root, "content"));
            if (content == null)
                return;
            result.Ok = true;
            var sb = new StringBuilder();
            foreach (var raw in content)
            {
                var block = MiniJson.Obj(raw);
                if (block == null)
                    continue;
                string type = MiniJson.Get(block, "type");
                if (type == "text")
                {
                    sb.Append(MiniJson.Get(block, "text"));
                }
                else if (type == "tool_use")
                {
                    result.ToolCalls.Add(new ToolCall
                    {
                        Id = MiniJson.Get(block, "id", "toolu_" + result.ToolCalls.Count),
                        Name = MiniJson.Get(block, "name"),
                        ArgumentsJson = NormalizeArguments(TryGet(block, "input"))
                    });
                }
            }
            result.Text = sb.ToString();
        }

        private static void ParseGeminiResponse(Dictionary<string, object> root, LlmResult result)
        {
            var candidates = MiniJson.Arr(TryGet(root, "candidates"));
            if (candidates == null || candidates.Count == 0)
                return;
            var candidate = MiniJson.Obj(candidates[0]);
            var content = MiniJson.Obj(TryGet(candidate, "content"));
            if (content == null)
                return;
            var parts = MiniJson.Arr(TryGet(content, "parts"));
            if (parts == null)
                return;
            result.Ok = true;
            var sb = new StringBuilder();
            foreach (var raw in parts)
            {
                var part = MiniJson.Obj(raw);
                if (part == null)
                    continue;
                object textValue = TryGet(part, "text");
                if (textValue != null)
                    sb.Append(MiniJson.Str(textValue));
                var functionCall = MiniJson.Obj(TryGet(part, "functionCall"));
                if (functionCall != null)
                {
                    result.ToolCalls.Add(new ToolCall
                    {
                        Id = "gemini_" + result.ToolCalls.Count,
                        Name = MiniJson.Get(functionCall, "name"),
                        ArgumentsJson = NormalizeArguments(TryGet(functionCall, "args"))
                    });
                }
            }
            result.Text = sb.ToString();
        }

        private static string NormalizeArguments(object value)
        {
            if (value == null)
                return "{}";
            if (value is string)
            {
                string s = (string)value;
                return string.IsNullOrEmpty(s) ? "{}" : s;
            }
            return MiniJson.Serialize(value);
        }

        private static object TryGet(Dictionary<string, object> dict, string key)
        {
            object value;
            if (dict != null && dict.TryGetValue(key, out value))
                return value;
            return null;
        }

        internal static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            return text.Length <= max ? text : text.Substring(0, max) + "...";
        }
    }
}
