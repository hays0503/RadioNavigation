// THROWAWAY MEASUREMENT PROTOTYPE. Synthetic data, no game/Unity references.
// Not a tide predictor, not a production atlas, no hull forces or state patches.
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

internal static class Program
{
    private const int Side = 65, Cells = 64, Nodes = Side * Side, Depths = 4;
    private const double BaseScale = 0.00025, TideScale = 0.00001;
    private const int BenchmarkFirst = 123456, BenchmarkLast = 123516, BenchmarkTime = 123480;
    private static readonly double[] DepthLevels = { 0, 30, 100, 300 };
    private static readonly double[] Periods = { 44714.16, 43200, 86164, 92950, 22357.08, 21600, 15483, 14400 };
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static double sink;
    private static int checks;
    private static StreamWriter log;
    private static string output;
    private static readonly Func<long> Allocated = AllocationCounter();

    private struct Vec
    {
        public double U, V;
        public Vec(double u, double v) { U = u; V = v; }
    }

    private struct Query
    {
        public int Node, Layer;
        public double X, Y, Z;
    }

    private sealed class Tile
    {
        public readonly int K;
        public readonly float[] Base, Coeff;
        public readonly short[] PackedBase, PackedCoeff;
        public readonly byte[] Wet;
        public readonly float[] Tide0, Tide1;

        public Tile(int k)
        {
            K = k;
            Base = new float[2 * Depths * Nodes * 2];
            PackedBase = new short[Base.Length];
            Coeff = new float[Nodes * k * 4];
            PackedCoeff = new short[Coeff.Length];
            Wet = new byte[Nodes];
            Tide0 = new float[Nodes * 2];
            Tide1 = new float[Nodes * 2];
        }

        public void Fill(int seed)
        {
            for (int n = 0; n < Nodes; n++)
            {
                Wet[n] = 1;
                double x = (n % Side) / 64.0, y = (n / Side) / 64.0;
                for (int m = 0; m < 2; m++)
                    for (int d = 0; d < Depths; d++)
                    {
                        int i = ((m * Depths + d) * Nodes + n) * 2;
                        Base[i] = (float)(0.45 * Math.Sin(x * 2 + seed * .1) + .1 * y + .03 * m - .04 * d);
                        Base[i + 1] = (float)(0.3 * Math.Cos(y * 3 + seed * .1) - .08 * x + .02 * m + .01 * d);
                    }
                for (int k = 0; k < K; k++)
                {
                    int i = (n * K + k) * 4;
                    double a = .18 / (k + 1), p = x * 1.7 + y * 2.1 + k * .7 + seed * .02;
                    Coeff[i] = (float)(a * Math.Cos(p));
                    Coeff[i + 1] = (float)(a * Math.Sin(p));
                    Coeff[i + 2] = (float)(.7 * a * Math.Cos(p + .4));
                    Coeff[i + 3] = (float)(.7 * a * Math.Sin(p + .4));
                }
            }
            Pack(Base, PackedBase, BaseScale);
            Pack(Coeff, PackedCoeff, TideScale);
        }
    }

    private sealed class Basis
    {
        public readonly double[] C, S;
        public Basis(int k) { C = new double[k]; S = new double[k]; }
        public void Set(double t)
        {
            for (int k = 0; k < C.Length; k++)
            {
                // Synthetic periods only. No astronomical/nodal inference here.
                double phase = 2 * Math.PI * t / Periods[k % Periods.Length];
                C[k] = Math.Cos(phase); S[k] = Math.Sin(phase);
            }
        }
    }

