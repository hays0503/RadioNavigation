using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using DWS.Common.InjectionFramework;
using UBOAT.Game.Core.Time;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Dock;
using UBOAT.Game.Scene.Entities;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    // Staged only for §13.36 high-TC and coastal-atlas verification.
    public static class HighTcLiveFixture
    {
        private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
        private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;

        private static readonly FieldInfo EtaSecondsField = typeof(CurrentRouteRuntime).GetField("etaSeconds", StaticPrivate);
        private static readonly FieldInfo EtaWaypointField = typeof(CurrentRouteRuntime).GetField("etaWaypoint", StaticPrivate);
        private static readonly FieldInfo SolvedField = typeof(CurrentRouteRuntime).GetField("solved", StaticPrivate);
        private static readonly FieldInfo AppliedWaterField = typeof(CurrentRuntimeAdapter).GetField("appliedWater", StaticPrivate);
        private static readonly FieldInfo LastUpdateField = typeof(SandboxMobileGroup).GetField("lastUpdateTime", InstancePrivate);

        public static string PlayerState()
        {
            var ship = InjectionFramework.Instance?.GetInstance<PlayerShip>();
            var time = InjectionFramework.Instance?.GetInstance<GameTime>();
            var sandbox = Sandbox.Instance;
            if (!ship || !ship.RigidBody || !time || sandbox == null)
                return "missing";
            Vector2 pos = sandbox.SceneToEncounterSpace(ship.RigidBody.position);
            Vector3 vel = ship.RigidBody.velocity;
            var route = ship.PrivateRoute;
            Vector2 wp0 = route != null && route.Count > 0 ? route[0] : pos;
            bool covered = CurrentRuntimeAdapter.TrySampleEncounter(pos, time.GmtDateTime, out Vector3 water);
            float etaS = EtaSecondsField?.GetValue(null) is float e ? e : float.NaN;
            Vector2 etaWp = EtaWaypointField?.GetValue(null) is Vector2 w ? w : Vector2.zero;
            int solved = SolvedField?.GetValue(null) is int s ? s : 0;
            int waterSteps = AppliedWaterField?.GetValue(null) is int ws ? ws : 0;
            return string.Format(CultureInfo.InvariantCulture,
                "{0},{1:O},{2:R},{3:R},{4:R},{5:R},{6:R},{7:R},{8:R},{9:R},{10},{11:R},{12:R},{13:R},{14:R},{15:R},{16},{17},{18:R},{19:R},{20}",
                time.StoryTicks, time.GmtDateTime, time.TimeScale.Value,
                pos.x, pos.y, vel.x, vel.y, vel.z, ship.transform.eulerAngles.y, ship.Depth,
                route != null ? route.Count : 0, wp0.x, wp0.y,
                etaS, etaWp.x, etaWp.y, solved, covered ? 1 : 0, water.x, water.z, waterSteps);
        }

        public static string SetPlayerRoute(double startE, double startN, int gear, string waypointsCsv)
        {
            var ship = InjectionFramework.Instance?.GetInstance<PlayerShip>();
            var wolfpack = InjectionFramework.Instance?.GetInstance<SandboxPlayerWolfpack>();
            if (!ship || ship.Docked || !wolfpack)
                return "missing";
            wolfpack.SetPosition(new Vector2((float)startE, (float)startN), true);
            ship.PrivateRoute.Clear(false);
            ship.MobileSandboxGroup.Route.Clear(false);
            var parts = waypointsCsv.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                var xy = parts[i].Split(',');
                if (xy.Length != 2)
                    continue;
                float x = float.Parse(xy[0], CultureInfo.InvariantCulture);
                float y = float.Parse(xy[1], CultureInfo.InvariantCulture);
                var wp = new Vector2(x, y);
                if (i == 0)
                    ship.PrivateRoute.Set(wp);
                else
                    ship.PrivateRoute.Add(wp);
            }
            ship.PrivateRoute.IsPlayerOrder = true;
            ship.OrderEngineGear(gear, false, 1);
            return "ok:" + ship.PrivateRoute.Count;
        }

        public static string FarGroupState(int id)
        {
            var time = InjectionFramework.Instance?.GetInstance<GameTime>();
            foreach (var g in UnityEngine.Object.FindObjectsOfType<SandboxMobileGroup>())
            {
                if (!g || g.GetInstanceID() != id)
                    continue;
                var body = g.GetComponent<Rigidbody2D>();
                Vector2 wp0 = g.Route != null && g.Route.Count > 0 ? g.Route[0] : g.Position;
                bool covered = CurrentRuntimeAdapter.TrySampleEncounter(g.Position,
                    time ? time.GmtDateTime : default(DateTime), out Vector3 water);
                return string.Format(CultureInfo.InvariantCulture,
                    "{0},{1},{2:R},{3:R},{4:R},{5:R},{6:R},{7},{8:R},{9:R},{10},{11:R},{12:R},{13},{14}",
                    time ? time.StoryTicks : 0L, (int)g.SpawnState,
                    g.Position.x, g.Position.y,
                    body ? body.velocity.x : 0f, body ? body.velocity.y : 0f,
                    g.Velocity, g.Route != null ? g.Route.Count : 0, wp0.x, wp0.y,
                    covered ? 1 : 0, water.x, water.z, g.LastUpdateTicks,
                    CurrentStrategicDrift.Describe());
            }
            return "missing";
        }

        public static string SetupFarGroup(double east, double north, string waypointsCsv, double speedKmps)
        {
            var player = InjectionFramework.Instance?.GetInstance<PlayerShip>();
            Vector2 pPos = player ? player.SandboxEntity.Position : Vector2.zero;
            foreach (var g in UnityEngine.Object.FindObjectsOfType<SandboxMobileGroup>())
            {
                if (!g || g is SandboxPlayerWolfpack || g.SpawnState != SpawnState.NotSpawned
                    || g.IsAirborne || g.IsBeingDestroyed || g.LastUpdateTicks <= 0)
                    continue;
                if ((g.Position - pPos).magnitude < 200f)
                    continue;
                g.Position = new Vector2((float)east, (float)north);
                g.Route.Clear(false);
                var parts = waypointsCsv.Split(';');
                for (int i = 0; i < parts.Length; i++)
                {
                    var xy = parts[i].Split(',');
                    if (xy.Length != 2)
                        continue;
                    var wp = new Vector2(float.Parse(xy[0], CultureInfo.InvariantCulture),
                        float.Parse(xy[1], CultureInfo.InvariantCulture));
                    if (i == 0)
                        g.Route.Set(wp);
                    else
                        g.Route.Add(wp);
                }
                if (speedKmps >= 0.0)
                    g.Velocity = (float)speedKmps;
                g.UpdateGroup();
                return g.GetInstanceID().ToString(CultureInfo.InvariantCulture);
            }
            return "missing";
        }

        public static string FindFarGroupNear(double east, double north)
        {
            var target = new Vector2((float)east, (float)north);
            SandboxMobileGroup best = null;
            float bestDist = 2.0f;
            foreach (var g in UnityEngine.Object.FindObjectsOfType<SandboxMobileGroup>())
            {
                if (!g || g is SandboxPlayerWolfpack || g.SpawnState != SpawnState.NotSpawned
                    || g.IsAirborne || g.IsBeingDestroyed)
                    continue;
                float d = (g.Position - target).magnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = g;
                }
            }
            return best ? best.GetInstanceID().ToString(CultureInfo.InvariantCulture) : "missing";
        }

        public static string ForceFarInterval(int id, int deltaSeconds)
        {
            foreach (var g in UnityEngine.Object.FindObjectsOfType<SandboxMobileGroup>())
            {
                if (!g || g.GetInstanceID() != id || LastUpdateField == null)
                    continue;
                LastUpdateField.SetValue(g, g.LastUpdateTicks - deltaSeconds * TimeSpan.TicksPerSecond);
                g.UpdateGroup();
                return FarGroupState(id);
            }
            return "missing";
        }

        public static string DumpAlliedPorts()
        {
            var sandbox = Sandbox.Instance;
            var wolfpack = InjectionFramework.Instance?.GetInstance<SandboxPlayerWolfpack>();
            if (sandbox == null || !wolfpack)
                return "missing";
            var text = new StringBuilder();
            foreach (DockArea area in Resources.FindObjectsOfTypeAll<DockArea>())
            {
                if (!area || !area.gameObject.scene.IsValid() || area.UndockRoute == null || area.UndockRoute.Length == 0)
                    continue;
                Vector2 trig = sandbox.SceneToEncounterSpace(area.transform.position);
                text.Append(area.name).Append("|trig=").Append(trig.x.ToString("R", CultureInfo.InvariantCulture))
                    .Append(',').Append(trig.y.ToString("R", CultureInfo.InvariantCulture)).Append("|lane=");
                for (int i = 0; i < area.UndockRoute.Length; i++)
                {
                    Vector2 p = area.UndockRoute[i];
                    if (i > 0) text.Append(';');
                    text.Append(p.x.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                        .Append(p.y.ToString("R", CultureInfo.InvariantCulture));
                }
                text.Append('\n');
            }
            return text.Length == 0 ? "none" : text.ToString();
        }
    }
}
