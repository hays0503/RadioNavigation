using System;
using System.Globalization;
using System.Text;
using DWS.Common.InjectionFramework;
using UBOAT.Game.Core.Time;
using UBOAT.Game.Sandbox;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    // Copied to Source only for a live test campaign. The bridge cannot build a
    // Vector2 argument and its object listing is capped, so this fixture keeps
    // the group it found and exposes explicit actions on it.
    public static class CurrentFarLiveFixture
    {
        private static SandboxMobileGroup watched;
        private static readonly System.Collections.Generic.HashSet<int> Before =
            new System.Collections.Generic.HashSet<int>();

        public static bool SpawnWatchStart()
        {
            Before.Clear();
            watched = null;
            foreach (SandboxMobileGroup group in All())
                if (group.SpawnState != SpawnState.NotSpawned)
                    Before.Add(group.GetInstanceID());
            return true;
        }

        public static bool SpawnWatchPick()
        {
            SandboxMobileGroup nearest = null;
            float best = float.MaxValue;
            var player = InjectionFramework.Instance != null
                ? InjectionFramework.Instance.GetInstance<UBOAT.Game.Scene.Entities.PlayerShip>() : null;
            Vector2 origin = player ? player.SandboxEntity.Position : Vector2.zero;
            foreach (SandboxMobileGroup group in All())
            {
                if (group.SpawnState == SpawnState.NotSpawned || Before.Contains(group.GetInstanceID()))
                    continue;
                float distance = (group.Position - origin).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    nearest = group;
                }
            }
            watched = nearest;
            return watched;
        }

        /// <summary>
        /// Deterministic pick for the arrival test: among unspawned groups with
        /// a route and a positive own speed, the leg closest to five kilometres.
        /// Name lookup is intentionally not used here.
        /// </summary>
        public static bool PickArrivalLeg()
        {
            SandboxMobileGroup best = null;
            float bestScore = float.MaxValue;
            foreach (SandboxMobileGroup group in All())
            {
                if (group.SpawnState != SpawnState.NotSpawned || group.Route == null
                    || group.Route.Count == 0 || group.Velocity <= 0f)
                    continue;
                float leg = (group.Route[0] - group.Position).magnitude;
                if (leg < 1f || leg > 25f)
                    continue;
                float score = Mathf.Abs(leg - 5f);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = group;
                }
            }
            watched = best;
            return watched;
        }

        public static string WatchedName() => watched ? watched.name : "";

        public static bool WatchZeroSpeed()
        {
            if (!watched)
                return false;
            watched.Velocity = 0f;
            return Mathf.Abs(watched.Velocity) < 1e-9f;
        }

        public static bool WatchUnspawn()
        {
            if (!watched)
                return false;
            watched.Unspawn();
            return true;
        }

        public static bool WatchMove(float x, float y)
        {
            if (!watched)
                return false;
            watched.SetPosition(new Vector2(x, y));
            return true;
        }

        /// <summary>Read one known group by its live object name.</summary>
        public static string Snapshot(string name)
        {
            SandboxMobileGroup group = Find(name);
            GameTime time = InjectionFramework.Instance?.GetInstance<GameTime>();
            if (!group || !time)
                return "missing";
            return Describe(group, time);
        }

        private static string Describe(SandboxMobileGroup group, GameTime time)
        {
            Vector2 waypoint = group.Route != null && group.Route.Count != 0
                ? group.Route[0] : Vector2.zero;
            var text = new StringBuilder(160);
            Append(text, (int)group.SpawnState);
            Append(text, group.Position.x);
            Append(text, group.Position.y);
            Append(text, group.Velocity);
            Append(text, group.LastUpdateTicks);
            Append(text, group.Route == null ? 0 : group.Route.Count);
            Append(text, waypoint.x);
            Append(text, waypoint.y);
            text.Append(time.StoryTicks.ToString(CultureInfo.InvariantCulture));
            return text.ToString();
        }

        private static SandboxMobileGroup Find(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            foreach (SandboxMobileGroup group in All())
                if (group.name == name || group.gameObject.name == name)
                    return group;
            return null;
        }

        // state,x,y,velocity,lastUpdateTicks,routeCount,waypointX,waypointY,storyTicks
        public static string WatchSnapshot()
        {
            GameTime time = InjectionFramework.Instance?.GetInstance<GameTime>();
            if (!watched || !time)
                return "missing";
            return Describe(watched, time);
        }

        private static void Append(StringBuilder text, int value) =>
            text.Append(value.ToString(CultureInfo.InvariantCulture)).Append(',');

        private static void Append(StringBuilder text, long value) =>
            text.Append(value.ToString(CultureInfo.InvariantCulture)).Append(',');

        private static void Append(StringBuilder text, float value) =>
            text.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append(',');

        private static System.Collections.Generic.List<SandboxMobileGroup> All()
        {
            var result = new System.Collections.Generic.List<SandboxMobileGroup>();
            UnityEngine.Object[] loaded = Resources.FindObjectsOfTypeAll(typeof(SandboxMobileGroup));
            for (int i = 0; i < loaded.Length; i++)
            {
                var group = loaded[i] as SandboxMobileGroup;
                if (group && group.gameObject.scene.IsValid())
                    result.Add(group);
            }
            return result;
        }
    }
}
