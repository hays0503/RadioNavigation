using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Sky brightness conditions shared by the point-star mesh and the
    /// Sternspreiz streak field. Unity-free so both layers agree on which stars
    /// the night can show.
    ///
    /// The Sun's zenith distance comes from the mod's own ephemeris, not from
    /// the game's TOD_Sky.SunZenith: live 2026-09-22 the reflected member
    /// returned a stale daytime value at night (74 deg instead of ~140 deg),
    /// which drove the twilight shift to daylight and hid most of the field.
    /// Our ephemeris matches the game Sun to 0.001 deg.
    /// </summary>
    public static class StarSkyConditions
    {
        /// <summary>Fails dark: an unavailable Sun position must never wash the field out.</summary>
        public const double FailDarkSunZenithDeg = 108.0;

        /// <summary>
        /// Limiting V magnitude for the given instant and observer: twilight
        /// (Sun zenith distance) plus moonwash (Moon altitude and phase).
        /// </summary>
        public static double LimitingMagnitude(DateTime utc, float latDeg, float lonDeg)
        {
            double sunZenith = CelestialForecast.SunZenithDeg(latDeg, lonDeg, utc);
            if (double.IsNaN(sunZenith))
                sunZenith = FailDarkSunZenithDeg;

            double moonAltDeg = MoonAltitudeDeg(utc, latDeg, lonDeg);
            double illumination = AstroMath.MoonIllumination(
                CelestialEphemeris.JdFor(CelestialBody.Moon(), utc));
            return SkyBrightness.LimitingMagnitude(sunZenith, illumination, moonAltDeg);
        }

        /// <summary>Moon altitude in degrees, or 0 when the ephemeris is unavailable.</summary>
        public static double MoonAltitudeDeg(DateTime utc, float latDeg, float lonDeg)
        {
            double moonJd = CelestialEphemeris.JdFor(CelestialBody.Moon(), utc);
            if (!AstroMath.MoonRaDec(moonJd, out double moonRa, out double moonDec))
                return 0.0;

            AstroMath.RaDecToEnu(moonRa, moonDec, AstroMath.LstDeg(moonJd, lonDeg), latDeg,
                out double altRad, out _, out _, out _, out _);
            return altRad * 180.0 / Math.PI;
        }
    }
}
