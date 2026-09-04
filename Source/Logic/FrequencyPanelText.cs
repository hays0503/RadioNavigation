namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Frequency/station header lines behind
    /// InformationPanelHandler.OnFrequencyChanged. Extracted (SRP — line
    /// assembly is not panel logic) so both run offline in
    /// FrequencyPanelTextTests. Pure C#, no Unity dependency.
    /// </summary>
    public static class FrequencyPanelText
    {
        public static string FreqLine(float freqKHz, int zoneIndex)
        {
            string formatted = DialConfig.FormatFrequency(freqKHz, zoneIndex);
            string bandId = zoneIndex.IsNonNegative() && zoneIndex < DialConfig.Bands.Length
                ? DialConfig.Bands[zoneIndex].Id : "?";
            return "Freq: " + formatted + " (Band " + bandId + ")";
        }

        public static string StationName(float freqKHz, int zoneIndex)
        {
            return StationMatchFormatter.Format(freqKHz, zoneIndex);
        }
    }
}
