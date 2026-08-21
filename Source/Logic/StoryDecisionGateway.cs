using System;
using System.Reflection;
using UBOAT.Game.Scene.Stories;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Gateway for RadioStory decision reflection (F04 slice 1).
    /// Extracted from RadioStoryIntegration to isolate G1-G4 fixes and make the
    /// reflection path testable. Handles: stage field (G1 boxed enum via Convert),
    /// InsertDecision/OnUpdated hierarchy walk (G2/G3), LocalizationKey field vs prop (G4).
    /// RadioStoryIntegration remains a thin facade for locale + panel lifecycle.
    /// </summary>
    internal static class StoryDecisionGateway
    {
        private const string StageFieldName = "stage";

        private static Type decisionType;
        private static ConstructorInfo decisionCtor;
        private static FieldInfo localizationKeyField;
        private static PropertyInfo localizationKeyProp;
        private static FieldInfo stageField;
        private static PropertyInfo decisionsProp;
        private static MethodInfo insertMethod;
        private static MethodInfo onUpdatedMethod;
        private static bool initialized;

        internal static Type DecisionType => decisionType;
        internal static ConstructorInfo DecisionCtor => decisionCtor;
        internal static FieldInfo LocalizationKeyField => localizationKeyField;
        internal static PropertyInfo LocalizationKeyProp => localizationKeyProp;
        internal static FieldInfo StageField => stageField;
        internal static PropertyInfo DecisionsProp => decisionsProp;
        internal static MethodInfo InsertMethod => insertMethod;
        internal static MethodInfo OnUpdatedMethod => onUpdatedMethod;
        internal static bool IsInitialized => initialized;

        internal static void Reset()
        {
            initialized = false;
            decisionType = null;
            decisionCtor = null;
            localizationKeyField = null;
            localizationKeyProp = null;
            stageField = null;
            decisionsProp = null;
            insertMethod = null;
            onUpdatedMethod = null;
        }

        internal static bool TryEnsureInitialized()
        {
            if (initialized)
                return true;
            EnsureInitialized();
            return initialized;
        }

        // GetMethod does NOT trace base types for NonPublic members, but InsertDecision
        // (Story.cs:377) and OnUpdated (Story.cs:438) are declared two levels above
        // RadioStory. Walk the whole hierarchy instead (G2/G3).
        internal static MethodInfo FindMethodDeep(Type start, string name, Type[] argTypes)
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

        private static void EnsureInitialized()
        {
            if (initialized)
                return;

            string gameVersion = Application.version;
            Debug.Log("[RadioNav] UBOAT version: " + gameVersion);

            var storyType = typeof(RadioStory).BaseType;
            if (storyType.IsNull())
            {
                Debug.LogError("[RadioNav] RadioStory.BaseType is null — game types may have changed in v" + gameVersion);
                return;
            }

            insertMethod = FindMethodDeep(typeof(RadioStory), "InsertDecision", null);
            if (insertMethod.IsNotNull())
                decisionType = insertMethod.GetParameters()[1].ParameterType;
            if (decisionType.IsNull())
            {
                Debug.LogError("[RadioNav] Decision type not found — check game version " + gameVersion);
                return;
            }

            Debug.Log("[RadioNav] Decision type: " + decisionType.FullName);
            localizationKeyField = decisionType.GetField("LocalizationKey", BindingFlags.Public | BindingFlags.Instance);
            if (localizationKeyField.IsNull())
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

            // G1 corrected: "stage" EXISTS (RadioStory.cs:83) but is typed
            // RadioStoryStage (enum), not int. The old (int) cast threw
            // InvalidCastException on the boxed enum, killing injection.
            stageField = typeof(RadioStory).GetField(StageFieldName, BindingFlags.NonPublic | BindingFlags.Instance);

            decisionsProp = storyType.GetProperty("Decisions",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public | BindingFlags.Instance);

            onUpdatedMethod = FindMethodDeep(typeof(RadioStory), "OnUpdated", Type.EmptyTypes);

            initialized = true;
        }

        internal static bool IsStageMainChoices(RadioStory instance)
        {
            // Convert.ToInt32 works on boxed enums ((int) would throw
            // InvalidCastException). RadioStoryStage: Unknown=0, MainChoices=1.
            int stageValue = Convert.ToInt32(stageField.GetValue(instance));
            Debug.Log("[RadioNav] stage=" + stageValue + " (1=MainChoices)");
            return stageValue == 1;
        }

        internal static object GetDecisions(RadioStory instance)
        {
            if (decisionsProp.IsNull())
            {
                Debug.LogError("[RadioNav] Decisions property not found");
                return null;
            }

            var decisions = decisionsProp.GetValue(instance);
            if (decisions.IsNull())
                Debug.LogWarning("[RadioNav] Decisions is null");
            return decisions;
        }

        internal static int GetDecisionCount(object decisions, System.Collections.IList list)
        {
            if (list.IsNotNull())
                return list.Count;

            if (decisions.IsNull()) return -1;
            int count = (int)decisions.GetType().GetProperty("Count").GetValue(decisions);
            Debug.LogWarning("[RadioNav] Decisions does not implement IList, using reflection fallback");
            return count;
        }

        internal static bool DecisionAlreadyExists(object decisions, System.Collections.IList list, int count, string localeKey)
        {
            for (int i = 0; i < count; i++)
            {
                object item = list.IsNotNull() ? list[i] : decisions.GetType().GetProperty("Item")?.GetValue(decisions, new object[] { i });
                if (item.IsNotNull())
                {
                    string key = GetLocalizationKey(item);
#if DEBUG
                    Debug.Log("[RadioNav]   Menu[" + i + "] key=" + key);
#endif
                    if (key == localeKey)
                        return true;
                }
            }
            return false;
        }

        // G4: Decision.LocalizationKey is a public readonly FIELD (Story.cs:24),
        // not a property. Prefer field, keep property as fallback.
        internal static string GetLocalizationKey(object decision)
        {
            if (localizationKeyField.IsNotNull())
                return localizationKeyField.GetValue(decision) as string;
            if (localizationKeyProp.IsNotNull())
                return localizationKeyProp.GetValue(decision) as string;
            return null;
        }

        internal static object CreateDecision(string localeKey, Action openAction)
        {
            int paramCount = decisionCtor.GetParameters().Length;
            object[] ctorArgs = new object[paramCount];
            ctorArgs[0] = localeKey;
            ctorArgs[1] = null;
            ctorArgs[2] = true;
            ctorArgs[3] = openAction;
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

        internal static void InsertDecision(RadioStory instance, int index, object decision)
        {
            try
            {
                insertMethod.Invoke(instance, new object[] { index, decision });
                Debug.Log("[RadioNav] Decision inserted at " + index);
            }
            catch (Exception ex)
            {
                Debug.LogError("[RadioNav] InsertDecision failed: " + ex);
            }
        }

        internal static void NotifyUpdated(RadioStory instance)
        {
            if (onUpdatedMethod.IsNotNull())
            {
                try
                {
                    onUpdatedMethod.Invoke(instance, null);
                    Debug.Log("[RadioNav] OnUpdated called");
                }
                catch (Exception ex)
                {
                    Debug.LogError("[RadioNav] OnUpdated invoke failed: " + ex);
                }
            }
            else
            {
                Debug.LogWarning("[RadioNav] OnUpdated method not found — UI may not reflect new decision");
            }
        }
    }
}
