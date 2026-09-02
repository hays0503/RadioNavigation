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
            var targets = ResolveTargets();

            string missing;
            string missingPatches;
            PreflightCheck(targets, out missing, out missingPatches);

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
                ApplyPatches(harmony, targets);
            }
        }

        private static PatchTarget[] ResolveTargets()
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

            return new[]
            {
                new PatchTarget(continueStory, injectT3PLL38.IsNotNull() ? new HarmonyMethod(injectT3PLL38) : null, null, "RadioStory.ContinueStory"),
                new PatchTarget(retrieveString1, overrideLocale.IsNotNull() ? new HarmonyMethod(overrideLocale) : null, null, "Locale.RetrieveString"),
                new PatchTarget(retrieveString2, overrideLocale.IsNotNull() ? new HarmonyMethod(overrideLocale) : null, null, "Locale.RetrieveString(string,string)"),
                new PatchTarget(onDecision, handleDecisions.IsNotNull() ? new HarmonyMethod(handleDecisions) : null, null, "RadioStory.OnDecisionSelected"),
                new PatchTarget(onStopped, null, hideOnStop.IsNotNull() ? new HarmonyMethod(hideOnStop) : null, "RadioStory.OnStopped"),
            };
        }

        private static void PreflightCheck(PatchTarget[] targets, out string missing, out string missingPatches)
        {
            missing = null;
            missingPatches = null;
            foreach (var t in targets)
            {
                if (t.Method.IsNull())
                    missing = (missing ?? "") + t.Name + ", ";
                if (t.Prefix.IsNotNull() && t.Prefix.method.IsNull())
                    missingPatches = (missingPatches ?? "") + t.Name + " (prefix), ";
                if (t.Postfix.IsNotNull() && t.Postfix.method.IsNull())
                    missingPatches = (missingPatches ?? "") + t.Name + " (postfix), ";
            }
        }

        private static void ApplyPatches(Harmony harmony, PatchTarget[] targets)
        {
            int applied = 0;
            foreach (var t in targets)
            {
                try
                {
                    harmony.Patch(t.Method, prefix: t.Prefix, postfix: t.Postfix);
                    Debug.Log(LogPrefix + " Patched " + t.Name);
                    applied++;
                }
                catch (Exception ex)
                {
                    Debug.LogError(LogPrefix + " Patch failed for " + t.Name + ": " + ex.Message);
                    harmony.UnpatchAll(harmony.Id); // F121: bare UnpatchAll() would roll back other mods' patches too
                    Debug.Log(LogPrefix + " RadioNavigation patches rolled back due to partial failure");
                    break;
                }
            }
            Debug.Log(LogPrefix + " Patches applied: " + applied + "/" + targets.Length);
        }

        private sealed class PatchTarget
        {
            public MethodInfo Method { get; }
            public HarmonyMethod Prefix { get; }
            public HarmonyMethod Postfix { get; }
            public string Name { get; }

            public PatchTarget(MethodInfo method, HarmonyMethod prefix, HarmonyMethod postfix, string name)
            {
                Method = method;
                Prefix = prefix;
                Postfix = postfix;
                Name = name;
            }
        }
    }
}
