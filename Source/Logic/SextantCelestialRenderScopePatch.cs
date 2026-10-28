using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Single celestial source in the measuring PIPs. DeepPipeline
    /// culls inside RenderDeferredCamera/RenderForwardCamera, so their prefixes
    /// precede culling and their finalizers restore even after an exception.
    /// Only explicit PIP registrations are affected; never Main Camera, lights,
    /// TOD ephemerides, shared materials or an entire sky layer.</summary>
    internal static class SextantCelestialRenderScopePatch
    {
        private const string LogPrefix = "[RadioNav][CelestialSource]";
        internal static bool IsAvailable { get; private set; }

        internal static bool Apply(Harmony harmony)
        {
            IsAvailable = false;
            Type pipeline = GameTypeCache.Resolve("UBOAT.Game.Scene.Rendering.DeepPipeline",
                "com.dws.pipeline.Runtime");
            if (pipeline == null)
                return false;
            bool deferred = Patch(harmony, pipeline, "RenderDeferredCamera");
            bool forward = Patch(harmony, pipeline, "RenderForwardCamera");
            IsAvailable = deferred && forward;
            Debug.Log(LogPrefix + " per-PIP hooks deferred=" + deferred + " forward=" + forward);
            return IsAvailable;
        }

        private static bool Patch(Harmony harmony, Type pipeline, string name)
        {
            MethodInfo target = pipeline.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { typeof(ScriptableRenderContext), typeof(Camera) }, null);
            if (target == null)
                return false;
            try
            {
                harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(SextantCelestialRenderScopePatch), nameof(BeforeCamera)),
                    finalizer: new HarmonyMethod(typeof(SextantCelestialRenderScopePatch), nameof(AfterCamera)));
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix + " " + name + " hook failed: " + e.Message);
                return false;
            }
        }

        internal static void Register(Camera pip, Renderer nativeSun, Renderer nativeMoon,
            Renderer measuringSun, Renderer measuringMoon)
        {
            if (pip == null || nativeSun == null || nativeMoon == null)
                return;
            Component sources = FindSources(pip) ?? pip.gameObject.AddComponent<SextantCelestialSources>();
            // Register is refreshed from LateUpdate. Enumerate the solar
            // subtree only when the native Sun reference changes, not per frame.
            if (ReadRenderer(sources, "NativeSun") != nativeSun)
                Write(sources, "NativeSunFlare", FindSunFlareRenderer(nativeSun));
            Write(sources, "NativeSun", nativeSun);
            Write(sources, "NativeMoon", nativeMoon);
            Write(sources, "MeasuringSun", measuringSun);
            Write(sources, "MeasuringMoon", measuringMoon);
        }

        private static Renderer FindSunFlareRenderer(Renderer nativeSun)
        {
            Renderer found = null;
            foreach (MonoBehaviour component in nativeSun.GetComponentsInChildren<MonoBehaviour>(true))
            {
                // Type name under THIS Sun, never a scene-wide flare/name search:
                // lighthouse and interior lamp flares must remain untouched.
                if (component == null || component.GetType().Name != "HDRPLensFlare")
                    continue;
                Renderer renderer = component.GetComponent<Renderer>();
                if (renderer == null)
                    continue;
                if (found != null && found != renderer)
                    return null; // ambiguous native prefab: do not hide unrelated effects
                found = renderer;
            }
            return found;
        }

        internal static bool IsRegistered(Camera pip)
        {
            Component sources = FindSources(pip);
            object frame = sources != null ? Read(sources, "SunIsolatedFrame") : null;
            return IsAvailable && sources != null && ReadRenderer(sources, "NativeSun") != null
                && ReadRenderer(sources, "NativeMoon") != null
                && ReplacementReady(ReadRenderer(sources, "MeasuringSun"), pip)
                && frame is int && (int)frame >= Time.frameCount - 1;
        }

        internal static void Unregister(Camera pip)
        {
            Component sources = FindSources(pip);
            if (sources != null)
                UnityEngine.Object.Destroy(sources);
        }

        [HarmonyPrefix]
        public static void BeforeCamera(Camera __1, ref CelestialRenderState __state)
        {
            __state = new CelestialRenderState();
            Component sources = FindSources(__1);
            if (!IsAvailable || sources == null)
                return;
            Renderer sun = ReadRenderer(sources, "NativeSun");
            Renderer moon = ReadRenderer(sources, "NativeMoon");
            Renderer sunFlare = ReadRenderer(sources, "NativeSunFlare");
            bool replaceSun = ReplacementReady(ReadRenderer(sources, "MeasuringSun"), __1);
            bool replaceMoon = ReplacementReady(ReadRenderer(sources, "MeasuringMoon"), __1);
            __state = CelestialRenderState.Begin(sun, replaceSun, moon, replaceMoon, sunFlare);
            if (replaceSun && sun != null)
                Write(sources, "SunIsolatedFrame", Time.frameCount);
        }

        [HarmonyFinalizer]
        public static Exception AfterCamera(CelestialRenderState __state, Exception __exception)
        {
            __state.Restore();
            return __exception;
        }

        private static Component FindSources(Camera pip)
        {
            if (pip == null || pip.targetTexture == null)
                return null;
            // A Type-based GetComponent would miss a controller from the other
            // assembly copy (the native-map double-load failure of 2026-09-25).
            foreach (MonoBehaviour component in pip.GetComponents<MonoBehaviour>())
            {
                if (component != null && component.GetType().FullName == typeof(SextantCelestialSources).FullName)
                    return component;
            }
            return null;
        }

        private static bool ReplacementReady(Renderer renderer, Camera pip)
        {
            if (renderer == null || pip == null)
                return false;
            return renderer.enabled && renderer.gameObject.activeInHierarchy
                && (pip.cullingMask & (1 << renderer.gameObject.layer)) != 0
                && renderer.sharedMaterial != null
                && CelestialShaderResolver.IsBundled(renderer.sharedMaterial.shader);
        }

        private static object Read(Component sources, string name)
        {
            FieldInfo field = TypeMemberCache.GetInstanceMember(sources.GetType(), name) as FieldInfo;
            return field != null ? field.GetValue(sources) : null;
        }

        private static Renderer ReadRenderer(Component sources, string name)
        {
            return Read(sources, name) as Renderer;
        }

        private static void Write(Component sources, string name, object value)
        {
            FieldInfo field = TypeMemberCache.GetInstanceMember(sources.GetType(), name) as FieldInfo;
            field?.SetValue(sources, value);
        }
    }
}
