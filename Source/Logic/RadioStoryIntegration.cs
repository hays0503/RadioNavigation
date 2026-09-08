using System;
using UBOAT.Game.Scene.Stories;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Facade for RadioStory integration — thin wrapper around StoryDecisionGateway (F04 slice 1)
    /// and RadioLocale (locale labels). Owns: init orchestration, panel lifecycle
    /// (keepPanelOpenAfterStoryStop), Harmony entry points. Delegates: all decision
    /// reflection (G1-G4) to StoryDecisionGateway — single reflection source of truth.
    /// </summary>
    internal static class RadioStoryIntegration
    {
        private const bool AllowOriginal = true;
        private const bool PreventOriginal = false;

        private static bool initialized;
        private static bool keepPanelOpenAfterStoryStop;

        internal static void ResetRuntimeState()
        {
            initialized = false;
            keepPanelOpenAfterStoryStop = false;
            RadioLocale.Reset();
            StoryDecisionGateway.Reset();
        }

        private static bool TryInit()
        {
            if (initialized)
                return true;
            Init();
            return initialized;
        }

        private static void Init()
        {
            if (initialized)
                return;

            if (!StoryDecisionGateway.TryEnsureInitialized())
                return;

            RadioLocale.EnsureInitialized();

            initialized = true;
        }

        public static void InjectT3PLL38Decision(RadioStory __instance)
        {
            try
            {
                if (!TryInit() || StoryDecisionGateway.DecisionCtor.IsNull() || StoryDecisionGateway.StageField.IsNull())
                {
                    Debug.LogWarning("[RadioNav] Init failed, cannot inject T3PLL38");
                    return;
                }

                if (!StoryDecisionGateway.IsStageMainChoices(__instance))
                    return;

                var decisions = StoryDecisionGateway.GetDecisions(__instance);
                if (decisions.IsNull())
                    return;

                var decisionsList = decisions as System.Collections.IList;
                int count = StoryDecisionGateway.GetDecisionCount(decisions, decisionsList);
                if (count.IsNegative()) return;

                Debug.Log("[RadioNav] Decision count=" + count);

                if (StoryDecisionGateway.DecisionAlreadyExists(decisions, decisionsList, count, RadioLocale.Key))
                {
                    Debug.Log("[RadioNav] T3PLL38 decision already exists, skipping injection");
                    return;
                }

                int insertIndex = count.IsPositive() ? count - 1 : 0;
                object decision = StoryDecisionGateway.CreateDecision(RadioLocale.Key, new Action(OpenRadio));
                if (decision.IsNull()) return;

                StoryDecisionGateway.InsertDecision(__instance, insertIndex, decision);
                StoryDecisionGateway.NotifyUpdated(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioNav] InjectT3PLL38Decision error: " + ex);
            }
        }

        public static bool OverrideLocaleKey(string key, ref string __result)
        {
            if (RadioLocale.MatchesLocaleKey(key))
            {
                __result = RadioLocale.GetLocalizedLabel();
                return false;
            }
            return true;
        }

        public static bool HandleCustomDecisions(RadioStory __instance, object decision)
        {
            try
            {
                if (!TryInit())
                    return AllowOriginal;
                if (StoryDecisionGateway.LocalizationKeyField.IsNull() && StoryDecisionGateway.LocalizationKeyProp.IsNull())
                    return AllowOriginal;

                string key = StoryDecisionGateway.GetLocalizationKey(decision);

                if (key == RadioLocale.LeaveStationKey)
                {
                    Debug.Log("[RadioNav] LeaveStation - hiding T3PLL38 panel");
                    keepPanelOpenAfterStoryStop = false;
                    if (RadioHandler.Instance.IsNotNull())
                        RadioHandler.Instance.IsVisible = false;
                    return AllowOriginal;
                }

                if (key == RadioLocale.Key)
                {
                    Debug.Log("[RadioNav] T3PLL38 open requested");
                    OpenRadio();
                    return PreventOriginal;
                }

                return AllowOriginal;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RadioNav] HandleCustomDecisions failed (fail-open): " + ex);
                return AllowOriginal;
            }
        }

        public static void HideRadioOnStop()
        {
            if (keepPanelOpenAfterStoryStop)
            {
                Debug.Log("[RadioNav] Story stopped; keeping T3PLL38 panel open");
                return;
            }

            if (RadioHandler.Instance.IsNotNull())
                RadioHandler.Instance.IsVisible = false;
        }

        private static void OpenRadio()
        {
            if (RadioHandler.Instance.IsNotNull())
            {
                keepPanelOpenAfterStoryStop = true;
                RadioHandler.Instance.IsVisible = true;
                Debug.Log("[RadioNav] T3PLL38 panel visible=" + RadioHandler.Instance.IsVisible);
            }
            else
            {
                Debug.LogError("[RadioNav] T3PLL38 open requested but RadioHandler.Instance is null");
            }
        }
    }
}
