using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Which limb of the solar disc the player brought to the horizon.
    /// The lower limb is the navigational standard (the disc's upper limb
    /// requires the Sun to be fully clear of the horizon). Keep both values for
    /// frozen legacy records; the current instrument defaults to lower limb.
    /// </summary>
    public enum SunSightLimb
    {
        /// <summary>Lower limb — the navigational default.</summary>
        LowerLimb = 0,

        /// <summary>Upper limb — not currently selectable in the overlay.</summary>
        UpperLimb = 1
    }

    /// <summary>
    /// Physical solar diameter, not an enlarged disc plus a compensating offset.
    /// Meeus ch.25 radius vector and the almanac semidiameter at 1 AU (959.63").
    /// The user's SH5 1939-1945 almanac gives SD=16.3' in January, 15.7' in
    /// July and 15.9' on 2 September. Zoom changes projection, never this angle.
    /// Input is TT, just like SunRaDec; no horizon/refraction or SUNDRIFT
    /// correction is introduced here.
    /// </summary>
    public static class SunDiscScale
    {
        /// <summary>Nominal diameter for an uninitialised renderer, not its live size.</summary>
        public const double TrueAngularDiameterDeg = 0.53;
        private const double Deg2Rad = Math.PI / 180.0;
        private const double SemidiameterAtOneAuArcSec = 959.63;

        public static double AngularDiameterDeg(double jdTt)
        {
            if (double.IsNaN(jdTt) || double.IsInfinity(jdTt))
                return 0.0;
            double t = (jdTt - 2451545.0) / 36525.0;
            double m = (357.52911 + 35999.05029 * t - 0.0001537 * t * t) * Deg2Rad;
            double c = (1.914602 - 0.004817 * t - 0.000014 * t * t) * Math.Sin(m)
                + (0.019993 - 0.000101 * t) * Math.Sin(2.0 * m)
                + 0.000289 * Math.Sin(3.0 * m);
            double e = 0.016708634 - 0.000042037 * t - 0.0000001267 * t * t;
            double distanceAu = 1.000001018 * (1.0 - e * e)
                / (1.0 + e * Math.Cos(m + c * Deg2Rad));
            double sineRadius = Math.Sin(SemidiameterAtOneAuArcSec / 3600.0 * Deg2Rad);
            return 2.0 * Math.Asin(sineRadius / distanceAu) / Deg2Rad;
        }
    }
}
