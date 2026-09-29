using System;
using System.Collections.Generic;
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
    /// The global alone did NOT remove the visible dome (measured live
    /// 2026-09-29), so a second stage disables the dome renderers directly -
    /// identified strictly by material name AND world extent, never by object
    /// name (28 same-named objects in the scene). It runs on a slow schedule
    /// because FindObjectsOfTypeAll is costly, and every renderer it disables
    /// keeps its previous enabled state so the preference reverts exactly what
    /// the mod changed.
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
        private const float RendererScanIntervalSeconds = 1f;

        private static int starVisibilityId = -1;
        private static int applyCount;
        private static int blockedCount;
        private static bool patchAttempted;
        private static bool patched;

        /// <summary>Renderers this mod disabled, with the state to restore.
        /// Only renderers that were enabled when first seen enter the list
        /// (see ApplyRendererStage), so the mod never claims one it did not
        /// turn off.</summary>
        private static readonly List<SuppressedRenderer> hiddenRenderers = new List<SuppressedRenderer>();
        private static float lastRendererScanRealtime = float.NegativeInfinity;
        private static int lastReportedHiddenCount = -1;

        private sealed class SuppressedRenderer
        {
            public Renderer Renderer;
            public bool OriginalEnabled;
            public GameObject GameObject;
            public bool OriginalActive;
        }

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

        internal static int HiddenRendererCount
        {
            get { return CountHiddenRenderers(); }
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
                // Fail-safe: restore everything this mod had disabled.
                RestoreRendererStage();
                return;
            }
            if (starVisibilityId <= 0)
                starVisibilityId = Shader.PropertyToID(StarLayerSuppression.GlobalName);
            Shader.SetGlobalFloat(starVisibilityId, 0f);
            applyCount++;

            // The game re-enables the dome renderer inside its own LateUpdate
            // (measured live 2026-11-05: setting enabled=false was reverted
            // within milliseconds), so re-assert the cached suppression every
            // frame - the postfix runs after TOD_Sky.LateUpdate and before the
            // camera render.
            ReassertRendererStage();

            if (Time.realtimeSinceStartup - lastRendererScanRealtime
                    >= RendererScanIntervalSeconds)
            {
                lastRendererScanRealtime = Time.realtimeSinceStartup;
                ApplyRendererStage();
            }
        }

        /// <summary>Cheap per-frame re-assertion of the cached suppression:
        /// the game owns the renderer component and turns it back on between
        /// frames, so a one-shot disable cannot stick. Also re-hides the
        /// GameObject (the level the game does not toggle itself).</summary>
        private static void ReassertRendererStage()
        {
            for (int i = 0; i < hiddenRenderers.Count; i++)
            {
                SuppressedRenderer entry = hiddenRenderers[i];
                if (entry.Renderer != null && entry.Renderer.enabled)
                    entry.Renderer.enabled = false;
                if (entry.GameObject != null && entry.GameObject.activeSelf)
                    entry.GameObject.SetActive(false);
            }
        }

        /// <summary>Renderer-level stage. Identification is deliberately strict:
        /// an ACTIVE object whose shared material name contains `Stars` AND
        /// whose largest world extent clears StarLayerSuppression.DomeMinExtent.
        /// That never touches the sub-metre flare renderers or the many UI
        /// `Stars` namesakes. Cached scan (Resources.FindObjectsOfTypeAll is
        /// costly) on a slow schedule; every disabled renderer's previous
        /// enabled state is retained for an exact restore.</summary>
        private static void ApplyRendererStage()
        {
            PruneDestroyedRenderers();

            Renderer[] all = Resources.FindObjectsOfTypeAll<Renderer>();
            for (int i = 0; i < all.Length; i++)
            {
                Renderer candidate = all[i];
                if (candidate == null)
                    continue;
                if (!StarLayerSuppression.IsStarDomeCandidate(
                        candidate.gameObject.activeInHierarchy, SharedMaterialName(candidate),
                        candidate.bounds.extents.x, candidate.bounds.extents.y,
                        candidate.bounds.extents.z))
                    continue;
                if (!candidate.enabled)
                    continue; // already off (ours or the game's): nothing to do
                // The game re-enables the dome on scene transitions (observed
                // live 2026-11-05: hidden=1 then the save load re-enabled it),
                // so an entry in our list is not enough - re-disable it.
                if (!IsAlreadySuppressed(candidate))
                    hiddenRenderers.Add(new SuppressedRenderer
                    {
                        Renderer = candidate,
                        OriginalEnabled = candidate.enabled,
                        GameObject = candidate.gameObject,
                        OriginalActive = candidate.gameObject.activeSelf,
                    });
                candidate.enabled = false;
                if (candidate.gameObject.activeSelf)
                    candidate.gameObject.SetActive(false);
            }

            ReportHiddenRendererCount();
        }

        private static string SharedMaterialName(Renderer renderer)
        {
            Material material = renderer.sharedMaterial;
            return material != null ? material.name : null;
        }

        private static bool IsAlreadySuppressed(Renderer renderer)
        {
            for (int i = 0; i < hiddenRenderers.Count; i++)
                if (hiddenRenderers[i].Renderer == renderer)
                    return true;
            return false;
        }

        private static void PruneDestroyedRenderers()
        {
            for (int i = hiddenRenderers.Count - 1; i >= 0; i--)
                if (hiddenRenderers[i].Renderer == null)
                    hiddenRenderers.RemoveAt(i);
        }

        private static void RestoreRendererStage()
        {
            PruneDestroyedRenderers();
            for (int i = 0; i < hiddenRenderers.Count; i++)
            {
                SuppressedRenderer entry = hiddenRenderers[i];
                if (entry.GameObject != null
                    && entry.GameObject.activeSelf != entry.OriginalActive)
                    entry.GameObject.SetActive(entry.OriginalActive);
                if (entry.Renderer != null)
                    entry.Renderer.enabled = entry.OriginalEnabled;
            }
            hiddenRenderers.Clear();
            ReportHiddenRendererCount();
        }

        private static void ReportHiddenRendererCount()
        {
            int hidden = CountHiddenRenderers();
            if (hidden == lastReportedHiddenCount)
                return;
            lastReportedHiddenCount = hidden;
            Debug.Log(LogPrefix + " game star dome renderers hidden=" + hidden
                + " [" + HiddenRendererNames() + "]");
        }

        private static string HiddenRendererNames()
        {
            string names = "";
            for (int i = 0; i < hiddenRenderers.Count; i++)
            {
                SuppressedRenderer entry = hiddenRenderers[i];
                if (!IsHidden(entry))
                    continue;
                string name = entry.GameObject != null ? entry.GameObject.name
                    : (entry.Renderer != null ? entry.Renderer.name : "?");
                names = names.Length == 0 ? name : names + "," + name;
            }
            return names;
        }

        private static bool IsHidden(SuppressedRenderer entry)
        {
            if (entry.Renderer != null && !entry.Renderer.enabled)
                return true;
            return entry.GameObject != null && !entry.GameObject.activeSelf;
        }

        private static int CountHiddenRenderers()
        {
            int hidden = 0;
            for (int i = 0; i < hiddenRenderers.Count; i++)
                if (IsHidden(hiddenRenderers[i]))
                    hidden++;
            return hidden;
        }

        internal static string SnapshotJson()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{{\"patched\":{0},\"preference\":{1},\"applied\":{2},\"blocked\":{3},"
                + "\"hidden\":{4},\"global\":\"{5}\"}}",
                patched ? "true" : "false", PreferenceOn ? "true" : "false",
                applyCount, blockedCount, CountHiddenRenderers(),
                StarLayerSuppression.GlobalName);
        }
    }
}
