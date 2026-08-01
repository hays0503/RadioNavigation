using System;

namespace UBOAT.Mods.RadioNavigation
{
    public static class MorseTiming
    {
        private const float TickMs = 50f;

        public static int GetDotSoundTicks(ConsolCycleConfig config)
        {
            float ms = config.DotSoundMs.IsPositive() ? config.DotSoundMs : 125f;
            return Math.Max(1, (int)Math.Round(ms / TickMs));
        }

        public static int GetDashSoundTicks(ConsolCycleConfig config)
        {
            float ms = config.DashSoundMs.IsPositive() ? config.DashSoundMs : 875f;
            return Math.Max(1, (int)Math.Round(ms / TickMs));
        }

        public static int GetDotSilenceTicks(ConsolCycleConfig config)
        {
            float period = config.ElementPeriodMs.IsPositive() ? config.ElementPeriodMs : 1000f;
            float sound = config.DotSoundMs.IsPositive() ? config.DotSoundMs : 125f;
            return ToSilenceTicks(period, sound, "DotSoundMs");
        }

        public static int GetDashSilenceTicks(ConsolCycleConfig config)
        {
            float period = config.ElementPeriodMs.IsPositive() ? config.ElementPeriodMs : 1000f;
            float sound = config.DashSoundMs.IsPositive() ? config.DashSoundMs : 875f;
            return ToSilenceTicks(period, sound, "DashSoundMs");
        }

        // F136: an inconsistent config (sound longer than the element period)
        // used to produce a misleading 1-tick silence instead of surfacing
        // the configuration error.
        private static int ToSilenceTicks(float period, float sound, string field)
        {
            float silenceMs = Math.Max(0, period - sound);
            if (silenceMs <= 0)
                UnityEngine.Debug.LogWarning("[RadioNav][MorseTiming] ElementPeriodMs (" + period + ") < " + field + " (" + sound + "), timing inaccurate");
            return Math.Max(1, (int)Math.Round(silenceMs / TickMs));
        }

        public static int GetToneTicks(ConsolCycleConfig config)
        {
            float ms = config.EquisignalMs.IsPositive() ? config.EquisignalMs : 1000f;
            return Math.Max(1, (int)Math.Round(ms / TickMs));
        }

        public static int ToTicks(float ms)
        {
            return Math.Max(1, (int)Math.Round(ms / TickMs));
        }
    }
}
