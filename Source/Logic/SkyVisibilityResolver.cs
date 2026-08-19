using DWS.Common.InjectionFramework;
using System;
using System.Reflection;
using UBOAT.Game.Scene.Environment;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Matches STAR_SKY visibility to the game's night transition and fog.</summary>
    internal static class SkyVisibilityResolver
    {
        [Inject] private static Weather weather;

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

            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
            if (property != null)
                return property.GetValue(target, null);

            FieldInfo field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
            return field == null ? null : field.GetValue(target);
        }
    }
}
