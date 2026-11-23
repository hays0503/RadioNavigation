// Offline source-transcription probe. Compares only the Perlin sequence, not full Weather.
using System;
using System.Diagnostics;
using System.Globalization;
using UnityEngine;

internal static class Program
{
    private const long HourTicks = 36_000_000_000;

    private struct Query
    {
        internal float EastKm, NorthKm, Dynamism, Seed, Override;
        internal long SinceStart;
    }

    private struct Samples
    {
        internal float SpeedNoise, AngleDegrees;
    }

    private static double sink;

    private static Vector3 Input(Query q)
    {
        float t = (float)((double)q.SinceStart * 1.0000000116860974E-07 * q.Dynamism);
        return new Vector3(t, q.EastKm * .00015f + t, q.NorthKm * .00015f - t);
    }

    private static float Next(ref Vector3 p, float seed)
    {
        p.x += seed;
        return Perlin.Noise(p) * .5f + .5f;
    }

    private static Samples ForecastTrace(Query q)
    {
        // The seven calls on coords in Weather.cs:897,909,910,915,919,922,926.
        Vector3 p = Input(q);
        var value = new Samples { SpeedNoise = Next(ref p, q.Seed) };
        Next(ref p, q.Seed); // storm bearing
        Next(ref p, q.Seed); // storm distance
        Next(ref p, q.Seed); // cloudiness
        Next(ref p, q.Seed); // rain
        Next(ref p, q.Seed); // thunder
        Next(ref p, q.Seed); // fog
        Vector3 angle = p * .25f;
        value.AngleDegrees = 360f * Next(ref angle, q.Seed);
        return value;
    }

    private static Samples SceneTrace(Query q)
    {
        // As in Weather.cs:958,969,970,977,1001,1004,1007,1017,1018,1024.
        Vector3 p = Input(q);
        var value = new Samples { SpeedNoise = Next(ref p, q.Seed) };
        for (int i = 0; i < 8; i++) Next(ref p, q.Seed);
        Vector3 angle = p * .25f;
        value.AngleDegrees = 360f * Next(ref angle, q.Seed);
        return value;
    }

    private static Samples Reduced(Query q, int skipped)
    {
        Vector3 p = Input(q);
        var value = new Samples { SpeedNoise = Next(ref p, q.Seed) };
        for (int i = 0; i < skipped; i++) p.x += q.Seed;
        Vector3 angle = p * .25f;
        value.AngleDegrees = 360f * Next(ref angle, q.Seed);
        return value;
    }

    private static float CollapsedAngle(Query q)
    {
        Vector3 p = Input(q);
        p.x += 7 * q.Seed;
        Vector3 angle = p * .25f;
        return 360f * Next(ref angle, q.Seed);
    }

    private static float BaseSpeed(Samples s, float windOverride)
    {
        // Identity curve used only to pin ordering of the override. The real
        // serialized AnimationCurve and Parameter modifiers are NOT evaluated.
        return windOverride > 0 ? windOverride : 40f * s.SpeedNoise;
    }

    private static void Check(bool ok, string name)
    {
        if (!ok) throw new InvalidOperationException(name);
    }

    private static float AngleError(float a, float b)
    {
        float d = Math.Abs(a - b) % 360;
        return Math.Min(d, 360 - d);
    }

    private static Query Make(Random rng, int i)
    {
        return new Query {
            EastKm = (float)(rng.NextDouble() * 13_000 - 8_000),
            NorthKm = (float)(rng.NextDouble() * 4_500 + 3_000),
            SinceStart = (long)(rng.NextDouble() * 5 * 365 * 24 * HourTicks),
            Seed = rng.Next(1, 1001), Dynamism = i % 2 == 0 ? .0001f : .0003f,
            Override = i % 17 == 0 ? 15f : i % 3 == 0 ? -1f : 0f
        };
    }

    private static void Verify(int count)
    {
        int differences = 0, acceleratedRoundoff = 0, civilTimeErrors = 0;
        float maxScene = 0, maxClock = 0, maxRoundoff = 0;
        var rng = new Random(4678);
        for (int i = 0; i < count; i++)
        {
            Query q = Make(rng, i);
            Samples forecast = ForecastTrace(q), scene = SceneTrace(q);
            Samples fast = Reduced(q, 6), matchingScene = Reduced(q, 8);
            Check(fast.AngleDegrees == forecast.AngleDegrees && fast.SpeedNoise == forecast.SpeedNoise,
                  "forecast_two_draws_exact");
            Check(matchingScene.AngleDegrees == scene.AngleDegrees && matchingScene.SpeedNoise == scene.SpeedNoise,
                  "scene_two_draws_exact");
            float speed = BaseSpeed(fast, q.Override);
            Check(speed == (q.Override > 0 ? q.Override : 40 * forecast.SpeedNoise), "override_order");
            float d = AngleError(scene.AngleDegrees, forecast.AngleDegrees);
            if (d > .1f) differences++;
            maxScene = Math.Max(maxScene, d);
            d = AngleError(CollapsedAngle(q), forecast.AngleDegrees);
            if (d != 0) acceleratedRoundoff++;
            maxRoundoff = Math.Max(maxRoundoff, d);
            q.SinceStart -= HourTicks; // Wrong: GmtDateTime.Ticks directly as story ticks.
            d = AngleError(Reduced(q, 6).AngleDegrees, forecast.AngleDegrees);
            if (d > .1f) civilTimeErrors++;
            maxClock = Math.Max(maxClock, d);
        }
        Check(differences > 0 && civilTimeErrors > 0 && acceleratedRoundoff > 0,
              "distinct_sources_and_rounding_negative_controls");
        Console.WriteLine("PASS count={0} scene_diffs_gt_0.1={1} scene_max_deg={2:F6} " +
                          "wrong_clock_diffs_gt_0.1={3} wrong_clock_max_deg={4:F6} " +
                          "collapsed_roundoff_cases={5} collapsed_max_deg={6:F6}",
                          count, differences, maxScene, civilTimeErrors, maxClock, acceleratedRoundoff, maxRoundoff);
    }

    private static double Batch(Query[] queries, bool reduced)
    {
        double total = 0;
        long start = Stopwatch.GetTimestamp();
        foreach (Query q in queries)
        {
            Samples v = reduced ? Reduced(q, 6) : ForecastTrace(q);
            total += v.SpeedNoise + v.AngleDegrees;
        }
        double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        sink = total;
        return ms;
    }

    private static void Measure(int n)
    {
        var rng = new Random(101);
        var q = new Query[n];
        for (int i = 0; i < n; i++) q[i] = Make(rng, i);
        for (int i = 0; i < 128; i++) { Batch(q, false); Batch(q, true); }
        double[] a = new double[180], b = new double[180];
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < a.Length; i++)
        {
            bool reverse = i % 2 != 0;
            double first = Batch(q, reverse), expected = sink;
            double second = Batch(q, !reverse);
            Check(expected == sink, "timed_checksum");
            a[i] = reverse ? second : first;
            b[i] = reverse ? first : second;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Array.Sort(a);
        Array.Sort(b);
        Console.WriteLine("BENCH batch={0} ref_p50_ms={1:F6} ref_p99_ms={2:F6} " +
                          "two_p50_ms={3:F6} two_p99_ms={4:F6} bytes={5}", n, a[89], a[178], b[89], b[178], allocated);
        Check(allocated == 0, "no_caller_allocations");
    }

    private static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (args.Length > 1 || args.Length == 1 && args[0] != "--measure") return 2;
        Verify(50_000);
        if (args.Length == 1) { Measure(264); Measure(1610); }
        return 0;
    }
}
