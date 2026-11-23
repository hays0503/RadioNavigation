using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib;
using UnityEngine;

// Replay an unchanged native course tick and an isolated slice of the native depth force block.
internal static class NativeCurrentControlsProbe
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const string Entities = "UBOAT.Game.Scene.Entities.";
    private static Assembly game;
    private static Type ship;
    private static Type playable;
    private static Vector3 water;
    private static bool relativeDepth;

    public static void Run(string directory, string signalRequest)
    {
        new NativeGameBridge(137f);
        game = NativeGameBridge.Game;
        ship = game.GetType(Entities + "Ship", true);
        playable = game.GetType(Entities + "PlayableSubmarine", true);
        var rudder = game.GetType("UBOAT.Game.Scene.Items.Rudder", true);
        var course = ship.GetMethod("FixedUpdate", Flags | BindingFlags.DeclaredOnly);
        var depth = playable.GetMethod("FixedUpdate", Flags | BindingFlags.DeclaredOnly);
        var root = new GameObject("inactive-current-controls");
        root.SetActive(false);
        var boat = root.AddComponent(game.GetType(Entities + "PlayerShip", true));
        var dynamics = new GameObject("control-physics");
        var body = dynamics.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.mass = 20f;
        body.inertiaTensor = new Vector3(20f, 20f, 20f);
        NativeGameBridge.Set(boat, "rigidBody", body);
        var control = root.AddComponent(rudder);
        var rudders = Array.CreateInstance(rudder, 1);
        rudders.SetValue(control, 0);
        NativeGameBridge.Set(boat, "rudders", rudders);
        var route = Activator.CreateInstance(game.GetType(Entities + "Route", true), new object[] { null });
        game.GetType(Entities + "Route", true).GetProperty("TargetDirection").SetValue(route, new Vector2(1f, 2f));
        NativeGameBridge.Set(boat, "groupRoute", route);
        NativeGameBridge.Set(boat, "privateRoute", Activator.CreateInstance(game.GetType(Entities + "EntityRoute", true),
            new object[] { boat }));
        ConfigureDepth(boat, root);

        var patch = new Harmony("radionav.isolated.current.controls");
        try
        {
            using (var writer = new StreamWriter(Path.Combine(directory, "controls.csv")))
            {
                writer.WriteLine("case,value1,value2");
                Write(writer, "course-stock-ahead", Course(course, boat, body, control, 2));
                Write(writer, "course-stock-world-astern", Course(course, boat, body, control, -1));
                patch.Patch(course, transpiler: Patch(nameof(CourseProjection)));
                Write(writer, "course-zero", Course(course, boat, body, control, 2));
                water = new Vector3(0, 0, -3);
                Write(writer, "course-relative-ahead", Course(course, boat, body, control, -1));
                water = new Vector3(1, 0, 0);
                route.GetType().GetProperty("TargetDirection").SetValue(route, Vector2.up);
                Write(writer, "course-beam-flow-on-heading", Course(course, boat, body, control, 2));
                ConfigureWaypoint(boat, root);
                body.position = Vector3.zero;
                Write(writer, "waypoint-on-line-beam-flow", Course(course, boat, body, control, 2));
                body.position = new Vector3(10, 0, 0);
                Write(writer, "waypoint-after-east-drift", Course(course, boat, body, control, 2));
                body.position = new Vector3(-10, 0, 0);
                Write(writer, "waypoint-after-west-drift", Course(course, boat, body, control, 2));

                patch.Patch(depth, transpiler: Patch(nameof(DepthBlock)));
                water = Vector3.zero;
                Write(writer, "depth-stock-2", Depth(depth, boat, body, 2, 0.25f));
                Write(writer, "depth-stock-3", Depth(depth, boat, body, 3, 0.25f));
                Write(writer, "depth-stock-rest", Depth(depth, boat, body, 0, 0.25f));
                relativeDepth = true;
                water = new Vector3(0, 0, 1);
                Write(writer, "depth-relative-2", Depth(depth, boat, body, 3, 0.25f));
                Write(writer, "depth-coflow", Depth(depth, boat, body, 1, 0.25f));
                Write(writer, "depth-command-reverse", Depth(depth, boat, body, 3, -0.25f));
            }
            if (!string.IsNullOrEmpty(signalRequest))
                DepthSignal(depth, boat, body, directory, int.Parse(signalRequest, CultureInfo.InvariantCulture));
        }
        finally
        {
            patch.UnpatchAll(patch.Id);
            UnityEngine.Object.DestroyImmediate(dynamics);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void ConfigureWaypoint(Component boat, GameObject root)
    {
        const string sandboxNamespace = "UBOAT.Game.Sandbox.";
        var sandbox = root.AddComponent(game.GetType(sandboxNamespace + "Sandbox", true));
        var origin = root.AddComponent(game.GetType(sandboxNamespace + "SandboxSceneOrigin", true));
        sandbox.GetType().GetField("sandboxSceneOrigin", Flags).SetValue(null, origin);
        game.GetType(Entities + "Observator", true).GetField("sandbox", Flags).SetValue(null, sandbox);
        // Seed just the route geometry; no world/navmesh or route planner.
        var route = NativeGameBridge.Get(boat, "privateRoute");
        ((List<Vector2>)NativeGameBridge.Get(route, "waypoints")).Add(new Vector2(0, .1f));
    }

    private static void ConfigureDepth(Component boat, GameObject root)
    {
        var gameTime = game.GetType("UBOAT.Game.Core.Time.GameTime", true);
        var time = FormatterServices.GetUninitializedObject(gameTime);
        NativeGameBridge.Set(time, "physicsTimeScale", 1f);
        playable.GetField("gameTime", Flags | BindingFlags.DeclaredOnly).SetValue(null, time);
        var engines = root.AddComponent(game.GetType(Entities + "Engines", true));
        var parameter = game.GetType("UBOAT.Game.Core.Data.Parameter", true);
        object enginePower = Activator.CreateInstance(parameter, "probePower");
        var addModifier = parameter.GetMethod("AddDeltaModifier", new[] { typeof(string), typeof(bool) });
        object modifier = addModifier.Invoke(enginePower, new object[] { "Probe", false });
        modifier.GetType().GetProperty("Value").SetValue(modifier, 1f);
        NativeGameBridge.Set(engines, "powerModifier", modifier);
        NativeGameBridge.Set(boat, "activeEngines", engines);
        NativeGameBridge.Set(boat, "wasFullySubmerged", true);
        NativeGameBridge.Set(boat, "currentWaterElevation", 25f);
        foreach (string name in new[] { "verticalSteersForceScale", "verticalSteersPerformance" })
        {
            object p = NativeGameBridge.Get(boat, name);
            object v = addModifier.Invoke(p, new object[] { "Probe", false });
            v.GetType().GetProperty("Value").SetValue(v, 1f);
        }
    }

    private static Vector2 Course(MethodInfo method, Component boat, Rigidbody body, Component rudder, float ground)
    {
        body.velocity = new Vector3(0, 0, ground);
        NativeGameBridge.Set(boat, "averageVelocity", 0f);
        method.Invoke(boat, null);
        return new Vector2((float)rudder.GetType().GetProperty("TargetTurn").GetValue(rudder),
            (float)NativeGameBridge.Get(boat, "averageVelocity"));
    }

    private static Vector2 Depth(MethodInfo method, Component boat, Rigidbody body, float ground, float command)
    {
        body.position = Vector3.zero;
        body.rotation = Quaternion.identity;
        body.angularVelocity = Vector3.zero;
        body.velocity = new Vector3(0, 0, ground);
        NativeGameBridge.Set(boat, "averageVelocity", ground);
        NativeGameBridge.Set(boat, "currentDepthSteersRotation", command);
        method.Invoke(boat, null);
        if ((float)NativeGameBridge.Get(boat, "averageVelocity") != ground)
            throw new InvalidOperationException("Depth slice modified shared AverageVelocity");
        Physics.autoSimulation = false;
        Physics.Simulate(0.013333333f);
        return new Vector2(body.velocity.y, body.angularVelocity.x);
    }

    private static void Write(StreamWriter writer, string name, Vector2 values) =>
        writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:R},{2:R}", name, values.x, values.y));

    /// <summary>
    /// 600 native depth ticks with a beam-free step of following water at 200,
    /// comparing the instantaneous and the ship-style filtered projections.
    /// </summary>
    private static void DepthSignal(MethodInfo depth, Component boat, Rigidbody body,
        string directory, int mode)
    {
        signalMode = mode;
        relativeDepth = true;
        water = Vector3.zero;
        var localSmoothed = 3f;
        ResetSignal(3f);
        using (var writer = new StreamWriter(Path.Combine(directory, "depth-signal.csv")))
        {
            writer.WriteLine("tick,flow,world,relative,stock,instant,smoothed,dvy,dwx");
            for (int tick = 0; tick < 600; tick++)
            {
                if (tick == 200)
                    water = new Vector3(0, 0, 1);
                body.position = Vector3.zero;
                body.rotation = Quaternion.identity;
                body.angularVelocity = Vector3.zero;
                body.velocity = new Vector3(0, 0, 3);
                float relative = tick < 200 ? 3f : 2f;
                localSmoothed += (relative - localSmoothed) * 0.012f;
                NativeGameBridge.Set(boat, "averageVelocity", mode == 0 ? 3f : 999f);
                NativeGameBridge.Set(boat, "currentDepthSteersRotation", 0.25f);
                depth.Invoke(boat, null);
                if (mode != 0)
                {
                    if ((float)NativeGameBridge.Get(boat, "averageVelocity") != 999f)
                        throw new InvalidOperationException("Depth signal mutated AverageVelocity");
                    if (mode == 2 && Mathf.Abs(SmoothedSignal - localSmoothed) > 1e-4f)
                        throw new InvalidOperationException("Filtered signal diverged from the reference filter");
                }
                Physics.autoSimulation = false;
                Physics.Simulate(0.013333333f);
                writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0},{1:R},{2:R},{3:R},{4:R},{5:R},{6:R},{7:R},{8:R}",
                    tick, tick < 200 ? 0f : 1f, 3f, relative, 3f, relative, localSmoothed,
                    body.velocity.y, body.angularVelocity.x));
            }
        }
        signalMode = 0;
        water = Vector3.zero;
    }

    private static HarmonyMethod Patch(string name) =>
        new HarmonyMethod(typeof(NativeCurrentControlsProbe).GetMethod(name, Flags));

    private static float Ahead(Vector3 predictedGround, Vector3 forward, Component boat)
    {
        var body = (Rigidbody)NativeGameBridge.Get(boat, "rigidBody");
        return Vector3.Dot(predictedGround - water * (1f - body.drag * Time.fixedDeltaTime), forward);
    }

    private static float DepthSpeed(Component boat)
    {
        var body = (Rigidbody)NativeGameBridge.Get(boat, "rigidBody");
        Vector3 forward = body.rotation * Vector3.forward;
        if (!relativeDepth)
            return (float)NativeGameBridge.Get(boat, "averageVelocity");
        float relative = Vector3.Dot(body.velocity - water, forward);
        if (signalMode == 1)
            return relative;
        // Same 0.012-per-tick low pass as Ship.FixedUpdate, over the
        // water-relative projection instead of the world projection.
        smoothedSignal += (relative - smoothedSignal) * 0.012f;
        return smoothedSignal;
    }

    private static float smoothedSignal;
    private static int signalMode;

    public static void ResetSignal(float value)
    {
        smoothedSignal = value;
    }

    public static float SmoothedSignal => smoothedSignal;


    private static IEnumerable<CodeInstruction> CourseProjection(IEnumerable<CodeInstruction> original)
    {
        var code = original.ToList();
        if (code.Count != 598 || !NamedCall(code[410], "Dot"))
            throw new InvalidOperationException("Unsupported native course IL");
        code[410].operand = Patch(nameof(Ahead)).method;
        code.Insert(410, new CodeInstruction(OpCodes.Ldarg_0));
        return code;
    }

    private static IEnumerable<CodeInstruction> DepthBlock(IEnumerable<CodeInstruction> original)
    {
        var code = original.ToList();
        if (code.Count != 842 || !NamedCall(code[512], "get_AverageVelocity")
            || !NamedCall(code[657], "AddRelativeTorque") || code[658].opcode != OpCodes.Ldarg_0)
            throw new InvalidOperationException("Unsupported native depth IL");
        code[512].operand = Patch(nameof(DepthSpeed)).method;
        // Inputs to the slice: commanded 20 m, buoyancy-control scale=1;
        // actual vertical command and fixture coefficients set on the instance.
        yield return new CodeInstruction(OpCodes.Ldc_R4, 20f);
        yield return new CodeInstruction(OpCodes.Stloc_0);
        yield return new CodeInstruction(OpCodes.Ldc_R4, 1f);
        yield return new CodeInstruction(OpCodes.Stloc, (short)9);
        for (int i = 496; i < 658; i++)
            yield return code[i];
        // This instruction carries the original exit-branch labels.
        code[658].opcode = OpCodes.Ret;
        yield return code[658];
    }

    private static bool NamedCall(CodeInstruction instruction, string name) =>
        instruction.operand is MethodInfo method && method.Name == name;
}
