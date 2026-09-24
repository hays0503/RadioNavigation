using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Sky-brightness model (CELESTIAL_REALISM_PLAN P4): how much the Moon and
    /// the twilight sky raise the limiting magnitude, and the marine haze band
    /// that eats low stars. The old layer faded every star equally; here the
    /// faint ones die first, so a full moon leaves only the bright field.
    /// Pure math, no Unity.
    /// </summary>
    public static class SkyBrightness
    {
        /// <summary>Limiting V magnitude of a pristine moonless zenith sky.</summary>
        public const double ZenithLimit = 6.0;

        /// <summary>Limiting magnitude at full moon overhead: 6.0 − 2.5 = 3.5
        /// (author decision: mag 3.5 is barely visible when you squint).</summary>
        public const double FullMoonShift = 2.5;

        /// <summary>Hard cap on the total brightening (mag).</summary>
        public const double MaxShift = 5.5;

        /// <summary>Width of the fade band around the limiting magnitude (mag).</summary>
        public const double FadeWidthMag = 0.8;

        /// <summary>Marine haze band at the horizon (mag), clear air.</summary>
        public const double HazeAmplitudeMag = 2.0;

        /// <summary>Extra haze from fog at the horizon (mag).</summary>
        public const double FogHazeAmplitudeMag = 1.0;

        /// <summary>Moon brightening: illumination × how high the Moon is.
        /// At or below the horizon the Moon does not wash the sky.</summary>
        public static double MoonShift(double moonIllumination, double moonAltDeg)
        {
            if (moonIllumination <= 0.0 || moonAltDeg <= 0.0)
                return 0.0;
            return FullMoonShift * moonIllumination * Smoothstep(0.0, 30.0, moonAltDeg);
        }

        /// <summary>Twilight brightening from the Sun's zenith distance:
        /// 0 from nautical twilight end (≥102°), 6 mag in full day.</summary>
        public static double TwilightShift(double sunZenithDeg)
        {
            if (sunZenithDeg >= 102.0)
                return 0.0;
            if (sunZenithDeg >= 97.0)
                return 0.5 * (102.0 - sunZenithDeg) / 5.0;
            if (sunZenithDeg >= 90.0)
                return 0.5 + 3.5 * (97.0 - sunZenithDeg) / 7.0;
            return 6.0;
        }

        /// <summary>Limiting V magnitude for the current sky.</summary>
        public static double LimitingMagnitude(double sunZenithDeg,
            double moonIllumination, double moonAltDeg)
        {
            double shift = MoonShift(moonIllumination, moonAltDeg)
                         + TwilightShift(sunZenithDeg);
            if (shift > MaxShift)
                shift = MaxShift;
            return ZenithLimit - shift;
        }

        /// <summary>Haze penalty in magnitudes for a star at this altitude.</summary>
        public static double HazeLoss(double altDeg, double fogginess)
        {
            if (altDeg < 0.0)
                altDeg = 0.0;
            double fog = fogginess < 0.0 ? 0.0 : (fogginess > 1.0 ? 1.0 : fogginess);
            return HazeAmplitudeMag * Math.Exp(-altDeg / 8.0)
                 + FogHazeAmplitudeMag * fog * Math.Exp(-altDeg / 12.0);
        }

        /// <summary>Per-star visibility: 1 well above the limit, fading to 0
        /// over FadeWidthMag magnitudes below it.</summary>
        public static float VisibilityFactor(double limitingMag, double vmag,
            double hazeLossMag)
        {
            double x = (limitingMag - hazeLossMag - vmag) / FadeWidthMag;
            return (float)Smoothstep(0.0, 1.0, x);
        }

        /// <summary>Hermite smoothstep, clamped.</summary>
        public static double Smoothstep(double edge0, double edge1, double x)
        {
            if (edge1 <= edge0)
                return x < edge0 ? 0.0 : 1.0;
            double t = (x - edge0) / (edge1 - edge0);
            if (t <= 0.0) return 0.0;
            if (t >= 1.0) return 1.0;
            return t * t * (3.0 - 2.0 * t);
        }
    }
}
