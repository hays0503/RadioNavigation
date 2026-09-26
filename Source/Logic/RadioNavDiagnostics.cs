using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// PlayerPrefs-gated switch for the mod's verbose diagnostics logging.
    /// Refactoring step: the verbose logs in LighthouseBeamGlow (glow diag,
    /// screenPt, dot/flare model dumps) and the LighthouseMapMarker position
    /// log check these keys every time they would log. Diagnostic flags are
    /// OFF by default; enable in-game with PlayerPrefs
    /// RadioNav.LighthouseGlowVerbose=1 (or via the console).
    /// </summary>
    public static class RadioNavDiagnostics
    {
        public const string LighthouseGlowVerboseKey = "RadioNav.LighthouseGlowVerbose";
        public const string LighthouseMapMarkerPositionKey = "RadioNav.LighthouseMapMarkerPosition";
        // Declutter shown/hidden counter (ADR-032); off by default.
        public const string LighthouseDeclutterStatsKey = "RadioNav.LighthouseDeclutterStats";
        // F221: informational release logs (bearing report, decision/stage,
        // panel visibility, flare over-range). OFF by default — these are
        // diagnostics, not errors; enable with PlayerPrefs RadioNav.VerboseLog=1.
        public const string VerboseLogKey = "RadioNav.VerboseLog";
        // Debug aim readout in the ocular (RadioNav.DebugAim): the total,
        // vertical and horizontal offsets between the line of sight and the
        // identified body, appended to the identification line. OFF by
        // default — it is an aiming aid for nulling the intercept.
        public const string AimDebugKey = "RadioNav.DebugAim";

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
                    IsEnabled(LighthouseGlowVerboseKey, false);
                cachedValue[LighthouseMapMarkerPositionKey] =
                    IsEnabled(LighthouseMapMarkerPositionKey, false);
                cachedValue[LighthouseDeclutterStatsKey] =
                    IsEnabled(LighthouseDeclutterStatsKey, false);
                cachedValue[VerboseLogKey] =
                    IsEnabled(VerboseLogKey, false);
                cachedValue[AimDebugKey] =
                    IsEnabled(AimDebugKey, false);
                // F233: the sextant aim-freeze gate is evaluated EVERY FRAME
                // while the sextant is open (SextantPipPose.Update -> :109 ->
                // UpdateAimFreeze). These two keys were missing here, so every
                // lookup missed the dictionary and fell through to a native
                // PlayerPrefs registry read — twice per frame. Feature gates
                // (default true) must be listed too, not just diagnostics.
                cachedValue[SextantStabilization.PrefsKey] =
                    IsEnabled(SextantStabilization.PrefsKey, true);
                cachedValue[SextantStabilization.LockPrefsKey] =
                    IsEnabled(SextantStabilization.LockPrefsKey, true);
                cacheRefreshedAt = now;
                cacheInitialized = true;
            }
            return cachedValue.TryGetValue(key, out bool value) ? value : IsEnabled(key, defaultValue);
        }

        /// <summary>Small diagnostic prefix for throttled/lifecycle log lines.
        /// Unity's Player.log has no timestamp or frame column; without this,
        /// correlating a mod line with the game's load coroutine is guesswork.</summary>
        public static string Stamp()
        {
            return "[t=" + Time.time.ToString("F2", CultureInfo.InvariantCulture) +
                " frame=" + Time.frameCount + "] ";
        }

        /// <summary>
        /// True when the shared informational-log switch is on (default off).
        /// Cached, so it is safe on per-frame paths (F221).
        /// </summary>
        public static bool IsVerboseLogEnabled()
        {
            return IsEnabledCached(VerboseLogKey, false);
        }
    }
}
