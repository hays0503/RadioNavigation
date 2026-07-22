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

        public static readonly ConsolCycleConfig Cycle40s = new ConsolCycleConfig
        {
            DotDurationMs = 222f,
            DashDurationMs = 666f,
            ElementGapMs = 222f,
            CallsignDotMs = 100f,
            EquisignalMs = 500f,
            MinCyclePauseMs = 2000f,
            MaxNavigationCount = 40
        };

        public static readonly ConsolCycleConfig Cycle60s = new ConsolCycleConfig
        {
            DotDurationMs = 231f,
            DashDurationMs = 693f,
            ElementGapMs = 231f,
            CallsignDotMs = 100f,
            EquisignalMs = 500f,
            MinCyclePauseMs = 2000f,
            MaxNavigationCount = 60
        };

        public static ConsolCycleConfig GetForCycleDuration(int cycleDurationSeconds)
        {
            return cycleDurationSeconds <= 40 ? Cycle40s : Cycle60s;
        }

        public float ElementDurationMs(bool isDash)
        {
            return isDash ? DashDurationMs : DotDurationMs;
        }
    }
}
