using System;
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

            Debug.Log("[SELFTEST] Tools registered: " + ToolRegistry.All.Count);
            Debug.Log("[SELFTEST] Project context:\n" + ProjectInspector.BuildContextText());

            var route = SkillRouter.Route("用新输入系统做一个 WASD 移动脚本");
            Debug.Log("[SELFTEST] Sample route cards: " + route.CardIds);
            Debug.Log("[SELFTEST] Sample route tools: " + route.AllowedTools.Count +
                " | system prompt chars: " + route.EstimatedChars);

            string report = SkillRouterEval.Run();
            Debug.Log("[SELFTEST] Evaluation report:\n" + report);

            bool cardsOk = SkillLibrary.All.Count >= 20;
            bool directoryOk = !string.IsNullOrEmpty(SkillLibrary.CardsDirectory);
            bool routeOk = route.CardIds.IndexOf("u6-input-system", StringComparison.Ordinal) >= 0;
            bool toolsOk = ToolRegistry.All.Count >= 15;
            bool evalOk = report.IndexOf("知识卡命中率: 30/30", StringComparison.Ordinal) >= 0 &&
                          report.IndexOf("工具白名单命中率: 30/30", StringComparison.Ordinal) >= 0;

            Debug.Log("[SELFTEST] cardsOk=" + cardsOk + " directoryOk=" + directoryOk +
                " routeOk=" + routeOk + " toolsOk=" + toolsOk + " evalOk=" + evalOk);
            if (cardsOk && directoryOk && routeOk && toolsOk && evalOk)
                Debug.Log("[SELFTEST] RESULT: PASS");
            else
                Debug.LogError("[SELFTEST] RESULT: FAIL");
        }
    }
}
