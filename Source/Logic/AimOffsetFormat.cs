using System;
using System.Globalization;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Debug aim readout (RadioNav.DebugAim): how far the line of sight is
    /// from the identified body, split into the two controls the player has.
    /// The vertical offset (body altitude minus the alidade arc, index error
    /// included) is exactly the number that becomes the fix intercept, so
    /// nulling it gives the "perfect" observation; the horizontal offset keeps
    /// the body inside the identification cone while the drum is set. One
    /// arcminute of offset is one nautical mile of intercept. Pure, no Unity —
    /// unit-tested offline.
    /// </summary>
    public static class AimOffsetFormat
    {
        public const double ArcMinPerDeg = 60.0;

        private const double Epsilon = 1e-9;

        /// <summary>Degrees to arcminutes (1' = 1 NM of intercept).</summary>
        public static double ToArcMin(double deg)
        {
            return deg * ArcMinPerDeg;
        }

        /// <summary>Total angle between the aim and the body (degrees; NaN for
        /// a degenerate vector).</summary>
        public static double TotalDeg(double ax, double ay, double az,
            double bx, double by, double bz)
        {
            double la = Length(ax, ay, az);
            double lb = Length(bx, by, bz);
            if (la < Epsilon || lb < Epsilon)
                return double.NaN;
            double cos = (ax * bx + ay * by + az * bz) / (la * lb);
            return Math.Acos(ClampUnit(cos)) * 180.0 / Math.PI;
        }

        /// <summary>Signed horizontal offset (degrees): positive = the body is
        /// to the right of the aim, so panning right centres it. Measured in
        /// the aim's own frame (atan2 of the body's right/forward components),
        /// which is what the player pans. NaN when the aim is (nearly)
        /// vertical — the right axis is undefined there.</summary>
        public static double HorizontalDeg(double ax, double ay, double az,
            double bx, double by, double bz)
        {
            // Unity right axis for forward a: cross(worldUp, a) = (az, 0, -ax).
            double rx = az;
            double rz = -ax;
            double rlen = Math.Sqrt(rx * rx + rz * rz);
            if (rlen < Epsilon)
                return double.NaN;
            double la = Length(ax, ay, az);
            double lb = Length(bx, by, bz);
            if (la < Epsilon || lb < Epsilon)
                return double.NaN;
            double right = (rx * bx + rz * bz) / (rlen * lb);
            double ahead = (ax * bx + ay * by + az * bz) / (la * lb);
            return Math.Atan2(right, ahead) * 180.0 / Math.PI;
        }

        /// <summary>Compact suffix for the identification line, e.g.
        /// "  Δ4.8′ в+4.6′ г−1.2′" (Δ total, в vertical, г horizontal).
        /// Empty when the total offset is unknown; an unknown component is
        /// shown as a dash without a unit.</summary>
        public static string Format(double totalArcMin, double verticalArcMin,
            double horizontalArcMin)
        {
            if (double.IsNaN(totalArcMin))
                return "";
            return "  \u0394" + Unsigned(totalArcMin)
                + "\u2032 \u0432" + Part(verticalArcMin)
                + " \u0433" + Part(horizontalArcMin);
        }

        /// <summary>"+4.6′" / "−1.2′" / "—".</summary>
        private static string Part(double arcMin)
        {
            return double.IsNaN(arcMin) ? "\u2014" : Signed(arcMin) + "\u2032";
        }

        /// <summary>"+4.6" / "−1.2" (U+2212, the mod's minus).</summary>
        public static string Signed(double arcMin)
        {
            if (double.IsNaN(arcMin))
                return "\u2014";
            return (arcMin < 0.0 ? "\u2212" : "+")
                + Math.Abs(arcMin).ToString("F1", CultureInfo.InvariantCulture);
        }

        /// <summary>"4.8" (magnitude only).</summary>
        public static string Unsigned(double arcMin)
        {
            if (double.IsNaN(arcMin))
                return "\u2014";
            return Math.Abs(arcMin).ToString("F1", CultureInfo.InvariantCulture);
        }

        private static double Length(double x, double y, double z)
        {
            return Math.Sqrt(x * x + y * y + z * z);
        }

        private static double ClampUnit(double value)
        {
            if (value > 1.0)
                return 1.0;
            if (value < -1.0)
                return -1.0;
            return value;
        }
    }
}
