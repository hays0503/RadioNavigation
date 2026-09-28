using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Temporarily suppresses only the explicitly registered source renderer
    /// while a sextant PIP is rendered by DeepPipeline. Modes 1/2 register the
    /// disposable Phase-0 fixture; modes 3/4 register StarSkyRoot's StarsMesh
    /// (mode 3 pass-through, mode 4 tinted redraw). This avoids depending on
    /// Unity camera messages (not dispatched for these PIPs in the current SRP
    /// configuration) and never changes Main Camera masks or registers the
    /// Main Camera.
    /// </summary>
    internal static class SternspreizPipRenderScopePatch
    {
        private const string LogPrefix = "[RadioNav][Sternspreiz][Phase0]";
        private const string PipelineTypeName = "UBOAT.Game.Scene.Rendering.DeepPipeline";
        private const string PipelineAssembly = "com.dws.pipeline.Runtime";

        private static readonly Dictionary<Camera, SourceTarget> sourceTargets =
            new Dictionary<Camera, SourceTarget>();
        private static bool deferredPatched;
        private static bool forwardPatched;
        private static bool resolveFailureLogged;

        internal static bool IsAvailable
        {
            get { return deferredPatched && forwardPatched; }
        }

        internal static bool Register(Camera camera, Renderer sourceRenderer)
        {
            if (camera == null || sourceRenderer == null)
                return false;

            SourceTarget target;
            if (!sourceTargets.TryGetValue(camera, out target))
            {
                target = new SourceTarget();
                sourceTargets.Add(camera, target);
            }
            target.Renderer = sourceRenderer;
            return true;
        }

        internal static void Unregister(Camera camera, Renderer expectedRenderer)
        {
            if (camera == null)
                return;

            SourceTarget target;
            if (!sourceTargets.TryGetValue(camera, out target))
                return;
            if (expectedRenderer != null && target.Renderer != expectedRenderer)
                return;

            if (target.Suppressed && target.Renderer != null)
                target.Renderer.enabled = true;
            sourceTargets.Remove(camera);
        }

        internal static bool Apply(Harmony harmony)
        {
            deferredPatched = false;
            forwardPatched = false;
            Type pipeline = GameTypeCache.Resolve(PipelineTypeName, PipelineAssembly);
            if (pipeline == null)
            {
                LogResolveFailure("DeepPipeline type not found");
                return false;
            }

            MethodInfo prefix = typeof(SternspreizPipRenderScopePatch).GetMethod(
                "BeforeDeferredCamera", BindingFlags.Public | BindingFlags.Static);
            MethodInfo finalizer = typeof(SternspreizPipRenderScopePatch).GetMethod(
                "RestoreDeferredSource", BindingFlags.Public | BindingFlags.Static);
            if (prefix == null || finalizer == null)
            {
                LogResolveFailure("deferred patch handler not found");
                return false;
            }

            deferredPatched = PatchCameraMethod(harmony, pipeline,
                "RenderDeferredCamera", prefix, finalizer);

            MethodInfo forwardPrefix = typeof(SternspreizPipRenderScopePatch).GetMethod(
                "BeforeForwardCamera", BindingFlags.Public | BindingFlags.Static);
            MethodInfo forwardFinalizer = typeof(SternspreizPipRenderScopePatch).GetMethod(
                "RestoreForwardSource", BindingFlags.Public | BindingFlags.Static);
            if (forwardPrefix != null && forwardFinalizer != null)
            {
                forwardPatched = PatchCameraMethod(harmony, pipeline,
                    "RenderForwardCamera", forwardPrefix, forwardFinalizer);
            }

            Debug.Log(LogPrefix + " render-scope hooks deferred=" + deferredPatched
                + " forward=" + forwardPatched);
            return IsAvailable;
        }

        private static bool PatchCameraMethod(Harmony harmony, Type pipeline,
            string methodName, MethodInfo prefix, MethodInfo finalizer)
        {
            MethodInfo target = pipeline.GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Instance, null,
                new[] { typeof(ScriptableRenderContext), typeof(Camera) }, null);
            if (target == null)
            {
                LogResolveFailure(methodName + "(ScriptableRenderContext, Camera) not found");
                return false;
            }

            try
            {
                harmony.Patch(target,
                    prefix: new HarmonyMethod(prefix),
                    finalizer: new HarmonyMethod(finalizer));
                Debug.Log(LogPrefix + " patched DeepPipeline." + methodName);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + " DeepPipeline." + methodName
                    + " patch failed: " + e.Message);
                return false;
            }
        }

        private static void LogResolveFailure(string reason)
        {
            if (resolveFailureLogged)
                return;
            resolveFailureLogged = true;
            Debug.LogWarning(LogPrefix + " render-scope unavailable: " + reason);
        }

        [HarmonyPrefix]
        public static void BeforeDeferredCamera(Camera __1, ref bool __state)
        {
            BeginCameraRender(__1, ref __state, false);
        }

        [HarmonyPrefix]
        public static void BeforeForwardCamera(Camera __1, ref bool __state)
        {
            BeginCameraRender(__1, ref __state, true);
        }

        private static void BeginCameraRender(Camera camera, ref bool state, bool forward)
        {
            state = false;
            if (camera == null)
                return;

            SourceTarget target;
            if (!sourceTargets.TryGetValue(camera, out target))
                return;

            target.RenderCallCount++;
            target.LastRenderFrame = Time.frameCount;
            if (forward)
                target.ForwardCallCount++;
            else
                target.DeferredCallCount++;

            Renderer renderer = target.Renderer;
            int mode = SternspreizPipProbeEffect.CurrentMode;
            bool suppressSource = mode == SternspreizPipProbeEffect.CustomDrawMode
                || mode == SternspreizPipProbeEffect.StarSourceSuppressionMode
                || mode == SternspreizPipProbeEffect.StarRedrawMode;
            if (!suppressSource
                || renderer?.enabled != true)
                return;

            renderer.enabled = false;
            target.Suppressed = true;
            target.SuppressionCount++;
            target.LastSuppressionFrame = Time.frameCount;
            state = true;
        }

        [HarmonyFinalizer]
        public static Exception RestoreDeferredSource(Camera __1, bool __state,
            Exception __exception)
        {
            CompleteCameraRender(__1, __state);
            return __exception;
        }

        [HarmonyFinalizer]
        public static Exception RestoreForwardSource(Camera __1, bool __state,
            Exception __exception)
        {
            CompleteCameraRender(__1, __state);
            return __exception;
        }

        private static void CompleteCameraRender(Camera camera, bool state)
        {
            if (!state || camera == null)
                return;

            SourceTarget target;
            if (!sourceTargets.TryGetValue(camera, out target))
                return;

            if (target.Renderer != null)
                target.Renderer.enabled = true;
            target.Suppressed = false;
            target.RestorationCount++;
            target.LastRestoreFrame = Time.frameCount;
        }

        internal static string SnapshotJson(Camera camera)
        {
            SourceTarget target = null;
            bool registered = camera != null && sourceTargets.TryGetValue(camera, out target);
            if (!registered)
                return "{\"registered\":false,\"hookAvailable\":"
                    + (IsAvailable ? "true" : "false") + "}";

            bool rendererEnabled = target.Renderer?.enabled == true;
            return string.Format(CultureInfo.InvariantCulture,
                "{{\"registered\":true,\"hookAvailable\":{0},\"rendererEnabled\":{1},"
                + "\"suppressed\":{2},\"renderCallCount\":{3},\"deferredCallCount\":{4},"
                + "\"forwardCallCount\":{5},\"suppressionCount\":{6},\"restorationCount\":{7},"
                + "\"lastRenderFrame\":{8},\"lastSuppressionFrame\":{9},\"lastRestoreFrame\":{10},"
                + "\"rendererId\":{11},\"rendererName\":\"{12}\"}}",
                IsAvailable ? "true" : "false", rendererEnabled ? "true" : "false",
                target.Suppressed ? "true" : "false", target.RenderCallCount,
                target.DeferredCallCount, target.ForwardCallCount, target.SuppressionCount,
                target.RestorationCount, target.LastRenderFrame,
                target.LastSuppressionFrame, target.LastRestoreFrame,
                target.Renderer != null ? target.Renderer.GetInstanceID() : 0,
                RadioNavProbeJson.Escape(target.Renderer != null
                    ? target.Renderer.gameObject.name : string.Empty));
        }

        internal static bool IsSourceSuppressedForCurrentRender(Camera camera)
        {
            SourceTarget target = null;
            return camera != null && sourceTargets.TryGetValue(camera, out target)
                && target.Suppressed && target.LastSuppressionFrame == Time.frameCount;
        }

        private sealed class SourceTarget
        {
            public Renderer Renderer;
            public bool Suppressed;
            public int RenderCallCount;
            public int DeferredCallCount;
            public int ForwardCallCount;
            public int SuppressionCount;
            public int RestorationCount;
            public int LastRenderFrame = -1;
            public int LastSuppressionFrame = -1;
            public int LastRestoreFrame = -1;
        }
    }
}
