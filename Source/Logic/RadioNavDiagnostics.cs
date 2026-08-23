using System.Collections.Generic;
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

        private const float CacheSeconds = 0.25f;
        // Stopwatch instead of Time.unscaledTime: this class compiles into
        // the test assembly too, where UnityEngine.Time has no stub.
        private static readonly System.Diagnostics.Stopwatch cacheClock =
            System.Diagnostics.Stopwatch.StartNew();
        private static readonly Dictionary<string, bool> cachedValue = new Dictionary<string, bool>();
        private static float cacheRefreshedAt = -1f;

        /// <summary>
        /// True when the PlayerPrefs int under key is non-zero, or when the
        /// key is absent and defaultValue is true.
        /// </summary>
        public static bool IsEnabled(string key, bool defaultValue)
        {
            int stored = PlayerPrefs.GetInt(key, defaultValue ? 1 : 0);
            return stored != 0;
        }

        /// <summary>
        /// Per-frame-hot variant: caches all keys for CacheSeconds so Update()
        /// callers stop hitting the native PlayerPrefs registry every frame.
        /// Console toggles take effect within that window.
        /// </summary>
        public static bool IsEnabledCached(string key, bool defaultValue)
        {
            float now = (float)cacheClock.Elapsed.TotalSeconds;
            if (now - cacheRefreshedAt >= CacheSeconds)
            {
                cachedValue[LighthouseGlowVerboseKey] =
                    IsEnabled(LighthouseGlowVerboseKey, true);
                cachedValue[LighthouseMapMarkerPositionKey] =
                    IsEnabled(LighthouseMapMarkerPositionKey, true);
                cacheRefreshedAt = now;
            }
            return cachedValue.TryGetValue(key, out bool value) ? value : IsEnabled(key, defaultValue);
        }
    }
}
