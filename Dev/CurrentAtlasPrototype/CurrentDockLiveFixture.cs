using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DWS.Common.InjectionFramework;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Entities;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    // Copied to Source only for a live test campaign: absolute encounter-space
    // teleports, routes and a capture of the game's own undock path.
    public static class CurrentDockLiveFixture
    {
        private static readonly List<Vector2> Captured = new List<Vector2>(16);

        // "x,y" of the current berth, or "missing".
        public static string DockPosition()
        {
            var player = InjectionFramework.Instance?.GetInstance<PlayerShip>();
            SandboxEntity dock = player ? player.CurrentDock : null;
            if (dock == null)
                return "missing";
            Vector2 position = dock.Position;
            return position.x.ToString("R", CultureInfo.InvariantCulture) + ","
                + position.y.ToString("R", CultureInfo.InvariantCulture);
        }

        public static bool Teleport(float x, float y)
        {
            var wolfpack = InjectionFramework.Instance?.GetInstance<SandboxPlayerWolfpack>();
            if (!wolfpack)
                return false;
            wolfpack.SetPosition(new Vector2(x, y), true);
            return true;
        }

        public static bool SetLegTo(float x, float y)
        {
            var player = InjectionFramework.Instance?.GetInstance<PlayerShip>();
            if (!player || player.Docked)
                return false;
            player.PrivateRoute.Set(new Vector2(x, y));
            player.PrivateRoute.IsPlayerOrder = true;
            return player.PrivateRoute.Count == 1;
        }

        // Keeps the game's automatic undock waypoints so they can be replayed
        // in reverse as a harbour entrance.
        public static int CaptureRoute()
        {
            Captured.Clear();
            var player = InjectionFramework.Instance?.GetInstance<PlayerShip>();
            IRoute route = player == null ? null : player.PrivateRoute;
            if (route == null)
                return 0;
            for (int i = 0; i < route.Count; i++)
                Captured.Add(route[i]);
            return Captured.Count;
        }

        public static int CapturedCount() => Captured.Count;

        // Reversed undock path plus the berth as the final waypoint.
        public static bool ReverseToDock(float dockX, float dockY)
        {
            var player = InjectionFramework.Instance?.GetInstance<PlayerShip>();
            if (!player || player.Docked || Captured.Count == 0)
                return false;
            player.PrivateRoute.Clear(true);
            for (int i = Captured.Count - 1; i >= 0; i--)
                player.PrivateRoute.Add(Captured[i]);
            player.PrivateRoute.Add(new Vector2(dockX, dockY));
            player.PrivateRoute.IsPlayerOrder = true;
            return player.PrivateRoute.Count > 1;
        }

        // docked,x,y,routeCount
        public static string Snapshot()
        {
            var player = InjectionFramework.Instance?.GetInstance<PlayerShip>();
            if (!player)
                return "missing";
            var text = new StringBuilder(96);
            text.Append(player.Docked ? "1" : "0").Append(',');
            Vector2 position = player.SandboxEntity.Position;
            text.Append(position.x.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            text.Append(position.y.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            text.Append(player.PrivateRoute == null ? 0 : player.PrivateRoute.Count);
            return text.ToString();
        }
    }
}
