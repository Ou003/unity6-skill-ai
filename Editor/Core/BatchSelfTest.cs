using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Unity6SkillAI.Editor
{
    /// <summary>
    /// Headless smoke test for CI / maintainers:
    /// Unity.exe -batchmode -quit -projectPath <project> -executeMethod Unity6SkillAI.Editor.BatchSelfTest.Run
    /// </summary>
    public static class BatchSelfTest
    {
        private static readonly string[,] OfficialCases =
        {
            { "用 NavMesh 做敌人巡逻寻路", "initialize-ai-navigation" },
            { "UI Toolkit 的 UXML 布局怎么写", "ui-uitk" },
            { "两个 Trigger 不触发 OnTriggerEnter", "physics-3d-collision" },
            { "把 Built-in 管线迁移到 URP 后材质变粉", "migrate-birp-to-urp" },
            { "像素风游戏画面模糊抖动", "2d-pixel-perfect" },
            { "Sprite Atlas 图集怎么打包", "manage-sprite-atlas" },
            { "TextMeshPro 字体图集太大怎么优化", "optimize-text-mesh-pro" },
            { "用 AudioMixer 做音量分组", "audio-setup-mixers" }
        };

        public static void Run()
        {
            Debug.Log("[SELFTEST] ==== Unity6 Skill AI headless self test ====");
            Debug.Log("[SELFTEST] Unity version: " + UnityVersionInfo.Raw + " series " + UnityVersionInfo.Series);

            SkillLibrary.Reload();
            Debug.Log("[SELFTEST] Cards directory: " +
                (string.IsNullOrEmpty(SkillLibrary.CardsDirectory) ? "(NOT FOUND)" : SkillLibrary.CardsDirectory));
            Debug.Log("[SELFTEST] Cards loaded: " + SkillLibrary.All.Count);
            if (!string.IsNullOrEmpty(SkillLibrary.Diagnostics))
                Debug.LogWarning("[SELFTEST] Diagnostics: " + SkillLibrary.Diagnostics);

            OfficialSkillLibrary.Reload();
            Debug.Log("[SELFTEST] Official skills: " + OfficialSkillLibrary.All.Count +
                " (core " + OfficialSkillLibrary.CoreCount + ") | upstream " + OfficialSkillLibrary.UpstreamCommit);
            Debug.Log("[SELFTEST] Official root: " +
                (string.IsNullOrEmpty(OfficialSkillLibrary.Root) ? "(NOT FOUND)" : OfficialSkillLibrary.Root));
            if (!string.IsNullOrEmpty(OfficialSkillLibrary.Diagnostics))
                Debug.LogWarning("[SELFTEST] Official diagnostics: " + OfficialSkillLibrary.Diagnostics);

            Debug.Log("[SELFTEST] Tools registered: " + ToolRegistry.All.Count);
            Debug.Log("[SELFTEST] Project context:\n" + ProjectInspector.BuildContextText());

            var route = SkillRouter.Route("用新输入系统做一个 WASD 移动脚本");
            Debug.Log("[SELFTEST] Sample route cards: " + route.CardIds);
            Debug.Log("[SELFTEST] Sample route tools: " + route.AllowedTools.Count +
                " | system prompt chars: " + route.EstimatedChars);

            string report = SkillRouterEval.Run();
            Debug.Log("[SELFTEST] Evaluation report:\n" + report);

            int officialPass = 0;
            bool licenseOk = false;
            bool noticeOk = false;
            bool officialIndexOk = OfficialSkillLibrary.All.Count >= 30 &&
                                   !string.IsNullOrEmpty(OfficialSkillLibrary.Root);
            if (officialIndexOk)
            {
                string root = OfficialSkillLibrary.Root;
                licenseOk = File.Exists(Path.Combine(root, "LICENSE-UCL-v1.4.md")) &&
                            File.Exists(Path.Combine(root, "UPSTREAM-LICENSE.md")) &&
                            File.Exists(Path.Combine(root, "NOTICE.md"));
                for (int i = 0; i < OfficialCases.GetLength(0); i++)
                {
                    string prompt = OfficialCases[i, 0];
                    string expected = OfficialCases[i, 1];
                    var hits = OfficialSkillLibrary.Pick(prompt, true, false, 1, 3000, 2, 2000);
                    bool ok = hits.Count > 0 && hits[0].Skill.Name == expected &&
                              !string.IsNullOrEmpty(hits[0].Body);
                    if (ok)
                        officialPass++;
                    Debug.Log("[SELFTEST] Official case " + (i + 1) + ": " +
                        (ok ? "PASS" : "FAIL") + " | " + prompt + " -> " +
                        (hits.Count > 0 ? hits[0].Skill.Name + " (" + hits[0].Score + ")" : "(none)") +
                        " | expected " + expected);
                    if (i == 0 && hits.Count > 0)
                    {
                        string rendered = OfficialSkillLibrary.RenderForPrompt(hits);
                        noticeOk = rendered.IndexOf("不支持 Unity CLI", StringComparison.Ordinal) >= 0 &&
                                   rendered.IndexOf("Unity Companion License v1.4", StringComparison.Ordinal) >= 0;
                    }
                }
            }
            Debug.Log("[SELFTEST] Official routing: " + officialPass + "/" + OfficialCases.GetLength(0) +
                " | licenseOk=" + licenseOk + " noticeOk=" + noticeOk);

            // The official layer must be read-only knowledge: turning it on
            // must not change the tool whitelist chosen by the Unity 6 cards.
            bool whitelistStable = false;
            try
            {
                var settings = AgentSettings.Current;
                bool saved = settings.enableOfficialSkills;
                string probe = "两个 Trigger 不触发 OnTriggerEnter";
                settings.enableOfficialSkills = false;
                var withoutOfficial = SkillRouter.Route(probe);
                settings.enableOfficialSkills = true;
                var withOfficial = SkillRouter.Route(probe);
                settings.enableOfficialSkills = saved;
                whitelistStable = withOfficial.OfficialSkillCount > 0 &&
                    withoutOfficial.AllowedTools.Count == withOfficial.AllowedTools.Count;
                if (whitelistStable)
                {
                    for (int i = 0; i < withoutOfficial.AllowedTools.Count; i++)
                    {
                        if (withoutOfficial.AllowedTools[i] != withOfficial.AllowedTools[i])
                        {
                            whitelistStable = false;
                            break;
                        }
                    }
                }
                Debug.Log("[SELFTEST] Whitelist unchanged by official layer: " + whitelistStable +
                    " | tools " + withoutOfficial.AllowedTools.Count + " -> " + withOfficial.AllowedTools.Count);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SELFTEST] Whitelist check failed: " + e.Message);
            }

            bool cardsOk = SkillLibrary.All.Count >= 20;
            bool directoryOk = !string.IsNullOrEmpty(SkillLibrary.CardsDirectory);
            bool routeOk = route.CardIds.IndexOf("u6-input-system", StringComparison.Ordinal) >= 0;
            bool toolsOk = ToolRegistry.All.Count >= 15;
            bool evalOk = report.IndexOf("全部用例通过", StringComparison.Ordinal) >= 0 &&
                          report.IndexOf("知识卡命中率:", StringComparison.Ordinal) >= 0 &&
                          report.IndexOf("工具白名单命中率:", StringComparison.Ordinal) >= 0 &&
                          report.IndexOf("未通过用例", StringComparison.Ordinal) < 0;
            bool officialOk = officialIndexOk && licenseOk && noticeOk &&
                              officialPass == OfficialCases.GetLength(0) && whitelistStable;

            Debug.Log("[SELFTEST] cardsOk=" + cardsOk + " directoryOk=" + directoryOk +
                " routeOk=" + routeOk + " toolsOk=" + toolsOk + " evalOk=" + evalOk +
                " officialOk=" + officialOk);
            if (cardsOk && directoryOk && routeOk && toolsOk && evalOk && officialOk)
                Debug.Log("[SELFTEST] RESULT: PASS");
            else
                Debug.LogError("[SELFTEST] RESULT: FAIL");
        }
    }
}
