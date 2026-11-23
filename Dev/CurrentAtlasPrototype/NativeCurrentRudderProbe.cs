using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using UBOAT.Mods.RadioNavigation;
using UnityEngine;

// Runs only inside the copied VIIC project. Only the test rudder is registered.
internal static class NativeCurrentRudderProbe
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
    private static Component target;
    private static Vector3 water;
    private static Type rudderType;

    public static void Run(string directory)
    {
        new NativeGameBridge(137f);
        rudderType = NativeGameBridge.Game.GetType("UBOAT.Game.Scene.Items.Rudder", true);
        var fields = rudderType.Assembly.GetType("UBOAT.Game.Core.Time.GameTime", true);
        object time = FormatterServices.GetUninitializedObject(fields);
        fields.GetField("physicsTimeScale", Private).SetValue(time, 1f);
        var timeField = rudderType.GetField("gameTime", Private);
        var originalTime = timeField.GetValue(null);
        timeField.SetValue(null, time);
        var unpatched = Measure(new Vector3(0, 0, 2));
        var falseRelative = Measure(new Vector3(0, 0, 3));

        var patch = new Harmony("radionav.isolated.current.rudder");
        if (!CurrentRudderAdapter.TryInstall(patch, rudderType))
            throw new InvalidOperationException("Production rudder adapter refused the native DLL");
        var zero = Measure(new Vector3(0, 0, 2));
        water = new Vector3(0, 0, 1);
        var matching = Measure(new Vector3(0, 0, 3));
        var coflow = Measure(water);
        var reverse = Measure(new Vector3(0, 0, -1));
        var held = MeasureHeld(new Vector3(0, 0, 3));
        patch.UnpatchAll(patch.Id);
        timeField.SetValue(null, originalTime);

        using (var writer = new StreamWriter(Path.Combine(directory, "rudder.csv")))
        {
            writer.WriteLine("case,fullYaw,simpleYaw,callbackDeltaZ");
            Write(writer, "stock-2", unpatched);
            Write(writer, "stock-3", falseRelative);
            Write(writer, "zero-2", zero);
            Write(writer, "water-1-world-3", matching);
            Write(writer, "coflow", coflow);
            Write(writer, "reverse-relative", reverse);
            Write(writer, "held-3", held);
        }
    }

    private delegate void Apply(ref Vector3 velocity, float forceToVelocity);

    private static Vector3 Measure(Vector3 initial)
    {
        float ignored;
        float full = YawStep(initial, true, out ignored);
        float callbackDelta;
        float simple = YawStep(initial, false, out callbackDelta);
        return new Vector3(full, simple, callbackDelta);
    }

    private static Vector3 MeasureHeld(Vector3 initial)
    {
        CurrentRudderAdapter.SetHeld(true);
        try { return Measure(initial); }
        finally { CurrentRudderAdapter.SetHeld(false); }
    }

    private static float YawStep(Vector3 initial, bool full, out float callbackDelta)
    {
        // Do not carry native physics transforms or inertia across cases.
        var root = new GameObject("isolated-rudder");
        var body = root.AddComponent<Rigidbody>();
        body.mass = 20f;
        body.inertiaTensor = new Vector3(20f, 20f, 20f);
        body.useGravity = false;
        var controls = new GameObject("inactive-rudder");
        controls.SetActive(false);
        controls.transform.SetParent(root.transform, false);
        controls.transform.localPosition = new Vector3(0, 0, -2);
        controls.transform.localRotation = Quaternion.Euler(0, 25, 0);
        target = controls.AddComponent(rudderType);
        CurrentRudderAdapter.Bind(body, new[] { target }, water);
        Set("rigidBody", body);
        Set("forceLocator", controls.transform);
        Set("waterMassScale", 100f);
        Set("fullPhysics", full);
        Set("eulerY", 25f);
        Set("targetTurn", 25f / 35f);
        var tick = (Action)Delegate.CreateDelegate(typeof(Action), target, rudderType.GetMethod("FixedUpdate", Private));
        Physics.SyncTransforms();
        body.velocity = initial;
        Physics.autoSimulation = true;
        tick();
        Physics.autoSimulation = false;
        Physics.Simulate(0.013333333f);
        float yaw = body.angularVelocity.y;
        callbackDelta = 0f;
        if (!full)
        {
            var callback = (Apply)Delegate.CreateDelegate(typeof(Apply), target, rudderType.GetMethod("ApplyForce", Private));
            var velocity = initial;
            callback(ref velocity, 0.001f);
            callbackDelta = velocity.z - initial.z;
        }
        UnityEngine.Object.DestroyImmediate(root);
        return yaw;
    }

    private static void Write(StreamWriter writer, string name, Vector3 result) =>
        writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:R},{2:R},{3:R}",
            name, result.x, result.y, result.z));

    private static void Set(string name, object value) => rudderType.GetField(name, Private).SetValue(target, value);
}
