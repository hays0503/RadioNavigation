using HarmonyLib;
using System;
using System.Reflection;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Scene.Stories;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Applies the mod's five Harmony patches with pre-flight target checks
    /// (extracted from Main.cs P5.5). Game assemblies are obfuscated, so every
    /// target is resolved by string name and validated before patching; a
    /// partial failure rolls back all RadioNavigation patches.
    /// </summary>
    public static class HarmonyPatchInstaller
    {
        private const string LogPrefix = "[RadioNav]";

        public static void Apply(Harmony harmony)
        {
            var patchType = typeof(RadioStoryIntegration);
            var anyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            var continueStory = typeof(RadioStory).GetMethod("ContinueStory", BindingFlags.Public | BindingFlags.Instance);
            var retrieveString1 = typeof(Locale).GetMethod("RetrieveString", new[] { typeof(string) });
            var retrieveString2 = typeof(Locale).GetMethod("RetrieveString", new[] { typeof(string), typeof(string) });
            var onDecision = typeof(RadioStory).GetMethod("OnDecisionSelected", BindingFlags.NonPublic | BindingFlags.Instance);
            var onStopped = typeof(RadioStory).GetMethod("OnStopped", BindingFlags.NonPublic | BindingFlags.Instance);

            var injectT3PLL38 = patchType.GetMethod("InjectT3PLL38Decision", anyStatic);
            var overrideLocale = patchType.GetMethod("OverrideLocaleKey", anyStatic);
            var handleDecisions = patchType.GetMethod("HandleCustomDecisions", anyStatic);
            var hideOnStop = patchType.GetMethod("HideRadioOnStop", anyStatic);

            var targets = new[]
            {
                new { method = continueStory, prefix = (HarmonyMethod)null, postfix = injectT3PLL38.IsNotNull() ? new HarmonyMethod(injectT3PLL38) : null, name = "RadioStory.ContinueStory" },
                new { method = retrieveString1, prefix = overrideLocale.IsNotNull() ? new HarmonyMethod(overrideLocale) : null, postfix = (HarmonyMethod)null, name = "Locale.RetrieveString" },
                new { method = retrieveString2, prefix = overrideLocale.IsNotNull() ? new HarmonyMethod(overrideLocale) : null, postfix = (HarmonyMethod)null, name = "Locale.RetrieveString(string,string)" },
                new { method = onDecision, prefix = handleDecisions.IsNotNull() ? new HarmonyMethod(handleDecisions) : null, postfix = (HarmonyMethod)null, name = "RadioStory.OnDecisionSelected" },
                new { method = onStopped, prefix = (HarmonyMethod)null, postfix = hideOnStop.IsNotNull() ? new HarmonyMethod(hideOnStop) : null, name = "RadioStory.OnStopped" },
            };

            string missing = null;
            string missingPatches = null;
            foreach (var t in targets)
            {
                if (t.method.IsNull())
                    missing = (missing ?? "") + t.name + ", ";
                if (t.prefix.IsNotNull() && t.prefix.method.IsNull())
                    missingPatches = (missingPatches ?? "") + t.name + " (prefix), ";
                if (t.postfix.IsNotNull() && t.postfix.method.IsNull())
                    missingPatches = (missingPatches ?? "") + t.name + " (postfix), ";
            }
            if (missing.IsNotNull())
            {
                Debug.LogError(LogPrefix + " Pre-flight failed \u2014 target methods not found: " + missing.TrimEnd(',', ' '));
            }
            else if (missingPatches.IsNotNull())
            {
                Debug.LogError(LogPrefix + " Pre-flight failed \u2014 patch methods not found: " + missingPatches.TrimEnd(',', ' '));
            }
            else
            {
                int applied = 0;
                foreach (var t in targets)
                {
                    try
                    {
                        harmony.Patch(t.method, prefix: t.prefix, postfix: t.postfix);
                        Debug.Log(LogPrefix + " Patched " + t.name);
                        applied++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError(LogPrefix + " Patch failed for " + t.name + ": " + ex.Message);
                        harmony.UnpatchAll(harmony.Id); // F121: bare UnpatchAll() would roll back other mods' patches too
                        Debug.Log(LogPrefix + " RadioNavigation patches rolled back due to partial failure");
                        break;
                    }
                }
                Debug.Log(LogPrefix + " Patches applied: " + applied + "/" + targets.Length);
            }
        }
    }
}
