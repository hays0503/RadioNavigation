using DWS.Common.InjectionFramework;
using System;
using System.Collections.Generic;
using System.Reflection;
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

        // "Map" enum comparison: Enum.ToString() allocates on Mono, so cache
        // the parsed enum value once instead of formatting per frame.
        private static bool mapModeResolved;
        private static object mapModeValue;

        /// <summary>Raw Atmosphere.Fogginess in [0..1]; 0 = clear air, 1 = dense fog.</summary>
        public static float GetFogginess()
        {
            try
            {
                if (weather == null)
                    return 0f;

                object sky = ReadMember(weather, "Sky");
                if (sky == null)
                    return 0f;

                object atmosphere = ReadMember(sky, "Atmosphere");
                float fogginess = ReadFloat(atmosphere, "Fogginess");
                return Math.Max(0f, Math.Min(1f, fogginess));
            }
            catch (Exception e)
            {
                // Scene transitions can temporarily invalidate injected Unity
                // references; log once per session so a permanent failure
                // (game rename) is visible instead of silently wrong.
                if (WarnOnce.ShouldLog("SkyVisibilityResolver.GetFogginess"))
                    Debug.LogWarning("[RadioNav][Sky] GetFogginess failed: " + e.Message);
                return 0f;
            }
        }

        public static float GetVisibility()
        {
            try
            {
                if (weather == null)
                    return 1f;

                object sky = ReadMember(weather, "Sky");
                if (sky == null)
                    return 1f;

                object atmosphere = ReadMember(sky, "Atmosphere");
                float fogginess = ReadFloat(atmosphere, "Fogginess");
                float lerpValue = ReadFloat(sky, "LerpValue");
                float visibility = (1f - fogginess) * (1f - lerpValue);
                return Math.Max(0f, Math.Min(1f, visibility));
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("SkyVisibilityResolver.GetVisibility"))
                    Debug.LogWarning("[RadioNav][Sky] GetVisibility failed: " + e.Message);
                return 1f;
            }
        }

        /// <summary>Sun zenith distance (degrees) from the game sky
        /// (TOD_Sky.SunZenith). The sky-brightness model (P4) uses it for the
        /// twilight curve. Fallback 108° = astronomical night, so a reflection
        /// failure cannot wash the star field out.</summary>
        public static double GetSunZenithDeg()
        {
            try
            {
                if (weather == null)
                    return 108.0;
                object sky = ReadMember(weather, "Sky");
                if (sky == null)
                    return 108.0;
                object value = ReadMember(sky, "SunZenith");
                return value == null ? 108.0 : Convert.ToDouble(value);
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("SkyVisibilityResolver.GetSunZenithDeg"))
                    Debug.LogWarning("[RadioNav][Sky] GetSunZenithDeg failed: " + e.Message);
                return 108.0;
            }
        }

        /// <summary>
        /// True when the player is outside the boat (bridge/deck) and not on
        /// the map. Star sky renders only then: binoculars/UZO used from the
        /// bridge keep the sky, the periscope inside the boat hides it
        /// (MainCamera.IsInsideBoat is true there).
        /// </summary>
        public static bool IsPlayerOutside()
        {
            try
            {
                object mainCamera = ReadStatic("UBOAT.Game.Scene.Camera.MainCamera", "Instance");
                if (mainCamera == null)
                    return false;

                bool insideBoat = ReadBool(mainCamera, "IsInsideBoat");
                if (insideBoat)
                    return false;

                object mode = ReadMember(mainCamera, "CurrentMode");
                if (mode != null && IsMode(mode, "Map", ref mapModeResolved, ref mapModeValue))
                    return false;

                return true;
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("SkyVisibilityResolver.IsPlayerOutside"))
                    Debug.LogWarning("[RadioNav][Sky] IsPlayerOutside failed: " + e.Message);
                return false;
            }
        }

        private static object ReadStatic(string typeName, string memberName)
        {
            Type type = GameTypeCache.Resolve(typeName);
            if (type == null)
                return null;

            MemberInfo cached = TypeMemberCache.GetStaticMember(type, memberName);
            if (cached == null)
            {
                if (WarnOnce.ShouldLog("SkyVisibilityResolver." + type.Name + "." + memberName + "#static"))
                    Debug.LogWarning("[RadioNav][Sky] static member not found: " + type.Name + "." + memberName);
                return null;
            }
            return cached is PropertyInfo property ? property.GetValue(null, null) : ((FieldInfo)cached).GetValue(null);
        }

        private static bool ReadBool(object target, string memberName)
        {
            object value = ReadMember(target, memberName);
            return value != null && Convert.ToBoolean(value);
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
        /// True when <paramref name="mode"/> equals the named enum value; the
        /// parsed value is cached so no per-frame Enum.ToString() allocation.
        /// </summary>
        private static bool IsMode(object mode, string name, ref bool resolved, ref object value)
        {
            if (!resolved)
            {
                resolved = true;
                Type modeType = mode.GetType();
                if (modeType.IsEnum)
                {
                    try { value = Enum.Parse(modeType, name); }
                    catch (ArgumentException) { value = null; }
                }
            }
            return value != null && value.Equals(mode);
        }

        /// <summary>
        /// Hot-reload hygiene: forget cached enum values. Member lookups live
        /// in TypeMemberCache, cleared by the bootstrapper.
        /// </summary>
        public static void Reset()
        {
            mapModeResolved = false;
            mapModeValue = null;
        }
    }
}
