using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Keeps the sextant PIP pass at exactly ONE GBuffer writer
    /// (SEXTANT_PLAN §16.21).
    ///
    /// The game's DeepPipeline.RenderDeferredCamera renders each PIP camera
    /// and, when that camera's GameObject carries an ACTIVE
    /// VirtualSceneRenderer, additionally collects the renderer's
    /// VirtualCamera (the interior clone) as cameras[1]. Both write the pass's
    /// single shared _GBufferZ (RenderTextureFormat.Depth, one gbufferManager
    /// per pipeline), but every deferred lighting/SSAO/light-combine stage
    /// still runs with the ROOT PIP camera as "camera" — so light-combine
    /// reads the clone's depth while lighting the PIP, overflows channel B to
    /// Inf/NaN and flashes a near-black hard-edged tile with a colour fringe
    /// (root cause §16.17; RenderDoc t8=_GBufferZ sentinel +/-1.7e38).
    ///
    /// The VirtualSceneRenderer itself MUST stay: it is what positions the
    /// interior VirtualScene (LateUpdate writes the clone transform from
    /// virtualScene.Target; §16.20 proved removing it yields a black ocular).
    /// So this patch only suppresses the CLONE's participation in the render:
    /// during CollectCameras it temporarily clears the renderer's private
    /// "renderingEnabled" flag, so the guard clause fails and cameras[1] is
    /// not added, then restores it in the postfix. Positioning happens in
    /// LateUpdate, before the SRP render, so it is unaffected.
    ///
    /// The flag is a private instance field on a type in an assembly the mod
    /// does not reference, so it is touched through reflection
    /// (TypeMemberCache) and the patch target is resolved by string.
    /// </summary>
    internal static class PipCloneCameraPatch
    {
        private const string LogPrefix = "[RadioNav]";
        private const string PipelineTypeName = "UBOAT.Game.Scene.Rendering.DeepPipeline";
        private const string PipelineAssembly = "com.dws.pipeline.Runtime";
        private const string RenderingEnabledField = "renderingEnabled";

        private static MemberInfo renderingEnabledMember;
        private static bool resolveFailedLogged;

        // Positive firing evidence (SEXTANT_PLAN §16.22): incremented on every
        // successful suppression; readable live via reflection. The __0 bug
        // taught that "no errors" is not evidence — this counter is.
        private static int suppressionCount;

        /// <summary>How many CollectCameras calls were suppressed so far.</summary>
        internal static int SuppressionCount
        {
            get { return suppressionCount; }
        }

        // Test-only A/B gate (same pattern as RadioNav.TestAim): set to 0 to
        // neutralize the patch so the pre/post difference can be measured on
        // one unchanged view without redeploying (SEXTANT_PLAN §16.21).
        private const string EnabledPrefsKey = "RadioNav.PipSingleWriter";

        /// <summary>
        /// Resolves DeepPipeline.CollectCameras and registers the skip patch.
        /// Returns true when the patch was applied.
        /// </summary>
        public static bool Apply(Harmony harmony)
        {
            MethodInfo target = ResolveCollectCameras();
            if (target == null)
                return false;

            MethodInfo prefix = typeof(PipCloneCameraPatch).GetMethod(
                "SuppressCloneForPip", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo postfix = typeof(PipCloneCameraPatch).GetMethod(
                "RestoreCloneAfterCollect", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (prefix == null || postfix == null)
                return false;

            try
            {
                harmony.Patch(target,
                    prefix: new HarmonyMethod(prefix),
                    postfix: new HarmonyMethod(postfix));
                Debug.Log(LogPrefix + " Patched DeepPipeline.CollectCameras (PIP single-writer)");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + " CollectCameras patch failed: " + e.Message);
                return false;
            }
        }

        private static MethodInfo ResolveCollectCameras()
        {
            Type pipeline = GameTypeCache.Resolve(PipelineTypeName, PipelineAssembly);
            if (pipeline == null)
            {
                LogResolveFailure("DeepPipeline type not found");
                return null;
            }
            MethodInfo method = pipeline.GetMethod("CollectCameras",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null)
                LogResolveFailure("CollectCameras method not found");
            return method;
        }

        private static void LogResolveFailure(string what)
        {
            if (!resolveFailedLogged)
            {
                resolveFailedLogged = true;
                Debug.LogWarning(LogPrefix + " PIP single-writer patch disabled: " + what);
            }
        }

        /// <summary>
        /// Prefix: when the root camera is a sextant PIP, clear its
        /// VirtualSceneRenderer's renderingEnabled flag so CollectCameras
        /// skips the interior clone (cameras[1]) and the pass keeps a single
        /// GBuffer writer. Preserves the previous value in the state slot.
        ///
        /// The camera binds positionally (Camera __1), NOT by name: the
        /// target is obfuscated and the decompiler-inferred name may differ
        /// between builds. __1 is the zero-based second argument per the
        /// Harmony injection contract — verified 2026-09-19 after the
        /// object-__0 variant silently received the ScriptableRenderContext
        /// (first argument) and never fired (SEXTANT_PLAN §16.22).
        /// </summary>
        [HarmonyPrefix]
        public static void SuppressCloneForPip(Camera __1, ref object __state)
        {
            __state = null;
            if (PlayerPrefs.GetInt(EnabledPrefsKey, 1) == 0)
                return;
            Camera rootCamera = __1;
            if (rootCamera == null)
                return;
            if (!SextantController.IsPipCamera(rootCamera))
                return;

            Component vsr = FindVsr(rootCamera);
            if (vsr == null)
                return;

            if (renderingEnabledMember == null)
                renderingEnabledMember = TypeMemberCache.GetInstanceMember(
                    vsr.GetType(), RenderingEnabledField);
            if (!(renderingEnabledMember is FieldInfo field))
                return;

            try
            {
                object previous = field.GetValue(vsr);
                __state = new SuppressionState { Vsr = vsr, Previous = previous };
                field.SetValue(vsr, false);
                suppressionCount++;
                if (suppressionCount == 1)
                    Debug.Log(LogPrefix + " PIP single-writer suppression active (first CollectCameras intercepted)");
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("PipCloneCameraPatch.Suppress"))
                    Debug.LogWarning(LogPrefix + " clone suppression failed: " + e.Message);
            }
        }

        /// <summary>Postfix: restores the renderer flag cleared by the prefix.</summary>
        [HarmonyPostfix]
        public static void RestoreCloneAfterCollect(ref object __state)
        {
            if (!(__state is SuppressionState state) || state.Vsr == null)
                return;
            if (!(renderingEnabledMember is FieldInfo field))
                return;
            try
            {
                field.SetValue(state.Vsr, state.Previous);
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("PipCloneCameraPatch.Restore"))
                    Debug.LogWarning(LogPrefix + " clone restore failed: " + e.Message);
            }
        }

        private static Component FindVsr(Camera camera)
        {
            MonoBehaviour[] behaviours = camera.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i]?.GetType().Name == "VirtualSceneRenderer")
                    return behaviours[i];
            }
            return null;
        }

        private sealed class SuppressionState
        {
            public Component Vsr;
            public object Previous;
        }
    }
}
