using System;
using System.Globalization;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Degree/minute (D°M.m′) coordinate formatting for the sextant
    /// worksheet and map markers. Extracted from SextantController (SRP:
    /// number formatting is not device logic).
    /// </summary>
    internal static class SextantDmFormat
    {
        /// <summary>"221°04.2′" (invariant culture; wraps 59.96′ rounding).</summary>
        public static string FormatDm(double deg)
        {
            double d = Math.Floor(deg);
            double m = (deg - d) * 60.0;
            if (m >= 59.95)
            {
                d += 1.0;
                m = 0.0;
            }
            return string.Format(CultureInfo.InvariantCulture,
                "{0}\u00B0{1:00.0}\u2032", (int)d, m);
        }

        public static string FormatSignedDm(double deg)
        {
            return (deg < 0 ? "\u2212" : "+") + FormatDm(Math.Abs(deg));
        }

        public static string FormatLatLon(double deg, bool isLat)
        {
            string hemi = isLat ? (deg >= 0 ? "N" : "S") : (deg >= 0 ? "E" : "W");
            return FormatDm(Math.Abs(deg)) + hemi;
        }

        /// <summary>Latin legend variant for the map marker tooltip: the
        /// game's TMP font has no Cyrillic and no U+2032 prime glyph, so
        /// this uses an ASCII apostrophe (in-game 2026-08-30).</summary>
        public static string FormatLatLonAscii(double deg, bool isLat)
        {
            string hemi = isLat ? (deg >= 0 ? "N" : "S") : (deg >= 0 ? "E" : "W");
            double d = Math.Floor(Math.Abs(deg));
            double m = (Math.Abs(deg) - d) * 60.0;
            if (m >= 59.95)
            {
                d += 1.0;
                m = 0.0;
            }
            return string.Format(CultureInfo.InvariantCulture,
                "{0}\u00B0{1:00.0}'{2}", (int)d, m, hemi);
        }
    }
}
