using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.UI.DevConsole;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    // Change only the bearing read by native steering. Route geometry,
    // waypoints, throttle, avoidance and the game's ETA stay owned by UBOAT.
    public static class CurrentRouteRuntime
    {
        private const string LogPrefix = "[RadioNav][CURRENT][ROUTE] ";
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags Handlers = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly FieldInfo Labels = typeof(CodeInstruction).GetField("labels");
        private static string status = "native";
        private static float headingOffsetDegrees;
        private static float etaSeconds;
        private static int solved;
        private static readonly CurrentRouteNoticeState Notices = new CurrentRouteNoticeState();
        private static string pendingNotice;
        private static UBOAT.Game.UI.INotificationBarUI notificationBar;
        private static bool noticeUnavailable;
        private static bool installed;
        private static Vector2 etaWaypoint;
        private static PlayerShip etaShip;
        private static float etaUpdatedAt;

        internal static void TryInstall(Harmony harmony)
        {
            if (!CurrentSettings.RouteCurrentEnabled()
                || !CurrentRuntimeAdapter.WaterConfigured)
                return;
            var target = typeof(Ship).GetMethod("FixedUpdate", Hidden, null, Type.EmptyTypes, null);
            if (target == null || Labels == null)
                return;
            try
            {
                harmony.Patch(target, transpiler: new HarmonyMethod(
                    typeof(CurrentRouteRuntime).GetMethod(nameof(PatchSteeringBearing), Handlers)));
                DevConsole.AddListener("rn_current_route", Report);
                CurrentFairway.Register();
                installed = true;
                Debug.Log(LogPrefix + "current-aware steering hook installed");
            }
            catch (Exception ex)
            {
                harmony.Unpatch(target, HarmonyPatchType.Transpiler, harmony.Id);
                Debug.LogWarning(LogPrefix + "steering hook rejected; native steering retained: " + ex.Message);
            }
        }

        private static IEnumerable<CodeInstruction> PatchSteeringBearing(IEnumerable<CodeInstruction> original)
        {
            var code = original.ToList();
            var atan = typeof(Mathf).GetMethod(nameof(Mathf.Atan2), new[] { typeof(float), typeof(float) });
            var avoidance = typeof(Ship).GetField("collisionAvoidanceMethod", Hidden);
            var obstacles = typeof(Ship).GetField("obstaclesAhead", Hidden);
            int index = code.FindIndex(c => Equals(c.operand, atan) && c.opcode == OpCodes.Call);
            if (index < 4 || code.Count(c => Equals(c.operand, atan)) != 1 || avoidance == null
                || obstacles == null || !HasDirectionProjection(code, index))
                throw new InvalidOperationException("Ship.FixedUpdate bearing projection changed");

            // Keep the native x/z on the evaluation stack. Pass native
            // obstacle state as well so Wait, Turn and ForcedWait are untouched.
            var loadShip = new CodeInstruction(OpCodes.Ldarg_0);
            Labels.SetValue(loadShip, Labels.GetValue(code[index]));
            Labels.SetValue(code[index], Labels.GetValue(new CodeInstruction(OpCodes.Nop)));
            code[index].operand = typeof(CurrentRouteRuntime).GetMethod(nameof(RouteBearing));
            code.InsertRange(index, new[]
            {
                loadShip,
                new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldfld, avoidance),
                new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldfld, obstacles)
            });
            return code;
        }

        private static bool HasDirectionProjection(List<CodeInstruction> code, int index) =>
            code[index - 3].opcode == OpCodes.Ldfld && code[index - 1].opcode == OpCodes.Ldfld
            && Equals(code[index - 3].operand, typeof(Vector3).GetField(nameof(Vector3.x)))
            && Equals(code[index - 1].operand, typeof(Vector3).GetField(nameof(Vector3.z)))
            && code[index - 4].opcode == OpCodes.Ldloc_S && code[index - 2].opcode == OpCodes.Ldloc_S
            && Equals(code[index - 4].operand, code[index - 2].operand);

        public static float RouteBearing(float x, float z, Ship ship, int avoidance, bool obstacles)
        {
            float native = Mathf.Atan2(x, z);
            if (!(ship is PlayerShip player) || !player)
                return native;
            status = "native";
            etaShip = null;
            if (!(ship.Route?.Count > 0))
            {
                Notices.Reset();
                return native;
            }
            if (avoidance != 0 || obstacles || !CurrentRuntimeAdapter.TryGetRouteWater(player, out Vector3 water))
                return native;

            var body = ship.RigidBody;
            if (!body || body.isKinematic || body.constraints == RigidbodyConstraints.FreezeAll)
                return native;
            var inverse = Quaternion.Inverse(body.rotation);
            var relative = inverse * (body.velocity - water);
            if (relative.z <= 0f || !(ship.TargetVelocity > 0f))
                return native;
            var local = inverse * water;
            if (!CurrentRouteSteering.TrySolve(x, z, ship.TargetVelocity, local.x, local.z,
                    out CurrentRouteSteering.Solution fix))
            {
                status = "unreachable-native";
                ObserveNotice(true);
                return native;
            }
            float angle = Mathf.Atan2(fix.HeadingEast, fix.HeadingNorth);
            status = "corrected";
            headingOffsetDegrees = Mathf.DeltaAngle(native * Mathf.Rad2Deg, angle * Mathf.Rad2Deg);
            etaSeconds = Mathf.Sqrt(x * x + z * z) / fix.GroundMetersPerSecond;
            etaWaypoint = ship.Route.CurrentWaypoint;
            etaShip = player;
            etaUpdatedAt = Time.realtimeSinceStartup;
            solved++;
            ObserveNotice(false);
            return angle;
        }

        internal static bool Installed => installed;

        internal static int HudMinutes(PlayerShip ship)
        {
            if (!ship || ship != etaShip || ship.Docked || !(ship.Route?.Count > 0)
                || ship.Route.CurrentWaypoint != etaWaypoint || Time.realtimeSinceStartup - etaUpdatedAt > 2f)
                return 0;
            return CurrentRouteNoticeText.Minutes(etaSeconds);
        }

        private static void ObserveNotice(bool unreachable)
        {
            RouteNotice notice = Notices.Observe(unreachable, ShipTicks());
            if (notice == RouteNotice.Unreachable)
                pendingNotice = CurrentRouteNoticeText.Unreachable;
            else if (notice == RouteNotice.Restored)
                pendingNotice = CurrentRouteNoticeText.Corrected(etaSeconds);
        }

        private static long ShipTicks()
        {
            var time = DWS.Common.InjectionFramework.InjectionFramework.Instance?
                .GetInstance<UBOAT.Game.Core.Time.GameTime>();
            return time ? time.StoryTicks : 0L;
        }

        /// <summary>Called from the binder's Update: UI work stays off FixedUpdate.</summary>
        internal static void FlushNotices()
        {
            string text = pendingNotice;
            if (string.IsNullOrEmpty(text))
                return;
            pendingNotice = null;
            if (noticeUnavailable)
                return;
            if ((notificationBar as UnityEngine.Object) == null)
                notificationBar = DWS.Common.InjectionFramework.InjectionFramework.Instance?
                    .GetInstance<UBOAT.Game.UI.INotificationBarUI>();
            if ((notificationBar as UnityEngine.Object) == null)
                return;
            try
            {
                notificationBar.OpenNow("", text);
                Debug.Log(LogPrefix + "notice \"" + text + "\"");
            }
            catch (Exception ex)
            {
                noticeUnavailable = true;
                Debug.LogWarning(LogPrefix + "notification failed; route notices disabled: " + ex.Message);
            }
        }

        private static void Report(string[] args)
        {
            string leg = status == "corrected"
                ? " correctionDeg=" + headingOffsetDegrees.ToString("F3", CultureInfo.InvariantCulture)
                    + " legSimSeconds=" + etaSeconds.ToString("F1", CultureInfo.InvariantCulture)
                : "";
            Debug.Log(LogPrefix + "status=" + status + " solvedSteps=" + solved + leg);
        }
    }
}
