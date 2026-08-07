using System;

namespace UBOAT.Mods.RadioNavigation
{
    public static class MorseTiming
    {
        private const float TickMs = 50f;

        public static int GetDotSoundTicks(ConsolCycleConfig config)
        {
            return GetSoundTicks(config.DotSoundMs, 200f);
        }

        public static int GetDashSoundTicks(ConsolCycleConfig config)
        {
            return GetSoundTicks(config.DashSoundMs, 400f);
        }

        public static int GetDotSilenceTicks(ConsolCycleConfig config)
        {
            float period = config.ElementPeriodMs.IsPositive() ? config.ElementPeriodMs : 1000f;
            float sound = config.DotSoundMs.IsPositive() ? config.DotSoundMs : 200f;
            return ToSilenceTicks(period, sound, "DotSoundMs");
        }

        public static int GetDashSilenceTicks(ConsolCycleConfig config)
        {
            float period = config.ElementPeriodMs.IsPositive() ? config.ElementPeriodMs : 1000f;
            float sound = config.DashSoundMs.IsPositive() ? config.DashSoundMs : 400f;
            return ToSilenceTicks(period, sound, "DashSoundMs");
        }

        public static int GetCallsignDotSoundTicks(ConsolCycleConfig config)
        {
            return GetSoundTicks(config.CallsignDotSoundMs, 125f);
        }

        public static int GetCallsignDashSoundTicks(ConsolCycleConfig config)
        {
            return GetSoundTicks(config.CallsignDashSoundMs, 875f);
        }

        public static int GetCallsignDotSilenceTicks(ConsolCycleConfig config)
        {
            float period = config.CallsignElementPeriodMs.IsPositive() ? config.CallsignElementPeriodMs : 1000f;
            float sound = config.CallsignDotSoundMs.IsPositive() ? config.CallsignDotSoundMs : 125f;
            return ToSilenceTicks(period, sound, "CallsignDotSoundMs");
        }

        public static int GetCallsignDashSilenceTicks(ConsolCycleConfig config)
        {
            float period = config.CallsignElementPeriodMs.IsPositive() ? config.CallsignElementPeriodMs : 1000f;
            float sound = config.CallsignDashSoundMs.IsPositive() ? config.CallsignDashSoundMs : 875f;
            return ToSilenceTicks(period, sound, "CallsignDashSoundMs");
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

        private static int GetSoundTicks(float milliseconds, float fallbackMilliseconds)
        {
            float ms = milliseconds.IsPositive() ? milliseconds : fallbackMilliseconds;
            return Math.Max(1, (int)Math.Round(ms / TickMs));
        }
    }
}
