namespace UBOAT.Mods.RadioNavigation
{
    public struct ConsolCycleConfig
    {
        public float DotSoundMs;
        public float DashSoundMs;
        public float ElementPeriodMs;
        public float EquisignalMs;
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
            DotSoundMs = 125f,
            DashSoundMs = 875f,
            ElementPeriodMs = 1000f,
            EquisignalMs = 1000f,
            PreNavPauseMs = 3000f,
            PostNavSilenceMs = 1000f,
            MaxNavigationCount = 60,
            DegPerSign = 5f / 60f,
            SectorWidthDeg = 5f
        };

        public static ConsolCycleConfig GetForCycleDuration(int cycleDurationSeconds)
        {
            if (!Cycle120s.IsValid())
                UnityEngine.Debug.LogWarning("[RadioNav] ConsolCycleConfig.Cycle120s is invalid");
            // F164: only the 120 s cycle is implemented; surface any other
            // request so a future cycle silently mapping onto 120 s is visible.
            if (cycleDurationSeconds != 120)
                UnityEngine.Debug.LogWarning("[RadioNav] ConsolCycleConfig: only the 120 s cycle is supported, got " + cycleDurationSeconds + " s");
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
                && PreNavPauseMs >= 0
                && PostNavSilenceMs >= 0
                && MaxNavigationCount > 0
                && DegPerSign > 0
                && SectorWidthDeg > 0;
        }
    }
}
