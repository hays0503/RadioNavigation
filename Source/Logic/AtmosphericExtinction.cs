using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Atmospheric extinction and horizon fade (CELESTIAL_REALISM_PLAN P4).
    /// Real stars dim and redden toward the horizon; the old code cut them
    /// dead at alt ≤ 0. Airmass is Young (1994), extinction 0.20 mag/airmass
    /// in V, reddening 0.10 in B−V. Pure math, no Unity.
    /// </summary>
    public static class AtmosphericExtinction
    {
        private const double Deg2Rad = Math.PI / 180.0;

        /// <summary>V-band extinction per airmass (mag).</summary>
        public const double VExtinction = 0.20;

        /// <summary>B−V reddening per airmass.</summary>
        public const double BvReddening = 0.10;

        /// <summary>Below this altitude a star is culled entirely (the fade
        /// reaches zero at 0°, but a small negative margin avoids popping).</summary>
        public const double CullAltitudeDeg = -0.5;

        /// <summary>Altitude where the horizon fade reaches full (degrees).</summary>
        public const double FadeFullAltitudeDeg = 10.0;

        /// <summary>Young (1994) relative airmass; altitude in degrees,
        /// clamped to 0.5° so the formula stays finite at the horizon and to
        /// 1.0 at the zenith (the fit dips a hair below unity there).</summary>
        public static double Airmass(double altDeg)
        {
            double h = altDeg < 0.5 ? 0.5 : altDeg;
            double x = 1.0 / (Math.Sin(h * Deg2Rad) + 0.50572 * Math.Pow(h + 6.07995, -1.63));
            return x < 1.0 ? 1.0 : x;
        }

        /// <summary>Extinction in magnitudes (0 at the zenith).</summary>
        public static double MagnitudeLoss(double altDeg)
        {
            return VExtinction * (Airmass(altDeg) - 1.0);
        }

        /// <summary>B−V reddening (add to the catalog B−V before the colour map).</summary>
        public static double BvShift(double altDeg)
        {
            return BvReddening * (Airmass(altDeg) - 1.0);
        }

        /// <summary>Horizon fade: 0 at the horizon → 1 at 10°.</summary>
        public static float HorizonFade(double altDeg)
        {
            if (altDeg <= 0.0)
                return 0f;
            if (altDeg >= FadeFullAltitudeDeg)
                return 1f;
            return (float)(altDeg / FadeFullAltitudeDeg);
        }
    }
}
