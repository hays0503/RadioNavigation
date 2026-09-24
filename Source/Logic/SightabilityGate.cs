namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Why a body may not be recorded (P5).</summary>
    public enum SightBlock
    {
        None = 0,
        Low = 1,
        Daylight = 2,
        MoonNew = 3,
        MoonSet = 4
    }

    /// <summary>
    /// Visibility gates for body identification (CELESTIAL_REALISM_PLAN P5).
    /// Author decisions: the ONLY geometric refusal is altitude &lt; 10°
    /// (faint stars stay recordable — their difficulty comes from the sky
    /// model, not from a magnitude ban); the Moon is exempt from the altitude
    /// gate and carries its own illumination/set gates; the Sun is never
    /// blocked here. Pure logic, no Unity.
    /// </summary>
    public static class SightabilityGate
    {
        /// <summary>Minimum altitude for a star sight (degrees).</summary>
        public const double MinStarAltitudeDeg = 10.0;

        /// <summary>Sun altitude below which the sky is dark enough to sight
        /// stars and the Moon (degrees).</summary>
        public const double DaylightSunAltitudeDeg = -1.0;

        /// <summary>New-moon illumination floor (kept in step with the
        /// readout and the ephemeris).</summary>
        public const double MoonMinIllumination = 0.05;

        /// <summary>Evaluates the gates for one body.</summary>
        public static SightBlock Evaluate(CelestialBodyKind kind, double altDeg,
            double ownSunAltDeg, double moonIllumination)
        {
            if (kind == CelestialBodyKind.Star)
            {
                if (altDeg < MinStarAltitudeDeg)
                    return SightBlock.Low;
                if (ownSunAltDeg > DaylightSunAltitudeDeg)
                    return SightBlock.Daylight;
                return SightBlock.None;
            }

            if (kind == CelestialBodyKind.Moon)
            {
                if (moonIllumination < MoonMinIllumination)
                    return SightBlock.MoonNew;
                if (altDeg <= 0.0)
                    return SightBlock.MoonSet;
                if (ownSunAltDeg > DaylightSunAltitudeDeg)
                    return SightBlock.Daylight;
                return SightBlock.None;
            }

            return SightBlock.None; // the Sun gates itself by being visible
        }

        /// <summary>Short ASCII reason for logs and the MCP probe.</summary>
        public static string Describe(SightBlock block)
        {
            switch (block)
            {
                case SightBlock.Low: return "low";
                case SightBlock.Daylight: return "daylight";
                case SightBlock.MoonNew: return "moon-new";
                case SightBlock.MoonSet: return "moon-set";
                default: return "ok";
            }
        }

        /// <summary>Identification cone (degrees): FIXED at every zoom (author
        /// decision 2026-11-04). The old shrink to 0.10° at ×6 (6 arcmin) made
        /// a body almost impossible to hold; the recorded Ho now comes from
        /// the drum at the I press, so the cone no longer carries precision.
        /// No zoom parameter: the cone does not depend on it.</summary>
        public const double FixedConeDeg = 1.0;

        /// <summary>Effective identification cone (degrees) — fixed.</summary>
        public static double EffectiveConeDeg()
        {
            return FixedConeDeg;
        }
    }
}
