using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using PlayWay.Water.Extensions;
using UnityEngine;
using UBOAT.Mods.RadioNavigation;

// F2 experiment on a disposable VIIC harness, not a RadioNavigation runtime patch.
internal static class NativeSubmergedCurrentProbe
{
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags StaticMethods = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo Labels = typeof(CodeInstruction).GetField("labels");
    private static readonly Type Hull = typeof(NeuralNetworkPhysics);
    private static NeuralNetworkPhysics target;
    private static Vector3 water;
    private static int fullTicks;
    private static int simpleTicks;
    private static int eligibleFull;
    private static int eligibleSimple;
    private static int submergedTicks;
    private static float submergedMinArea = 1f;
    private static float submergedMaxArea;
    private static long nativeTicks;
    private static int count;
    private static readonly long[] WarmDurations = new long[1200];
    private static int warmCount;
    private static string lastPath;
    private static Vector3 wind;
    private static CurrentSurfaceDragStep surfaceStep;
    private static float exposure;
    private static Vector3 waterForce;
    private static Vector3 airForce;

    public static void Install(bool surface = false)
    {
        // Runner verifies the exact water DLL before copying the harness.
        var patch = new Harmony("radionav.isolated.current.f2");
        patch.Patch(Hull.GetMethod("SimpleUpdate", InstanceFields),
            transpiler: new HarmonyMethod(Method(surface ? nameof(PatchSurfaceSimple) : nameof(PatchSimple))));
        patch.Patch(Hull.GetMethod("Execute", StaticMethods),
            transpiler: new HarmonyMethod(Method(surface ? nameof(PatchSurfaceFull) : nameof(PatchFull))));
    }

    public static void Bind(NeuralNetworkPhysics hull, float east)
    {
        target = hull;
        var fixedWater = new ConstantCurrentField(east, 0f).Sample(0, 0, 30, DateTime.UnixEpoch);
        water = new Vector3(fixedWater.EastMetersPerSecond, 0f, fixedWater.NorthMetersPerSecond);
    }

    public static void BindSurface(NeuralNetworkPhysics hull, float east, float airEast)
    {
        Bind(hull, east);
        wind = new Vector3(airEast, 0f, 0f);
        surfaceStep = new CurrentSurfaceDragStep((float)Field(hull, "linearDragCoefficient"),
            (float)Field(hull, "quadraticDragCoefficient"),
            (bool)Field(hull, "uniformDragCoefficient") ? null : (float[])Field(hull, "quadraticDragCoefficients"));
    }

    public static void SetSurfaceWater(float east)
    {
        water = new Vector3(east, 0f, 0f);
    }

    public static void RecordFieldSwitch(string directory, long step, Vector3 before, Vector3 after)
    {
        File.AppendAllText(Path.Combine(directory, "field-switch.csv"), string.Format(
            CultureInfo.InvariantCulture, "{0},{1},{2:R},{3:R},{4:R},{5:R},{6:R}\n",
            step, lastPath ?? "stock", water.x, before.x, before.z, after.x, after.z));
    }

    public static void SetExposure(float topAboveWater)
    {
        // One-metre linear transition, kept stand-side: the production step
        // has no exposure because a zero wind cancels the air term exactly.
        exposure = Mathf.Clamp01(topAboveWater);
        waterForce = airForce = Vector3.zero;
    }

    public static void RecordSurface(string directory, long step, float depth, float vx)
    {
        File.AppendAllText(Path.Combine(directory, "surface-steps.csv"), string.Format(
            CultureInfo.InvariantCulture, "{0},{1},{2:R},{3:R},{4:R},{5:R},{6:R}\n",
            step, lastPath ?? "stock", depth, exposure, vx, waterForce.x, airForce.x));
    }

    private static object Field(NeuralNetworkPhysics hull, string name) =>
        Hull.GetField(name, InstanceFields).GetValue(hull);

    public static void RecordNativeTicks(long elapsed)
    {
        nativeTicks += elapsed;
        count++;
        if (count > 200 && warmCount < WarmDurations.Length)
            WarmDurations[warmCount++] = elapsed;
    }

    public static void RecordModeBoundary(string directory, long completedStep, float speedEast)
    {
        File.AppendAllText(Path.Combine(directory, "current-mode-boundary.csv"),
            string.Format(CultureInfo.InvariantCulture, "{0},{1},{2:R}\n", completedStep,
                lastPath ?? "stock", speedEast));
    }