    private static Func<long> AllocationCounter()
    {
        MethodInfo m = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes);
        return m == null ? null : (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), m);
    }

    private static void Print(string s)
    {
        Console.WriteLine(s);
        log.WriteLine(DateTime.UtcNow.ToString("o", Inv) + " " + s);
        log.Flush();
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL " + name);
        checks++;
        Print("PASS " + name);
    }

    private static short Quantize(double v, double scale)
    {
        double q = Math.Round(v / scale);
        if (double.IsNaN(q) || double.IsInfinity(q) || q < -32767 || q > 32767)
            throw new InvalidDataException("Unrepresentable velocity/coefficient");
        return (short)q; // -32768 reserved, mask remains authoritative.
    }

    private static void Pack(float[] input, short[] packed, double scale)
    {
        for (int i = 0; i < input.Length; i++) packed[i] = Quantize(input[i], scale);
    }

    private static Query MakeQuery(double x, double y, double depth)
    {
        if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(depth)
            || x < 0 || y < 0 || x >= Cells || y >= Cells || depth < 0 || depth > 300)
            throw new ArgumentOutOfRangeException("query");
        int ix = (int)x, iy = (int)y, layer = 0;
        while (layer < Depths - 2 && depth > DepthLevels[layer + 1]) layer++;
        return new Query { Node = iy * Side + ix, X = x - ix, Y = y - iy, Layer = layer,
            Z = (depth - DepthLevels[layer]) / (DepthLevels[layer + 1] - DepthLevels[layer]) };
    }

    private static bool Valid(Tile t, Query q)
    {
        return t.Wet[q.Node] != 0 && t.Wet[q.Node + 1] != 0
            && t.Wet[q.Node + Side] != 0 && t.Wet[q.Node + Side + 1] != 0;
    }

    private static double Blend(double a, double b, double f) { return a + (b - a) * f; }
    private static double Bilinear(float[] a, int offset, int stride, Query q)
    {
        int i = offset + q.Node * stride, row = Side * stride;
        return Blend(Blend(a[i], a[i + stride], q.X),
            Blend(a[i + row], a[i + row + stride], q.X), q.Y);
    }

    private static double Bilinear(short[] a, int offset, int stride, Query q)
    {
        int i = offset + q.Node * stride, row = Side * stride;
        return Blend(Blend(a[i], a[i + stride], q.X),
            Blend(a[i + row], a[i + row + stride], q.X), q.Y);
    }

    private static double Background(Tile t, Query q, int component, bool packed)
    {
        int plane = Nodes * 2, off = q.Layer * plane + component;
        double a, b, c, d;
        if (packed)
        {
            a = Bilinear(t.PackedBase, off, 2, q);
            b = Bilinear(t.PackedBase, off + plane, 2, q);
            c = Bilinear(t.PackedBase, off + Depths * plane, 2, q);
            d = Bilinear(t.PackedBase, off + (Depths + 1) * plane, 2, q);
        }
        else
        {
            a = Bilinear(t.Base, off, 2, q);
            b = Bilinear(t.Base, off + plane, 2, q);
            c = Bilinear(t.Base, off + Depths * plane, 2, q);
            d = Bilinear(t.Base, off + (Depths + 1) * plane, 2, q);
        }
        return Blend(Blend(a, b, q.Z), Blend(c, d, q.Z), .35) * (packed ? BaseScale : 1);
    }

    private static Vec Sample(Tile t, Query q, Basis basis, int mode, Basis scratch, double time)
    {
        if (!Valid(t, q)) return new Vec(double.NaN, double.NaN);
        // 0 float shared basis; 1 packed shared basis; 2 packed materialized snapshots;
        // 3 float with per-object sin/cos (intentionally inefficient comparison).
        if (mode == 3) { scratch.Set(time); basis = scratch; }
        bool packed = mode == 1 || mode == 2;
        double u = Background(t, q, 0, packed), v = Background(t, q, 1, packed);
        if (mode == 2)
        {
            u += Blend(Bilinear(t.Tide0, 0, 2, q), Bilinear(t.Tide1, 0, 2, q), .4);
            v += Blend(Bilinear(t.Tide0, 1, 2, q), Bilinear(t.Tide1, 1, 2, q), .4);
        }
        else
        {
            for (int k = 0; k < t.K; k++)
            {
                int off = k * 4, stride = t.K * 4;
                double a, b, c, d;
                if (packed)
                {
                    a = Bilinear(t.PackedCoeff, off, stride, q) * TideScale;
                    b = Bilinear(t.PackedCoeff, off + 1, stride, q) * TideScale;
                    c = Bilinear(t.PackedCoeff, off + 2, stride, q) * TideScale;
                    d = Bilinear(t.PackedCoeff, off + 3, stride, q) * TideScale;
                }
                else
                {
                    a = Bilinear(t.Coeff, off, stride, q);
                    b = Bilinear(t.Coeff, off + 1, stride, q);
                    c = Bilinear(t.Coeff, off + 2, stride, q);
                    d = Bilinear(t.Coeff, off + 3, stride, q);
                }
                u += a * basis.C[k] + b * basis.S[k];
                v += c * basis.C[k] + d * basis.S[k];
            }
        }
        return new Vec(u, v);
    }

    private static void Materialize(Tile t, Basis basis, float[] target)
    {
        for (int n = 0; n < Nodes; n++)
        {
            double u = 0, v = 0;
            for (int k = 0; k < t.K; k++)
            {
                int i = (n * t.K + k) * 4;
                u += (t.PackedCoeff[i] * basis.C[k] + t.PackedCoeff[i + 1] * basis.S[k]) * TideScale;
                v += (t.PackedCoeff[i + 2] * basis.C[k] + t.PackedCoeff[i + 3] * basis.S[k]) * TideScale;
            }
            target[n * 2] = (float)u; target[n * 2 + 1] = (float)v;
        }
    }

    private static double Distance(Vec a, Vec b)
    {
        double u = a.U - b.U, v = a.V - b.V;
        return Math.Sqrt(u * u + v * v);
    }

    private static byte[] Encode(Tile tile)
    {
        using (var memory = new MemoryStream())
        {
            using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, true))
            {
                writer.Write(0x3141434F); // OCA1, experimental LE block.
                writer.Write(Side); writer.Write(Depths); writer.Write(tile.K);
                writer.Write(BaseScale); writer.Write(TideScale);
                foreach (short x in tile.PackedBase) writer.Write(x);
                foreach (short x in tile.PackedCoeff) writer.Write(x);
                writer.Write(tile.Wet);
            }
            return memory.ToArray();
        }
    }

    private static Tile Decode(byte[] data)
    {
        using (var reader = new BinaryReader(new MemoryStream(data, false)))
        {
            if (reader.ReadInt32() != 0x3141434F || reader.ReadInt32() != Side
                || reader.ReadInt32() != Depths) throw new InvalidDataException("Header");
            int k = reader.ReadInt32();
            if (k < 1 || k > 34) throw new InvalidDataException("Constituent count");
            long expected = checked(32L + 2L * (2 * Depths * Nodes * 2 + Nodes * k * 4) + Nodes);
            if (data.LongLength != expected || reader.ReadDouble() != BaseScale
                || reader.ReadDouble() != TideScale) throw new InvalidDataException("Size/scales");
            var tile = new Tile(k);
            for (int i = 0; i < tile.PackedBase.Length; i++) tile.PackedBase[i] = reader.ReadInt16();
            for (int i = 0; i < tile.PackedCoeff.Length; i++) tile.PackedCoeff[i] = reader.ReadInt16();
            if (reader.Read(tile.Wet, 0, Nodes) != Nodes) throw new EndOfStreamException();
            // Float references deliberately absent from serialized data.
            return tile;
        }
    }

    private static byte[] Compress(byte[] raw)
    {
        using (var memory = new MemoryStream())
        {
            using (var zip = new DeflateStream(memory, CompressionLevel.Fastest, true))
                zip.Write(raw, 0, raw.Length);
            return memory.ToArray();
        }
    }

    private static byte[] Expand(byte[] packed, int expected)
    {
        // Bound output before allocation, reject truncated and excess payload.
        var raw = new byte[expected];
        using (var zip = new DeflateStream(new MemoryStream(packed, false), CompressionMode.Decompress))
        {
            int n = 0;
            while (n < expected)
            {
                int count = zip.Read(raw, n, expected - n);
                if (count == 0) throw new InvalidDataException("Truncated");
                n += count;
            }
            if (zip.ReadByte() != -1) throw new InvalidDataException("Excess output");
        }
        return raw;
    }

    private static void Correctness(bool measure)
    {
        var tile = new Tile(34); tile.Fill(0);
        var b = new Basis(34); b.Set(123456);
        var unused = new Basis(34);
        var random = new Random(8128);
        double max = 0;
        for (int i = 0; i < 12000; i++)
        {
            Query q = MakeQuery(random.NextDouble() * 63.99, random.NextDouble() * 63.99, random.NextDouble() * 300);
            max = Math.Max(max, Distance(Sample(tile, q, b, 0, unused, 0), Sample(tile, q, b, 1, unused, 0)));
        }
        double bound = Math.Sqrt(2) * BaseScale / 2 + 34 * TideScale;
        Check(max <= bound, "packed-vs-float 12000 queries max=" + max.ToString("R", Inv)
            + " bound=" + bound.ToString("R", Inv));
        Query query = MakeQuery(20.25, 31.75, 40);
        Check(Distance(Sample(tile, query, b, 0, unused, 0),
            Sample(tile, query, b, 3, unused, 123456)) < 1e-12, "shared-vs-per-object basis");
        Materialize(tile, b, tile.Tide0); Materialize(tile, b, tile.Tide1);
        Check(Distance(Sample(tile, query, b, 1, unused, 0),
            Sample(tile, query, b, 2, unused, 0)) < 1e-7, "snapshot commutes with spatial interpolation");
        var start = new Basis(34); var end = new Basis(34);
        start.Set(123456); end.Set(123516);
        b.Set(123480);
        Materialize(tile, start, tile.Tide0); Materialize(tile, end, tile.Tide1);
        double err = Distance(Sample(tile, query, b, 1, unused, 0), Sample(tile, query, b, 2, unused, 0));
        Check(err < 0.0001, "synthetic 60-game-second snapshot interpolation error=" + err.ToString("R", Inv));
        tile.Wet[query.Node + Side + 1] = 0;
        Check(double.IsNaN(Sample(tile, query, b, 1, unused, 0).U), "one dry corner returns unavailable not zero");
        tile.Wet[query.Node + Side + 1] = 1;
        bool reject = false;
        try { Quantize(20, BaseScale); } catch (InvalidDataException) { reject = true; }
        Check(reject, "quantizer rejects overflow instead of clipping");
        reject = false;
        try { Quantize(double.NaN, BaseScale); } catch (InvalidDataException) { reject = true; }
        Check(reject, "quantizer rejects NaN");
        reject = false;
        try { MakeQuery(1, 1, 301); } catch (ArgumentOutOfRangeException) { reject = true; }
        Check(reject, "no depth extrapolation below configured profile");
        byte[] raw = Encode(tile), compressed = Compress(raw), expanded = Expand(compressed, raw.Length);
        Check(Hash(raw) == Hash(expanded), "bounded deflate lossless roundtrip");
        Tile decoded = Decode(expanded);
        Check(Distance(Sample(tile, query, b, 1, unused, 0),
            Sample(decoded, query, b, 1, unused, 0)) == 0, "binary sample roundtrip");
        byte[] corrupt = (byte[])raw.Clone(); corrupt[0] ^= 1;
        reject = false;
        try { Decode(corrupt); } catch (InvalidDataException) { reject = true; }
        Check(reject, "wrong magic rejected");
        corrupt = (byte[])raw.Clone(); corrupt[12] = 255;
        reject = false;
        try { Decode(corrupt); } catch (InvalidDataException) { reject = true; }
        Check(reject, "huge constituent count rejected before tile allocation");
        reject = false;
        try { Decode(new byte[32]); } catch (InvalidDataException) { reject = true; }
        Check(reject, "invalid short block rejected");
        reject = false;
        try { Expand(compressed, raw.Length - 1); } catch (InvalidDataException) { reject = true; }
        Check(reject, "decompression output cap enforced");
        File.WriteAllBytes(Path.Combine(output, "synthetic-tile.bin"), raw);
        File.WriteAllBytes(Path.Combine(output, "synthetic-tile.deflate"), compressed);
        Print("SIZES K34 raw=" + raw.Length + " deflate=" + compressed.Length
            + " (synthetic smooth tile, not real ocean compression ratio)");
        Trajectories(tile);
        if (measure)
        {
            using (var refresh = new StreamWriter(Path.Combine(output, "refresh-preservation-check.csv")))
                RefreshBenchmark(refresh, new[] { tile }, true);
        }
        else
        {
            RefreshBenchmark(null, new[] { tile }, false);
        }
    }

    private static void Trajectories(Tile tile)
    {
        // Same integrator/grid, only packed representation differs. 1 grid cell=2km.
        // Periodic synthetic domain, not an ocean trajectory validation.
        var a = new Basis(tile.K); var unused = new Basis(tile.K);
        double x0 = 20.2, y0 = 25.4, x1 = x0, y1 = y0;
        double max = 0;
        using (var csv = new StreamWriter(Path.Combine(output, "trajectories.csv")))
        {
            csv.WriteLine("hours,packed_float_endpoint_difference_m");
            for (int step = 0; step < 72 * 60; step++)
            {
                a.Set(step * 60);
                Vec f = Sample(tile, MakeQuery(x0, y0, 60), a, 0, unused, 0);
                Vec p = Sample(tile, MakeQuery(x1, y1, 60), a, 1, unused, 0);
                x0 = Wrap(x0 + f.U * .03); y0 = Wrap(y0 + f.V * .03);
                x1 = Wrap(x1 + p.U * .03); y1 = Wrap(y1 + p.V * .03);
                double e = 2000 * Math.Sqrt(Math.Pow(WrappedDelta(x0 - x1), 2) + Math.Pow(WrappedDelta(y0 - y1), 2));
                max = Math.Max(max, e);
                if (step + 1 == 360 || step + 1 == 1440 || step + 1 == 4320)
                    csv.WriteLine(((step + 1) / 60.0).ToString("R", Inv) + "," + e.ToString("R", Inv));
            }
        }
        // Deliberately loose guard for this synthetic fixture, not a release tolerance.
        Check(max < 150, "synthetic passive 72h packed-vs-float max separation m=" + max.ToString("R", Inv));
    }

    private static double Wrap(double x) { return (x % Cells + Cells) % Cells; }
    private static double WrappedDelta(double x) { return x > 32 ? x - 64 : x < -32 ? x + 64 : x; }
    private static string Hash(byte[] bytes)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    private static void MakeWorkload(Tile[] tiles, int objects, out Query[] queries, out int[] indices)
    {
        queries = new Query[objects];
        indices = new int[objects];
        var random = new Random(19281);
        for (int i = 0; i < objects; i++)
        {
            queries[i] = MakeQuery(random.NextDouble() * 63.999, random.NextDouble() * 63.999, random.NextDouble() * 300);
            indices[i] = random.Next(tiles.Length);
        }
    }

    private static void Anchor(Tile[] tiles, double first, double second)
    {
        // Explicitly restore both anchor planes for a workload. RefreshBenchmark no longer
        // writes tile snapshots, but this keeps the benchmark valid even if that changes.
        var a = new Basis(tiles[0].K); a.Set(first);
        var b = new Basis(tiles[0].K); b.Set(second);
        for (int i = 0; i < tiles.Length; i++)
        {
            Materialize(tiles[i], a, tiles[i].Tide0);
            Materialize(tiles[i], b, tiles[i].Tide1);
        }
    }

    private static void WorkloadPreflight(Tile[] tiles, Query[] queries, int[] indices)
    {
        // Numeric equivalence at the workload timestamp, before any timing loop.
        // Mode 0 float direct, mode 1 packed direct, mode 2 packed snapshots, mode 3 per-object basis.
        int objects = queries.Length;
        var basis = new Basis(tiles[0].K); basis.Set(BenchmarkTime);
        var scratch = new Basis(tiles[0].K);
        const double h = BenchmarkLast - BenchmarkFirst;
        double sharedVsObject = 0, packedVsFloat = 0, snapshotError = 0, boundWorst = 0, cursorError = 0;
        for (int i = 0; i < queries.Length; i++)
        {
            Tile tile = tiles[indices[i]];
            Query q = queries[i];
            Vec f = SampleWorkload(tile, q, basis, 0, scratch);
            Vec p = SampleWorkload(tile, q, basis, 1, scratch);
            Vec s = SampleWorkload(tile, q, basis, 2, scratch);
            Vec o = SampleWorkload(tile, q, basis, 3, scratch);
            Vec cursor = SampleWorkload(tile, q, basis, 4, scratch);
            cursorError = Math.Max(cursorError, Distance(s, cursor));
            sharedVsObject = Math.Max(sharedVsObject, Distance(f, o));
            packedVsFloat = Math.Max(packedVsFloat, Distance(f, p));
            double error = Distance(p, s);
            snapshotError = Math.Max(snapshotError, error);
            double bound = SnapshotBound(tile, q, h);
            boundWorst = Math.Max(boundWorst, bound);
            if (!(error <= bound + 1e-7))
                throw new InvalidOperationException("snapshot preflight error " + error
                    + " exceeds bound " + bound + " at query " + i);
        }
        Check(sharedVsObject < 1e-12, "workload shared-vs-per-object basis max="
            + sharedVsObject.ToString("R", Inv) + " tiles=" + tiles.Length + " objects=" + objects);
        double packedBound = Math.Sqrt(2) * BaseScale / 2 + 34 * TideScale;
        Check(packedVsFloat <= packedBound, "workload packed-vs-float max="
            + packedVsFloat.ToString("R", Inv) + " bound=" + packedBound.ToString("R", Inv));
        Check(cursorError <= 1e-12, "workload snapshot-vs-rebuilt-cursor per-query max="
            + cursorError.ToString("R", Inv));
        Print("PREFLIGHT K=" + tiles[0].K + " tiles=" + tiles.Length + " objects=" + objects
            + " snapshot_max=" + snapshotError.ToString("R", Inv)
            + " bound_max=" + boundWorst.ToString("R", Inv)
            + " packed_vs_float=" + packedVsFloat.ToString("R", Inv));
    }

    private static double SnapshotBound(Tile tile, Query q, double h)
    {
        // h^2/8 * sum |coefficient| * omega^2 for the two velocity components.
        double u = 0, v = 0;
        for (int k = 0; k < tile.K; k++)
        {
            int off = k * 4, stride = tile.K * 4;
            double w = 2 * Math.PI / Periods[k % Periods.Length];
            double w2 = w * w;
            double a = Math.Abs(Bilinear(tile.PackedCoeff, off, stride, q) * TideScale);
            double b = Math.Abs(Bilinear(tile.PackedCoeff, off + 1, stride, q) * TideScale);
            double c = Math.Abs(Bilinear(tile.PackedCoeff, off + 2, stride, q) * TideScale);
            double d = Math.Abs(Bilinear(tile.PackedCoeff, off + 3, stride, q) * TideScale);
            u += (a + b) * w2;
            v += (c + d) * w2;
        }
        return VectorBound(u, v) * h * h / 8;
    }

    private static double VectorBound(double u, double v)
    {
        return Math.Sqrt(u * u + v * v);
    }

    private static Vec SampleWorkload(Tile tile, Query q, Basis basis, int mode, Basis scratch)
    {
        if (mode == 4)
            q = MakeQuery(q.Node % Side + q.X, q.Node / Side + q.Y,
                Blend(DepthLevels[q.Layer], DepthLevels[q.Layer + 1], q.Z));
        return Sample(tile, q, basis, mode == 4 ? 2 : mode, scratch, BenchmarkTime);
    }

    private static void MovingCursorChecks()
    {
        Check(VectorBound(3, 4) == 5, "snapshot bound composes both component bounds in Euclidean norm");
        var tile = new Tile(1);
        for (int n = 0; n < Nodes; n++)
        {
            tile.Wet[n] = 1;
            for (int month = 0; month < 2; month++)
                for (int depth = 0; depth < Depths; depth++)
                {
                    int index = ((month * Depths + depth) * Nodes + n) * 2;
                    tile.PackedBase[index] = (short)(10 * (n % Side) + 20 * depth);
                    tile.PackedBase[index + 1] = (short)(30 * (n / Side) - 40 * depth);
                }
        }
        var basis = new Basis(1);
        Query start = MakeQuery(20.2, 30.3, 40);
        Query horizontal = MakeQuery(20.8, 30.7, 40);
        Query vertical = MakeQuery(20.8, 30.7, 80);
        Check(start.Node == horizontal.Node && horizontal.Node == vertical.Node
            && start.Layer == horizontal.Layer && horizontal.Layer == vertical.Layer,
            "motion fixture stays in same horizontal cell and depth bracket");
        Vec a = SampleWorkload(tile, start, basis, 4, basis);
        Vec b = SampleWorkload(tile, horizontal, basis, 4, basis);
        Vec c = SampleWorkload(tile, vertical, basis, 4, basis);
        Check(Distance(new Vec(b.U - a.U, b.V - a.V), new Vec(6 * BaseScale, 12 * BaseScale)) < 1e-12,
            "horizontal motion recomputes weights inside one cell");
        Check(Distance(new Vec(c.U - b.U, c.V - b.V),
            new Vec(20 * (40.0 / 70) * BaseScale, -40 * (40.0 / 70) * BaseScale)) < 1e-12,
            "vertical motion recomputes weight inside one depth bracket");
    }

    private static void Benchmark(StreamWriter csv, Tile[] tiles, Query[] queries, int[] indices, int mode)
    {
        var basis = new Basis(tiles[0].K); basis.Set(BenchmarkTime);
        var scratch = new Basis(tiles[0].K);
        Action batch = delegate
        {
            double sum = 0;
            for (int i = 0; i < queries.Length; i++)
            {
                Vec v = SampleWorkload(tiles[indices[i]], queries[i], basis, mode, scratch);
                sum += v.U + v.V;
            }
            sink = sum;
        };
        for (int i = 0; i < 32; i++) batch();
        int repeats = Math.Max(1, 16384 / queries.Length);
        var ms = new double[101];
        var clock = new Stopwatch();
        // Clock + allocation probe outside measured closure. No queries allocated in loop.
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long before = Allocated == null ? -1 : Allocated();
        int gen0 = GC.CollectionCount(0);
        for (int i = 0; i < ms.Length; i++)
        {
            clock.Restart();
            for (int r = 0; r < repeats; r++) batch();
            clock.Stop();
            ms[i] = clock.Elapsed.TotalMilliseconds / repeats;
        }
        long allocation = Allocated == null ? -1 : Allocated() - before;
        gen0 = GC.CollectionCount(0) - gen0;
        Array.Sort(ms);
        string line = string.Join(",", new[] { tiles[0].K.ToString(), tiles.Length.ToString(),
            queries.Length.ToString(), mode.ToString(), ms[50].ToString("R", Inv), ms[95].ToString("R", Inv),
            ms[99].ToString("R", Inv), ms[100].ToString("R", Inv), allocation.ToString(), gen0.ToString(), sink.ToString("R", Inv) });
        csv.WriteLine(line); csv.Flush();
        Print("BENCH K=" + tiles[0].K + " tiles=" + tiles.Length + " objects=" + queries.Length
            + " mode=" + mode + " batch p99 ms=" + ms[99].ToString("F6", Inv)
            + " alloc=" + allocation + " gen0=" + gen0);
    }

    private static void RefreshBenchmark(StreamWriter csv, Tile[] tiles, bool measure)
    {
        var basis = new Basis(tiles[0].K);
        // Dedicated outputs preserve benchmark snapshots and keep one output per tile.
        // Guard clones and output allocation are outside timed materialization loops.
        var preserved = new float[tiles.Length][];
        var targets = new float[tiles.Length][];
        for (int i = 0; i < tiles.Length; i++)
        {
            preserved[i] = (float[])tiles[i].Tide1.Clone();
            targets[i] = new float[tiles[i].Tide1.Length];
        }
        var timings = new double[measure ? 41 : 1];
        var sw = new Stopwatch();
        for (int run = 0; run < timings.Length; run++)
        {
            basis.Set(run * 60);
            sw.Restart();
            for (int i = 0; i < tiles.Length; i++) Materialize(tiles[i], basis, targets[i]);
            sw.Stop(); timings[run] = sw.Elapsed.TotalMilliseconds;
        }
        bool unchanged = true;
        for (int i = 0; i < tiles.Length; i++)
            for (int j = 0; j < preserved[i].Length; j++)
                unchanged &= preserved[i][j] == tiles[i].Tide1[j];
        Check(unchanged, "refresh benchmark preserves snapshot anchors");
        sink += targets[0][0];
        if (!measure)
        {
            Print("REFRESH preservation checked (no timing; --checks-only)");
            return;
        }
        Array.Sort(timings);
        csv.WriteLine(tiles[0].K + "," + tiles.Length + "," + timings[20].ToString("R", Inv)
            + "," + timings[38].ToString("R", Inv) + "," + timings[40].ToString("R", Inv));
        csv.Flush();
        Print("REFRESH K=" + tiles[0].K + " tiles=" + tiles.Length
            + " p95 ms=" + timings[38].ToString("F4", Inv) + " max ms=" + timings[40].ToString("F4", Inv));
    }

    private static void IoBenchmark(Tile tile)
    {
        byte[] raw = Encode(tile), packed = Compress(raw);
        var times = new double[41]; var sw = new Stopwatch();
        for (int i = 0; i < times.Length; i++)
        {
            sw.Restart();
            Tile t = Decode(Expand(packed, raw.Length));
            sw.Stop(); times[i] = sw.Elapsed.TotalMilliseconds;
            sink += t.PackedCoeff[0];
        }
        Array.Sort(times);
        Print("DECODE memory-deflate+parse K34 p50=" + times[20].ToString("F4", Inv)
            + " p95=" + times[38].ToString("F4", Inv) + " max=" + times[40].ToString("F4", Inv)
            + " ms (allocates prototype float reference arrays too; NOT disk latency)");
    }

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Specify a new results directory.");
            return 2;
        }
        output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        using (log = new StreamWriter(new FileStream(Path.Combine(output, "run.log"), FileMode.CreateNew)))
        {
            try
            {
                Print("START synthetic offline atlas, CLR=" + Environment.Version + " ptr=" + IntPtr.Size
                    + " CPUs=" + Environment.ProcessorCount + " allocationCounter=" + (Allocated != null));
                bool checksOnly = args.Length > 1 && args[1] == "--checks-only";
                Correctness(!checksOnly);
                MovingCursorChecks();
                if (checksOnly)
                {
                    var fixture = new[] { new Tile(34) };
                    fixture[0].Fill(0);
                    Anchor(fixture, BenchmarkFirst, BenchmarkLast);
                    Query[] queries; int[] indices;
                    MakeWorkload(fixture, 256, out queries, out indices);
                    WorkloadPreflight(fixture, queries, indices);
                    Print("DONE checks=" + checks + " (checks only; no benchmark rows)");
                    return 0;
                }
                using (var csv = new StreamWriter(Path.Combine(output, "benchmark.csv")))
                using (var refresh = new StreamWriter(Path.Combine(output, "refresh.csv")))
                {
                    csv.WriteLine("constituents,tiles,objects,mode,p50_batch_ms,p95_batch_ms,p99_batch_ms,max_batch_ms,allocated_bytes,gen0,checksum");
                    refresh.WriteLine("constituents,tiles,p50_ms,p95_ms,max_ms");
                    int[] counts = { 32, 256, 2048, 8192 };
                    int[] harmonics = { 8, 16, 34 };
                    for (int h = 0; h < harmonics.Length; h++)
                    {
                        var all = new Tile[16];
                        for (int i = 0; i < all.Length; i++)
                        {
                            all[i] = new Tile(harmonics[h]); all[i].Fill(i);
                        }
                        foreach (int number in new[] { 1, 16 })
                        {
                            var active = new Tile[number]; Array.Copy(all, active, number);
                            // RefreshBenchmark previously mutated Tide1; anchor explicitly for every
                            // workload and prove numeric equivalence before collecting timings.
                            Anchor(active, BenchmarkFirst, BenchmarkLast);
                            foreach (int count in counts)
                            {
                                Query[] queries; int[] indices;
                                MakeWorkload(active, count, out queries, out indices);
                                WorkloadPreflight(active, queries, indices);
                                for (int mode = 0; mode < 5; mode++) Benchmark(csv, active, queries, indices, mode);
                            }
                            RefreshBenchmark(refresh, active, true);
                        }
                        Print("PROGRESS " + (h + 1) + "/3 harmonic configurations done; "
                            + ((h + 1) * 100 / 3) + "%");
                        if (harmonics[h] == 34) IoBenchmark(all[0]);
                    }
                }
                Print("DONE checks=" + checks + " (synthetic only; no FPS certification)");
                return 0;
            }
            catch (Exception e) { Print(e.ToString()); return 1; }
        }
    }
}
