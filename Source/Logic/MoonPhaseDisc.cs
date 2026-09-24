using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Lunar phase disc (CELESTIAL_REALISM_PLAN P2): the illuminated fraction
    /// with a real terminator instead of fading the whole quad to transparent
    /// at new moon. The phase angle and the bright-limb position angle come
    /// from the Sun and Moon ephemerides; the alpha field is a rotated
    /// elliptical terminator (semi-minor axis |cos i|). Pure math — the
    /// renderer bakes it into a Texture2D.
    /// </summary>
    public static class MoonPhaseDisc
    {
        /// <summary>Phase angle (radians): 0 = full moon, π = new moon.
        /// Geocentric approximation cos i = −cos(elongation).</summary>
        public static double PhaseAngle(double sunRaRad, double sunDecRad,
            double moonRaRad, double moonDecRad)
        {
            double cosElongation = Math.Sin(moonDecRad) * Math.Sin(sunDecRad)
                + Math.Cos(moonDecRad) * Math.Cos(sunDecRad)
                * Math.Cos(moonRaRad - sunRaRad);
            if (cosElongation > 1.0) cosElongation = 1.0;
            if (cosElongation < -1.0) cosElongation = -1.0;
            return Math.Acos(-cosElongation);
        }

        /// <summary>Illuminated fraction k = (1 + cos i)/2 (0 = new, 1 = full).</summary>
        public static double IlluminatedFraction(double phaseAngleRad)
        {
            return 0.5 * (1.0 + Math.Cos(phaseAngleRad));
        }

        /// <summary>
        /// Position angle of the bright limb (radians, measured from celestial
        /// north through east): where the Sun is seen from the Moon, i.e. the
        /// direction of the lit edge on the disc.
        /// </summary>
        public static double BrightLimbAngle(double sunRaRad, double sunDecRad,
            double moonRaRad, double moonDecRad)
        {
            double dRa = sunRaRad - moonRaRad;
            double y = Math.Cos(sunDecRad) * Math.Sin(dRa);
            double x = Math.Sin(sunDecRad) * Math.Cos(moonDecRad)
                - Math.Cos(sunDecRad) * Math.Sin(moonDecRad) * Math.Cos(dRa);
            double angle = Math.Atan2(y, x);
            if (angle < 0.0)
                angle += 2.0 * Math.PI;
            return angle;
        }

        /// <summary>
        /// Alpha field of the phase disc: size×size row-major. The disc spans
        /// [−1,1]²; the terminator is the ellipse x = −cos(i)·√(1−y²) in the
        /// frame rotated to the bright limb. A soft edge (SoftEdge in disc
        /// units) keeps the limb from aliasing.
        /// </summary>
        public static float[] BuildAlphaField(int size, double phaseAngleRad,
            double brightLimbRad)
        {
            if (size < 4)
                size = 4;
            float[] field = new float[size * size];
            double half = size * 0.5;
            double cosI = Math.Cos(phaseAngleRad);
            double cosPa = Math.Cos(brightLimbRad);
            double sinPa = Math.Sin(brightLimbRad);
            const double soft = 0.02;

            for (int py = 0; py < size; py++)
            {
                // Screen rows grow downward; the disc's +y is celestial north.
                double y = (half - (py + 0.5)) / half;
                for (int px = 0; px < size; px++)
                {
                    double x = ((px + 0.5) - half) / half;
                    double r2 = x * x + y * y;
                    double disc = 1.0 - Smoothstep(1.0 - soft, 1.0, Math.Sqrt(r2));
                    if (disc <= 0.0)
                    {
                        field[py * size + px] = 0f;
                        continue;
                    }

                    // Rotate so the bright limb points along +xr.
                    double xr = x * cosPa + y * sinPa;
                    double yr = -x * sinPa + y * cosPa;
                    double terminator = -cosI * Math.Sqrt(Math.Max(0.0, 1.0 - yr * yr));
                    double lit = Smoothstep(-soft, soft, xr - terminator);
                    field[py * size + px] = (float)(disc * lit);
                }
            }
            return field;
        }

        private static double Smoothstep(double edge0, double edge1, double value)
        {
            if (edge1 <= edge0)
                return value < edge0 ? 0.0 : 1.0;
            double t = (value - edge0) / (edge1 - edge0);
            if (t <= 0.0) return 0.0;
            if (t >= 1.0) return 1.0;
            return t * t * (3.0 - 2.0 * t);
        }
    }
}
