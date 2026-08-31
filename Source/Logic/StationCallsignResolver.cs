namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Resolves a station's callsign from its beacon history (P5.6 SC-1).
    /// Pure function over BeaconStation; extracted from
    /// SonneController.TryGetCallsignFromHistory.
    /// </summary>
    public static class StationCallsignResolver
    {
        /// <summary>
        /// Returns the first history entry's callsign, or false when absent.
        /// F87: absence is reported instead of fabricating a fake one; the
        /// scheduler skips the callsign phase and the decoder clears its
        /// buffer. F66: station.Name may be null and is not used here.
        /// </summary>
        public static bool TryGet(BeaconStation station, out string callsign)
        {
            if (station.History.IsNotNull()
                && station.History.Length.IsPositive()
                && !string.IsNullOrEmpty(station.History[0].Callsign))
            {
                callsign = station.History[0].Callsign;
                return true;
            }

            callsign = null;
            return false;
        }
    }
}