    public static void WriteReceipt(string directory)
    {
        Array.Sort(WarmDurations, 0, warmCount);
        File.WriteAllText(Path.Combine(directory, "current-native.csv"),
            "full,simple,eligibleFull,eligibleSimple,runnerCalls,meanRunnerUs,submergedTicks,minArea,maxArea,warmCalls,warmP50Us,warmP99Us\n" +
            string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4},{5:R},{6},{7:R},{8:R},{9},{10:R},{11:R}\n",
                fullTicks, simpleTicks, eligibleFull, eligibleSimple, count,
                1e6 * nativeTicks / System.Diagnostics.Stopwatch.Frequency / count,
                submergedTicks, submergedMinArea, submergedMaxArea, warmCount, WarmPercentile(.5), WarmPercentile(.99)));
    }

    private static double WarmPercentile(double quantile) => warmCount == 0 ? double.NaN :
        1e6 * WarmDurations[(int)Math.Ceiling(warmCount * quantile) - 1] / System.Diagnostics.Stopwatch.Frequency;

    private static Vector3 FullWater(NeuralNetworkPhysics hull, Rigidbody body, float volume, float area)
    {
        if (!ReferenceEquals(hull, target))
            return default(Vector3);
        fullTicks++;
        lastPath = "full";
        if (!IsEligible(body, volume, area))
            return default(Vector3);
        eligibleFull++;
        return water;
    }

    private static Vector3 SimpleWater(NeuralNetworkPhysics hull, Rigidbody body, float volume, float area)
    {
        if (!ReferenceEquals(hull, target))
            return default(Vector3);
        simpleTicks++;
        lastPath = "simple";
        if (!IsEligible(body, volume, area))
            return default(Vector3);
        eligibleSimple++;
        return water;
    }

    private static bool IsEligible(Rigidbody body, float volume, float area)
    {
        if (body.isKinematic || volume != 1f)
            return false;

        submergedTicks++;
        submergedMinArea = Math.Min(submergedMinArea, area);
        submergedMaxArea = Math.Max(submergedMaxArea, area);
        // The -30 m fixture is completely below its flat water plane.
        // Stock NN's projected wet-area estimate need not reach exactly one.
        return area > 0f;
    }

    private static Vector3 Relative(Vector3 world, Vector3 flow)
    {
        return IsStill(flow) ? world : world - flow;
    }

    private static Vector3 World(Vector3 relative, Vector3 flow)
    {
        return IsStill(flow) ? relative : relative + flow;
    }

    private static bool IsStill(Vector3 flow)
    {
        return flow.x == 0f && flow.z == 0f;
    }

    private static float AirWeight(float nativeWeight, Vector3 flow)
    {
        return IsStill(flow) ? nativeWeight : 0f;
    }

    private static IEnumerable<CodeInstruction> PatchSimple(IEnumerable<CodeInstruction> original, ILGenerator generator)
    {
        var code = original.ToList();
        Verify(code, 468, 154, OpCodes.Ldloca_S);
        Verify(code, 468, 273, OpCodes.Mul);
        var flow = generator.DeclareLocal(typeof(Vector3));
        Insert(code, 273, new CodeInstruction(OpCodes.Ldloc, flow), Call(nameof(AirWeight)));
        // The local is used for drag only after this point; Rigidbody.velocity
        // and stored force processing have already seen the original world value.
        Insert(code, 154, new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldarg_0),
            ReadField("rigidBody"), new CodeInstruction(OpCodes.Ldarg_0), ReadField("submergedVolumeFraction"),
            new CodeInstruction(OpCodes.Ldarg_0), ReadField("submergedAreaFraction"),
            Call(nameof(SimpleWater)), new CodeInstruction(OpCodes.Stloc, flow), Load(3),
            new CodeInstruction(OpCodes.Ldloc, flow), Call(nameof(Relative)), Store(3));
        return code;
    }

    private static IEnumerable<CodeInstruction> PatchFull(IEnumerable<CodeInstruction> original, ILGenerator generator)
    {
        var code = original.ToList();
        Verify(code, 2288, 1508, OpCodes.Ldloc_S);
        Verify(code, 2288, 1518, OpCodes.Ldloc_S);
        Verify(code, 2288, 1666, OpCodes.Stloc_S);
        Verify(code, 2288, 1693, OpCodes.Ldloca_S);
        Verify(code, 2288, 1724, OpCodes.Ldloc_S);
        Verify(code, 2288, 1790, OpCodes.Ldloc_S);
        var flow = generator.DeclareLocal(typeof(Vector3));
        var relative = generator.DeclareLocal(typeof(Vector3));
        var beforeDrag = generator.DeclareLocal(typeof(Vector3));
        object magnitude = code[1402].operand;

        // Operate backwards so indices refer to the verified original IL.
        // Restore world frame *before* native extra Rigidbody drag in either branch.
        Insert(code, 1790, Load(56), new CodeInstruction(OpCodes.Ldloc, flow),
            Call(nameof(World)), Store(56));
        Insert(code, 1724, new CodeInstruction(OpCodes.Ldloc, beforeDrag), Store(56));
        Insert(code, 1693, Load(56), new CodeInstruction(OpCodes.Stloc, beforeDrag),
            Load(56), new CodeInstruction(OpCodes.Ldloc, flow), Call(nameof(Relative)), Store(56));
        Insert(code, 1666, new CodeInstruction(OpCodes.Ldloc, flow), Call(nameof(AirWeight)));

        // Directional coefficient uses relative velocity, without shifting the
        // world value used by the NN position prediction and force callbacks.
        code[1518].opcode = OpCodes.Ldloc;
        code[1518].operand = relative;
        Insert(code, 1508, Load(37), Load(45), Load(54), Load(66), Call(nameof(FullWater)),
            new CodeInstruction(OpCodes.Stloc, flow), Load(56),
            new CodeInstruction(OpCodes.Ldloc, flow), Call(nameof(Relative)),
            new CodeInstruction(OpCodes.Stloc, relative), new CodeInstruction(OpCodes.Ldloca, relative),
            new CodeInstruction(OpCodes.Call, magnitude), Store(67));
        return code;
    }

    private static bool CanApplySurface(NeuralNetworkPhysics hull, Rigidbody body)
    {
        return ReferenceEquals(hull, target) && !body.isKinematic && (!IsStill(water) || !IsStill(wind));
    }

    private static bool SurfaceFull(NeuralNetworkPhysics hull, Rigidbody body, Vector3 velocity,
        float wet, float dry, float stepPerMass, Quaternion inverseHull, float blend,
        out Vector3 waterResult, out Vector3 airResult)
    {
        waterResult = airResult = default(Vector3);
        if (ReferenceEquals(hull, target))
            lastPath = "full";
        if (!CanApplySurface(hull, body))
            return false;
        fullTicks++;
        lastPath = "full";
        SurfaceDifference(velocity, wet, dry, stepPerMass, inverseHull, blend, out waterResult, out airResult);
        return true;
    }

    private static bool SurfaceSimple(NeuralNetworkPhysics hull, Rigidbody body, Vector3 velocity,
        float dragPart, float wetFraction, float airDensity, float stepPerMass, Quaternion rotation,
        float blend, out Vector3 waterResult, out Vector3 airResult)
    {
        waterResult = airResult = default(Vector3);
        if (ReferenceEquals(hull, target))
            lastPath = "simple";
        if (!CanApplySurface(hull, body))
            return false;
        simpleTicks++;
        lastPath = "simple";
        SurfaceDifference(velocity, dragPart * wetFraction,
            dragPart * (1f - wetFraction) * airDensity / 998.6f,
            stepPerMass, Quaternion.Inverse(rotation), blend, out waterResult, out airResult);
        return true;
    }

    private static void SurfaceDifference(Vector3 velocity, float wet, float dry, float stepPerMass,
        Quaternion inverseHull, float blend, out Vector3 waterResult, out Vector3 airResult)
    {
        Vector3 input = velocity;
        surfaceStep.Advance(ref velocity, water, wind, wet, dry * exposure, stepPerMass,
            inverseHull, blend, out waterForce, out airForce);
        // Keep the force witnesses above separate; acceptance uses the runtime delta.
        surfaceStep.Difference(input, water, wind, wet, dry, exposure, stepPerMass,
            inverseHull, blend, out waterResult, out airResult);
    }

    private static void AddFullDifference(ref Vector3 velocity, ref Vector3 stockWater, ref Vector3 stockAir,
        Vector3 waterDelta, Vector3 airDelta, float stepPerMass)
    {
        velocity += (waterDelta + airDelta) * stepPerMass;
        stockWater += waterDelta;
        stockAir += airDelta;
    }

    private static IEnumerable<CodeInstruction> PatchSurfaceFull(IEnumerable<CodeInstruction> original, ILGenerator generator)
    {
        var code = original.ToList();
        Verify(code, 2288, 1693, OpCodes.Ldloca_S);
        Verify(code, 2288, 1724, OpCodes.Ldloc_S);
        Verify(code, 2288, 1775, OpCodes.Ldloc_S);
        var waterDelta = generator.DeclareLocal(typeof(Vector3));
        var airDelta = generator.DeclareLocal(typeof(Vector3));
        var active = generator.DeclareLocal(typeof(bool));
        var accumulate = code[1775];
        var skipStockCorrection = code[1724];
        var finishEarly = LabelAt(skipStockCorrection, generator);
        var afterEarly = LabelAt(accumulate, generator);

        // Native short-circuit suppresses its small forces. Nonzero ambient
        // flow must still act, even when the body was motionless.
        Insert(code, 1775, new CodeInstruction(OpCodes.Ldloc, active),
            new CodeInstruction(OpCodes.Brfalse, afterEarly),
            Address(56), Address(98), Address(99), new CodeInstruction(OpCodes.Ldloc, waterDelta),
            new CodeInstruction(OpCodes.Ldloc, airDelta), Load(86), Call(nameof(AddFullDifference)));
        // afterEarly must remain on the ORIGINAL accumulator, not the injected block.
        MoveLabelToOriginal(code[1775], accumulate, afterEarly);
        Insert(code, 1724, new CodeInstruction(OpCodes.Ldloc, active),
            new CodeInstruction(OpCodes.Brfalse, finishEarly),
            Address(98), new CodeInstruction(OpCodes.Initobj, typeof(Vector3)),
            Address(99), new CodeInstruction(OpCodes.Initobj, typeof(Vector3)),
            new CodeInstruction(OpCodes.Br, LabelAt(code[1775], generator)));
        MoveLabelToOriginal(code[1724], skipStockCorrection, finishEarly);
        Insert(code, 1693, Load(37), Load(45), Load(56), Load(90), Load(91), Load(86),
            Load(62), Load(49), new CodeInstruction(OpCodes.Ldloca, waterDelta),
            new CodeInstruction(OpCodes.Ldloca, airDelta), Call(nameof(SurfaceFull)),
            new CodeInstruction(OpCodes.Stloc, active));
        return code;
    }

    private static IEnumerable<CodeInstruction> PatchSurfaceSimple(IEnumerable<CodeInstruction> original, ILGenerator generator)
    {
        var code = original.ToList();
        Verify(code, 468, 154, OpCodes.Ldloca_S);
        Verify(code, 468, 327, OpCodes.Ldarg_0);
        var waterDelta = generator.DeclareLocal(typeof(Vector3));
        var airDelta = generator.DeclareLocal(typeof(Vector3));
        var active = generator.DeclareLocal(typeof(bool));
        var verticalCorrection = code[327];
        var skip = LabelAt(verticalCorrection, generator);
        Insert(code, 327, new CodeInstruction(OpCodes.Ldloc, active), new CodeInstruction(OpCodes.Brfalse, skip),
            Address(3), Address(9), Address(10), new CodeInstruction(OpCodes.Ldloc, waterDelta),
            new CodeInstruction(OpCodes.Ldloc, airDelta), Load(6), Call(nameof(AddFullDifference)));
        MoveLabelToOriginal(code[327], verticalCorrection, skip);
        Insert(code, 154, Arg0(), Arg0(), ReadField("rigidBody"), Load(3),
            Arg0(), ReadField("dragPart"), Arg0(), ReadField("submergedAreaFraction"),
            Arg0(), ReadField("airDensity"), Load(6), Load(2), new CodeInstruction(OpCodes.Ldarg_1),
            new CodeInstruction(OpCodes.Ldloca, waterDelta), new CodeInstruction(OpCodes.Ldloca, airDelta),
            Call(nameof(SurfaceSimple)), new CodeInstruction(OpCodes.Stloc, active));
        return code;
    }

    private static object LabelAt(CodeInstruction instruction, ILGenerator generator)
    {
        object label = generator.DefineLabel();
        ((System.Collections.IList)Labels.GetValue(instruction)).Add(label);
        return label;
    }

    private static void MoveLabelToOriginal(CodeInstruction inserted, CodeInstruction original, object label)
    {
        ((System.Collections.IList)Labels.GetValue(inserted)).Remove(label);
        ((System.Collections.IList)Labels.GetValue(original)).Add(label);
    }

    private static void Verify(List<CodeInstruction> code, int length, int anchor, OpCode expected)
    {
        if (code.Count != length || code[anchor].opcode != expected)
            throw new InvalidOperationException("Native water IL mismatch at instruction " + anchor);
    }

    private static void Insert(List<CodeInstruction> code, int index, params CodeInstruction[] extra)
    {
        // Unity's editor facade and the game's Harmony expose Label through
        // different facade assemblies. Move the list without binding its type.
        Labels.SetValue(extra[0], Labels.GetValue(code[index]));
        Labels.SetValue(code[index], Labels.GetValue(new CodeInstruction(OpCodes.Nop)));
        code.InsertRange(index, extra);
    }

    private static MethodInfo Method(string name) => typeof(NativeSubmergedCurrentProbe).GetMethod(name, StaticMethods);
    private static CodeInstruction Call(string name) => new CodeInstruction(OpCodes.Call, Method(name));
    private static CodeInstruction Load(short local) => new CodeInstruction(OpCodes.Ldloc, local);
    private static CodeInstruction Store(short local) => new CodeInstruction(OpCodes.Stloc, local);
    private static CodeInstruction Address(short local) => new CodeInstruction(OpCodes.Ldloca, local);
    private static CodeInstruction Arg0() => new CodeInstruction(OpCodes.Ldarg_0);
    private static CodeInstruction ReadField(string name) => new CodeInstruction(OpCodes.Ldfld, Hull.GetField(name, InstanceFields));
}
