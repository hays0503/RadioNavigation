using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Resolves the RA/Dec of a sightable body from the mod's own ephemeris
    /// (SEXTANT_PLAN v2.2). The game renders its own Sun and Moon with a sky
    /// time shift, so both carry the SUNDRIFT calibration Δt; the stars are
    /// rendered by the mod from the almanac and use plain UTC. Pure C#, no
    /// Unity — the routing is unit-tested offline in CelestialEphemerisTests.
    /// </summary>
    public static class CelestialEphemeris
    {
        private const double Deg2Rad = Math.PI / 180.0;

        /// <summary>The game-rendered Sun and Moon carry the sky time shift Δt
        /// (a single game clock drives both, so one Δt fits both — measured
        /// 2026-09-16: 6.6 min × 15.04°/h ≈ the 1.6° moon gap).</summary>
        public static bool KindUsesCalibration(CelestialBodyKind kind)
        {
            return kind == CelestialBodyKind.Sun || kind == CelestialBodyKind.Moon;
        }

        public static bool UsesCalibration(CelestialBody body)
        {
            return KindUsesCalibration(body.Kind);
        }

        /// <summary>Julian day of the body's ephemeris: calibrated for the
        /// Sun and Moon, plain UTC for the stars.</summary>
        public static double JdForKind(CelestialBodyKind kind, DateTime utc)
        {
            return KindUsesCalibration(kind)
                ? SunEphemerisCalibration.CalibratedJd(utc)
                : AstroMath.ToJulianDay(utc);
        }

        public static double JdFor(CelestialBody body, DateTime utc)
        {
            return JdForKind(body.Kind, utc);
        }

        /// <summary>Geocentric RA/Dec (radians) of the body. False for
        /// CelestialBodyKind.None or an unknown star HIP. The Sun is evaluated
        /// on TT (UT + ΔT), the Moon/stars on the UT scale — LST/GHA stay UT
        /// (CELESTIAL_REALISM_PLAN P1/P1b).</summary>
        public static bool Compute(CelestialBody body, DateTime utc,
            out double raRad, out double decRad)
        {
            raRad = 0.0;
            decRad = 0.0;
            double jd = JdFor(body, utc);
            switch (body.Kind)
            {
                case CelestialBodyKind.Sun:
                    AstroMath.SunRaDec(SunEphemerisCalibration.JdTt(utc), out raRad, out decRad);
                    return true;
                case CelestialBodyKind.Moon:
                    return AstroMath.MoonRaDec(jd, out raRad, out decRad);
                case CelestialBodyKind.Star:
                    return TryGetStarRaDec(body.Hip, jd, out raRad, out decRad);
                default:
                    return false;
            }
        }

        /// <summary>RA/Dec for an observer at (lat, lon): the Moon carries the
        /// topocentric parallax correction (P2/R5), every other body stays
        /// geocentric. The renderer and the sight reduction must share this
        /// path, or the measured arc disagrees with Hc by up to 57′.</summary>
        public static bool ComputeForObserver(CelestialBody body, DateTime utc,
            double latDeg, double lonDeg, out double raRad, out double decRad)
        {
            raRad = 0.0;
            decRad = 0.0;
            if (body.Kind == CelestialBodyKind.Moon)
            {
                double jd = JdFor(body, utc);
                return AstroMath.MoonTopocentricRaDec(jd, latDeg,
                    AstroMath.LstDeg(jd, lonDeg), out raRad, out decRad);
            }
            return Compute(body, utc, out raRad, out decRad);
        }

        /// <summary>A star is sightable only with a real HIP. The catalog's
        /// Hip=0 rows are the no-HIP sentinel (17 distinct stars); a HIP-keyed
        /// lookup would silently return the first of them, so a Hip=0 body must
        /// never reach the ephemeris or the worksheet.</summary>
        public static bool IsSightableStar(int hip)
        {
            return hip > 0;
        }

        /// <summary>
        /// Effective epoch of the generated StarCatalog, JD.
        /// MEASURED, not assumed: the converter applied a 74-year shift from
        /// J2000 (T = (1942−2016)/100 against J2000-referenced positions), so
        /// the baked epoch is 1926.0, not the 1942.0 its header claims. Check
        /// against independent J2000+proper-motion positions for 10 stars:
        /// best-fit epoch 1926.0 for all ten, mean residual 0.002°; at 1942
        /// the same stars are off by 0.09–0.22° (5–13 NM). Runtime precession
        /// must therefore start HERE (CELESTIAL_REALISM_PLAN P1b).
        /// </summary>
        public const double CatalogEpochJd = 2424516.5;

        /// <summary>Precesses a raw catalog RA/Dec (degrees) from the catalog
        /// epoch to the requested jd. Use for every catalog row, including the
        /// Hip=0 sentinels (they have no HIP lookup).</summary>
        public static void PrecessCatalog(double raDeg, double decDeg, double jd,
            out double raRad, out double decRad)
        {
            AstroMath.Precess(raDeg * Deg2Rad, decDeg * Deg2Rad, CatalogEpochJd, jd,
                out raRad, out decRad);
        }

        /// <summary>Catalog RA/Dec of a star by HIP (raw, catalog epoch) —
        /// internal: production must go through the jd-aware overload.</summary>
        internal static bool TryGetStarRaDec(int hip, out double raRad, out double decRad)
        {
            raRad = 0.0;
            decRad = 0.0;
            if (!IsSightableStar(hip))
                return false;
            StarEntry[] stars = StarCatalog.Stars;
            for (int i = 0; i < stars.Length; i++)
            {
                if (stars[i].Hip == hip)
                {
                    raRad = stars[i].RaDeg * Deg2Rad;
                    decRad = stars[i].DecDeg * Deg2Rad;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Catalog RA/Dec of a star by HIP, precessed to jd. Multiple
        /// system rows share a HIP; the first wins (brighter, catalog order).</summary>
        public static bool TryGetStarRaDec(int hip, double jd,
            out double raRad, out double decRad)
        {
            raRad = 0.0;
            decRad = 0.0;
            if (!IsSightableStar(hip))
                return false;
            StarEntry[] stars = StarCatalog.Stars;
            for (int i = 0; i < stars.Length; i++)
            {
                if (stars[i].Hip == hip)
                {
                    PrecessCatalog(stars[i].RaDeg, stars[i].DecDeg, jd,
                        out raRad, out decRad);
                    return true;
                }
            }
            return false;
        }
    }
}
