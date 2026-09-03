using DWS.Common.InjectionFramework;
using System;
using System.Collections.Generic;
using System.Reflection;
using UBOAT.Game.Scene.Environment;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Matches STAR_SKY visibility to the game's night transition and fog.</summary>
    internal static class SkyVisibilityResolver
    {
        private static readonly Dictionary<string, MemberInfo> memberCache = new Dictionary<string, MemberInfo>();
        // P5.1: Type.GetType with assembly-qualified name is a string parse +
        // assembly scan per call; IsPlayerOutside hits it every frame — type
        // resolution is cached centrally in GameTypeCache.

        [Inject] private static Weather weather;

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
            catch
            {
                // A scene transition can temporarily invalidate injected Unity references.
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
            catch
            {
                // A scene transition can temporarily invalidate injected Unity references.
                return 1f;
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
                if (mode != null && mode.ToString() == "Map")
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static object ReadStatic(string typeName, string memberName)
        {
            Type type = GameTypeCache.Resolve(typeName);
            if (type == null)
                return null;

            string cacheKey = typeName + "." + memberName + "#static";
            if (!memberCache.TryGetValue(cacheKey, out MemberInfo cached))
            {
                cached = (MemberInfo)type.GetProperty(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? type.GetField(memberName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                memberCache[cacheKey] = cached;
            }

            if (cached == null)
                return null;
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

            // Resolved MemberInfos are cached: GetFogginess/GetVisibility run
            // per frame per lighthouse instance, reflection lookup is not free.
            string cacheKey = target.GetType().FullName + "." + memberName;
            if (!memberCache.TryGetValue(cacheKey, out MemberInfo cached))
            {
                Type type = target.GetType();
                cached = (MemberInfo)type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance)
                    ?? type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
                memberCache[cacheKey] = cached;
            }

            if (cached == null)
                return null;
            return cached is PropertyInfo property ? property.GetValue(target, null) : ((FieldInfo)cached).GetValue(target);
        }
    }
}
