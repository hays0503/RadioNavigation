using System;
using System.Reflection;
using UBOAT.Game.Scene.Stories;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    internal static class RadioStoryIntegration
    {
        private const string LocaleKey = "Radio/OpenT3PLL38";
        private const string LeaveStationKey = "Radio/LeaveStation";
        private const string LocalePrefix = "Stories/";
        private const string StageFieldName = "stage";
        private const bool AllowOriginal = true;
        private const bool PreventOriginal = false;

        private static Type decisionType;
        private static ConstructorInfo decisionCtor;
        private static PropertyInfo localizationKeyProp;
        private static FieldInfo stageField;
        private static PropertyInfo decisionsProp;
        private static MethodInfo insertMethod;
        private static MethodInfo onUpdatedMethod;
        private static bool initialized;
        private static PropertyInfo languageProp;

        internal static void ResetRuntimeState()
        {
            initialized = false;
            decisionType = null;
            decisionCtor = null;
            localizationKeyProp = null;
            stageField = null;
            decisionsProp = null;
            insertMethod = null;
            onUpdatedMethod = null;
            languageProp = null;
        }

        private static bool TryInit()
        {
            if (initialized)
                return true;
            Init();
            return initialized;
        }

        // GetMethod does NOT trace base types for NonPublic members, but InsertDecision
        // (Story.cs:377) and OnUpdated (Story.cs:438) are declared two levels above
        // RadioStory. Walk the whole hierarchy instead (G2/G3).
        private static MethodInfo FindMethodDeep(Type start, string name, Type[] argTypes)
        {
            for (Type t = start; t.IsNotNull(); t = t.BaseType)
            {
                var m = argTypes == null
                    ? t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)
                    : t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance, null, argTypes, null);
                if (m.IsNotNull())
                    return m;
            }
            return null;
        }

        private static void Init()
        {
            if (initialized)
                return;

            string gameVersion = Application.version;
            Debug.Log("[RadioNav] UBOAT version: " + gameVersion);

            var storyType = typeof(RadioStory).BaseType;
            if (storyType.IsNull())
            {
                Debug.LogError("[RadioNav] RadioStory.BaseType is null \u2014 game types may have changed in v" + gameVersion);
                return;
            }

            insertMethod = FindMethodDeep(typeof(RadioStory), "InsertDecision", null);
            if (insertMethod.IsNotNull())
                decisionType = insertMethod.GetParameters()[1].ParameterType;
            if (decisionType.IsNull())
            {
                Debug.LogError("[RadioNav] Decision type not found \u2014 check game version " + gameVersion);
                return;
            }

            Debug.Log("[RadioNav] Decision type: " + decisionType.FullName);
            localizationKeyProp = decisionType.GetProperty("LocalizationKey");

            foreach (var ctor in decisionType.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                var pars = ctor.GetParameters();
                if (pars.Length < 6 || pars[0].ParameterType != typeof(string))
                    continue;
                if (pars[3].ParameterType == typeof(Action))
                {
                    decisionCtor = ctor;
                    Debug.Log("[RadioNav] Decision ctor selected: " + pars.Length + " params");
                    break;
                }
            }

            if (decisionCtor.IsNull())
            {
                Debug.LogError("[RadioNav] No suitable Decision constructor found in v" + gameVersion);
                return;
            }

            stageField = typeof(RadioStory).GetField(StageFieldName, BindingFlags.NonPublic | BindingFlags.Instance);

            decisionsProp = storyType.GetProperty("Decisions",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public | BindingFlags.Instance);

            onUpdatedMethod = storyType.GetMethod("OnUpdated",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public | BindingFlags.Instance,
                null, Type.EmptyTypes, null);

            languageProp = GetLanguageProperty();

            initialized = true;
        }

        public static void InjectT3PLL38Decision(RadioStory __instance)
        {
            try
            {
                if (!TryInit() || decisionCtor.IsNull() || stageField.IsNull())
                {
                    Debug.LogWarning("[RadioNav] Init failed, cannot inject T3PLL38");
                    return;
                }

                if (!IsStageOne(__instance))
                    return;

                var decisions = GetDecisions(__instance);
                if (decisions.IsNull())
                    return;

                var decisionsList = decisions as System.Collections.IList;
                int count = GetDecisionCount(decisions, decisionsList);
                if (count.IsNegative()) return;

                Debug.Log("[RadioNav] Decision count=" + count);

                if (DecisionAlreadyExists(decisions, decisionsList, count))
                {
                    Debug.Log("[RadioNav] T3PLL38 decision already exists, skipping injection");
                    return;
                }

                int insertIndex = count.IsPositive() ? count - 1 : 0;
                object decision = CreateDecision();
                if (decision.IsNull()) return;

                InsertDecision(__instance, insertIndex, decision);
                NotifyUpdated(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioNav] InjectT3PLL38Decision error: " + ex);
            }
        }

        private static bool IsStageOne(RadioStory __instance)
        {
            int stageValue = (int)stageField.GetValue(__instance);
            Debug.Log("[RadioNav] stage=" + stageValue + " (1=MainChoices)");
            return stageValue == 1;
        }

        private static object GetDecisions(RadioStory __instance)
        {
            if (decisionsProp.IsNull())
            {
                Debug.LogError("[RadioNav] Decisions property not found");
                return null;
            }

            var decisions = decisionsProp.GetValue(__instance);
            if (decisions.IsNull())
                Debug.LogWarning("[RadioNav] Decisions is null");
            return decisions;
        }

        private static int GetDecisionCount(object decisions, System.Collections.IList list)
        {
            if (list.IsNotNull())
                return list.Count;

            if (decisions.IsNull()) return -1;
            int count = (int)decisions.GetType().GetProperty("Count").GetValue(decisions);
            Debug.LogWarning("[RadioNav] Decisions does not implement IList, using reflection fallback");
            return count;
        }

        private static bool DecisionAlreadyExists(object decisions, System.Collections.IList list, int count)
        {
            for (int i = 0; i < count; i++)
            {
                object item = list.IsNotNull() ? list[i] : decisions.GetType().GetProperty("Item")?.GetValue(decisions, new object[] { i });
                if (item.IsNotNull() && localizationKeyProp.IsNotNull())
                {
                    string key = (string)localizationKeyProp.GetValue(item);
#if DEBUG
                    Debug.Log("[RadioNav]   Menu[" + i + "] key=" + key);
#endif
                    if (key == LocaleKey)
                        return true;
                }
            }
            return false;
        }

        private static object CreateDecision()
        {
            int paramCount = decisionCtor.GetParameters().Length;
            object[] ctorArgs = new object[paramCount];
            ctorArgs[0] = LocaleKey;
            ctorArgs[1] = null;
            ctorArgs[2] = true;
            ctorArgs[3] = new Action(ToggleRadio);
            ctorArgs[4] = 0;
            ctorArgs[5] = null;
            var ctorParams = decisionCtor.GetParameters();
            for (int i = 6; i < paramCount; i++)
            {
                var paramType = ctorParams[i].ParameterType;
                if (paramType == typeof(object[]))
                    ctorArgs[i] = Array.Empty<object>();
                else if (paramType == typeof(string))
                    ctorArgs[i] = null;
                else if (paramType.IsValueType)
                    ctorArgs[i] = Activator.CreateInstance(paramType);
                else
                    ctorArgs[i] = null;
            }

            object decision = decisionCtor.Invoke(ctorArgs);
            if (decision.IsNull())
                Debug.LogWarning("[RadioNav] Decision creation returned null");
            return decision;
        }

        private static void InsertDecision(RadioStory __instance, int index, object decision)
        {
            try
            {
                insertMethod.Invoke(__instance, new object[] { index, decision });
                Debug.Log("[RadioNav] Decision inserted at " + index);
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioNav] InsertDecision failed: " + ex);
            }
        }

        private static void NotifyUpdated(RadioStory __instance)
        {
            if (onUpdatedMethod.IsNotNull())
            {
                try
                {
                    onUpdatedMethod.Invoke(__instance, null);
                    Debug.Log("[RadioNav] OnUpdated called");
                }
                catch (Exception ex)
                {
                    Debug.LogError("[RadioNav] OnUpdated invoke failed: " + ex);
                }
            }
            else
            {
                Debug.LogWarning("[RadioNav] OnUpdated method not found \u2014 UI may not reflect new decision");
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

        private static PropertyInfo GetLanguageProperty()
        {
            var singletonType = Type.GetType("DWS.Common.InjectionFramework.ScriptableObjectSingleton, com.uboat.game")
                ?? Type.GetType("DWS.Common.InjectionFramework.ScriptableObjectSingleton, Assembly-CSharp");
            if (singletonType.IsNull()) return null;
            var loadSingleton = singletonType.GetMethod("LoadSingleton", new[] { typeof(Type) });
            if (loadSingleton.IsNull()) return null;
            var localeType = Type.GetType("UBOAT.Game.Core.Data.Locale, com.uboat.game")
                ?? Type.GetType("UBOAT.Game.Core.Data.Locale, Assembly-CSharp");
            if (localeType.IsNull()) return null;
            return localeType.GetProperty("Language", BindingFlags.Public | BindingFlags.Instance);
        }

        private static string GetCurrentLanguage()
        {
            try
            {
                if (languageProp.IsNull())
                    return "english";
                var singletonType = Type.GetType("DWS.Common.InjectionFramework.ScriptableObjectSingleton, com.uboat.game")
                    ?? Type.GetType("DWS.Common.InjectionFramework.ScriptableObjectSingleton, Assembly-CSharp");
                if (singletonType.IsNull()) return "english";
                var loadSingleton = singletonType.GetMethod("LoadSingleton", new[] { typeof(Type) });
                if (loadSingleton.IsNull()) return "english";
                var localeType = Type.GetType("UBOAT.Game.Core.Data.Locale, com.uboat.game")
                    ?? Type.GetType("UBOAT.Game.Core.Data.Locale, Assembly-CSharp");
                if (localeType.IsNull()) return "english";
                object locale = loadSingleton.Invoke(null, new object[] { localeType });
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
                if (localizationKeyProp.IsNull())
                    return AllowOriginal;

                string key = (string)localizationKeyProp.GetValue(decision);

                if (key == LeaveStationKey)
                {
                    Debug.Log("[RadioNav] LeaveStation - hiding T3PLL38 panel");
                    if (RadioHandler.Instance.IsNotNull())
                        RadioHandler.Instance.IsVisible = false;
                    return AllowOriginal;
                }

                if (key == LocaleKey)
                {
                    Debug.Log("[RadioNav] T3PLL38 toggled");
                    ToggleRadio();
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
            if (RadioHandler.Instance.IsNotNull())
                RadioHandler.Instance.IsVisible = false;
        }

        private static void ToggleRadio()
        {
            if (RadioHandler.Instance.IsNotNull())
                RadioHandler.Instance.ToggleVisibility();
        }
    }
}
