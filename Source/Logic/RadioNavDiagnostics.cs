using System;
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

        private const int CacheMs = 250;
        // Environment.TickCount instead of Stopwatch: System.Diagnostics is
        // denied by the game's mod sandbox (whole mod fails load on it).
        // mscorlib member, works in tests too (they sleep real time).
        private static readonly Dictionary<string, bool> cachedValue = new Dictionary<string, bool>();
        private static int cacheRefreshedAt;
        private static bool cacheInitialized;

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
            int now = Environment.TickCount;
            if (!cacheInitialized || unchecked(now - cacheRefreshedAt) >= CacheMs)
            {
                cachedValue[LighthouseGlowVerboseKey] =
                    IsEnabled(LighthouseGlowVerboseKey, true);
                cachedValue[LighthouseMapMarkerPositionKey] =
                    IsEnabled(LighthouseMapMarkerPositionKey, true);
                cacheRefreshedAt = now;
                cacheInitialized = true;
            }
            return cachedValue.TryGetValue(key, out bool value) ? value : IsEnabled(key, defaultValue);
        }
    }
}
