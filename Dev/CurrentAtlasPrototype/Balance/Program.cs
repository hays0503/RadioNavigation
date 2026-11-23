// Synthetic offline surrogate. No Unity, game data, real hull coefficients or FPS claims.
using System;
using System.Diagnostics;
using System.Globalization;

internal static class Program
{
    // One body axis. Drive is signed acceleration; Kw/Ka include area and inverse mass.
    // Valid fixture domain: finite values, Kw>0, Ka>=0.
    private struct Axis
    {
        internal double U, W, Kw, Ka, Drive;

        internal Axis(double u, double w, double kw, double ka, double drive)
        {
            U = u; W = w; Kw = kw; Ka = ka; Drive = drive;
        }

        internal double Net(double v)
        {
            double r = U - v, a = W - v;
            return Drive + Kw * r * Math.Abs(r) + Ka * a * Math.Abs(a);
        }
    }

    private static int passes;
    private static double checksum;

    private static double SignedRoot(double x)
    {
        return x == 0 ? 0 : Math.Sign(x) * Math.Sqrt(Math.Abs(x));
    }

    private static double PositiveRoot(double a, double b, double c)
    {
        // Solve a*x*x+b*x=c, using the smaller positive root when a<0.
        if (c == 0) return 0;
        return 2 * c / (b + Math.Sqrt(Math.Max(0, b * b + 4 * a * c)));
    }

    private static double Fast(Axis p)
    {
        if (p.Ka == 0 || p.U == p.W)
            return p.U + SignedRoot(p.Drive / (p.Kw + p.Ka));

        double lo, hi, kl, kh;
        if (p.U < p.W) { lo = p.U; hi = p.W; kl = p.Kw; kh = p.Ka; }
        else { lo = p.W; hi = p.U; kl = p.Ka; kh = p.Kw; }
        double d = hi - lo, atLow = p.Drive + kh * d * d, atHigh = p.Drive - kl * d * d;
        if (atLow <= 0)
            return lo - PositiveRoot(kl + kh, 2 * kh * d, -atLow);
        if (atHigh >= 0)
            return hi + PositiveRoot(kl + kh, 2 * kl * d, atHigh);
        // Solve from the nearer boundary to avoid cancellation close to either medium.
        return p.Drive + (kh - kl) * (d / 2) * (d / 2) <= 0
            ? lo + PositiveRoot(kl - kh, 2 * kh * d, atLow)
            : hi - PositiveRoot(kh - kl, 2 * kl * d, -atHigh);
    }

    private static double Reference(Axis p)
    {
        double shift = SignedRoot(p.Drive / (p.Ka + p.Kw));
        double lo = Math.Min(p.U, p.W) + shift, hi = Math.Max(p.U, p.W) + shift;
        // Any two colocated media have exact shift; otherwise a bounded monotone reference.
        for (int i = 0; i < 48; i++)
        {
            double mid = (lo + hi) / 2;
            if (p.Net(mid) > 0) lo = mid; else hi = mid;
        }
        return (lo + hi) / 2;
    }

    private static Axis WithCalmBaseline(double u, double w, double kw, double ka, double v0)
    {
        // Fixed-thrust hypothesis, not a speed-control law.
        return new Axis(u, w, kw, ka, (kw + ka) * v0 * Math.Abs(v0));
    }

    private static void Check(bool ok, string name)
    {
        if (!ok) throw new InvalidOperationException(name);
        passes++;
        Console.WriteLine("PASS " + name);
    }

    private static double Between(Random rng, double lo, double hi)
    {
        return lo + (hi - lo) * rng.NextDouble();
    }

    private static void FitComparison()
    {
        double max = 0, residual = 0;
        var rng = new Random(3201);
        for (int i = 0; i < 50_000; i++)
        {
            double u = Between(rng, -5, 5), w = Between(rng, -45, 45);
            double kw = Math.Pow(10, Between(rng, -4, 0));
            double ka = i % 11 == 0 ? 0 : kw * Math.Pow(10, Between(rng, -6, 2));
            double t = Between(rng, -.3, .3);
            var p = new Axis(u, w, kw, ka, t);
            double v = Fast(p);
            max = Math.Max(max, Math.Abs(v - Reference(p)));
            residual = Math.Max(residual, Math.Abs(p.Net(v)));
        }
        Check(max < 1e-9 && residual < 1e-8, "50000 roots match monotone reference and force residual");
        Console.WriteLine("Max speed error [m/s]: " + max.ToString("R") + "; max residual [m/s²]: " + residual.ToString("R"));
    }

    private static void Boundaries()
    {
        const double u = -.6, w = 12, kw = .01, ka = .001;
        double d = w - u;
        double[] boundaries = { -ka * d * d, kw * d * d };
        for (int i = 0; i < boundaries.Length; i++)
        {
            double b = boundaries[i], expected = i == 0 ? u : w;
            var p = new Axis(u, w, kw, ka, b);
            Check(Math.Abs(Fast(p) - expected) < 1e-10, "medium boundary " + i);
            p.Drive -= 1e-7; double left = Fast(p);
            p.Drive += 2e-7; double right = Fast(p);
            Check(left < expected && expected < right && right - left < 1e-4,
                  "continuous across medium boundary " + i);
        }
        Check(Fast(new Axis(2, 20, kw, 0, 0)) == 2, "fully submerged with no drive follows water");
        Check(Math.Abs(Fast(new Axis(2, 2, kw, ka, 0)) - 2) < 1e-12, "co-moving media");
        Check(Math.Abs(Fast(new Axis(-2, 2, kw, kw, 0))) < 1e-12, "equal coefficients");
    }

