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
        /// CelestialBodyKind.None or an unknown star HIP.</summary>
        public static bool Compute(CelestialBody body, DateTime utc,
            out double raRad, out double decRad)
        {
            raRad = 0.0;
            decRad = 0.0;
            double jd = JdFor(body, utc);
            switch (body.Kind)
            {
                case CelestialBodyKind.Sun:
                    AstroMath.SunRaDec(jd, out raRad, out decRad);
                    return true;
                case CelestialBodyKind.Moon:
                    return AstroMath.MoonRaDec(jd, out raRad, out decRad);
                case CelestialBodyKind.Star:
                    return TryGetStarRaDec(body.Hip, out raRad, out decRad);
                default:
                    return false;
            }
        }

        /// <summary>A star is sightable only with a real HIP. The catalog's
        /// Hip=0 rows are the no-HIP sentinel (17 distinct stars); a HIP-keyed
        /// lookup would silently return the first of them, so a Hip=0 body must
        /// never reach the ephemeris or the worksheet.</summary>
        public static bool IsSightableStar(int hip)
        {
            return hip > 0;
        }

        /// <summary>Catalog RA/Dec of a star by HIP (J1942.0, the epoch the
        /// mod renders). Multiple-system rows share a HIP; the first wins.</summary>
        public static bool TryGetStarRaDec(int hip, out double raRad, out double decRad)
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
    }
}
