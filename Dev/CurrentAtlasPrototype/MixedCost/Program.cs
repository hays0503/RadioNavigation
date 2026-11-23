// Offline synthetic workload. No Unity, weather preparation, scheduling or FPS claims.
using System;
using System.Diagnostics;
using System.Globalization;

internal static class Program
{
    private const int K = 34;
    private const int Stride = 4 * K;
    private const int NodeCount = 4096;
    private static readonly int[][] Levels = {
        new[] { 0, 10, 20, 50, 100, 200 },
        new[] { 0, 4, 10, 15, 20, 30, 40, 50, 70, 100, 125, 150, 200 }
    };
    private static readonly byte[][] Hints = MakeHints();
    private static readonly double[] Basis = new double[2 * K];
    private static double sink;

    private struct Vec
    {
        internal double U, V;

        internal static Vec Lerp(Vec a, Vec b, double f)
        {
            return new Vec { U = a.U + (b.U - a.U) * f, V = a.V + (b.V - a.V) * f };
        }
    }

    private struct Query
    {
        internal int A;
        internal bool Mixed;
        internal double X, Y, Z, EastWind, NorthWind, U, V, Exposure;
    }

    private sealed class Fixture
    {
        internal readonly Query[] Queries;
        internal readonly int[] Offsets = new int[2 * NodeCount];
        internal readonly short[] Coefficients;

        internal Fixture(int percent, int queryCount)
        {
            var rng = new Random(1729);
            int length = 0;
            for (int i = 0; i < Offsets.Length; i++)
            {
                Offsets[i] = length;
                length += Levels[Profile(i)].Length * Stride;
            }
            Coefficients = new short[length];
            for (int i = 0; i < length; i++) Coefficients[i] = (short)rng.Next(-100, 101);
            Queries = new Query[queryCount];
            for (int i = 0; i < Queries.Length; i++)
            {
                bool mixed = i * percent / 100 != (i + 1) * percent / 100;
                Queries[i] = new Query {
                    A = rng.Next(NodeCount / 4) * 4 + (mixed ? NodeCount : 0),
                    Mixed = mixed,
                    X = rng.NextDouble(), Y = rng.NextDouble(), Z = rng.NextDouble() * 200,
                    EastWind = rng.NextDouble() * 28 - 14, NorthWind = rng.NextDouble() * 28 - 14,
                    U = rng.NextDouble() * 8 - 4, V = rng.NextDouble() * 8 - 4,
                    Exposure = i % 3 == 0 ? 0 : i % 3 == 1 ? .25 : 1
                };
            }
        }
    }

    private static int Profile(int node)
    {
        return node >= NodeCount ? node & 1 : 0;
    }