    private static void VesselCases()
    {
        const double kw = .01, ka = .001, v0 = 4;
        Check(Math.Abs(Fast(WithCalmBaseline(0, 0, kw, ka, v0)) - v0) < 1e-12,
              "calm fixed-thrust baseline");
        Check(Math.Abs(Fast(WithCalmBaseline(0, 0, kw, ka, -v0)) + v0) < 1e-12,
              "reverse fixed-thrust baseline");
        double head = Fast(WithCalmBaseline(0, -10, kw, ka, v0));
        double tail = Fast(WithCalmBaseline(0, 10, kw, ka, v0));
        Check(head < v0 && v0 < tail, "same thrust: slower headwind and faster tailwind");
        Check(Fast(WithCalmBaseline(0, -25, kw, ka, v0)) < 0,
              "insufficient fixed thrust permits backwards ground drift");
        Check(Fast(new Axis(0, 10, .08, .001, 0))
              < Fast(new Axis(0, 10, .08, .004, 0)), "distinct exposures on lateral axis");
        var p = new Axis(1, 8, kw, ka, .05);
        Check(Math.Abs(Fast(p) + 5 - Fast(new Axis(p.U + 5, p.W + 5, p.Kw, p.Ka, p.Drive))) < 1e-12,
              "both media translate, relative physics unchanged");
        Check(Fast(new Axis(p.U + 5, p.W, p.Kw, p.Ka, p.Drive)) < Fast(p) + 5,
              "current alone changes apparent air; not scalar drift addition");
        var scaled = new Axis(p.U, p.W, 9 * p.Kw, 9 * p.Ka, 9 * p.Drive);
        Check(Math.Abs(Fast(p) - Fast(scaled)) < 1e-12 && Math.Abs(p.Net(0) - scaled.Net(0)) > .01,
              "terminal speeds alone cannot identify acceleration scale");
        Console.WriteLine("Synthetic fixed-thrust head/calm/tail [m/s]: {0:F4}/{1:F4}/{2:F4}", head, v0, tail);
    }

    private static void HandoffCases()
    {
        // Water-only, inertial hull, constant U, start at rest relative to ground.
        // Integrate the exact one-medium quadratic relaxation.
        const double u = .5, h = 60, kw = .04;
        double a = u * h - Math.Log(1 + kw * u * h) / kw;
        double b = u * h - Math.Log(1 + 9 * kw * u * h) / (9 * kw);
        double instantaneous = h * Fast(new Axis(u, 0, kw, 0, 0));
        Check(0 < a && a < b && b < instantaneous && instantaneous - a > 19,
              "same terminal speed but distinct transient displacement; no spawn velocity snap");
        double vAtSpawn = u - u / (1 + kw * u * h);
        Check(Math.Abs(vAtSpawn - .2727272727272727) < 1e-12,
              "handoff uses current world velocity, not equilibrium or another current addition");
        Console.WriteLine("Synthetic 60-s displacement [m]: slow/fast/immediate={0:F4}/{1:F4}/{2:F4}", a, b, instantaneous);
    }

    private static double Batch(Axis[] axes, bool fast, int repeats, out long bytes)
    {
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        double total = 0;
        for (int r = 0; r < repeats; r++)
            for (int i = 0; i < axes.Length; i++)
                total += fast ? Fast(axes[i]) : Reference(axes[i]);
        long end = Stopwatch.GetTimestamp();
        bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        checksum = total;
        return (end - start) * 1000.0 / Stopwatch.Frequency / repeats;
    }

    private static void Measure(int groups)
    {
        var axes = new Axis[2 * groups];
        var rng = new Random(372);
        for (int i = 0; i < axes.Length; i++)
            axes[i] = WithCalmBaseline(Between(rng, -2, 2), Between(rng, -22, 22),
                Between(rng, .01, .05), Between(rng, .0001, .002), i % 2 == 0 ? 4 : 0);
        long bytes;
        Batch(axes, true, 128, out bytes);
        Batch(axes, false, 128, out bytes);
        var fast = new double[5];
        var reference = new double[5];
        long alloc = 0;
        for (int i = 0; i < 5; i++)
        {
            fast[i] = Batch(axes, true, 128, out bytes);
            alloc += bytes;
            double expected = checksum;
            reference[i] = Batch(axes, false, 128, out bytes);
            Check(Math.Abs(expected - checksum) < 1e-4, "benchmark checksum agrees " + groups + "/" + i);
        }
        Array.Sort(fast);
        Array.Sort(reference);
        Console.WriteLine("BENCH groups={0} two_axes fast_ms min/median/max={1:F6}/{2:F6}/{3:F6} " +
            "reference_median_ms={4:F6} fast_alloc_bytes={5}",
            groups, fast[0], fast[2], fast[4], reference[2], alloc);
        Check(alloc == 0, "fast batch does not allocate on the caller thread");
    }

    private static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            FitComparison();
            Boundaries();
            VesselCases();
            HandoffCases();
            if (args.Length == 1 && args[0] == "--measure")
            {
                Measure(200);
                Measure(2048);
            }
            Console.WriteLine("DONE {0} checks; offline scalar surrogate, not Unity or FPS.", passes);
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("FAIL " + e.Message);
            return 1;
        }
    }
}
