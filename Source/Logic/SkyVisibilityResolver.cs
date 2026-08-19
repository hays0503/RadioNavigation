using DWS.Common.InjectionFramework;
using UBOAT.Game.Scene.Environment;
using UnityEngine;

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
                if (weather == null || weather.Sky == null)
                    return 1f;

                TOD_Sky sky = weather.Sky;
                return Mathf.Clamp01((1f - sky.Atmosphere.Fogginess) * (1f - sky.LerpValue));
            }
            catch
            {
                // A scene transition can temporarily invalidate injected Unity references.
                return 1f;
            }
        }
    }
}
