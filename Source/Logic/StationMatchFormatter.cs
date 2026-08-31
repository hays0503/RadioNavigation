using System;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Formats the closest-station match line for the information panel's
    /// frequency display (P5.7 IPH-3). Pure function over BeaconData +
    /// DialConfig + Constants; no UI or controller state. Extracted from
    /// InformationPanelHandler.FindClosestStation.
    /// </summary>
    public static class StationMatchFormatter
    {
        /// <summary>
        /// Returns a human-readable "callsign freq gain%" line for the station
        /// whose historical frequency is nearest to freqKHz, or "" when none
        /// matches within Constants.StationMatchDeltaKHz. Only meaningful in
        /// zone 1 (the Sonne band); other zones return "".
        /// </summary>
        public static string Format(float freqKHz, int zoneIndex)
        {
            if (zoneIndex != 1)
                return "";

            float bestDelta = float.MaxValue;
            float bestFreq = 0f;
            string bestName = null;
            string bestCallsign = null;

            var stations = BeaconData.Stations;
            for (int i = 0; i < stations.Length; i++)
            {
                var history = stations[i].History;
                if (history.IsNull() || history.Length.IsZero()) continue;
                float? stationFreq = history[0].FrequencyKHz;
                if (stationFreq.IsNull()) continue;

                float delta = Math.Abs(freqKHz - stationFreq.Value);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    bestFreq = stationFreq.Value;
                    bestName = stations[i].Name;
                    string cs = history[0].Callsign;
                    bestCallsign = string.IsNullOrEmpty(cs) ? null : cs;
                }
            }

            if (bestName.IsNull() || bestDelta > Constants.StationMatchDeltaKHz)
                return "";

            float gain = Mathf.Exp(-(bestDelta * bestDelta) / (2f * 1.5f * 1.5f));
            string displayName = !string.IsNullOrEmpty(bestCallsign) ? bestCallsign : bestName;
            return displayName + " " + DialConfig.FormatFrequency(bestFreq, zoneIndex) + " " + Mathf.RoundToInt(gain * 100f) + "%";
        }
    }
}
