using System;

namespace UBOAT.Mods.RadioNavigation
{
    public struct ConsolCycleConfig
    {
        public float CycleDurationSeconds;
        public float DotSoundMs;
        public float DashSoundMs;
        public float ElementPeriodMs;
        public float EquisignalMs;
        public float CallsignDotSoundMs;
        public float CallsignDashSoundMs;
        public float CallsignElementPeriodMs;
        public float PreNavPauseMs;
        public float PostNavSilenceMs;
        public int MaxNavigationCount;
        public float DegPerSign;
        public float SectorWidthDeg;

        // F92: CallsignDotMs/CallsignDurationMs/NavigationDurationMs/
        // TotalSignals were configured but never read outside the validator;
        // timings come from MorseTiming/StationScheduler, and
        // SonneSignalGenerator carries its own sector constants.
        public static readonly ConsolCycleConfig Cycle120s = new ConsolCycleConfig
        {
            CycleDurationSeconds = 120f,
            DotSoundMs = 200f,
            DashSoundMs = 600f,
            ElementPeriodMs = 1000f,
            EquisignalMs = 1000f,
            CallsignDotSoundMs = 125f,
            CallsignDashSoundMs = 375f,
            CallsignElementPeriodMs = 1000f,
            PreNavPauseMs = 3000f,
            PostNavSilenceMs = 3000f,
            MaxNavigationCount = 60,
            DegPerSign = 5f / 60f,
            SectorWidthDeg = 5f
        };

        public static readonly ConsolCycleConfig Cycle40s = new ConsolCycleConfig
        {
            CycleDurationSeconds = 40f,
            DotSoundMs = 200f,
            DashSoundMs = 600f,
            ElementPeriodMs = 1000f,
            EquisignalMs = 1000f,
            CallsignDotSoundMs = 62.5f,
            CallsignDashSoundMs = 187.5f,
            CallsignElementPeriodMs = 500f,
            PreNavPauseMs = 3000f,
            PostNavSilenceMs = 3000f,
            MaxNavigationCount = 60,
            DegPerSign = 5f / 60f,
            SectorWidthDeg = 5f
        };

        public static readonly ConsolCycleConfig Cycle60s = new ConsolCycleConfig
        {
            CycleDurationSeconds = 60f,
            DotSoundMs = 200f,
            DashSoundMs = 600f,
            ElementPeriodMs = 1000f,
            EquisignalMs = 1000f,
            CallsignDotSoundMs = 125f,
            CallsignDashSoundMs = 375f,
            CallsignElementPeriodMs = 1000f,
            PreNavPauseMs = 3000f,
            PostNavSilenceMs = 3000f,
            MaxNavigationCount = 60,
            DegPerSign = 5f / 60f,
            SectorWidthDeg = 5f
        };

        public static ConsolCycleConfig GetForCycleDuration(float cycleDurationSeconds)
        {
            if (!Cycle120s.IsValid())
                UnityEngine.Debug.LogWarning("[RadioNav] ConsolCycleConfig.Cycle120s is invalid");

            if (Math.Abs(cycleDurationSeconds - 40f) < 0.01f)
                return Cycle40s;
            if (Math.Abs(cycleDurationSeconds - 60f) < 0.01f)
                return Cycle60s;
            if (Math.Abs(cycleDurationSeconds - 120f) < 0.01f)
                return Cycle120s;

            UnityEngine.Debug.LogWarning("[RadioNav] Unsupported Consol cycle duration: " + cycleDurationSeconds + " s");
            return Cycle120s;
        }

        // F126: contract for field sanity. A config that fails this is
        // unusable (negative timing, zero navigation window, ...).
        public bool IsValid()
        {
            return DotSoundMs > 0
                && DashSoundMs > 0
                && ElementPeriodMs >= DotSoundMs
                && ElementPeriodMs >= DashSoundMs
                && EquisignalMs > 0
                && CallsignDotSoundMs > 0
                && CallsignDashSoundMs > 0
                && CallsignElementPeriodMs >= CallsignDotSoundMs
                && CallsignElementPeriodMs >= CallsignDashSoundMs
                && PreNavPauseMs >= 0
                && PostNavSilenceMs >= 0
                && MaxNavigationCount > 0
                && DegPerSign > 0
                && SectorWidthDeg > 0;
        }
    }
}
