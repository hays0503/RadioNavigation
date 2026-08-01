namespace UBOAT.Mods.RadioNavigation
{
    public struct ConsolCycleConfig
    {
        public float DotSoundMs;
        public float DashSoundMs;
        public float ElementPeriodMs;
        public float CallsignDotMs;
        public float EquisignalMs;
        public float CallsignDurationMs;
        public float PreNavPauseMs;
        public float NavigationDurationMs;
        public float PostNavSilenceMs;
        public int MaxNavigationCount;
        public float DegPerSign;
        public float SectorWidthDeg;
        public int TotalSignals;

        public static readonly ConsolCycleConfig Cycle120s = new ConsolCycleConfig
        {
            DotSoundMs = 125f,
            DashSoundMs = 875f,
            ElementPeriodMs = 1000f,
            CallsignDotMs = 125f,
            EquisignalMs = 1000f,
            CallsignDurationMs = 56000f,
            PreNavPauseMs = 3000f,
            NavigationDurationMs = 60000f,
            PostNavSilenceMs = 1000f,
            MaxNavigationCount = 60,
            DegPerSign = 5f / 60f,
            SectorWidthDeg = 5f,
            TotalSignals = 60
        };

        public static ConsolCycleConfig GetForCycleDuration(int cycleDurationSeconds)
        {
            if (!Cycle120s.IsValid())
                UnityEngine.Debug.LogWarning("[RadioNav] ConsolCycleConfig.Cycle120s is invalid");
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
                && CallsignDotMs > 0
                && EquisignalMs > 0
                && CallsignDurationMs > 0
                && PreNavPauseMs >= 0
                && NavigationDurationMs > 0
                && PostNavSilenceMs >= 0
                && MaxNavigationCount > 0
                && DegPerSign > 0
                && SectorWidthDeg > 0
                && TotalSignals > 0;
        }
    }
}
