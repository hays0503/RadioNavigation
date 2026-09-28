using System;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Hides the game's decorative star sphere while the mod's own catalog sky
    /// is the visible one. TOD_Sky writes the `TOD_StarVisibility` global in
    /// its private UpdateShaderProperties each frame, so this runs as a
    /// POSTFIX on the public TOD_Sky.LateUpdate: the zero lands after the game
    /// has written its value and survives the frame.
    ///
    /// Fail-safe: the global is only zeroed while StarsMesh reports that it is
    /// actually drawing with the bundled star shader (otherwise the sky would
    /// go empty on a shader/loading failure). Preference
    /// `RadioNav.HideGameStars` (default 1) lets the author revert at runtime.
    ///
    /// Non-fatal: a miss (TOD renamed by obfuscation) only leaves the game's
    /// sphere visible, which is the pre-existing behaviour.
    /// </summary>
    internal static class GameStarLayerSuppressor
    {
        internal const string PrefsKey = "RadioNav.HideGameStars";
        private const string LogPrefix = "[RadioNav][Sky]";
        private const string SkyTypeName = "TOD_Sky";
        private const string SkyAssembly = "com.uboat.dependencies";
        private const string SkyMethodName = "LateUpdate";

        private static int starVisibilityId = -1;
        private static int applyCount;
        private static int blockedCount;
        private static bool patchAttempted;
        private static bool patched;

        internal static bool PreferenceOn
        {
            get { return PlayerPrefs.GetInt(PrefsKey, 1) == 1; }
        }

        internal static bool Patched
        {
            get { return patched; }
        }

        internal static int ApplyCount
        {
            get { return applyCount; }
        }

        internal static int BlockedCount
        {
            get { return blockedCount; }
        }

        internal static bool Apply(Harmony harmony)
        {
            if (patchAttempted)
                return patched;
            patchAttempted = true;

            Type sky = GameTypeCache.Resolve(SkyTypeName, SkyAssembly);
            if (sky == null)
            {
                Debug.LogWarning(LogPrefix + " game-star suppression unavailable: TOD_Sky not found");
                return false;
            }

            MethodInfo target = sky.GetMethod(SkyMethodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, Type.EmptyTypes, null);
            MethodInfo postfix = typeof(GameStarLayerSuppressor).GetMethod(
                "AfterSkyLateUpdate", BindingFlags.Public | BindingFlags.Static);
            if (target == null || postfix == null)
            {
                Debug.LogWarning(LogPrefix + " game-star suppression unavailable: "
                    + SkyTypeName + "." + SkyMethodName + " not found");
                return false;
            }

            try
            {
                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                patched = true;
                Debug.Log(LogPrefix + " game-star suppression patched "
                    + SkyTypeName + "." + SkyMethodName + " (preference "
                    + PrefsKey + ", default on)");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + " game-star suppression patch failed: " + e.Message);
                return false;
            }
        }

        /// <summary>Harmony postfix: runs right after TOD_Sky.LateUpdate, i.e.
        /// after the game wrote its own visibility value for this frame.</summary>
        public static void AfterSkyLateUpdate()
        {
            if (!StarLayerSuppression.ShouldHide(PreferenceOn, StarsMesh.UsesBundledStarShader))
            {
                blockedCount++;
                return;
            }
            if (starVisibilityId <= 0)
                starVisibilityId = Shader.PropertyToID(StarLayerSuppression.GlobalName);
            Shader.SetGlobalFloat(starVisibilityId, 0f);
            applyCount++;
        }

        internal static string SnapshotJson()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{{\"patched\":{0},\"preference\":{1},\"applied\":{2},\"blocked\":{3},\"global\":\"{4}\"}}",
                patched ? "true" : "false", PreferenceOn ? "true" : "false",
                applyCount, blockedCount, StarLayerSuppression.GlobalName);
        }
    }
}
