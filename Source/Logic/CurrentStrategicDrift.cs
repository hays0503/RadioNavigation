using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Entities;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Current for distant sandbox groups (plan §13.10 item 4). The game stays
    /// the only owner of each group's interval: this reads the group's own
    /// lastUpdateTime before the native advance and adds one horizontal offset
    /// from the full water vector for exactly that interval, inside the same
    /// update. Nothing is awarded
    /// retroactively after a clock advance. Surface velocity only; no local
    /// weather exists for a distant group, so no wind is applied here.
    /// </summary>
    public static class CurrentStrategicDrift
    {
        private const string LogPrefix = "[RadioNav][CURRENT][FAR] ";
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public
            | BindingFlags.NonPublic;
        private const BindingFlags HandlerFlags = BindingFlags.Static | BindingFlags.NonPublic;
        private static FieldInfo groupBody;
        private static bool enabled;
        private static int applied;
        private static int skippedLong;
        private static int noCoverage;
        private static int noOrigin;
        private static int unsupported;
        private static float maxSkippedSeconds;
        private static float lastUnsupportedSeconds;
        private static float nextUnsupportedLog;
        private static Vector2 lastOffsetKm;

        private struct Interval
        {
            public long Ticks;
            public Vector2 Position;
        }

        internal static void TryInstall(Harmony harmony)
        {
            if (!CurrentSettings.FarDriftEnabled())
                return;
            if (!CurrentRuntimeAdapter.HasField)
            {
                Debug.LogWarning(LogPrefix + CurrentSettings.FarVariable + " enabled without a water source; far drift inert");
                return;
            }
            var method = typeof(SandboxMobileGroup).GetMethod("UpdateGroup", AnyInstance, null, Type.EmptyTypes, null);
            var validateVelocity = typeof(SandboxMobileGroup).GetMethod("ValidateRigidBodyVelocity", Hidden);
            var routeChanged = typeof(SandboxMobileGroup).GetMethod("Route_Changed", Hidden);
            groupBody = typeof(SandboxMobileGroup).GetField("rigidBody2D", Hidden);
            if (method == null || groupBody == null || validateVelocity == null || routeChanged == null)
            {
                Debug.LogWarning(LogPrefix + "native group hooks not found (game update?); far drift inert:"
                    + " method=" + (method != null)
                    + " body=" + (groupBody != null) + " velocity hooks="
                    + (validateVelocity != null && routeChanged != null));
                return;
            }
            try
            {
                harmony.Patch(method, prefix: Handler(nameof(Before)), postfix: Handler(nameof(After)));
                harmony.Patch(validateVelocity, postfix: Handler(nameof(AfterVelocityReset)));
                harmony.Patch(routeChanged, postfix: Handler(nameof(AfterRouteChanged)));
                enabled = true;
                Debug.Log(LogPrefix + "installed; one offset per native group interval, max "
                    + CurrentStrategicStep.MaxSupportedSeconds.ToString(CultureInfo.InvariantCulture) + " s");
            }
            catch (Exception ex)
            {
                harmony.Unpatch(method, HarmonyPatchType.All, harmony.Id);
                harmony.Unpatch(validateVelocity, HarmonyPatchType.All, harmony.Id);
                harmony.Unpatch(routeChanged, HarmonyPatchType.All, harmony.Id);
                Debug.LogWarning(LogPrefix + "patch rejected; far groups stay stock: " + ex.Message);
            }
            if (enabled)
                TryInstallSceneHandoff(harmony);
        }

        private static HarmonyMethod Handler(string name) =>
            new HarmonyMethod(typeof(CurrentStrategicDrift).GetMethod(name, HandlerFlags));

        private static void TryInstallSceneHandoff(Harmony harmony)
        {
            var spawn = typeof(SandboxGroup).GetMethod(nameof(SandboxGroup.OnSpawned), AnyInstance,
                null, new[] { typeof(SandboxEntity) }, null);
            if (spawn == null)
            {
                Debug.LogWarning(LogPrefix + "scene handoff hook missing; native spawn velocity retained");
                return;
            }
            try
            {
                harmony.Patch(spawn, Handler(nameof(BeforeSceneHandoff)), Handler(nameof(AfterSceneHandoff)));
            }
            catch (Exception ex)
            {
                harmony.Unpatch(spawn, HarmonyPatchType.All, harmony.Id);
                Debug.LogWarning(LogPrefix + "scene handoff hook rejected; native spawn velocity retained: " + ex.Message);
            }
        }

        private struct SceneHandoffHull
        {
            public Ship Ship;
            public Rigidbody Body;
        }

        private struct SceneHandoff
        {
            public SceneHandoffHull Single;
            public List<SceneHandoffHull> GroupHulls;
            public Vector3 Water;
        }

        private static void BeforeSceneHandoff(SandboxGroup __instance, SandboxEntity sandboxEntity,
            out SceneHandoff __state)
        {
            __state = default(SceneHandoff);
            var group = __instance as SandboxMobileGroup;
            if (!IsSurfaceHandoffGroup(group))
                return;
            if (!CurrentRuntimeAdapter.TrySampleEncounter(group.Position, CurrentRuntimeAdapter.GameUtc(),
                    out Vector3 water) || (water.x == 0f && water.z == 0f))
                return;
            var entities = group.Entities;
            if (entities.Count == 1)
            {
                if (!ReferenceEquals(entities[0], sandboxEntity))
                    return;
                if (TryCaptureHandoffHull(sandboxEntity, out SceneHandoffHull single))
                    __state = new SceneHandoff { Single = single, Water = water };
                return;
            }
            var captured = new List<SceneHandoffHull>(entities.Count);
            for (int i = 0; i < entities.Count; i++)
            {
                if (TryCaptureHandoffHull(entities[i], out SceneHandoffHull item))
                    captured.Add(item);
            }
            if (captured.Count != 0)
                __state = new SceneHandoff { GroupHulls = captured, Water = water };
        }

        private static bool IsSurfaceHandoffGroup(SandboxMobileGroup group)
        {
            if (!enabled || !CurrentRuntimeAdapter.NearbyDriftEnabled || !group
                || group is SandboxPlayerWolfpack || group.IsAirborne || group.IsBeingDestroyed
                || group.SpawnState != SpawnState.Spawning || group.Route == null || group.Route.Count == 0)
                return false;
            var entities = group.Entities;
            if (entities == null)
                return false;
            return entities.Count != 0;
        }

        private static bool TryCaptureHandoffHull(SandboxEntity sandboxEntity, out SceneHandoffHull captured)
        {
            captured = default(SceneHandoffHull);
            if (sandboxEntity == null)
                return false;
            if (!sandboxEntity.Enabled)
                return false;
            var ship = sandboxEntity.SpawnedEntity as Ship;
            var transform = sandboxEntity.SpawnedTransform;
            if (!ship || ship is PlayerShip || ship is Aircraft || ship.DockPoint != null || !transform)
                return false;
            var body = transform.GetComponent<Rigidbody>();
            if (!body || body.isKinematic || body.constraints != RigidbodyConstraints.FreezeAll)
                return false;
            captured = new SceneHandoffHull { Ship = ship, Body = body };
            return true;
        }

        private static void AfterSceneHandoff(SandboxGroup __instance, SceneHandoff __state)
        {
            if (__instance.SpawnState != SpawnState.Spawned)
                return;
            if (__state.Single.Body != null)
            {
                ApplySceneHandoffHull(__state.Single, __state.Water);
                return;
            }
            if (__state.GroupHulls == null)
                return;
            for (int i = 0; i < __state.GroupHulls.Count; i++)
                ApplySceneHandoffHull(__state.GroupHulls[i], __state.Water);
        }

        private static void ApplySceneHandoffHull(SceneHandoffHull item, Vector3 water)
        {
            var body = item.Body;
            // Native initialization only releases FreezeAll and assigns the
            // launch speed on undocked bodies. Do not touch a body it left
            // frozen; add only the carried current once.
            if (!body || !item.Ship || body.isKinematic || body.constraints != RigidbodyConstraints.None
                || !CurrentRuntimeAdapter.BindHandoffHull(item.Ship, body))
                return;
            body.velocity += water;
        }

        internal static string Describe()
        {
            if (!enabled)
                return "far=off";
            return "far applied=" + applied + " skippedLong=" + skippedLong
                + " noCoverage=" + noCoverage + " noOrigin=" + noOrigin
                + " unsupported=" + unsupported
                + " lastUnsupportedS=" + lastUnsupportedSeconds.ToString("F0", CultureInfo.InvariantCulture)
                + " maxSkippedS=" + maxSkippedSeconds.ToString("F1", CultureInfo.InvariantCulture)
                + " lastOffsetKm=" + lastOffsetKm.x.ToString("F6", CultureInfo.InvariantCulture)
                + "," + lastOffsetKm.y.ToString("F6", CultureInfo.InvariantCulture);
        }

        // A skipped interval must be visible, not silent: the counter and the
        // warning name it. The log is throttled because a high time compression
        // hits every group at once.
        private static void ReportUnsupported(float seconds, string reason)
        {
            skippedLong++;
            if (seconds > maxSkippedSeconds)
                maxSkippedSeconds = seconds;
            unsupported++;
            lastUnsupportedSeconds = seconds;
            if (Time.realtimeSinceStartup < nextUnsupportedLog)
                return;
            nextUnsupportedLog = Time.realtimeSinceStartup + 10f;
            Debug.LogWarning(LogPrefix + "unsupported " + reason + " (" + seconds.ToString("F0",
                CultureInfo.InvariantCulture) + " s); no drift applied for that interval. "
                + "Far groups support up to "
                + CurrentStrategicStep.MaxSupportedSeconds.ToString("F0", CultureInfo.InvariantCulture) + " s.");
        }

        private static void Before(SandboxMobileGroup __instance, out Interval __state)
        {
            __state = default(Interval);
            if (!enabled || !Eligible(__instance))
                return;
            __state.Ticks = __instance.LastUpdateTicks;
            __state.Position = __instance.Position;
        }

        private static void After(SandboxMobileGroup __instance, Interval __state)
        {
            if (!enabled || !Eligible(__instance))
                return;
            var utc = CurrentRuntimeAdapter.GameUtc();
            long elapsed = __instance.LastUpdateTicks - __state.Ticks;
            if (__state.Ticks <= 0)
                noOrigin++;
            else if (elapsed > 0)
                Advance(__instance, __state.Position, elapsed, utc);
            SyncWorldVelocity(__instance, utc);
        }

        private static void Advance(SandboxMobileGroup group, Vector2 start, long elapsed, DateTime utc)
        {
            float seconds = elapsed / (float)TimeSpan.TicksPerSecond;
            int steps = CurrentStrategicStep.StepCount(seconds);
            if (steps == 0)
            {
                ReportUnsupported(seconds, "native interval");
                return;
            }
            Vector2 end = group.Position;
            Vector2 drift = Integrate(start, end, utc, elapsed, steps);
            if (drift.x == 0f && drift.y == 0f)
                return;
            group.Position = end + drift;
            var body = (Rigidbody2D)groupBody.GetValue(group);
            if (body)
                body.position = group.Position;
            lastOffsetKm = drift;
            applied++;
        }

        private static Vector2 Integrate(Vector2 start, Vector2 end, DateTime utc, long elapsed, int steps)
        {
            float stepSeconds = elapsed / (float)TimeSpan.TicksPerSecond / steps;
            Vector2 drift = Vector2.zero;
            for (int i = 0; i < steps; i++)
            {
                Vector2 position = CurrentStrategicStep.Midpoint(start, end, drift, i, steps);
                DateTime sampleUtc = CurrentStrategicStep.MidpointUtc(utc, elapsed, i, steps);
                if (!CurrentRuntimeAdapter.TrySampleEncounter(position, sampleUtc, out Vector3 water))
                {
                    noCoverage++;
                    continue;
                }
                if (CurrentStrategicStep.TryOffsetKm(stepSeconds, water, out Vector2 offset))
                    drift += offset;
            }
            return drift;
        }

        // Unity's 2D body predicts inter-update motion. The game advances its own
        // separate position in UpdateGroup; writing the vector here does not
        // change the scalar (through-water) speed used by its route and fuel.
        private static void SyncWorldVelocity(SandboxMobileGroup group, DateTime utc)
        {
            var body = (Rigidbody2D)groupBody.GetValue(group);
            if (!body)
                return;
            Vector2 position = group.Position;
            Vector2 stock = group.Route != null && group.Route.Count != 0 && group.Route[0] != position
                ? group.Up * group.Velocity : Vector2.zero;
            CurrentRuntimeAdapter.TrySampleEncounter(position, utc, out Vector3 water);
            body.velocity = CurrentStrategicStep.WorldVelocity(stock, water);
        }

        private static void AfterVelocityReset(SandboxMobileGroup __instance)
        {
            if (enabled && Eligible(__instance))
                SyncWorldVelocity(__instance, CurrentRuntimeAdapter.GameUtc());
        }

        private static void AfterRouteChanged(SandboxMobileGroup __instance)
        {
            // The empty-route branch bypasses ValidateRigidBodyVelocity.
            if (__instance && __instance.Route?.Count == 0)
                AfterVelocityReset(__instance);
        }

        private static bool Eligible(SandboxMobileGroup group) =>
            group && !(group is SandboxPlayerWolfpack) && !group.IsBeingDestroyed && !group.IsAirborne
            && group.SpawnState != SpawnState.Spawned && group.SpawnState != SpawnState.Spawning;
    }
}
