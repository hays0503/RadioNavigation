using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UBOAT.Game.Scene.Entities;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    // Keeps AI ship speed feedback (averageVelocity) in water-relative units
    // so an active ocean current does not cause the native throttle PID to
    // cut engine power while matching ground speed in the 3D scene.
    public static class CurrentShipSpeedAdapter
    {
        private const BindingFlags DeclaredInstance = BindingFlags.Instance | BindingFlags.NonPublic
            | BindingFlags.Public | BindingFlags.DeclaredOnly;
        private const BindingFlags StaticHandler = BindingFlags.Static | BindingFlags.NonPublic;

        private static readonly Dictionary<Ship, Vector3> WaterByShip = new Dictionary<Ship, Vector3>();
        private static readonly List<Ship> Expired = new List<Ship>();
        private static Func<Ship, float> getAverage;
        private static Action<Ship, float> setAverage;
        private static Func<Ship, int> getCounter;
        private static bool installed;

        private struct SpeedState
        {
            public bool Active;
            public float PreviousAverage;
            public int PreviousCounter;
        }

        internal static bool TryInstall(Harmony harmony)
        {
            if (installed)
                return true;
            var fixedUpdate = typeof(Ship).GetMethod("FixedUpdate", DeclaredInstance, null, Type.EmptyTypes, null);
            var avg = typeof(Ship).GetField("averageVelocity", DeclaredInstance);
            var counter = typeof(Ship).GetField("averageVelocityCounter", DeclaredInstance);
            var before = typeof(CurrentShipSpeedAdapter).GetMethod(nameof(BeforeFixedUpdate), StaticHandler);
            var after = typeof(CurrentShipSpeedAdapter).GetMethod(nameof(AfterFixedUpdate), StaticHandler);
            if (fixedUpdate == null || avg == null || counter == null || before == null || after == null)
            {
                Debug.LogWarning("[RadioNav][CURRENT] ship speed hooks unavailable; using native throttle feedback");
                return false;
            }
            try
            {
                harmony.Patch(fixedUpdate, prefix: new HarmonyMethod(before), postfix: new HarmonyMethod(after));
                getAverage = BuildGetter<float>(avg, "GetShipAverageVelocity");
                setAverage = BuildSetter<float>(avg, "SetShipAverageVelocity");
                getCounter = BuildGetter<int>(counter, "GetShipAverageCounter");
                installed = true;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RadioNav][CURRENT] ship speed patch rejected: " + ex.Message);
                return false;
            }
        }

        private static Func<Ship, T> BuildGetter<T>(FieldInfo field, string name)
        {
            try
            {
                var dm = new DynamicMethod(name, typeof(T), new[] { typeof(Ship) }, typeof(Ship), true);
                ILGenerator il = dm.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, field);
                il.Emit(OpCodes.Ret);
                return (Func<Ship, T>)dm.CreateDelegate(typeof(Func<Ship, T>));
            }
            catch (Exception)
            {
                return ship => (T)field.GetValue(ship);
            }
        }

        private static Action<Ship, T> BuildSetter<T>(FieldInfo field, string name)
        {
            try
            {
                var dm = new DynamicMethod(name, typeof(void), new[] { typeof(Ship), typeof(T) }, typeof(Ship), true);
                ILGenerator il = dm.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Stfld, field);
                il.Emit(OpCodes.Ret);
                return (Action<Ship, T>)dm.CreateDelegate(typeof(Action<Ship, T>));
            }
            catch (Exception)
            {
                return (ship, val) => field.SetValue(ship, val);
            }
        }

        internal static void Bind(Ship ship, Vector3 water)
        {
            if (ship == null || ship is PlayerShip)
                return;
            WaterByShip[ship] = water;
        }

        internal static void SetWater(Ship ship, Vector3 water)
        {
            if (ship != null && WaterByShip.ContainsKey(ship))
                WaterByShip[ship] = water;
        }

        internal static void Unbind(Ship ship)
        {
            if (ship != null)
                WaterByShip.Remove(ship);
            PruneDestroyed();
        }

        internal static void Clear() => WaterByShip.Clear();

        public static float ComputeThroughWaterAverage(Vector3 bodyVelocity, float linearDrag,
            float fixedDeltaTime, Vector3 water, Quaternion rotation, float timeScale, float previousAverage)
        {
            Vector3 velocity = bodyVelocity * (1f - linearDrag * fixedDeltaTime) - water;
            float updated;
            if (timeScale > 100f)
            {
                updated = Mathf.Sqrt(velocity.x * velocity.x + velocity.z * velocity.z);
            }
            else
            {
                Vector3 forward = rotation * new Vector3(0f, 0f, 1f);
                float projected = velocity.x * forward.x + velocity.z * forward.z;
                updated = previousAverage + (projected - previousAverage) * 0.012f;
            }
            return float.IsNaN(updated) ? 0f : updated;
        }

        private static bool TryGetFreeWater(Ship ship, out Vector3 water, out Rigidbody body)
        {
            water = Vector3.zero;
            body = null;
            if (!installed || ship == null || ship is PlayerShip || !WaterByShip.TryGetValue(ship, out water))
                return false;
            body = ship.RigidBody;
            if (body == null)
                return false;
            return !body.isKinematic && body.constraints != RigidbodyConstraints.FreezeAll;
        }

        private static void BeforeFixedUpdate(Ship __instance, out SpeedState __state)
        {
            __state = default(SpeedState);
            if (!TryGetFreeWater(__instance, out Vector3 water, out Rigidbody body))
                return;
            // Unity's linear drag damps world velocity (v - 0) instead of water-relative velocity (v - water).
            if (Physics.autoSimulation && body.drag > 0f && (water.x != 0f || water.z != 0f))
                body.AddForce(water * (body.mass * body.drag), ForceMode.Force);
            __state.Active = true;
            __state.PreviousAverage = getAverage(__instance);
            __state.PreviousCounter = getCounter(__instance);
        }

        private static void AfterFixedUpdate(Ship __instance, SpeedState __state)
        {
            if (!__state.Active || !TryGetFreeWater(__instance, out Vector3 water, out Rigidbody body))
                return;
            float updated = ComputeThroughWaterAverage(body.velocity, body.drag, Time.fixedDeltaTime,
                water, body.rotation, Time.timeScale, __state.PreviousAverage);
            setAverage(__instance, updated);
            if (__state.PreviousCounter <= 1)
                __instance.AverageVelocityLowFrequency = updated;
        }

        private static void PruneDestroyed()
        {
            Expired.Clear();
            foreach (var ship in WaterByShip.Keys)
                if (ship == null)
                    Expired.Add(ship);
            foreach (var ship in Expired)
                WaterByShip.Remove(ship);
        }
    }
}
