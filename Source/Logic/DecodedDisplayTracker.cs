namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Decoded-callsign display text + log-once latch behind
    /// InformationPanelHandler.UpdateSignalDisplay. Extracted (SRP — latch
    /// transitions are not panel logic) so text selection and latch run
    /// offline in DecodedDisplayTrackerTests. Pure C#.
    /// </summary>
    public sealed class DecodedDisplayTracker
    {
        private string lastLogged = "";

        public static string DisplayText(string decoded)
        {
            return string.IsNullOrEmpty(decoded) ? "Decoded: " : "Decoded: " + decoded;
        }

        public bool ShouldLog(string decoded)
        {
            return !string.IsNullOrEmpty(decoded) && decoded != lastLogged;
        }

        public void MarkLogged(string decoded)
        {
            lastLogged = decoded;
        }

        public void Reset()
        {
            lastLogged = "";
        }
    }
}
