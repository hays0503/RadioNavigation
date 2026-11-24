using DWS.Common.InjectionFramework;
using System;
using System.Collections.Generic;
using System.Reflection;
using UBOAT.Game.Scene;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.Scene.Environment;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Matches STAR_SKY visibility to the game's night transition and fog.</summary>
    internal static class SkyVisibilityResolver
    {
        // P5.1: Type.GetType with assembly-qualified name is a string parse +
        // assembly scan per call; IsPlayerOutside hits it every frame — type
        // resolution is cached centrally in GameTypeCache. Member lookups are
        // cached in TypeMemberCache (keyed by Type, no per-frame string key).
        [Inject] private static Weather weather;

        private static int fogginessFrame = -1;
        private static float cachedFogginess;
        private static int visibilityFrame = -1;
        private static float cachedVisibility = 1f;
        private static int sunZenithFrame = -1;
        private static double cachedSunZenithDeg = 108.0;
        private static int outsideFrame = -1;
        private static bool cachedOutside;

        private static bool TryGetSky(out object sky)
        {
            sky = null;
            if (weather.IsNull())
            {
                InjectionFramework framework = InjectionFramework.Instance;
                if (framework != null)
                    weather = framework.GetInstance<Weather>();
            }
            if (weather.IsNull())
                return false;
            sky = ReadMember(weather, "Sky");
            return sky.IsNotNull();
        }

        /// <summary>Raw Atmosphere.Fogginess in [0..1]; 0 = clear air, 1 = dense fog.</summary>
        public static float GetFogginess()
        {
            int frame = Time.frameCount;
            if (fogginessFrame == frame)
                return cachedFogginess;

            float result = 0f;
            try
            {
                if (TryGetSky(out object sky))
                {
                    object atmosphere = ReadMember(sky, "Atmosphere");
                    float fogginess = ReadFloat(atmosphere, "Fogginess");
                    result = Math.Max(0f, Math.Min(1f, fogginess));
                }
            }
            catch (Exception e)
            {
                // Scene transitions can temporarily invalidate injected Unity
                // references; log once per session so a permanent failure
                // (game rename) is visible instead of silently wrong.
                if (WarnOnce.ShouldLog("SkyVisibilityResolver.GetFogginess"))
                    Debug.LogWarning("[RadioNav][Sky] GetFogginess failed: " + e.Message);
            }

            fogginessFrame = frame;
            cachedFogginess = result;
            return result;
        }

        public static float GetVisibility()
        {
            int frame = Time.frameCount;
            if (visibilityFrame == frame)
                return cachedVisibility;

            float result = 1f;
            try
            {
                if (TryGetSky(out object sky))
                {
                    object atmosphere = ReadMember(sky, "Atmosphere");
                    float fogginess = ReadFloat(atmosphere, "Fogginess");
                    float lerpValue = ReadFloat(sky, "LerpValue");
                    float visibility = (1f - fogginess) * (1f - lerpValue);
                    result = Math.Max(0f, Math.Min(1f, visibility));
                }
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("SkyVisibilityResolver.GetVisibility"))
                    Debug.LogWarning("[RadioNav][Sky] GetVisibility failed: " + e.Message);
            }

            visibilityFrame = frame;
            cachedVisibility = result;
            return result;
        }

        /// <summary>Sun zenith distance (degrees) from the game sky
        /// (TOD_Sky.SunZenith). The sky-brightness model (P4) uses it for the
        /// twilight curve. Fallback 108° = astronomical night, so a reflection
        /// failure cannot wash the star field out.</summary>
        public static double GetSunZenithDeg()
        {
            int frame = Time.frameCount;
            if (sunZenithFrame == frame)
                return cachedSunZenithDeg;

            double result = 108.0;
            try
            {
                if (TryGetSky(out object sky))
                {
                    object value = ReadMember(sky, "SunZenith");
                    if (value != null)
                        result = Convert.ToDouble(value);
                }
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("SkyVisibilityResolver.GetSunZenithDeg"))
                    Debug.LogWarning("[RadioNav][Sky] GetSunZenithDeg failed: " + e.Message);
            }

            sunZenithFrame = frame;
            cachedSunZenithDeg = result;
            return result;
        }

        /// <summary>
        /// True when the player is outside the boat (bridge/deck) and not on
        /// the map. Star sky renders only then: binoculars/UZO used from the
        /// bridge keep the sky, the periscope inside the boat hides it
        /// (MainCamera.IsInsideBoat is true there).
        /// </summary>
        public static bool IsPlayerOutside()
        {
            int frame = Time.frameCount;
            if (outsideFrame == frame)
                return cachedOutside;

            bool result = false;
            try
            {
                MainCamera mainCamera = MainCamera.Instance;
                if (mainCamera.IsNotNull() && !mainCamera.IsInsideBoat)
                {
                    result = mainCamera.CurrentMode != CameraMode.Map;
                }
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("SkyVisibilityResolver.IsPlayerOutside"))
                    Debug.LogWarning("[RadioNav][Sky] IsPlayerOutside failed: " + e.Message);
            }

            outsideFrame = frame;
            cachedOutside = result;
            return result;
        }

        private static float ReadFloat(object target, string memberName)
        {
            if (target == null)
                return 0f;

            object value = ReadMember(target, memberName);
            return value == null ? 0f : Convert.ToSingle(value);
        }

        private static object ReadMember(object target, string memberName)
        {
            if (target == null)
                return null;

            // Resolved MemberInfos are cached (TypeMemberCache): GetFogginess/
            // GetVisibility run per frame per lighthouse instance.
            MemberInfo cached = TypeMemberCache.GetInstanceMember(target.GetType(), memberName);
            if (cached == null)
            {
                Type type = target.GetType();
                if (WarnOnce.ShouldLog("SkyVisibilityResolver." + type.Name + "." + memberName))
                    Debug.LogWarning("[RadioNav][Sky] member not found: " + type.Name + "." + memberName);
                return null;
            }
            return cached is PropertyInfo property ? property.GetValue(target, null) : ((FieldInfo)cached).GetValue(target);
        }

        /// <summary>
        /// Hot-reload hygiene: forget cached frame state and weather ref.
        /// Member lookups live in TypeMemberCache, cleared by the bootstrapper.
        /// </summary>
        public static void Reset()
        {
            weather = null;
            fogginessFrame = -1;
            visibilityFrame = -1;
            sunZenithFrame = -1;
            outsideFrame = -1;
        }
    }
}
