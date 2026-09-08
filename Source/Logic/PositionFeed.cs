using System;
using System.Globalization;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Pure position-poll decisions behind SonneController.Update:
    /// significant-movement threshold (~11 m) and the 10 s position log
    /// line. Extracted (SRP — poll math is not controller logic) so both
    /// are pinned offline in PositionFeedTests. Coordinates use
    /// InvariantCulture like BearingDiagnostics (ambient-locale commas
    /// broke the old inline format on RU systems). Pure C#.
    /// </summary>
    public static class PositionFeed
    {
        private const float MovementThresholdDeg = 0.0001f;

        public static bool HasMoved(float lat, float lon, float prevLat, float prevLon)
        {
            return Math.Abs(lat - prevLat) > MovementThresholdDeg
                || Math.Abs(lon - prevLon) > MovementThresholdDeg;
        }

        public static string FormatLogLine(float lat, float lon, DateTime utc)
        {
            return "[RadioNav][POSITION] provider returned " + lat.ToString("F5", CultureInfo.InvariantCulture) + "," + lon.ToString("F5", CultureInfo.InvariantCulture)
                + " | " + lat.ToString("F4", CultureInfo.InvariantCulture) + "," + lon.ToString("F4", CultureInfo.InvariantCulture)
                + " | " + utc.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture);
        }
    }
}
