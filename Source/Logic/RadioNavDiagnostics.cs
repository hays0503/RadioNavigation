using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// PlayerPrefs-gated switch for the mod's verbose diagnostics logging.
    /// Refactoring step: the verbose logs in LighthouseBeamGlow (glow diag,
    /// screenPt, dot/flare model dumps) and the LighthouseMapMarker position
    /// log check these keys every time they would log. Default is enabled —
    /// behavior unchanged after the refactor; disable in-game with
    /// PlayerPrefs RadioNav.LighthouseGlowVerbose=0 (or via the console).
    /// </summary>
    public static class RadioNavDiagnostics
    {
        public const string LighthouseGlowVerboseKey = "RadioNav.LighthouseGlowVerbose";
        public const string LighthouseMapMarkerPositionKey = "RadioNav.LighthouseMapMarkerPosition";

        /// <summary>
        /// True when the PlayerPrefs int under key is non-zero, or when the
        /// key is absent and defaultValue is true.
        /// </summary>
        public static bool IsEnabled(string key, bool defaultValue)
        {
            int stored = PlayerPrefs.GetInt(key, defaultValue ? 1 : 0);
            return stored != 0;
        }
    }
}
