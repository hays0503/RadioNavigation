using System;

namespace UBOAT.Mods.RadioNavigation
{
    public static class MorseTiming
    {
        private const float TickMs = 50f;

        public static int GetDotSoundTicks(ConsolCycleConfig config)
        {
            float ms = config.DotSoundMs > 0 ? config.DotSoundMs : 125f;
            return Math.Max(1, (int)(ms / TickMs));
        }

        public static int GetDashSoundTicks(ConsolCycleConfig config)
        {
            float ms = config.DashSoundMs > 0 ? config.DashSoundMs : 875f;
            return Math.Max(1, (int)(ms / TickMs));
        }

        public static int GetDotSilenceTicks(ConsolCycleConfig config)
        {
            float period = config.ElementPeriodMs > 0 ? config.ElementPeriodMs : 1000f;
            float sound = config.DotSoundMs > 0 ? config.DotSoundMs : 125f;
            return Math.Max(1, (int)((period - sound) / TickMs));
        }

        public static int GetDashSilenceTicks(ConsolCycleConfig config)
        {
            float period = config.ElementPeriodMs > 0 ? config.ElementPeriodMs : 1000f;
            float sound = config.DashSoundMs > 0 ? config.DashSoundMs : 875f;
            return Math.Max(1, (int)((period - sound) / TickMs));
        }

        public static int GetToneTicks(ConsolCycleConfig config)
        {
            float ms = config.EquisignalMs > 0 ? config.EquisignalMs : 1000f;
            return Math.Max(1, (int)(ms / TickMs));
        }

        public static int ToTicks(float ms)
        {
            return Math.Max(1, (int)(ms / TickMs));
        }
    }
}
