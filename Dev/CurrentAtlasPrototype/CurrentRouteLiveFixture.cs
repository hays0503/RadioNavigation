using System.Reflection;
using DWS.Common.InjectionFramework;
using PlayWay.Water;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Environment;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    // Copied to Source only for a live test campaign. The stock map order takes
    // a Vector2 which the bridge cannot construct as a reflection argument.
    public static class CurrentRouteLiveFixture
    {
        public static bool SetSingleLeg(double eastKm, double northKm)
        {
            var player = InjectionFramework.Instance?.GetInstance<PlayerShip>();
            if (player == null || player.Docked || Sandbox.Instance == null)
                return false;
            var route = player.PrivateRoute;
            if (route == null || route.Count != 0)
                return false;
            route.Set(new Vector2((float)eastKm, (float)northKm));
            route.IsPlayerOrder = true;
            return route.Count == 1;
        }

        public static float StormRadius()
        {
            var weather = InjectionFramework.Instance?.GetInstance<Weather>();
            var storm = weather == null ? null : typeof(Weather)
                .GetField("storm", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(weather) as WeatherSystem;
            return storm == null ? -1f : storm.Radius;
        }

        public static bool SetStormRadius(float metres)
        {
            var weather = InjectionFramework.Instance?.GetInstance<Weather>();
            var storm = weather == null ? null : typeof(Weather)
                .GetField("storm", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(weather) as WeatherSystem;
            var radius = typeof(WeatherSystem).GetField("radius", BindingFlags.Instance | BindingFlags.NonPublic);
            if (storm == null || radius == null || !(metres > 0f && metres <= 2000000f))
                return false;
            radius.SetValue(storm, metres);
            return storm.Radius == metres;
        }
    }
}
