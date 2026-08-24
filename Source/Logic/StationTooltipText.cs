using System;
using System.Globalization;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Pure formatter for the station map-marker tooltip (unit-tested, no
    /// Unity types). Hover shows the station name; the pinned tooltip adds
    /// the callsign/frequency line and the country/role line, omitting any
    /// line whose data is missing. Frequency digits are invariant-cultured
    /// so a RU OS locale cannot produce a decimal comma on the map.
    /// </summary>
    public static class StationTooltipText
    {
        public static string Hover(BeaconStation station)
        {
            return station.Name ?? string.Empty;
        }

        public static string Pinned(BeaconStation station)
        {
            string name = station.Name ?? string.Empty;

            string callsign = null;
            float? frequencyKHz = null;
            if (station.History.IsNotNull() && station.History.Length.IsPositive())
            {
                callsign = station.History[0].Callsign;
                frequencyKHz = station.History[0].FrequencyKHz;
            }

            string signalLine = JoinNonEmpty(" · ",
                callsign,
                frequencyKHz.HasValue ? FormatFrequencyKhz(frequencyKHz.Value) : null);

            string originLine = JoinNonEmpty(" · ",
                station.Country,
                RoleLabel(station.Role));

            return JoinNonEmpty("\n", name, signalLine, originLine);
        }

        private static string JoinNonEmpty(string separator, params string[] parts)
        {
            int count = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(parts[i]))
                    parts[count++] = parts[i];
            }

            if (count == 0)
                return string.Empty;

            string result = parts[0];
            for (int i = 1; i < count; i++)
                result += separator + parts[i];
            return result;
        }

        private static string RoleLabel(BeaconStationRole role)
        {
            return role == BeaconStationRole.DirectionalSonne
                ? "Directional Sonne"
                : "Radio beacon";
        }

        private static string FormatFrequencyKhz(float khz)
        {
            bool whole = khz == Math.Floor(khz);
            string digits = whole
                ? khz.ToString("F0", CultureInfo.InvariantCulture)
                : khz.ToString("F1", CultureInfo.InvariantCulture);
            return digits + " kHz";
        }
    }
}
