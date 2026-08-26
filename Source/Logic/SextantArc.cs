using System;
using System.Globalization;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Pure math of the sextant arc (SEXTANT_PLAN §6.1): clamp to 0..130°,
    /// quantize to 0.2′ (12″, 1/300°) and format marine-style "45°23.4′".
    /// No Unity — unit-tested offline.
    /// </summary>
    public static class SextantArc
    {
        /// <summary>Arc limit, clamped from both sides (SEXTANT_PLAN §2.2).</summary>
        public const double MaxDeg = 130.0;

        /// <summary>Display/storage quantum: 0.2′ = 12″ = 1/300° (SEXTANT_PLAN §2.4).</summary>
        public const double StepsPerDeg = 300.0;

        public static double Clamp(double deg)
        {
            if (deg < 0.0) return 0.0;
            if (deg > MaxDeg) return MaxDeg;
            return deg;
        }

        public static double Quantize(double deg)
        {
            // Round-half-down at exact half-steps (SEXTANT_PLAN §8 pins
            // 0.005° -> 1/300°); everything above the tie rounds up normally.
            double scaled = Clamp(deg) * StepsPerDeg;
            return Math.Ceiling(scaled - 0.5) / StepsPerDeg;
        }

        /// <summary>Marine reading "45°23.4′" (invariant culture, degree sign U+00B0, prime U+2032).</summary>
        public static string Format(double deg)
        {
            double q = Quantize(deg);
            int d = (int)q;
            double minutes = (q - d) * 60.0;
            return string.Format(CultureInfo.InvariantCulture, "{0}\u00B0{1:00.0}\u2032", d, minutes);
        }
    }
}
