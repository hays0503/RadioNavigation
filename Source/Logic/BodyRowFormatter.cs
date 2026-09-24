using System;
using System.Globalization;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Readout row formatting for the sextant body list (CELESTIAL_REALISM_PLAN
    /// P5): "name mag alt Az az" plus steering arrows from the current reticle
    /// direction, so the player can find a body instead of guessing. Pure,
    /// invariant culture, no Unity. Glyphs use \u escapes for the game
    /// compiler (C# 7.0 source encoding rule).
    /// </summary>
    public static class BodyRowFormatter
    {
        /// <summary>Below this the arrow is dropped and the axis reads "on target".</summary>
        public const double ArrowDeadZoneDeg = 2.0;

        public static string FormatMagnitude(double vmag)
        {
            return vmag.ToString("F1", CultureInfo.InvariantCulture) + "m";
        }

        public static string FormatPercent(double fraction)
        {
            int percent = (int)Math.Round(fraction * 100.0);
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            return percent.ToString(CultureInfo.InvariantCulture) + "%";
        }

        public static string FormatAlt(double altDeg)
        {
            return altDeg.ToString("F1", CultureInfo.InvariantCulture) + "\u00B0";
        }

        public static string FormatAz(double azDeg)
        {
            return azDeg.ToString("F0", CultureInfo.InvariantCulture) + "\u00B0";
        }

        /// <summary>Steering arrows: "\u219212\u00B0 \u21918\u00B0" while off
        /// target, "\u2022" when the body is on the reticle.</summary>
        public static string FormatArrows(double deltaAzDeg, double deltaAltDeg)
        {
            string az = Arrow(deltaAzDeg, "\u2192", "\u2190");
            string alt = Arrow(deltaAltDeg, "\u2191", "\u2193");
            if (az.Length == 0 && alt.Length == 0)
                return "\u2022";
            return (az + alt).Trim();
        }

        private static string Arrow(double delta, string positive, string negative)
        {
            if (Math.Abs(delta) < ArrowDeadZoneDeg)
                return "";
            string glyph = delta > 0 ? positive : negative;
            return glyph
                + Math.Round(Math.Abs(delta)).ToString(CultureInfo.InvariantCulture)
                + "\u00B0 ";
        }

        /// <summary>One list row: name, magnitude (or "%" for the Moon), altitude,
        /// azimuth, steering arrows.</summary>
        public static string FormatRow(string name, string magnitude, double altDeg, double azDeg,
            double deltaAzDeg, double deltaAltDeg)
        {
            return FormatRow(name, magnitude, altDeg, azDeg)
                + " " + FormatArrows(deltaAzDeg, deltaAltDeg);
        }

        /// <summary>Row without steering arrows (inside the boat the ocular
        /// shows the interior, so a reticle-relative arrow would lie). An
        /// empty magnitude (the Sun carries no tag) is skipped cleanly.</summary>
        public static string FormatRow(string name, string magnitude, double altDeg, double azDeg)
        {
            string tag = string.IsNullOrEmpty(magnitude) ? "" : " " + magnitude;
            return name + tag + " " + FormatAlt(altDeg) + " Az " + FormatAz(azDeg);
        }

        /// <summary>Wraps an angle difference into [-180, 180].</summary>
        public static double Wrap180(double deg)
        {
            double d = deg % 360.0;
            if (d > 180.0) d -= 360.0;
            if (d < -180.0) d += 360.0;
            return d;
        }

        /// <summary>Angular separation (degrees) between two alt/az directions
        /// (P5: the "near the reticle" section without another ephemeris call).</summary>
        public static double AngularSeparationDeg(double alt1Deg, double az1Deg,
            double alt2Deg, double az2Deg)
        {
            double alt1 = alt1Deg * Deg2Rad;
            double alt2 = alt2Deg * Deg2Rad;
            double cosSep = Math.Sin(alt1) * Math.Sin(alt2)
                + Math.Cos(alt1) * Math.Cos(alt2)
                * Math.Cos((az1Deg - az2Deg) * Deg2Rad);
            if (cosSep > 1.0) cosSep = 1.0;
            if (cosSep < -1.0) cosSep = -1.0;
            return Math.Acos(cosSep) / Deg2Rad;
        }

        private const double Deg2Rad = Math.PI / 180.0;
    }
}
