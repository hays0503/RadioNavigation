using System;
using System.Reflection;
using UBOAT.Game.Scene.Stories;
using UnityEngine;

namespace UBOAT.Mods.TestUI
{
    internal static class RadioStoryIntegration
    {
        private const string LocaleKey = "Radio/OpenT3PLL38";

        private static Type _decisionType;
        private static ConstructorInfo _decisionCtor;
        private static PropertyInfo _localizationKeyProp;
        private static FieldInfo _stageField;
        private static PropertyInfo _decisionsProp;
        private static MethodInfo _insertMethod;
        private static MethodInfo _onUpdatedMethod;
        private static bool _initialized;

        private static void Init()
        {
            if (_initialized)
                return;
            _initialized = true;

            var storyType = typeof(RadioStory).BaseType;

            _insertMethod = storyType?.GetMethod("InsertDecision",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public | BindingFlags.Instance);
            if (_insertMethod != null)
            {
                _decisionType = _insertMethod.GetParameters()[1].ParameterType;
                Debug.Log("[TestUI] Decision type: " + _decisionType.FullName);
            }
            if (_decisionType == null)
            {
                Debug.LogError("[TestUI] Decision type not found");
                return;
            }

            _localizationKeyProp = _decisionType.GetProperty("LocalizationKey");

            foreach (var ctor in _decisionType.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                var pars = ctor.GetParameters();
                if (pars.Length < 6 || pars[0].ParameterType != typeof(string))
                    continue;
                if (pars[3].ParameterType == typeof(Action))
                {
                    _decisionCtor = ctor;
                    Debug.Log("[TestUI] Decision ctor selected: " + pars.Length + " params");
                    break;
                }
            }

            if (_decisionCtor == null)
                Debug.LogError("[TestUI] No suitable Decision constructor found");

            _stageField = typeof(RadioStory).GetField("stage", BindingFlags.NonPublic | BindingFlags.Instance);

            _decisionsProp = storyType?.GetProperty("Decisions",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public | BindingFlags.Instance);

            _onUpdatedMethod = storyType?.GetMethod("OnUpdated",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public | BindingFlags.Instance,
                null, Type.EmptyTypes, null);
        }

        public static void InjectT3PLL38Decision(RadioStory __instance)
        {
            try
            {
                Debug.Log("[TestUI] ContinueStory postfix entered");
                Init();
                if (_decisionCtor == null || _stageField == null)
                {
                    Debug.LogWarning("[TestUI] Required reflection targets not found, aborting");
                    return;
                }

                int stageValue = (int)_stageField.GetValue(__instance);
                Debug.Log("[TestUI] stage=" + stageValue + " (1=MainChoices)");
                if (stageValue != 1)
                    return;

                if (_decisionsProp == null)
                {
                    Debug.LogError("[TestUI] Decisions property not found");
                    return;
                }

                var decisions = _decisionsProp.GetValue(__instance);
                if (decisions == null)
                {
                    Debug.LogWarning("[TestUI] Decisions is null");
                    return;
                }

                int count = (int)decisions.GetType().GetProperty("Count").GetValue(decisions);
                Debug.Log("[TestUI] Decision count=" + count);
                for (int i = 0; i < count; i++)
                {
                    var item = decisions.GetType().GetProperty("Item")?.GetValue(decisions, new object[] { i });
                    if (item != null && _localizationKeyProp != null)
                    {
                        string key = (string)_localizationKeyProp.GetValue(item);
                        Debug.Log("[TestUI]   Menu[" + i + "] key=" + key);
                    }
                }

                int insertIndex = count > 0 ? count - 1 : 0;

                int paramCount = _decisionCtor.GetParameters().Length;
                object[] ctorArgs = new object[paramCount];
                ctorArgs[0] = LocaleKey;
                ctorArgs[1] = null;
                ctorArgs[2] = true;
                ctorArgs[3] = new Action(ToggleRadio);
                ctorArgs[4] = 0;
                ctorArgs[5] = null;
                for (int i = 6; i < paramCount; i++)
                    ctorArgs[i] = Array.Empty<object>();

                object decision = _decisionCtor.Invoke(ctorArgs);
                Debug.Log("[TestUI] Decision created OK");

                _insertMethod?.Invoke(__instance, new object[] { insertIndex, decision });
                Debug.Log("[TestUI] Decision inserted at " + insertIndex);

                _onUpdatedMethod?.Invoke(__instance, null);
                Debug.Log("[TestUI] OnUpdated called");
            }
            catch (Exception ex)
            {
                Debug.LogError("[TestUI] InjectT3PLL38Decision error: " + ex);
            }
        }

        public static bool OverrideLocaleKey(string key, ref string __result)
        {
            if (key == "Stories/" + LocaleKey || key == LocaleKey)
            {
                __result = GetLocalizedLabel();
                return false;
            }
            return true;
        }

        private static string GetLocalizedLabel()
        {
            try
            {
                string lang = GetCurrentLanguage();
                if (lang == "russian")
                    return "T3PLL38 бортовой радиоприемник сверхдлинных и длинных волн";
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TestUI] GetLocalizedLabel error: " + ex);
            }
            return "T3PLL38 on-board radio receiver for very long and long waves";
        }

        private static string GetCurrentLanguage()
        {
            try
            {
                var singletonType = Type.GetType("DWS.Common.InjectionFramework.ScriptableObjectSingleton, com.uboat.game");
                if (singletonType == null)
                    singletonType = Type.GetType("DWS.Common.InjectionFramework.ScriptableObjectSingleton, Assembly-CSharp");
                if (singletonType == null)
                    return "english";
                var loadSingleton = singletonType.GetMethod("LoadSingleton", new[] { typeof(Type) });
                if (loadSingleton == null)
                    return "english";
                var localeType = Type.GetType("UBOAT.Game.Core.Data.Locale, com.uboat.game");
                if (localeType == null)
                    return "english";
                object locale = loadSingleton.Invoke(null, new object[] { localeType });
                if (locale == null)
                    return "english";
                var langProp = localeType.GetProperty("Language", BindingFlags.Public | BindingFlags.Instance);
                if (langProp == null)
                    return "english";
                string lang = langProp.GetValue(locale) as string;
                return lang?.ToLowerInvariant() ?? "english";
            }
            catch
            {
                return "english";
            }
        }

        public static bool HandleCustomDecisions(RadioStory __instance, object decision)
        {
            Init();
            if (_localizationKeyProp == null)
                return true;

            string key = (string)_localizationKeyProp.GetValue(decision);

            if (key == "Radio/LeaveStation")
            {
                Debug.Log("[TestUI] LeaveStation - hiding T3PLLä38 panel");
                if (RadioHandler.Instance != null)
                    RadioHandler.Instance.IsVisible = false;
                return true;
            }

            if (key == LocaleKey)
            {
                Debug.Log("[TestUI] T3PLLä38 toggled");
                ToggleRadio();
                return false;
            }

            return true;
        }

        public static void HideRadioOnStop()
        {
            if (RadioHandler.Instance != null)
                RadioHandler.Instance.IsVisible = false;
        }

        private static void ToggleRadio()
        {
            if (RadioHandler.Instance != null)
                RadioHandler.Instance.ToggleVisibility();
        }
    }
}
