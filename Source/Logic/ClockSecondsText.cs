using System;
using System.Globalization;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Pure formatting rules for the game's four clock labels.</summary>
    internal static class ClockSecondsText
    {
        private const string MinuteToken = "{4:D2}";

        public static bool TryAddSecondsToDateFormat(string format, out string formatWithSeconds)
        {
            formatWithSeconds = null;
            if (string.IsNullOrEmpty(format))
                return false;

            int tokenIndex = format.IndexOf(MinuteToken, StringComparison.Ordinal);
            if (tokenIndex < 0 || format.IndexOf(MinuteToken, tokenIndex + MinuteToken.Length, StringComparison.Ordinal) >= 0)
                return false;

            formatWithSeconds = format.Substring(0, tokenIndex)
                + MinuteToken + ":{5:D2}"
                + format.Substring(tokenIndex + MinuteToken.Length);
            return true;
        }

        public static string FormatLocalizedDate(string formatWithSeconds, DateTime value)
        {
            if (string.IsNullOrEmpty(formatWithSeconds))
                throw new ArgumentException("A localized date format is required.", "formatWithSeconds");

            return string.Format(CultureInfo.CurrentCulture, formatWithSeconds,
                value, value.Day, value.Year, value.Hour, value.Minute, value.Second);
        }

        public static string FormatClock(DateTime value)
        {
            return string.Format(CultureInfo.CurrentCulture, "{0:D2}:{1:D2}:{2:D2}",
                value.Hour, value.Minute, value.Second);
        }

        public static string FormatClockWithoutSeconds(DateTime value)
        {
            return string.Format(CultureInfo.CurrentCulture, "{0:D2}:{1:D2}",
                value.Hour, value.Minute);
        }

        public static long SecondKey(DateTime value)
        {
            return value.Ticks / TimeSpan.TicksPerSecond;
        }
    }
}
