using System.Globalization;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Per-station pattern card line + mm:ss duration behind
    /// InformationPanelHandler. Extracted (SRP — line assembly is not
    /// panel logic) so the format runs offline on synthetic diagnostics
    /// in PatternLineFormatterTests. Numbers use InvariantCulture like
    /// BearingDiagnostics (ambient-locale commas broke the old inline
    /// format on RU systems). Mathf comes from UnityEngine (stubbed in
    /// tests).
    /// </summary>
    public static class PatternLineFormatter
    {
        public static string BuildLine(StationDiagnostics diag, int index, int dominantIndex,
            int recvDots, int recvDashes)
        {
            string callsign = diag.Callsign ?? "?";
            string beam = diag.InBeam ? "IN BEAM" : "OUTSIDE";
            string bfo = diag.BeatFrequency.ToString("F0", CultureInfo.InvariantCulture) + "Hz";
            string gain = Mathf.RoundToInt(diag.Gain * 100f) + "%";
            string phase = diag.SchedulerPhase.ToString();

            string line = callsign + " [" + beam + "] " + gain + " BFO:" + bfo
                + " " + phase + " ToBoat:" + diag.BearingToBoat.ToString("F1", CultureInfo.InvariantCulture) + "\u00B0";
            if (diag.NavigationElapsed.IsPositive())
                line += " " + FormatDuration(diag.NavigationElapsed);
            line += "\n  Zone:S" + diag.SectorNumber
                + (diag.IsDashSector ? "(DASH)" : "(DOT)")
                + " EQ@" + diag.EqSignIndex + "/" + (diag.DotCount + diag.DashCount)
                + " Dots:" + diag.DotCount
                + " Dashes:" + diag.DashCount
                + " Bearing:" + diag.CurrentBearing.ToString("F1", CultureInfo.InvariantCulture) + "\u00B0";
            if (index == dominantIndex)
            {
                line += "\n  Recv: " + recvDots
                    + "\u2022 " + recvDashes + "\u25AC";
            }
            return line;
        }

        public static string FormatDuration(float seconds)
        {
            int s = Mathf.RoundToInt(seconds);
            int min = s / 60;
            int sec = s % 60;
            return min.ToString("D2") + ":" + sec.ToString("D2");
        }

        public static string StationSuffix(string baseText, StationScheduler.Phase phase, float navigationElapsed)
        {
            string suffix = " [" + phase.ToString() + "]";
            if (navigationElapsed.IsPositive())
                suffix += " " + FormatDuration(navigationElapsed);
            return baseText + suffix;
        }
    }
}
