using System;
using System.Collections.Generic;

namespace UBOAT.Mods.RadioNavigation
{
    public struct SextantLopSegment
    {
        public int Slot;
        public bool Transferred;
        public bool Coincident;
        public DateTime Utc;
        public double StartLat, StartLon, EndLat, EndLon;
    }

    /// <summary>First-pass worksheet LOPs, not triangle edges. Frozen at I;
    /// no live speed, position or ephemeris is consulted by the map renderer.</summary>
    public static class SextantLopPlot
    {
        private const string Prefix = "RadioNav SIGHTLINE ";
        public const double HalfLengthNm = 20;

        public static SextantLopSegment[] Build(RunningFixCalculator calc, double courseDeg, double speedKn)
        {
            var result = new List<SextantLopSegment>(6);
            DateTime latest = LatestUtc(calc);
            Add(result, calc.Observation1, 1, latest, courseDeg, speedKn);
            Add(result, calc.Observation2, 2, latest, courseDeg, speedKn);
            Add(result, calc.Observation3, 3, latest, courseDeg, speedKn);
            return result.ToArray();
        }

        private static DateTime LatestUtc(RunningFixCalculator calc)
        {
            DateTime latest = DateTime.MinValue;
            if (calc.Observation1 != null) latest = calc.Observation1.Utc;
            if (calc.Observation2 != null && calc.Observation2.Utc > latest) latest = calc.Observation2.Utc;
            if (calc.Observation3 != null && calc.Observation3.Utc > latest) latest = calc.Observation3.Utc;
            return latest;
        }

        private static void Add(List<SextantLopSegment> result, SextantObservation obs, int slot,
            DateTime latest, double course, double speed)
        {
            if (obs == null)
                return;
            double run = speed * Math.Max(0, (latest - obs.Utc).TotalHours);
            LineOfPosition original = RunningFixCalculator.BuildLop(obs);
            SextantLopSegment segment;
            if (TrySegment(original, slot, false, run == 0, obs.Utc, out segment))
                result.Add(segment);
            if (run == 0)
                return; // O=T: do not draw two indistinguishable copies
            LineOfPosition moved = RunningFixCalculator.TranslateLop(original, course, run);
            if (TrySegment(moved, slot, true, false, latest, out segment))
                result.Add(segment);
        }

        private static bool TrySegment(LineOfPosition line, int slot, bool transferred, bool coincident,
            DateTime utc, out SextantLopSegment segment)
        {
            segment = new SextantLopSegment();
            double cosLat = Math.Cos(line.TangentLatDeg * Math.PI / 180);
            if (Math.Abs(cosLat) < 0.01)
                return false; // local tangent charting is not a polar projection
            double az = line.AzDeg * Math.PI / 180;
            double north = -HalfLengthNm * Math.Sin(az) / 60;
            double east = HalfLengthNm * Math.Cos(az) / (60 * cosLat);
            segment = new SextantLopSegment
            {
                Slot = slot, Transferred = transferred, Coincident = coincident, Utc = utc,
                StartLat = line.TangentLatDeg - north, StartLon = line.TangentLonDeg - east,
                EndLat = line.TangentLatDeg + north, EndLon = line.TangentLonDeg + east
            };
            return Valid(segment.StartLat, segment.StartLon) && Valid(segment.EndLat, segment.EndLon);
        }

        private static bool Valid(double lat, double lon)
        {
            return !double.IsNaN(lat) && !double.IsNaN(lon) && Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180;
        }

        public static string EndpointKey(int slot, bool transferred, bool end)
        {
            return Prefix + (transferred ? "T" : "O") + slot + (end ? " B" : " A");
        }

        public static bool TryKey(string key, out int slot, out bool transferred, out bool end)
        {
            slot = 0; transferred = false; end = false;
            if (key == null || key.Length != Prefix.Length + 4 || !key.StartsWith(Prefix, StringComparison.Ordinal))
                return false;
            int n = Prefix.Length;
            char kind = key[n];
            slot = key[n + 1] - '0';
            transferred = kind == 'T';
            end = key[n + 3] == 'B';
            return (kind == 'O' || kind == 'T') && slot >= 1 && slot <= 3
                && key[n + 2] == ' ' && (key[n + 3] == 'A' || key[n + 3] == 'B');
        }

        public static bool OwnsEndpoint(string key)
        {
            int slot; bool transferred, end;
            return TryKey(key, out slot, out transferred, out end);
        }

        public static bool OwnsLine(string first, string second)
        {
            int a, b; bool ta, tb, ea, eb;
            return TryKey(first, out a, out ta, out ea) && TryKey(second, out b, out tb, out eb)
                && a == b && ta == tb && ea != eb;
        }
    }

    internal interface ISextantLopChart
    {
        bool TryReplace(SextantLopSegment[] lines);
        bool TryAdopt();
    }

    /// <summary>One event, one replace/adopt. Erasing a line does not cause
    /// per-frame resurrection. Load adopts saved native geometry, never re-solves.</summary>
    internal sealed class SextantLopMapSession
    {
        private SextantLopSegment[] pending;
        private bool dirty;

        internal void Publish(SextantLopSegment[] lines)
        {
            pending = lines;
            dirty = true;
        }

        internal void Restore()
        {
            pending = null;
            dirty = true;
        }

        internal void Flush(ISextantLopChart chart, bool loading)
        {
            if (!dirty || loading || chart == null)
                return;
            bool ok = pending == null ? chart.TryAdopt() : chart.TryReplace(pending);
            if (ok)
                dirty = false;
        }
    }
}
