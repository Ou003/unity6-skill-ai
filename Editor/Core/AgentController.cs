using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Unity6SkillAI.Editor
{
    internal enum EntryKind
    {
        User = 0,
        Assistant = 1,
        Tool = 2,
        Status = 3,
        Error = 4,
        Info = 5
    }

    internal sealed class ChatEntry
    {
        public EntryKind Kind;
        public string Title = "";
        public string Text = "";
        public string Time = "";

        public static ChatEntry Make(EntryKind kind, string title, string text)
        {
            return new ChatEntry
            {
                Kind = kind,
                Title = title ?? "",
                Text = text ?? "",
                Time = DateTime.Now.ToString("HH:mm:ss")
            };
        }
    }

    internal sealed class PendingApproval
    {
        public ToolCall Call;
        public string Preview = "";
        public bool IsWrite;
    }

    /// <summary>
    /// Owns the conversation, the tool-calling loop and the approval gate.
    /// Chat history is mirrored into SessionState so a script-compilation
    /// domain reload does not wipe what the user was doing.
    /// </summary>
    internal sealed class AgentController
    {
        private const string SessionKey = "Unity6SkillAI.Session.v1";
        private const int MaxEntries = 60;
        private const int MaxMessages = 40;
        private const int MaxEntryChars = 20000;

        public readonly List<ChatEntry> Entries = new List<ChatEntry>();
        public readonly List<ChatMessage> Messages = new List<ChatMessage>();

        public bool Busy;
        public bool WaitingForCompileCheck;
        public PendingApproval Pending;
        public RouteResult LastRoute;
        public string ConnectionStatus = "尚未测试";

        public event Action Changed;

        private readonly LlmClient _client = new LlmClient();
        private Queue<ToolCall> _pendingCalls;
        private int _round;

        private void Notify()
        {
            var handler = Changed;
            if (handler != null)
                handler();
            SaveSession();
        }

        private void Add(EntryKind kind, string title, string text)
        {
            Entries.Add(ChatEntry.Make(kind, title, text));
            while (Entries.Count > MaxEntries)
                Entries.RemoveAt(0);
            Notify();
        }

        public void Submit(string userText)
        {
            if (Busy || Pending != null || string.IsNullOrEmpty(userText) || string.IsNullOrEmpty(userText.Trim()))
                return;
            Add(EntryKind.User, "你", userText.Trim());
            Messages.Add(ChatMessage.User(userText.Trim()));
            TrimMessages();
            LastRoute = SkillRouter.Route(userText);
            Add(EntryKind.Info, "路由", "命中知识卡: " + LastRoute.CardIds +
                "\n命中官方技能: " + (LastRoute.OfficialSkillCount > 0 ? LastRoute.OfficialSkillIds : "(无)") +
                "\n开放工具 " + LastRoute.AllowedTools.Count + " 个: " + string.Join(", ", LastRoute.AllowedTools.ToArray()) +
                "\nSystem Prompt 约 " + LastRoute.EstimatedChars + " 字符");
            WaitingForCompileCheck = false;
            _round = 0;
            RunRound();
        }

        private void RunRound()
        {
            if (LastRoute == null)
                return;
            int maxRounds = Mathf.Clamp(AgentSettings.Current.maxToolRounds, 1, 20);
            if (_round >= maxRounds)
            {
                Add(EntryKind.Error, "循环上限", "已达到最大工具轮次 " + maxRounds + "，停止自动执行。可继续追问或调高设置里的上限。");
                Finish();
                return;
            }
            _round++;
            var settings = AgentSettings.Current;
            var request = new LlmRequest
            {
                SystemPrompt = LastRoute.SystemPrompt,
                Temperature = settings.temperature,
                MaxTokens = settings.maxTokens
            };
            request.Messages.AddRange(Messages);
            request.Tools.AddRange(ToolRegistry.SpecsFor(LastRoute.AllowedTools));
            Busy = true;
            Add(EntryKind.Status, "状态", "第 " + _round + " 轮，等待模型响应...");
            _client.Send(request, OnModelResult);
        }

        private void OnModelResult(LlmResult result)
        {
            Busy = false;
            if (!result.Ok)
            {
                Add(EntryKind.Error, "模型请求失败", result.Error +
                    (string.IsNullOrEmpty(result.Raw) ? "" : "\n原始响应(前 500 字符):\n" + LlmClient.Truncate(result.Raw, 500)));
                Finish();
                return;
            }
            if (result.ToolCalls.Count > 0)
            {
                if (!string.IsNullOrEmpty(result.Text))
                    Add(EntryKind.Assistant, "助手", result.Text);
                Messages.Add(ChatMessage.Assistant(result.Text, result.ToolCalls));
                TrimMessages();
                _pendingCalls = new Queue<ToolCall>(result.ToolCalls);
                Add(EntryKind.Info, "工具", "模型请求调用 " + result.ToolCalls.Count + " 个工具");
                ProcessNext();
            }
            else
            {
                string answer = string.IsNullOrEmpty(result.Text) ? "(模型返回了空文本)" : result.Text;
                Add(EntryKind.Assistant, "助手", answer);
                Messages.Add(ChatMessage.Assistant(answer, null));
                TrimMessages();
                Finish();
            }
        }

        private void ProcessNext()
        {
            if (_pendingCalls == null || _pendingCalls.Count == 0)
            {
                _pendingCalls = null;
                EditorApplication.delayCall += RunRound;
                return;
            }
            var call = _pendingCalls.Dequeue();
            var definition = ToolRegistry.Get(call.Name);
            if (definition == null)
            {
                Add(EntryKind.Error, "未知工具", call.Name + " 不在工具注册表中，已跳过。");
                Messages.Add(ChatMessage.Tool(call.Id, call.Name, "[工具执行失败] 未注册的工具: " + call.Name));
                EditorApplication.delayCall += ProcessNext;
                return;
            }
            if (definition.IsWrite && AgentSettings.Current.requireApprovalForWrite)
            {
                Pending = new PendingApproval
                {
                    Call = call,
                    IsWrite = true,
                    Preview = LlmClient.Truncate(call.ArgumentsJson, 4000)
                };
                Add(EntryKind.Info, "等待审批", "写操作 " + call.Name + " 需要你确认。");
                Notify();
                return;
            }
            Execute(call, definition);
        }

        public void ApprovePending()
        {
            if (Pending == null)
                return;
            var call = Pending.Call;
            Pending = null;
            var definition = ToolRegistry.Get(call.Name);
            if (definition == null)
            {
                Add(EntryKind.Error, "未知工具", call.Name);
                Finish();
                return;
            }
            Execute(call, definition);
        }

        public void SkipPending()
        {
            if (Pending == null)
                return;
            var call = Pending.Call;
            Pending = null;
            Add(EntryKind.Info, "已跳过", call.Name);
            Messages.Add(ChatMessage.Tool(call.Id, call.Name, "用户跳过了这次工具调用，请不要重复发起同样的操作。"));
            EditorApplication.delayCall += ProcessNext;
        }

        private void Execute(ToolCall call, ToolDefinition definition)
        {
            ToolResult result;
            try
            {
                result = definition.Execute(ToolArgs.Parse(call.ArgumentsJson));
            }
            catch (Exception e)
            {
                result = ToolResult.Failure("工具抛出异常: " + e.GetType().Name + ": " + e.Message);
            }
            string display = call.Name + "(" + LlmClient.Truncate(call.ArgumentsJson, 300) + ")\n" + result.Content;
            Add(result.Ok ? EntryKind.Tool : EntryKind.Error, result.Ok ? "工具结果" : "工具执行失败", display);
            Messages.Add(ChatMessage.Tool(call.Id, call.Name, result.AsToolMessage()));
            TrimMessages();

            if (result.MayTriggerReload)
            {
                WaitingForCompileCheck = true;
                Add(EntryKind.Info, "等待编译", "Unity 可能正在编译脚本。编译完成后点窗口底部的「检查编译结果」，会把 Console 错误回灌给模型继续。");
                Finish();
                return;
            }
            EditorApplication.delayCall += ProcessNext;
        }

        public void CheckCompileAndContinue()
        {
            var definition = ToolRegistry.Get("get_compile_errors");
            if (definition == null)
                return;
            var args = new Dictionary<string, object> { { "count", 40 } };
            string content;
            try { content = definition.Execute(args).AsToolMessage(); }
            catch (Exception e) { content = "读取 Console 失败: " + e.Message; }
            Add(EntryKind.Tool, "编译检查", content);
            Messages.Add(ChatMessage.Tool("compile_check", "get_compile_errors", content));
            TrimMessages();
            WaitingForCompileCheck = false;
            Pending = null;
            _round = 0;
            if (LastRoute == null && Messages.Count > 0)
                LastRoute = SkillRouter.Route("检查刚才生成脚本的编译结果");
            RunRound();
        }

        public void TestConnection(Action<string> onDone)
        {
            var request = new LlmRequest
            {
                SystemPrompt = "You are a connectivity probe. Reply with exactly: OK",
                TextOnly = true
            };
            request.Messages.Add(ChatMessage.User("ping"));
            _client.Send(request, result =>
            {
                string status = result.Ok
                    ? "连接成功，模型返回: " + LlmClient.Truncate(result.Text, 120)
                    : "连接失败: " + result.Error;
                ConnectionStatus = status;
                Notify();
                if (onDone != null)
                    onDone(status);
            });
        }

        public RouteResult Preview(string userText)
        {
            return SkillRouter.Route(userText ?? "");
        }

        public void UndoLastScript()
        {
            Add(EntryKind.Info, "撤销", UndoLog.UndoLast());
        }

        public void UndoSceneChange()
        {
            Undo.PerformUndo();
            Add(EntryKind.Info, "撤销", "已执行一次场景撤销 (Undo.PerformUndo)。");
        }

        public void NewSession()
        {
            _client.Cancel();
            Busy = false;
            Pending = null;
            _pendingCalls = null;
            WaitingForCompileCheck = false;
            _round = 0;
            Entries.Clear();
            Messages.Clear();
            LastRoute = null;
            Add(EntryKind.Info, "新会话", "已清空聊天记录。知识库会在下一次发送时重新路由。");
        }

        private void Finish()
        {
            Busy = false;
            Pending = null;
            _pendingCalls = null;
            Notify();
        }

        private void TrimMessages()
        {
            while (Messages.Count > MaxMessages)
                Messages.RemoveAt(0);
        }

        public void SaveSession()
        {
            try
            {
                var entries = new List<object>();
                foreach (var entry in Entries)
                {
                    entries.Add(MiniJson.Dict(
                        "kind", (int)entry.Kind,
                        "title", entry.Title,
                        "text", LlmClient.Truncate(entry.Text, MaxEntryChars),
                        "time", entry.Time));
                }
                var messages = new List<object>();
                foreach (var message in Messages)
                {
                    var calls = new List<object>();
                    if (message.ToolCalls != null)
                    {
                        foreach (var call in message.ToolCalls)
                        {
                            calls.Add(MiniJson.Dict("id", call.Id, "name", call.Name, "arguments", call.ArgumentsJson));
                        }
                    }
                    messages.Add(MiniJson.Dict(
                        "role", message.Role,
                        "content", LlmClient.Truncate(message.Content, MaxEntryChars),
                        "toolCallId", message.ToolCallId,
                        "toolName", message.ToolName,
                        "toolCalls", calls));
                }
                SessionState.SetString(SessionKey, MiniJson.Serialize(MiniJson.Dict(
                    "entries", entries, "messages", messages, "busy", Busy)));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Unity6 Skill AI] Failed to persist session: " + e.Message);
            }
        }

        public void RestoreSession()
        {
            try
            {
                string raw = SessionState.GetString(SessionKey, "");
                if (string.IsNullOrEmpty(raw))
                    return;
                var root = MiniJson.Obj(MiniJson.Parse(raw));
                if (root == null)
                    return;
                var entries = MiniJson.Arr(root.ContainsKey("entries") ? root["entries"] : null);
                if (entries != null)
                {
                    foreach (var rawEntry in entries)
                    {
                        var item = MiniJson.Obj(rawEntry);
                        if (item == null)
                            continue;
                        Entries.Add(new ChatEntry
                        {
                            Kind = (EntryKind)(int)MiniJson.GetNum(item, "kind", 0),
                            Title = MiniJson.Get(item, "title"),
                            Text = MiniJson.Get(item, "text"),
                            Time = MiniJson.Get(item, "time")
                        });
                    }
                }
                var messages = MiniJson.Arr(root.ContainsKey("messages") ? root["messages"] : null);
                if (messages != null)
                {
                    foreach (var rawMessage in messages)
                    {
                        var item = MiniJson.Obj(rawMessage);
                        if (item == null)
                            continue;
                        var message = new ChatMessage
                        {
                            Role = MiniJson.Get(item, "role", "user"),
                            Content = MiniJson.Get(item, "content"),
                            ToolCallId = MiniJson.Get(item, "toolCallId"),
                            ToolName = MiniJson.Get(item, "toolName")
                        };
                        var calls = MiniJson.Arr(item.ContainsKey("toolCalls") ? item["toolCalls"] : null);
                        if (calls != null && calls.Count > 0)
                        {
                            message.ToolCalls = new List<ToolCall>();
                            foreach (var rawCall in calls)
                            {
                                var call = MiniJson.Obj(rawCall);
                                if (call == null)
                                    continue;
                                message.ToolCalls.Add(new ToolCall
                                {
                                    Id = MiniJson.Get(call, "id"),
                                    Name = MiniJson.Get(call, "name"),
                                    ArgumentsJson = MiniJson.Get(call, "arguments", "{}")
                                });
                            }
                        }
                        Messages.Add(message);
                    }
                }
                bool wasBusy = MiniJson.GetBool(root, "busy", false);
                Busy = false;
                Pending = null;
                _pendingCalls = null;
                if (wasBusy)
                {
                    Add(EntryKind.Info, "会话恢复", "已从 SessionState 恢复聊天记录。刚才的请求因域重载中断，若刚写过脚本可点「检查编译结果」继续，否则重新发送即可。");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Unity6 Skill AI] Failed to restore session: " + e.Message);
            }
        }
    }
}
