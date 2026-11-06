using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Unity6SkillAI.Editor
{
    /// <summary>
    /// The single floating/dockable window users interact with. Pure IMGUI so
    /// it works on every Unity 6 minor without UXML/USS asset dependencies.
    /// </summary>
    public class SkillChatWindow : EditorWindow
    {
        private static readonly string[] PresetLabels = BuildPresetLabels();

        [MenuItem("Tools/Unity6 Skill AI/Chat (Floating)")]
        public static void OpenFloating()
        {
            var window = CreateInstance<SkillChatWindow>();
            window.titleContent = new GUIContent("Unity6 Skill AI");
            window.minSize = new Vector2(470, 640);
            window.ShowUtility();
        }

        [MenuItem("Tools/Unity6 Skill AI/Chat (Dockable)")]
        public static void OpenDocked()
        {
            var window = GetWindow<SkillChatWindow>("Unity6 Skill AI");
            window.minSize = new Vector2(420, 520);
            window.Show();
        }

        [MenuItem("Tools/Unity6 Skill AI/Reload Knowledge Cards")]
        public static void ReloadCards()
        {
            SkillLibrary.Reload();
            string report = SkillRouterEval.Run();
            Debug.Log("[Unity6 Skill AI] 知识卡已重载。\n" + report);
        }

        private AgentController _agent;
        private Vector2 _chatScroll;
        private Vector2 _settingsScroll;
        private Vector2 _previewScroll;
        private string _input = "";
        private string _keyDraft = "";
        private bool _showSettings;
        private bool _showPreview;
        private bool _scrollToBottom;
        private bool _testingConnection;
        private RouteResult _previewRoute;
        private readonly HashSet<int> _expanded = new HashSet<int>();
        private GUIStyle _wrapped;

        private static string[] BuildPresetLabels()
        {
            var labels = new string[ProviderPresets.Items.Length];
            for (int i = 0; i < ProviderPresets.Items.Length; i++)
                labels[i] = ProviderPresets.Items[i].Label;
            return labels;
        }

        private void OnEnable()
        {
            _agent = new AgentController();
            _agent.Changed += () =>
            {
                _scrollToBottom = true;
                Repaint();
            };
            _agent.RestoreSession();
            _keyDraft = AgentSettings.GetApiKey();
            _scrollToBottom = true;
        }

        private void OnDisable()
        {
            if (_agent != null)
                _agent.SaveSession();
        }

        private void EnsureStyles()
        {
            if (_wrapped == null)
            {
                _wrapped = new GUIStyle(EditorStyles.wordWrappedLabel);
                _wrapped.wordWrap = true;
                _wrapped.richText = false;
                _wrapped.padding = new RectOffset(4, 4, 2, 2);
            }
        }

        private void OnGUI()
        {
            EnsureStyles();
            HandleShortcuts();
            DrawToolbar();
            if (_showSettings)
                DrawSettings();
            if (_showPreview)
                DrawPreview();
            DrawChat();
            DrawApproval();
            DrawComposer();
            DrawStatusBar();
        }

        private void HandleShortcuts()
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown && (e.control || e.command) && e.keyCode == KeyCode.Return)
            {
                Send();
                e.Use();
            }
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _showSettings = GUILayout.Toggle(_showSettings, "设置", EditorStyles.toolbarButton, GUILayout.Width(60));
            _showPreview = GUILayout.Toggle(_showPreview, "注入预览", EditorStyles.toolbarButton, GUILayout.Width(90));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("新会话", EditorStyles.toolbarButton, GUILayout.Width(70)))
                _agent.NewSession();
            if (GUILayout.Button("重载知识卡", EditorStyles.toolbarButton, GUILayout.Width(90)))
            {
                SkillLibrary.Reload();
                OfficialSkillLibrary.Reload();
                _showPreview = false;
                _previewRoute = null;
                Repaint();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("BYOK 设置（密钥只存本机，不进入工程版本库）", EditorStyles.boldLabel);
            var settings = AgentSettings.Current;

            EditorGUI.BeginChangeCheck();
            int preset = EditorGUILayout.Popup("服务商预设", Mathf.Clamp(settings.presetIndex, 0, PresetLabels.Length - 1), PresetLabels);
            if (preset != settings.presetIndex)
            {
                AgentSettings.ApplyPreset(preset);
                _keyDraft = AgentSettings.GetApiKey();
            }
            var kind = (ProviderKind)EditorGUILayout.EnumPopup("接口协议", (ProviderKind)settings.provider);
            settings.provider = (int)kind;
            settings.baseUrl = EditorGUILayout.TextField("Base URL", settings.baseUrl);
            settings.model = EditorGUILayout.TextField("模型名", settings.model);
            _keyDraft = EditorGUILayout.PasswordField("API Key", _keyDraft);
            if (EditorGUI.EndChangeCheck())
            {
                settings.presetIndex = preset;
                AgentSettings.Save();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(_testingConnection ? "测试中..." : "测试连接", GUILayout.Height(24)) && !_testingConnection)
            {
                AgentSettings.SetApiKey(_keyDraft);
                AgentSettings.Save();
                _testingConnection = true;
                _agent.TestConnection(_ => { _testingConnection = false; Repaint(); });
            }
            if (GUILayout.Button("保存密钥与设置", GUILayout.Height(24)))
            {
                AgentSettings.SetApiKey(_keyDraft);
                AgentSettings.Save();
                Debug.Log("[Unity6 Skill AI] 设置已保存到 UserSettings/ 与 Library/。");
            }
            if (GUILayout.Button("清除本机密钥", GUILayout.Height(24)))
            {
                _keyDraft = "";
                AgentSettings.SetApiKey("");
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("连接状态: " + _agent.ConnectionStatus, EditorStyles.miniLabel);
            if (AgentSettings.ApiKeyComesFromEnvironment())
                EditorGUILayout.HelpBox("检测到环境变量 " + AgentSettings.ApiKeyEnvironmentVariable + "，它将优先于本机保存的密钥。", MessageType.Info);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Agent 行为", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            settings.temperature = EditorGUILayout.Slider("温度", (float)settings.temperature, 0f, 1f);
            settings.maxTokens = EditorGUILayout.IntSlider("单次最大 Token", settings.maxTokens, 256, 8192);
            settings.maxToolRounds = EditorGUILayout.IntSlider("最大工具轮次", settings.maxToolRounds, 1, 20);
            settings.requireApprovalForWrite = EditorGUILayout.Toggle("写操作需人工审批", settings.requireApprovalForWrite);
            settings.autoInjectProjectContext = EditorGUILayout.Toggle("自动注入工程实测信息", settings.autoInjectProjectContext);
            settings.showRawToolResults = EditorGUILayout.Toggle("工具结果默认展开", settings.showRawToolResults);
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Unity 官方 Skills 参考层（Unity-Technologies/skills）", EditorStyles.boldLabel);
            settings.enableOfficialSkills = EditorGUILayout.Toggle("注入官方技能知识", settings.enableOfficialSkills);
            settings.officialSkillsIncludeAllTiers = EditorGUILayout.Toggle("包含 CLI / 服务类技能", settings.officialSkillsIncludeAllTiers);
            settings.officialSkillsMaxCount = EditorGUILayout.IntSlider("最多注入技能数", settings.officialSkillsMaxCount, 1, 3);
            settings.officialSkillsMaxChars = EditorGUILayout.IntSlider("单技能最大字符", settings.officialSkillsMaxChars, 1000, 8000);
            EditorGUILayout.LabelField("官方技能按 Unity Companion License v1.4 随包附带，仅作知识参考，不改变工具白名单。", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("补充 System Prompt（可选）");
            settings.extraSystemPrompt = EditorGUILayout.TextArea(settings.extraSystemPrompt, GUILayout.Height(48));
            if (EditorGUI.EndChangeCheck())
                AgentSettings.Save();
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4);
        }

        private void DrawPreview()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("本次注入预览（不调用模型，不消耗 Token）", EditorStyles.boldLabel);
            if (GUILayout.Button("用输入框内容重新计算", GUILayout.Width(170)))
                _previewRoute = _agent.Preview(_input);
            if (GUILayout.Button("关闭", GUILayout.Width(50)))
                _showPreview = false;
            EditorGUILayout.EndHorizontal();

            if (_previewRoute == null)
                _previewRoute = _agent.Preview(_input);
            EditorGUILayout.LabelField("命中知识卡: " + _previewRoute.CardIds, EditorStyles.miniLabel);
            EditorGUILayout.LabelField("命中官方技能: " +
                (_previewRoute.OfficialSkillCount > 0 ? _previewRoute.OfficialSkillIds : "(无)"), EditorStyles.miniLabel);
            EditorGUILayout.LabelField("开放工具 (" + _previewRoute.AllowedTools.Count + "): " +
                string.Join(", ", _previewRoute.AllowedTools.ToArray()), EditorStyles.miniLabel);
            EditorGUILayout.LabelField("评分明细: " + string.Join(" | ", _previewRoute.ScoreDetails.ToArray()), EditorStyles.miniLabel);

            _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll, GUILayout.Height(180));
            float width = Mathf.Max(220, EditorGUIUtility.currentViewWidth - 60);
            float height = Mathf.Clamp(_wrapped.CalcHeight(new GUIContent(_previewRoute.SystemPrompt), width), 60, 6000);
            EditorGUILayout.SelectableLabel(_previewRoute.SystemPrompt, _wrapped, GUILayout.Height(height));
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("复制完整 System Prompt", GUILayout.Height(22)))
                EditorGUIUtility.systemCopyBuffer = _previewRoute.SystemPrompt;
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4);
        }

        private void DrawChat()
        {
            if (_scrollToBottom && Event.current.type == EventType.Repaint)
            {
                _chatScroll = new Vector2(0, float.MaxValue);
                _scrollToBottom = false;
            }
            _chatScroll = EditorGUILayout.BeginScrollView(_chatScroll, EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            if (_agent.Entries.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "这是 Unity6 Skill AI 的对话窗口。\n" +
                    "1. 点顶部「设置」填入自备的大模型 API Key；\n" +
                    "2. 点「注入预览」可离线查看本次会加载哪些 Unity 6 知识卡与工具；\n" +
                    "3. 输入自然语言，例如「用新输入系统做一个 WASD 移动并挂到 Player 上」。\n" +
                    "知识卡位于 Editor/Skills/Cards/，可直接增删；Official/ 附带 Unity 官方 Skills 原文作参考层。\n" +
                    "菜单 Tools > Unity6 Skill AI > Run Routing Eval 可离线评测命中率。",
                    MessageType.Info);
            }
            for (int i = 0; i < _agent.Entries.Count; i++)
                DrawEntry(i, _agent.Entries[i]);
            EditorGUILayout.EndScrollView();
        }

        private void DrawEntry(int index, ChatEntry entry)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(Title(entry), EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("复制", EditorStyles.miniButton, GUILayout.Width(44)))
                EditorGUIUtility.systemCopyBuffer = entry.Text;
            if ((entry.Kind == EntryKind.Tool || entry.Kind == EntryKind.Info || entry.Kind == EntryKind.Error) &&
                entry.Text.Length > 400)
            {
                bool expanded = _expanded.Contains(index) || AgentSettings.Current.showRawToolResults;
                if (GUILayout.Button(expanded ? "收起" : "展开", EditorStyles.miniButton, GUILayout.Width(44)))
                {
                    if (_expanded.Contains(index)) _expanded.Remove(index);
                    else _expanded.Add(index);
                }
            }
            EditorGUILayout.EndHorizontal();

            bool showFull = entry.Text.Length <= 400 || AgentSettings.Current.showRawToolResults || _expanded.Contains(index);
            string text = showFull ? entry.Text : entry.Text.Substring(0, 400) + "\n...[已折叠，点展开查看]";
            float width = Mathf.Max(220, EditorGUIUtility.currentViewWidth - 70);
            float height = Mathf.Clamp(_wrapped.CalcHeight(new GUIContent(text), width), 18, 8000);
            EditorGUILayout.SelectableLabel(text, _wrapped, GUILayout.Height(height));
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        private static string Title(ChatEntry entry)
        {
            string prefix;
            switch (entry.Kind)
            {
                case EntryKind.User: prefix = "[你]"; break;
                case EntryKind.Assistant: prefix = "[助手]"; break;
                case EntryKind.Tool: prefix = "[工具]"; break;
                case EntryKind.Status: prefix = "[状态]"; break;
                case EntryKind.Error: prefix = "[错误]"; break;
                default: prefix = "[信息]"; break;
            }
            return prefix + " " + entry.Title + "  " + entry.Time;
        }

        private void DrawApproval()
        {
            var pending = _agent.Pending;
            if (pending == null)
                return;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("待审批写操作: " + pending.Call.Name, EditorStyles.boldLabel);
            float width = Mathf.Max(220, EditorGUIUtility.currentViewWidth - 70);
            float height = Mathf.Clamp(_wrapped.CalcHeight(new GUIContent(pending.Preview), width), 20, 400);
            EditorGUILayout.SelectableLabel(pending.Preview, _wrapped, GUILayout.Height(height));
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("执行", GUILayout.Height(26)))
                _agent.ApprovePending();
            if (GUILayout.Button("跳过", GUILayout.Height(26)))
                _agent.SkipPending();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawComposer()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("输入需求 (Ctrl+Enter 发送)", EditorStyles.miniLabel);
            _input = EditorGUILayout.TextArea(_input, GUILayout.Height(72));
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !_agent.Busy && _agent.Pending == null;
            if (GUILayout.Button(_agent.Busy ? "处理中..." : "发送", GUILayout.Height(28)))
                Send();
            GUI.enabled = true;
            if (GUILayout.Button("预览注入", GUILayout.Height(28), GUILayout.Width(90)))
            {
                _previewRoute = _agent.Preview(_input);
                _showPreview = true;
            }
            if (_agent.WaitingForCompileCheck)
            {
                GUI.backgroundColor = new Color(0.75f, 0.9f, 0.75f);
                if (GUILayout.Button("检查编译结果", GUILayout.Height(28), GUILayout.Width(110)))
                    _agent.CheckCompileAndContinue();
                GUI.backgroundColor = Color.white;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("撤销场景改动", EditorStyles.miniButton))
                _agent.UndoSceneChange();
            if (GUILayout.Button("撤销脚本写入", EditorStyles.miniButton))
                _agent.UndoLastScript();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("离线路由评测", EditorStyles.miniButton))
                Debug.Log(SkillRouterEval.Run());
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawStatusBar()
        {
            string keyState = AgentSettings.HasApiKey()
                ? (AgentSettings.ApiKeyComesFromEnvironment() ? "环境变量" : "已配置")
                : "未配置";
            EditorGUILayout.LabelField(
                "Unity " + UnityVersionInfo.Raw +
                " | 知识卡 " + SkillLibrary.All.Count + " 张" +
                " | 官方技能 " + OfficialSkillLibrary.All.Count + " 个" +
                " | 工具 " + ToolRegistry.All.Count + " 个" +
                " | Key: " + keyState +
                " | " + (_agent.Busy ? "请求中" : _agent.Pending != null ? "等待审批" : "空闲"),
                EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(SkillLibrary.Diagnostics))
                EditorGUILayout.HelpBox(SkillLibrary.Diagnostics.Trim(), MessageType.Warning);
            if (!string.IsNullOrEmpty(OfficialSkillLibrary.Diagnostics))
                EditorGUILayout.HelpBox(OfficialSkillLibrary.Diagnostics.Trim(), MessageType.Warning);
        }

        private void Send()
        {
            if (string.IsNullOrEmpty(_input) || string.IsNullOrEmpty(_input.Trim()))
                return;
            if (!AgentSettings.HasApiKey())
            {
                _showSettings = true;
                EditorUtility.DisplayDialog("需要 API Key",
                    "请先在设置里填入自备的大模型 API Key（或设置环境变量 " +
                    AgentSettings.ApiKeyEnvironmentVariable + "）。\n\n" +
                    "若只想验证知识库效果，可以点「预览注入」或「离线路由评测」，两者都不需要密钥。",
                    "知道了");
                return;
            }
            string text = _input;
            _input = "";
            _previewRoute = null;
            _agent.Submit(text);
            Repaint();
        }
    }
}
