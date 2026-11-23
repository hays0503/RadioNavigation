using System;
using System.Globalization;
using System.Reflection;
using DWS.Common.InjectionFramework;
using UBOAT.Game.Core.Time;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Dock;
using UBOAT.Game.Scene.Entities;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    // Copied to Source only for a live acceptance run (plan §13.34).
    public static class CurrentPlanLiveFixture
    {
        private static SandboxMobileGroup watched;
        private static Vector2 trigger;

        private static System.Collections.Generic.List<SandboxMobileGroup> Groups()
        {
            var result = new System.Collections.Generic.List<SandboxMobileGroup>();
            foreach (UnityEngine.Object item in Resources.FindObjectsOfTypeAll(typeof(SandboxMobileGroup)))
            {
                var group = item as SandboxMobileGroup;
                if (group && group.gameObject.scene.IsValid())
                    result.Add(group);
            }
            return result;
        }

        /// <summary>Docked boat: outer fairway entry, trigger and the lane points.</summary>
        public static string PassageRoute()
        {
            var ship = InjectionFramework.Instance?.GetInstance<PlayerShip>();
            if (!ship || !ship.Docked)
                return "missing";
            DockArea area = ship.CurrentDockArea;
            if (!area && ship.CurrentDock?.SpawnedEntity)
                area = DockArea.FindPreferredAutoDockArea(ship.CurrentDock.SpawnedEntity);
            if (!area || area.UndockRoute == null || area.UndockRoute.Length < 3)
                return "missing";
            trigger = Sandbox.Instance.SceneToEncounterSpace(area.transform.position);
            WorldPosition[] lane = area.UndockRoute;
            Vector2 entry = lane[lane.Length - 1];
            Vector2 prior = lane[lane.Length - 2];
            Vector2 start = entry + (prior - entry).normalized * 0.10f;
            Vector2 first = lane[lane.Length - 2];
            float heading = Mathf.Atan2(first.x - start.x, first.y - start.y) * Mathf.Rad2Deg;
            return string.Format(CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R},{3:R},{4:R},{5},{6}",
                start.x, start.y, trigger.x, trigger.y, heading,
                (entry - trigger).magnitude, lane.Length);
        }

        public static bool Place(double x, double y)
        {
            var group = InjectionFramework.Instance?.GetInstance<SandboxPlayerWolfpack>();
            if (!group)
                return false;
            group.SetPosition(new Vector2((float)x, (float)y), true);
            return true;
        }

        /// <summary>docked,x,y,route,distance to the berth trigger in km.</summary>
        public static string Progress()
        {
            var ship = InjectionFramework.Instance?.GetInstance<PlayerShip>();
            if (!ship)
                return "missing";
            Vector2 point = Sandbox.Instance.SceneToEncounterSpace(ship.transform.position);
            return string.Format(CultureInfo.InvariantCulture, "{0},{1:R},{2:R},{3},{4:R}",
                ship.Docked ? 1 : 0, point.x, point.y, ship.PrivateRoute.Count,
                (point - trigger).magnitude);
        }

        // Supplemental P2 trace. Ground movement comes from the scene body,
        // not the stock scalar ship speed used by native route steering.
        public static string FairwayMotion()
        {
            var ship = InjectionFramework.Instance?.GetInstance<PlayerShip>();
            var time = InjectionFramework.Instance?.GetInstance<GameTime>();
            if (!ship || !ship.RigidBody || !time)
                return "missing";
            bool weather = CurrentRuntimeAdapter.TrySampleWind(out Vector3 wind, out float knots);
            Vector2 point = Sandbox.Instance.SceneToEncounterSpace(ship.RigidBody.position);
            bool covered = CurrentRuntimeAdapter.TrySampleEncounter(point, time.GmtDateTime, out Vector3 water);
            Vector3 velocity = ship.RigidBody.velocity;
            var route = ship.PrivateRoute;
            Vector2 goal = route.Count == 0 ? point : route[0];
            return string.Format(CultureInfo.InvariantCulture,
                "{0},{1:O},{2:R},{3:R},{4:R},{5:R},{6:R},{7:R},{8:R},{9:R},{10:R},{11:R},{12},{13},{14:R},{15:R}",
                time.StoryTicks, time.GmtDateTime, time.TimeScale.Value, time.SandboxTimeScale.Value,
                ship.Depth, ship.transform.eulerAngles.y, ship.AverageVelocity, velocity.x, velocity.z,
                wind.x, wind.z, knots, weather ? 1 : 0, covered ? 1 : 0, goal.x, goal.y)
                + string.Format(CultureInfo.InvariantCulture, ",{0:R},{1:R}", water.x, water.z);
        }

        public static string FairwayDrift() => CurrentRuntimeAdapter.Describe();

        public static bool DepartureCacheReady()
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            var skipper = typeof(CurrentFairway).GetField("lastSkipper", flags);
            var lane = typeof(CurrentFairway).GetField("lastLane", flags);
            var ship = InjectionFramework.Instance?.GetInstance<PlayerShip>();
            return ship && ship.Docked && ReferenceEquals(skipper?.GetValue(null), ship)
                && lane?.GetValue(null) is WorldPosition[] points && points.Length >= 2;
        }

        private static readonly System.Collections.Generic.HashSet<int> Before =
            new System.Collections.Generic.HashSet<int>();

        /// <summary>Remembers which groups were already spawned, like the proven far receipt.</summary>
        public static bool WatchStart()
        {
            Before.Clear();
            watched = null;
            foreach (var group in Groups())
                if (group.SpawnState != SpawnState.NotSpawned)
                    Before.Add(group.GetInstanceID());
            return true;
        }

        /// <summary>Picks the nearest newly spawned group and keeps it for the measurement.</summary>
        public static string WatchPick()
        {
            try
            {
                var player = InjectionFramework.Instance?.GetInstance<PlayerShip>();
                Vector2 origin = player ? player.SandboxEntity.Position : Vector2.zero;
                SandboxMobileGroup nearest = null;
                float best = float.MaxValue;
                foreach (var group in Groups())
                {
                    if (!group || group.SpawnState != SpawnState.Spawned
                        || Before.Contains(group.GetInstanceID()))
                        continue;
                    float distance = (group.Position - origin).sqrMagnitude;
                    if (distance >= best)
                        continue;
                    best = distance;
                    nearest = group;
                }
                watched = nearest;
                return nearest ? "spawned" : "missing";
            }
            catch (Exception ex)
            {
                return "error:" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        /// <summary>World position of the watched ship: ticks,x,y,z and its velocity.</summary>
        public static string HandoffShip()
        {
            if (!watched || watched.SpawnState != SpawnState.Spawned)
                return "missing";
            var ship = watched.Entities.Count == 1 ? watched.Entities[0].SpawnedEntity as Ship : null;
            if (!ship)
                return "missing";
            var time = InjectionFramework.Instance?.GetInstance<GameTime>();
            Vector3 point = ship.RigidBody.position;
            Vector3 velocity = ship.RigidBody.velocity;
            return string.Format(CultureInfo.InvariantCulture, "{0},{1:R},{2:R},{3:R},{4:R},{5:R},{6:R}",
                time ? time.StoryTicks : 0L, point.x, point.y, point.z, velocity.x, velocity.y, velocity.z);
        }

        /// <summary>Native unspawn, then the far body's vector against stock plus the field.</summary>
        public static string HandoffUnspawn()
        {
            if (!watched)
                return "missing";
            try
            {
                watched.Unspawn();
            var body = watched.GetComponent<Rigidbody2D>();
            CurrentRuntimeAdapter.TrySampleEncounter(watched.Position, CurrentRuntimeAdapter.GameUtc(),
                out Vector3 water);
            Vector2 stock = watched.Up * watched.Velocity;
            Vector2 expected = CurrentStrategicStep.WorldVelocity(stock, water);
            return string.Format(CultureInfo.InvariantCulture, "{0},{1:R},{2:R},{3:R},{4:R},{5:R},{6:R},{7}",
                (int)watched.SpawnState, body ? body.velocity.x : 0f, body ? body.velocity.y : 0f,
                expected.x, expected.y, water.x, water.z, watched.LastUpdateTicks);
            }
            catch (Exception ex)
            {
                return "error:" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        /// <summary>Far group's own position and tick for a rate over a window.</summary>
        public static string HandoffFar()
        {
            if (!watched)
                return "missing";
            var time = InjectionFramework.Instance?.GetInstance<GameTime>();
            return string.Format(CultureInfo.InvariantCulture, "{0},{1:R},{2:R},{3}",
                time ? time.StoryTicks : 0L, watched.Position.x, watched.Position.y,
                (int)watched.SpawnState);
        }

        public static string Forward()
        {
            if (!watched)
                return "missing";
            watched.SetPosition(watched.Position + new Vector2(0f, 3000f), true);
            return watched.Position.y.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>Remaining route points and the native motion state.</summary>
        public static string Motion()
        {
            var ship = InjectionFramework.Instance?.GetInstance<PlayerShip>();
            if (!ship)
                return "missing";
            var avoidance = typeof(Ship).GetField("collisionAvoidanceMethod",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var obstacles = typeof(Ship).GetField("obstaclesAhead",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var route = ship.PrivateRoute;
            var text = new System.Text.StringBuilder();
            text.AppendFormat(CultureInfo.InvariantCulture, "{0:R},{1:R},{2},{3},{4:R}",
                ship.TargetVelocity, ship.AverageVelocity,
                avoidance == null ? "?" : avoidance.GetValue(ship).ToString(),
                obstacles != null && (bool)obstacles.GetValue(ship), ship.Depth);
            for (int i = 0; i < route.Count; i++)
                text.AppendFormat(CultureInfo.InvariantCulture, ";{0:R},{1:R}", route[i].x, route[i].y);
            return text.ToString();
        }

        public static bool ClearLeg()
        {
            var ship = InjectionFramework.Instance?.GetInstance<PlayerShip>();
            if (!ship)
                return false;
            ship.PrivateRoute.Clear(false);
            ship.MobileSandboxGroup.Route.Clear(false);
            return ship.Route.Count == 0;
        }

        /// <summary>One native interval of an exact length, on a group with zero own speed.</summary>
        public static string Boundary(int seconds)
        {
            var origin = typeof(SandboxMobileGroup).GetField("lastUpdateTime",
                BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var group in UnityEngine.Object.FindObjectsOfType<SandboxMobileGroup>())
            {
                if (group is SandboxPlayerWolfpack || group.SpawnState != SpawnState.NotSpawned
                    || group.IsAirborne || group.IsBeingDestroyed || group.LastUpdateTicks <= 0
                    || group.Route == null || group.Route.Count == 0)
                    continue;
                var body = group.GetComponent<Rigidbody2D>();
                if (!body)
                    continue;
                group.Velocity = 0f;
                group.UpdateGroup();
                Vector2 before = group.Position;
                origin.SetValue(group, group.LastUpdateTicks - seconds * TimeSpan.TicksPerSecond);
                group.UpdateGroup();
                Vector2 after = group.Position;
                Vector2 drift = after - before;
                CurrentRuntimeAdapter.TrySampleEncounter((before + after) * 0.5f,
                    CurrentRuntimeAdapter.GameUtc(), out Vector3 water);
                return string.Format(CultureInfo.InvariantCulture,
                    "{0:R},{1:R},{2:R},{3:R},{4},{5:R},{6:R},{7:R},{8:R},{9:R},{10:R}",
                    drift.x, drift.y, body.velocity.x, body.velocity.y, group.LastUpdateTicks,
                    before.x, before.y, after.x, after.y, water.x, water.z);
            }
            return "missing";
        }
    }
}
