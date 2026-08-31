using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// SUNDRIFT calibration (SEXTANT_PLAN v1.10): the game's sun equals our
    /// Meeus ephemeris shifted in time (v1.5 finding — the daz/dalt ratio
    /// matched the daily rates), so a single time shift Δt corrects both
    /// altitude and azimuth. The shift is solved against the game sun
    /// direction (reflection read by the controller) and applied to every
    /// ephemeris call of the sextant worksheet. Pure math, no Unity —
    /// unit-tested offline.
    /// </summary>
    public static class SunEphemerisCalibration
    {
        /// <summary>Applied time shift in minutes (game sun = ephemeris at
        /// utc + Δt). Zero until a calibration is solved and applied.</summary>
        public static double DeltaMinutes { get; private set; }

        public static void SetDelta(double minutes)
        {
            DeltaMinutes = minutes;
        }

        /// <summary>Julian day of the calibrated ephemeris: identical to
        /// AstroMath.ToJulianDay while Δt = 0.</summary>
        public static double CalibratedJd(DateTime utc)
        {
            return AstroMath.ToJulianDay(utc) + DeltaMinutes / 1440.0;
        }

        /// <summary>
        /// Solves the time shift that best aligns the own ephemeris sun
        /// direction with the observed (game) direction at the given time
        /// and position. Coarse scan ±15 min, two refinement passes.
        /// Returns false when the best residual stays above the acceptance
        /// threshold (target direction is not a time-shifted sun).
        /// </summary>
        public static bool SolveDelta(DateTime utc, double latDeg, double lonDeg,
            double targetX, double targetY, double targetZ,
            out double deltaMinutes, out double residualDeg)
        {
            double targetLen = Math.Sqrt(targetX * targetX + targetY * targetY + targetZ * targetZ);
            if (targetLen < 1e-9)
            {
                deltaMinutes = 0.0;
                residualDeg = 180.0;
                return false;
            }
            double tx = targetX / targetLen, ty = targetY / targetLen, tz = targetZ / targetLen;

            double best = 0.0;
            double bestAngle = 180.0;
            for (double t = -15.0; t <= 15.0; t += 1.0)
            {
                double a = AngleFor(t, utc, latDeg, lonDeg, tx, ty, tz);
                if (a < bestAngle)
                {
                    bestAngle = a;
                    best = t;
                }
            }
            best = Refine(best, 1.0, 0.1, utc, latDeg, lonDeg, tx, ty, tz, ref bestAngle);
            best = Refine(best, 0.1, 0.01, utc, latDeg, lonDeg, tx, ty, tz, ref bestAngle);

            deltaMinutes = best;
            residualDeg = bestAngle;
            return bestAngle <= ResidualAcceptDeg;
        }

        /// <summary>Residual above which the alignment is rejected: the game
        /// sun must be explainable as a time-shifted ephemeris.</summary>
        public const double ResidualAcceptDeg = 0.25;

        private static double Refine(double center, double span, double step,
            DateTime utc, double latDeg, double lonDeg,
            double tx, double ty, double tz, ref double bestAngle)
        {
            double best = center;
            for (double t = center - span; t <= center + span + 1e-9; t += step)
            {
                double a = AngleFor(t, utc, latDeg, lonDeg, tx, ty, tz);
                if (a < bestAngle)
                {
                    bestAngle = a;
                    best = t;
                }
            }
            return best;
        }

        /// <summary>Angle (degrees) between the ephemeris sun at utc + Δt
        /// and the normalized target direction, in the proven world mapping
        /// (e→x, u→y, n→z) of the star sky.</summary>
        public static double AngleFor(double deltaMinutes, DateTime utc,
            double latDeg, double lonDeg, double tx, double ty, double tz)
        {
            double jd = AstroMath.ToJulianDay(utc) + deltaMinutes / 1440.0;
            double raRad, decRad;
            AstroMath.SunRaDec(jd, out raRad, out decRad);
            double altRad, azRad, e, n, u;
            AstroMath.RaDecToEnu(raRad, decRad, AstroMath.LstDeg(jd, lonDeg), latDeg,
                out altRad, out azRad, out e, out n, out u);
            double len = Math.Sqrt(e * e + n * n + u * u);
            if (len < 1e-9)
                return 180.0;
            double dot = (e * tx + u * ty + n * tz) / len;
            if (dot > 1.0) dot = 1.0;
            if (dot < -1.0) dot = -1.0;
            return Math.Acos(dot) * 180.0 / Math.PI;
        }
    }
}
