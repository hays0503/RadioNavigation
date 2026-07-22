namespace UBOAT.Mods.TestUI
{
    public struct ConsolCycleConfig
    {
        public float DotDurationMs;
        public float DashDurationMs;
        public float ElementGapMs;
        public float CallsignDotMs;
        public float EquisignalMs;
        public float MinCyclePauseMs;
        public int MaxNavigationCount;
        public float DegPerSign;
        public float SectorWidthDeg;
        public int TotalSignals;

        public static readonly ConsolCycleConfig Cycle60s = new ConsolCycleConfig
        {
            DotDurationMs = 250f,
            DashDurationMs = 250f,
            ElementGapMs = 0f,
            CallsignDotMs = 100f,
            EquisignalMs = 500f,
            MinCyclePauseMs = 2000f,
            MaxNavigationCount = 60,
            DegPerSign = 5f / 60f,
            SectorWidthDeg = 5f,
            TotalSignals = 60
        };

        public static ConsolCycleConfig GetForCycleDuration(int cycleDurationSeconds)
        {
            return Cycle60s;
        }

        public float ElementDurationMs(bool isDash)
        {
            return isDash ? DashDurationMs : DotDurationMs;
        }
    }
}
