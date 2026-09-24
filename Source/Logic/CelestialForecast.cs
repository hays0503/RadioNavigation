using System;
using System.Collections.Generic;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Night planning for the navigator (CELESTIAL_REALISM_PLAN P5): when the
    /// twilight windows open and close, when the Moon rises or sets, and which
    /// pair of stars gives the best crossing angle in the next hours. Every
    /// entry point is pure and takes the start time explicitly — the caller
    /// supplies GameTimeResolver.GetUtcAt(...) — so the whole forecast runs
    /// offline on the same ephemeris the sights use.
    /// </summary>
    public static class CelestialForecast
    {
        /// <summary>Sun zenith distance at civil twilight (degrees).</summary>
        public const double CivilZenithDeg = 96.0;

        /// <summary>Sun zenith distance at nautical twilight (degrees).</summary>
        public const double NauticalZenithDeg = 102.0;

        /// <summary>Sun zenith distance at astronomical twilight (degrees).</summary>
        public const double AstronomicalZenithDeg = 108.0;

        /// <summary>Coarse scan step (minutes) before the bisection.</summary>
        public const double ScanStepMinutes = 5.0;

        /// <summary>Minimum altitude for a star to be usable in a pair.</summary>
        public const double MinPairAltitudeDeg = 15.0;

        /// <summary>Ideal crossing angle between two lines of position.</summary>
        public const double IdealCrossingDeg = 90.0;

        /// <summary>Ideal spread between the three members of a triple: the
        /// objective stops as soon as every pair reaches it.</summary>
        public const double IdealTripleSpreadDeg = 60.0;

        /// <summary>Smallest separation a triple may have to be recommended.</summary>
        public const double TripleAcceptMinSepDeg = 30.0;

        /// <summary>Sun zenith distance at which the sky is dark enough for
        /// stars: the same -1° Sun-altitude gate the sight gate uses.</summary>
        public const double StarDarkZenithDeg = 90.0 - SightabilityGate.DaylightSunAltitudeDeg;

        /// <summary>Best triple at one moment: the three star names and the
        /// smallest pairwise azimuth separation between them.</summary>
        public struct TripleResult
        {
            public bool Found;
            public string NameA;
            public string NameB;
            public string NameC;
            public double MinSeparationDeg;
            public DateTime WhenUtc;
        }

        /// <summary>Sun zenith distance at a position and time (NaN when the
        /// ephemeris is unavailable).</summary>
        public static double SunZenithDeg(double latDeg, double lonDeg, DateTime utc)
        {
            SunEphemeris.SunFix fix = SunEphemeris.Compute(latDeg, lonDeg, utc);
            return fix.Ok ? 90.0 - fix.AltDeg : double.NaN;
        }

        /// <summary>
        /// Next instant (≥ fromUtc) when the Sun's zenith distance crosses the
        /// target: rising = dawn (the zenith falls through the target, so
        /// target − zenith goes negative → positive), false = dusk. False when
        /// no crossing happens inside the window (polar day/night).
        /// </summary>
        public static bool TryFindSunCrossing(double latDeg, double lonDeg, DateTime fromUtc,
            double targetZenithDeg, bool rising, double maxHours, out DateTime whenUtc)
        {
            return TryFindCrossing(fromUtc, maxHours,
                t => targetZenithDeg - SunZenithDeg(latDeg, lonDeg, t), rising, out whenUtc);
        }

        /// <summary>Next Moonrise (rising = true) or moonset inside the window.</summary>
        public static bool TryFindMoonCrossing(double latDeg, double lonDeg, DateTime fromUtc,
            bool rising, double maxHours, out DateTime whenUtc)
        {
            return TryFindCrossing(fromUtc, maxHours,
                t => MoonAltitudeDeg(latDeg, lonDeg, t), rising, out whenUtc);
        }

        /// <summary>Moon altitude (topocentric) at a position and time.</summary>
        public static double MoonAltitudeDeg(double latDeg, double lonDeg, DateTime utc)
        {
            double jd = CelestialEphemeris.JdFor(CelestialBody.Moon(), utc);
            double lstDeg = AstroMath.LstDeg(jd, lonDeg);
            if (!AstroMath.MoonTopocentricRaDec(jd, latDeg, lstDeg,
                    out double raRad, out double decRad))
                return double.NaN;
            AstroMath.RaDecToEnu(raRad, decRad, lstDeg, latDeg,
                out double altRad, out _, out _, out _, out _);
            return altRad * 180.0 / Math.PI;
        }

        /// <summary>
        /// Minutes until a SAME-BODY running fix reaches the requested azimuth
        /// change (the F-Tafel "wait for a wider crossing" advice, now with a
        /// number). Scans the body's azimuth over time; false when the target
        /// is not reached inside the window.
        /// </summary>
        public static bool TryWaitMinutesForCrossing(CelestialBody body,
            double latDeg, double lonDeg, DateTime fromUtc, double targetCrossDeg,
            double maxHours, out double minutes, out double reachedCrossDeg)
        {
            minutes = 0.0;
            reachedCrossDeg = 0.0;
            if (!TryBodyAzimuth(body, latDeg, lonDeg, fromUtc, out double startAz))
                return false;

            int steps = (int)(maxHours * 60.0 / ScanStepMinutes);
            for (int step = 1; step <= steps; step++)
            {
                double elapsed = step * ScanStepMinutes;
                if (!TryBodyAzimuth(body, latDeg, lonDeg, fromUtc.AddMinutes(elapsed),
                        out double az))
                    return false;
                double cross = Math.Abs(BodyRecommendation.SeparationDeg(startAz, az));
                if (cross >= targetCrossDeg)
                {
                    minutes = elapsed;
                    reachedCrossDeg = cross;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Altitude/azimuth of a body (Sun, Moon or a catalog star).</summary>
        public static bool TryBodyAzimuth(CelestialBody body, double latDeg, double lonDeg,
            DateTime utc, out double azimuthDeg)
        {
            azimuthDeg = 0.0;
            double raRad, decRad;
            double lstDeg;
            if (body.Kind == CelestialBodyKind.Moon)
            {
                double jd = CelestialEphemeris.JdFor(body, utc);
                lstDeg = AstroMath.LstDeg(jd, lonDeg);
                if (!AstroMath.MoonTopocentricRaDec(jd, latDeg, lstDeg,
                        out raRad, out decRad))
                    return false;
            }
            else
            {
                if (!CelestialEphemeris.ComputeForObserver(body, utc, latDeg, lonDeg,
                        out raRad, out decRad))
                    return false;
                lstDeg = AstroMath.LstDeg(CelestialEphemeris.JdFor(body, utc), lonDeg);
            }
            AstroMath.RaDecToEnu(raRad, decRad, lstDeg, latDeg,
                out double altRad, out double azRad, out _, out _, out _);
            if (altRad <= 0.0)
                return false;
            azimuthDeg = azRad * 180.0 / Math.PI;
            return true;
        }

        /// <summary>Best pair of navigation stars for a two-body fix inside the next
        /// <paramref name="withinHours"/>: both above MinPairAltitudeDeg and
        /// the azimuth separation closest to 90°. The step must be dark for
        /// stars, so a daytime step never recommends an invisible pair.
        /// Returns the first step where such a pair exists.
        /// </summary>
        public static bool TryFindBestPair(double latDeg, double lonDeg, DateTime fromUtc,
            double withinHours, double stepMinutes,
            out string nameA, out string nameB, out double separationDeg, out DateTime whenUtc)
        {
            nameA = null;
            nameB = null;
            separationDeg = 0.0;
            whenUtc = fromUtc;
            if (stepMinutes <= 0.0)
                stepMinutes = 10.0;

            int steps = (int)(withinHours * 60.0 / stepMinutes);
            var bodies = new List<BodyRecommendation.Candidate>();
            var seen = new HashSet<int>();
            for (int step = 0; step <= steps; step++)
            {
                DateTime utc = fromUtc.AddMinutes(step * stepMinutes);
                if (!IsStarDarkZenith(SunZenithDeg(latDeg, lonDeg, utc)))
                    continue;
                CollectVisibleStars(latDeg, lonDeg, utc, MinPairAltitudeDeg, bodies, seen);
                if (!TryBestSeparation(bodies, out string a, out string b, out double sep))
                    continue;
                nameA = a;
                nameB = b;
                separationDeg = sep;
                whenUtc = utc;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Best THREE-star series inside the next <paramref name="withinHours"/>:
        /// at the first dark step, the triple maximizing the smallest pairwise
        /// azimuth separation, provided that minimum reaches
        /// TripleAcceptMinSepDeg (a triple fixes better than a pair — one bad
        /// line is outvoted). The dark gate runs before the star scan, so a
        /// polar day costs one sun evaluation per step and no star work.
        /// </summary>
        public static bool TryFindBestTriple(double latDeg, double lonDeg, DateTime fromUtc,
            double withinHours, double stepMinutes, out TripleResult result)
        {
            result = new TripleResult();
            result.WhenUtc = fromUtc;
            if (stepMinutes <= 0.0)
                stepMinutes = 10.0;

            int steps = (int)(withinHours * 60.0 / stepMinutes);
            var bodies = new List<BodyRecommendation.Candidate>();
            var seen = new HashSet<int>();
            double[] sepScratch = null;
            for (int step = 0; step <= steps; step++)
            {
                DateTime utc = fromUtc.AddMinutes(step * stepMinutes);
                if (!IsStarDarkZenith(SunZenithDeg(latDeg, lonDeg, utc)))
                    continue;
                CollectVisibleStars(latDeg, lonDeg, utc, MinPairAltitudeDeg, bodies, seen);
                if (bodies.Count < 3)
                    continue;
                int needed = bodies.Count * bodies.Count;
                if (sepScratch == null || sepScratch.Length < needed)
                    sepScratch = new double[needed];
                if (!TryBestTriple(bodies, sepScratch,
                        out string a, out string b, out string c, out double minSep))
                    continue;
                if (!IsTripleAcceptable(minSep))
                    continue;
                result.Found = true;
                result.NameA = a;
                result.NameB = b;
                result.NameC = c;
                result.MinSeparationDeg = minSep;
                result.WhenUtc = utc;
                return true;
            }
            return false;
        }

        /// <summary>Best triple at one moment (offline objective): over the
        /// k×k separation matrix in <paramref name="sepScratch"/> (caller-
        /// owned, at least k×k), the triple with the largest SMALLEST
        /// pairwise separation. Strict `>` keeps the first catalog order on
        /// ties; stops early once the spread reaches IdealTripleSpreadDeg.</summary>
        internal static bool TryBestTriple(IList<BodyRecommendation.Candidate> bodies,
            double[] sepScratch, out string a, out string b, out string c, out double minSep)
        {
            a = null;
            b = null;
            c = null;
            minSep = 0.0;
            if (bodies == null || sepScratch == null)
                return false;
            int count = bodies.Count;
            if (count < 3 || sepScratch.Length < count * count)
                return false;

            for (int i = 0; i < count; i++)
            {
                for (int j = 0; j < count; j++)
                {
                    sepScratch[i * count + j] = BodyRecommendation.SeparationDeg(
                        bodies[i].AzDeg, bodies[j].AzDeg);
                }
            }

            double best = 0.0;
            for (int i = 0; i < count - 2; i++)
            {
                for (int j = i + 1; j < count - 1; j++)
                {
                    double sepIj = sepScratch[i * count + j];
                    if (sepIj <= best)
                        continue;
                    for (int k = j + 1; k < count; k++)
                    {
                        double candidate = Math.Min(sepIj,
                            Math.Min(sepScratch[i * count + k], sepScratch[j * count + k]));
                        if (candidate <= best)
                            continue;
                        best = candidate;
                        a = bodies[i].Name;
                        b = bodies[j].Name;
                        c = bodies[k].Name;
                        minSep = candidate;
                        if (best >= IdealTripleSpreadDeg)
                            return true;
                    }
                }
            }
            return a != null;
        }

        /// <summary>True when a triple's smallest separation reaches the
        /// acceptance floor.</summary>
        internal static bool IsTripleAcceptable(double minSep)
        {
            return minSep >= TripleAcceptMinSepDeg;
        }

        /// <summary>True when the Sun's zenith distance puts the sky in the
        /// star-dark band. NaN (no ephemeris) is never dark: the readout must
        /// not recommend a star that cannot be seen.</summary>
        internal static bool IsStarDarkZenith(double sunZenithDeg)
        {
            return !double.IsNaN(sunZenithDeg) && sunZenithDeg >= StarDarkZenithDeg;
        }

        /// <summary>Nav stars above <paramref name="minAltitudeDeg"/> at a
        /// moment (one row per HIP). The seen-set is caller-owned so a
        /// per-step search reuses it with Clear() instead of allocating.</summary>
        internal static void CollectVisibleStars(double latDeg, double lonDeg, DateTime utc,
            double minAltitudeDeg, List<BodyRecommendation.Candidate> into, HashSet<int> seen)
        {
            into.Clear();
            seen.Clear();
            double jd = AstroMath.ToJulianDay(utc);
            double lstDeg = AstroMath.LstDeg(jd, lonDeg);

            StarEntry[] stars = StarCatalog.Stars;
            for (int i = 0; i < stars.Length; i++)
            {
                StarEntry star = stars[i];
                if (!star.IsNav)
                    continue;
                if (star.Hip != 0 && !seen.Add(star.Hip))
                    continue;
                CelestialEphemeris.PrecessCatalog(star.RaDeg, star.DecDeg, jd,
                    out double raRad, out double decRad);
                AstroMath.RaDecToEnu(raRad, decRad, lstDeg, latDeg,
                    out double altRad, out double azRad, out _, out _, out _);
                double alt = altRad * 180.0 / Math.PI;
                if (alt < minAltitudeDeg)
                    continue;
                string name = NavStarNames.TryGetName(star.Hip, out string starName)
                    ? starName
                    : "HIP " + star.Hip;
                into.Add(new BodyRecommendation.Candidate
                {
                    Name = name,
                    AltDeg = alt,
                    AzDeg = azRad * 180.0 / Math.PI,
                    Hip = star.Hip
                });
            }
        }

        /// <summary>Pair with the azimuth separation closest to 90°.</summary>
        private static bool TryBestSeparation(List<BodyRecommendation.Candidate> bodies,
            out string nameA, out string nameB, out double separationDeg)
        {
            nameA = null;
            nameB = null;
            separationDeg = 0.0;
            double bestScore = double.MaxValue;

            for (int i = 0; i < bodies.Count; i++)
            {
                for (int j = i + 1; j < bodies.Count; j++)
                {
                    double sep = BodyRecommendation.SeparationDeg(
                        bodies[i].AzDeg, bodies[j].AzDeg);
                    double score = Math.Abs(sep - IdealCrossingDeg);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        nameA = bodies[i].Name;
                        nameB = bodies[j].Name;
                        separationDeg = sep;
                    }
                }
            }
            return nameA != null;
        }

        /// <summary>
        /// Coarse scan + bisection for the first zero crossing of
        /// <paramref name="value"/> in the requested direction.
        /// </summary>
        private static bool TryFindCrossing(DateTime fromUtc, double maxHours,
            Func<DateTime, double> value, bool rising, out DateTime whenUtc)
        {
            whenUtc = fromUtc;
            double stepMinutes = ScanStepMinutes;
            int steps = (int)(maxHours * 60.0 / stepMinutes);
            double previous = value(fromUtc);
            if (double.IsNaN(previous))
                return false;

            for (int step = 1; step <= steps; step++)
            {
                DateTime t = fromUtc.AddMinutes(step * stepMinutes);
                double current = value(t);
                if (double.IsNaN(current))
                    return false;

                bool crossed = rising
                    ? previous < 0.0 && current >= 0.0
                    : previous > 0.0 && current <= 0.0;
                if (crossed)
                {
                    whenUtc = Bisect(t.AddMinutes(-stepMinutes), t, value, rising);
                    return true;
                }
                previous = current;
            }
            return false;
        }

        private static DateTime Bisect(DateTime lo, DateTime hi,
            Func<DateTime, double> value, bool rising)
        {
            for (int i = 0; i < 24; i++)
            {
                DateTime mid = lo + TimeSpan.FromTicks((hi - lo).Ticks / 2);
                double v = value(mid);
                bool below = rising ? v < 0.0 : v > 0.0;
                if (below)
                    lo = mid;
                else
                    hi = mid;
            }
            return hi;
        }
    }
}
