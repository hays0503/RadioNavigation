using System;
using System.Reflection;
using UBOAT.Game.Scene.Stories;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Facade for RadioStory integration — thin wrapper around StoryDecisionGateway (F04 slice 1).
    /// Owns: locale labels, language resolution, panel lifecycle (keepPanelOpenAfterStoryStop).
    /// Delegates: all decision reflection (G1-G4) to StoryDecisionGateway — single reflection source of truth.
    /// </summary>
    internal static class RadioStoryIntegration
    {
        private const string LocaleKey = "Radio/OpenT3PLL38";
        private const string LeaveStationKey = "Radio/LeaveStation";
        private const string LocalePrefix = "Stories/";
        private const bool AllowOriginal = true;
        private const bool PreventOriginal = false;

        private static bool initialized;
        private static bool keepPanelOpenAfterStoryStop;
        private static PropertyInfo languageProp;
        private static MethodInfo loadSingletonMethod;
        private static Type localeType;

        internal static void ResetRuntimeState()
        {
            initialized = false;
            keepPanelOpenAfterStoryStop = false;
            languageProp = null;
            loadSingletonMethod = null;
            localeType = null;
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

            ResolveRuntimeTypes();
            languageProp = GetLanguageProperty();

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

                if (StoryDecisionGateway.DecisionAlreadyExists(decisions, decisionsList, count, LocaleKey))
                {
                    Debug.Log("[RadioNav] T3PLL38 decision already exists, skipping injection");
                    return;
                }

                int insertIndex = count.IsPositive() ? count - 1 : 0;
                object decision = StoryDecisionGateway.CreateDecision(LocaleKey, new Action(OpenRadio));
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
            if (key == LocalePrefix + LocaleKey || key == LocaleKey)
            {
                __result = GetLocalizedLabel();
                return false;
            }
            return true;
        }

        private static readonly System.Collections.Generic.Dictionary<string, string> LocaleLabels =
            new System.Collections.Generic.Dictionary<string, string>
        {
            { "russian", "T3PLL38 \u0431\u043E\u0440\u0442\u043E\u0432\u043E\u0439 \u0440\u0430\u0434\u0438\u043E\u043F\u0440\u0438\u0435\u043C\u043D\u0438\u043A \u0441\u0432\u0435\u0440\u0445\u0434\u043B\u0438\u043D\u043D\u044B\u0445 \u0438 \u0434\u043B\u0438\u043D\u043D\u044B\u0445 \u0432\u043E\u043B\u043D" },
        };

        private static string GetLocalizedLabel()
        {
            try
            {
                string lang = GetCurrentLanguage();
                if (lang.IsNotNull() && LocaleLabels.TryGetValue(lang, out string label))
                    return label;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RadioNav] GetLocalizedLabel error: " + ex);
            }
            return "T3PLL38 on-board radio receiver for very long and long waves";
        }

        private static Type ResolveType(string typeName)
        {
            return GameTypeCache.Resolve(typeName);
        }

        private static void ResolveRuntimeTypes()
        {
            var singletonType = ResolveType("DWS.Common.InjectionFramework.ScriptableObjectSingleton");
            if (singletonType.IsNotNull())
                loadSingletonMethod = singletonType.GetMethod("LoadSingleton", new[] { typeof(Type) });
            localeType = ResolveType("UBOAT.Game.Core.Data.Locale");
        }

        private static PropertyInfo GetLanguageProperty()
        {
            if (loadSingletonMethod.IsNull() || localeType.IsNull()) return null;
            return localeType.GetProperty("Language", BindingFlags.Public | BindingFlags.Instance);
        }

        private static string GetCurrentLanguage()
        {
            try
            {
                if (loadSingletonMethod.IsNull() || localeType.IsNull() || languageProp.IsNull())
                    return "english";
                object locale = loadSingletonMethod.Invoke(null, new object[] { localeType });
                if (locale.IsNull()) return "english";
                string lang = languageProp.GetValue(locale) as string;
                return lang?.ToLowerInvariant() ?? "english";
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RadioNav] GetCurrentLanguage failed: " + ex);
                return "english";
            }
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

                if (key == LeaveStationKey)
                {
                    Debug.Log("[RadioNav] LeaveStation - hiding T3PLL38 panel");
                    keepPanelOpenAfterStoryStop = false;
                    if (RadioHandler.Instance.IsNotNull())
                        RadioHandler.Instance.IsVisible = false;
                    return AllowOriginal;
                }

                if (key == LocaleKey)
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