    private static int Binary(int[] levels, double z)
    {
        int lo = 0, hi = levels.Length - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (z < levels[mid]) hi = mid; else lo = mid;
        }
        return lo;
    }

    private static byte[][] MakeHints()
    {
        var hints = new byte[Levels.Length][];
        for (int i = 0; i < Levels.Length; i++)
        {
            hints[i] = new byte[201];
            for (int j = 0; j <= 200; j++) hints[i][j] = (byte)Binary(Levels[i], j);
        }
        return hints;
    }

    private static int Bracket(int profile, double z, bool hint)
    {
        // Fixed integer-metre fixture levels: every bucket stays in its interval.
        // No public extrapolation contract; all callers have already checked range.
        return hint ? Hints[profile][(int)z] : Binary(Levels[profile], z);
    }

    private static Vec AtCorner(Fixture f, int node, int layer, double weight)
    {
        int start = f.Offsets[node] + layer * Stride;
        int end = start + Stride;
        double u = 0, v = 0;
        for (int i = start, b = 0; i < end; i += 4, b += 2)
        {
            double c = Basis[b], s = Basis[b + 1];
            u += (f.Coefficients[i] * c + f.Coefficients[i + 1] * s) * (1 - weight)
               + (f.Coefficients[i + Stride] * c + f.Coefficients[i + Stride + 1] * s) * weight;
            v += (f.Coefficients[i + 2] * c + f.Coefficients[i + 3] * s) * (1 - weight)
               + (f.Coefficients[i + Stride + 2] * c + f.Coefficients[i + Stride + 3] * s) * weight;
        }
        return new Vec { U = u * .001, V = v * .001 };
    }

    private static Vec CornerAtDepth(Fixture f, int node, double z, bool hint)
    {
        int p = Profile(node);
        int i = Bracket(p, z, hint);
        return AtCorner(f, node, i, (z - Levels[p][i]) / (Levels[p][i + 1] - Levels[p][i]));
    }

    private static Vec Water(Fixture f, Query q, bool hint)
    {
        if (q.Mixed)
            return Vec.Lerp(Vec.Lerp(CornerAtDepth(f, q.A, q.Z, hint),
                                     CornerAtDepth(f, q.A + 1, q.Z, hint), q.X),
                            Vec.Lerp(CornerAtDepth(f, q.A + 2, q.Z, hint),
                                     CornerAtDepth(f, q.A + 3, q.Z, hint), q.X), q.Y);
        int i = Bracket(0, q.Z, hint);
        double z = (q.Z - Levels[0][i]) / (Levels[0][i + 1] - Levels[0][i]);
        return Vec.Lerp(Vec.Lerp(AtCorner(f, q.A, i, z), AtCorner(f, q.A + 1, i, z), q.X),
                        Vec.Lerp(AtCorner(f, q.A + 2, i, z), AtCorner(f, q.A + 3, i, z), q.X), q.Y);
    }

    private static Vec Forces(Vec water, Query q)
    {
        // Axes coincide in fixture. Wind is already supplied, not fetched or
        // interpolated from game weather. Values are fixture parameters only.
        double ru = water.U - q.U, rv = water.V - q.V;
        double au = q.EastWind - q.U, av = q.NorthWind - q.V;
        return new Vec {
            U = .04 * ru * Math.Abs(ru) + q.Exposure * .0001 * au * Math.Abs(au),
            V = .04 * rv * Math.Abs(rv) + q.Exposure * .0001 * av * Math.Abs(av)
        };
    }

    private static void Require(bool ok, string name)
    {
        if (!ok) throw new InvalidOperationException(name);
    }

    private static Vec Reference(Fixture f, Query q)
    {
        Vec result = default(Vec);
        for (int j = 0; j < 4; j++)
        {
            int node = q.A + j;
            int[] levels = Levels[Profile(node)];
            int a = 0;
            while (a + 1 < levels.Length - 1 && levels[a + 1] <= q.Z) a++;
            double z = (q.Z - levels[a]) / (levels[a + 1] - levels[a]);
            double horizontal = (j % 2 == 0 ? 1 - q.X : q.X) * (j < 2 ? 1 - q.Y : q.Y);
            for (int k = 0; k < 2; k++)
            {
                int row = f.Offsets[node] + (a + k) * Stride;
                double weight = horizontal * (k == 0 ? 1 - z : z) * .001;
                for (int i = 0; i < K; i++)
                {
                    int offset = row + 4 * i;
                    double c = weight * Basis[2 * i], s = weight * Basis[2 * i + 1];
                    result.U += f.Coefficients[offset] * c + f.Coefficients[offset + 1] * s;
                    result.V += f.Coefficients[offset + 2] * c + f.Coefficients[offset + 3] * s;
                }
            }
        }
        return result;
    }

    private static void Check(Fixture f)
    {
        for (int p = 0; p < Levels.Length; p++)
            for (int i = 0; i <= 6400; i++)
                Require(Bracket(p, i / 32.0, true) == Binary(Levels[p], i / 32.0), "lookup_vs_binary");
        for (int i = 0; i < f.Queries.Length; i++)
        {
            Query q = f.Queries[i];
            Vec a = Water(f, q, false), b = Water(f, q, true);
            Require(a.U == b.U && a.V == b.V, "per_query_water_vector");
            Vec r = Reference(f, q);
            Require(Math.Abs(a.U - r.U) < 1e-12 && Math.Abs(a.V - r.V) < 1e-12,
                    "reference_weighted_sum");
            if (!q.Mixed)
            {
                q.Mixed = true;
                b = Water(f, q, true);
                Require(a.U == b.U && a.V == b.V, "common_profile_equivalent_to_mixed");
            }
            q.Exposure = 0;
            a = Forces(a, q);
            q.EastWind += 35;
            q.NorthWind -= 20;
            b = Forces(Water(f, q, false), q);
            Require(a.U == b.U && a.V == b.V, "submerged_no_wind");
        }
        Vec downwind = Forces(default(Vec), new Query { Exposure = 1, NorthWind = 10 });
        Require(downwind.U == 0 && Math.Abs(downwind.V - .01) < 1e-12, "exposed_wind_component");
    }

    private static double Batch(Fixture f, bool hint)
    {
        double sum = 0;
        long start = Stopwatch.GetTimestamp();
        foreach (Query q in f.Queries)
        {
            Vec force = Forces(Water(f, q, hint), q);
            sum += force.U - force.V;
        }
        double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        sink = sum;
        return ms;
    }

    private static void Measure(Fixture f, int percent)
    {
        for (int i = 0; i < 128; i++) { Batch(f, false); Batch(f, true); }
        var binary = new double[180];
        var hint = new double[binary.Length];
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < binary.Length; i++)
        {
            bool alternate = i % 2 != 0;
            double first = Batch(f, alternate), value = sink;
            double second = Batch(f, !alternate);
            Require(value == sink, "identical_batch_checksums");
            binary[i] = alternate ? second : first;
            hint[i] = alternate ? first : second;
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(bytes == 0, "zero_caller_allocations");
        Array.Sort(binary);
        Array.Sort(hint);
        Console.WriteLine("ROW mixed={0} batch={1} K={2} binary_p50={3:F6} binary_p99={4:F6} " +
                          "hint_p50={5:F6} hint_p99={6:F6} allocated_bytes={7}",
                          percent, f.Queries.Length, K, binary[89], binary[178], hint[89], hint[178], bytes);
    }

    private static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        if (args.Length > 1 || args.Length == 1 && args[0] != "--measure") return 2;
        for (int i = 0; i < K; i++)
        {
            Basis[i * 2] = Math.Cos(i * .17);
            Basis[i * 2 + 1] = Math.Sin(i * .17);
        }
        Console.WriteLine("START CLR={0}; 8192 nodes; shared temporal basis; timings are batch ms", Environment.Version);
        foreach (int queryCount in new[] { 264, 2048 })
        {
            foreach (int percent in new[] { 0, 10, 100 })
            {
                var f = new Fixture(percent, queryCount);
                Check(f);
                if (args.Length != 0) Measure(f, percent);
                Console.WriteLine("PASS batch={0} mixed={1}: brackets, weighted reference, common path and forces",
                                  queryCount, percent);
            }
        }
        return 0;
    }
}
