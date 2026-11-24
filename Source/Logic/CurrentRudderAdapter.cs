using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    // Only the two native hydrodynamic inputs change reference frame. Neither
    // the rudder command nor the world-space Rigidbody velocity is rewritten.
    public static class CurrentRudderAdapter
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo Labels = typeof(CodeInstruction).GetField("labels");
        private static readonly HashSet<Component> Rudders = new HashSet<Component>();
        private static readonly Dictionary<Component, NearbyBinding> Nearby = new Dictionary<Component, NearbyBinding>();
        private static readonly List<Component> Expired = new List<Component>();
        private static Rigidbody body;
        private static Vector3 water;
        private static bool held;

        private sealed class NearbyBinding
        {
            internal readonly Rigidbody Body;
            internal Vector3 Water;

            internal NearbyBinding(Rigidbody body, Vector3 water)
            {
                Body = body;
                Water = water;
            }
        }

        internal static bool TryInstall(Harmony harmony, Type rudderType)
        {
            var fixedUpdate = rudderType?.GetMethod("FixedUpdate", Instance, null, Type.EmptyTypes, null);
            var applyForce = rudderType?.GetMethod("ApplyForce", Instance, null,
                new[] { typeof(Vector3).MakeByRefType(), typeof(float) }, null);
            if (Labels == null || fixedUpdate == null || applyForce == null)
            {
                Debug.LogWarning("[RadioNav][CURRENT] rudder hooks unavailable; using native rudder");
                return false;
            }
            try
            {
                harmony.Patch(fixedUpdate, transpiler: Patch(nameof(FixedReads)));
                harmony.Patch(applyForce, transpiler: Patch(nameof(CallbackReads)));
                Debug.Log("[RadioNav][CURRENT] relative rudder hooks installed");
                return true;
            }
            catch (Exception ex)
            {
                harmony.Unpatch(fixedUpdate, HarmonyPatchType.Transpiler, harmony.Id);
                harmony.Unpatch(applyForce, HarmonyPatchType.Transpiler, harmony.Id);
                Debug.LogWarning("[RadioNav][CURRENT] relative rudder rejected: " + ex.Message);
                return false;
            }
        }

        internal static void ClearNearby()
        {
            Nearby.Clear();
        }

        internal static void Bind(Rigidbody targetBody, System.Collections.IEnumerable rudders, Vector3 flow)
        {
            Rudders.Clear();
            body = targetBody;
            water = flow;
            if (body == null || rudders == null)
                return;
            foreach (object item in rudders)
                if (item is Component rudder)
                    Rudders.Add(rudder);
        }

        internal static void BindNearby(Rigidbody targetBody, System.Collections.IEnumerable rudders, Vector3 flow)
        {
            if (targetBody == null || rudders == null)
                return;
            var binding = new NearbyBinding(targetBody, flow);
            foreach (object item in rudders)
                if (item is Component rudder)
                    Nearby[rudder] = binding;
        }

        internal static void SetNearbyWater(Rigidbody targetBody, Vector3 flow)
        {
            if (targetBody == null)
                return;
            foreach (var binding in Nearby.Values)
                if (ReferenceEquals(binding.Body, targetBody))
                    binding.Water = flow;
        }

        internal static void UnbindNearby(Rigidbody targetBody)
        {
            if (Nearby.Count == 0)
                return;
            Expired.Clear();
            foreach (var pair in Nearby)
            {
                if (pair.Key == null || pair.Value.Body == null || ReferenceEquals(pair.Value.Body, targetBody))
                    Expired.Add(pair.Key);
            }
            foreach (var key in Expired)
                Nearby.Remove(key);
        }

        internal static void SetHeld(bool docked) => held = docked;

        // The sampled water is depth-dependent when a profile is active; the
        // rudder reference frame must follow the same 10 Hz sample as drag.
        internal static void SetWater(Vector3 flow) => water = flow;

        public static Vector3 Relative(Component rudder, Vector3 world)
        {
            if (!held && body != null && Rudders.Contains(rudder))
                return IsFreeBody(body) ? world - water : world;
            if (Nearby.TryGetValue(rudder, out NearbyBinding binding))
                return IsFreeBody(binding.Body) ? world - binding.Water : world;
            return world;
        }

        public static Vector3 ReadRelative(Rigidbody nativeBody, Component rudder)
        {
            if (ReferenceEquals(nativeBody, body))
                return Relative(rudder, nativeBody.velocity);
            if (Nearby.TryGetValue(rudder, out NearbyBinding binding) && ReferenceEquals(nativeBody, binding.Body))
                return IsFreeBody(binding.Body) ? nativeBody.velocity - binding.Water : nativeBody.velocity;
            return nativeBody.velocity;
        }

        private static bool IsFreeBody(Rigidbody target)
        {
            if (target == null)
                return false;
            return !target.isKinematic && target.constraints != RigidbodyConstraints.FreezeAll;
        }

        private static IEnumerable<CodeInstruction> FixedReads(IEnumerable<CodeInstruction> original)
        {
            var code = original.ToList();
            var reads = code.Where(c => c.operand is MethodInfo method
                && method.DeclaringType == typeof(Rigidbody) && method.Name == "get_velocity").ToArray();
            if (code.Count != 238 || reads.Length != 2 || reads.Any(read => read.opcode != OpCodes.Callvirt))
                throw new InvalidOperationException("Rudder.FixedUpdate velocity reads changed");
            foreach (var read in reads)
            {
                int index = code.IndexOf(read);
                if (index < 0)
                    throw new InvalidOperationException("Rudder.FixedUpdate read not located");
                read.opcode = OpCodes.Call;
                read.operand = Method(nameof(ReadRelative));
                Insert(code, index, new CodeInstruction(OpCodes.Ldarg_0));
            }
            return code;
        }

        private static IEnumerable<CodeInstruction> CallbackReads(IEnumerable<CodeInstruction> original,
            ILGenerator generator)
        {
            var code = original.ToList();
            int[] projections = { 0, 6, 13 };
            if (code.Count != 134 || projections.Any(i => code[i].opcode != OpCodes.Ldarg_1))
                throw new InvalidOperationException("Rudder.ApplyForce projection changed");
            var relative = generator.DeclareLocal(typeof(Vector3));
            foreach (int i in projections)
            {
                code[i].opcode = OpCodes.Ldloca;
                code[i].operand = relative;
            }
            Insert(code, 0, new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldarg_1),
                new CodeInstruction(OpCodes.Ldobj, typeof(Vector3)),
                new CodeInstruction(OpCodes.Call, Method(nameof(Relative))),
                new CodeInstruction(OpCodes.Stloc, relative));
            return code;
        }

        private static void Insert(List<CodeInstruction> code, int index, params CodeInstruction[] extra)
        {
            Labels.SetValue(extra[0], Labels.GetValue(code[index]));
            Labels.SetValue(code[index], Labels.GetValue(new CodeInstruction(OpCodes.Nop)));
            code.InsertRange(index, extra);
        }

        private static MethodInfo Method(string name) => typeof(CurrentRudderAdapter).GetMethod(name, Static);
        private static HarmonyMethod Patch(string name) => new HarmonyMethod(Method(name));
    }
}
