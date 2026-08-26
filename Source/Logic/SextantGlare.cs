using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Pure glare model of the sextant optics (SEXTANT_PLAN §6.2): sun intensity
    /// from the live sun altitude, gaussian falloff of the angle to the sun with
    /// σ = PIP FOV / 4, exponential attenuation by 1..3 shade filters.
    /// No Unity — unit-tested offline.
    /// </summary>
    public static class SextantGlare
    {
        /// <summary>σ divisor: σ = pipFovDeg / SigmaDivisor (SEXTANT_PLAN §6.2).</summary>
        public const double SigmaDivisor = 4.0;

        /// <summary>Light transmission of one shade filter (SEXTANT_PLAN §2.8).</summary>
        public const double FilterTransmission = 0.35;

        /// <summary>
        /// Glare in 0..1. Night (sunAltitudeDeg &lt;= 0) → 0. filters = 0..3.
        /// </summary>
        public static double Glare(double angleToSunDeg, double sunAltitudeDeg, int filters, double pipFovDeg)
        {
            if (sunAltitudeDeg <= 0.0)
                return 0.0;

            double sunIntensity = sunAltitudeDeg / 60.0;
            if (sunIntensity > 1.0) sunIntensity = 1.0;

            double sigma = pipFovDeg / SigmaDivisor;
            if (sigma <= 0.0) sigma = 1e-6;

            double x = angleToSunDeg / sigma;
            double peak = sunIntensity * Math.Exp(-x * x);

            int f = filters < 0 ? 0 : filters;
            double glare = peak * Math.Pow(FilterTransmission, f);

            if (glare < 0.0) return 0.0;
            if (glare > 1.0) return 1.0;
            return glare;
        }
    }
}
