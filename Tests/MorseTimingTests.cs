using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class MorseTimingTests
    {
        private static ConsolCycleConfig Default() => ConsolCycleConfig.Cycle120s;

        // F81: Math.Round replaces truncation — 2.5 rounds to 2 (banker's
        // rounding: ToEven), 17.5 rounds to 18.
        [Fact]
        public void GetDotSoundTicks_DefaultConfig()
        {
            Assert.Equal(4, MorseTiming.GetDotSoundTicks(Default()));
        }

        [Fact]
        public void GetDashSoundTicks_DefaultConfig()
        {
            Assert.Equal(8, MorseTiming.GetDashSoundTicks(Default()));
        }

        [Fact]
        public void GetDotSilenceTicks_DefaultConfig()
        {
            Assert.Equal(16, MorseTiming.GetDotSilenceTicks(Default()));
        }

        [Fact]
        public void GetDashSilenceTicks_DefaultConfig()
        {
            Assert.Equal(12, MorseTiming.GetDashSilenceTicks(Default()));
        }

        [Fact]
        public void GetToneTicks_DefaultConfig()
        {
            Assert.Equal(20, MorseTiming.GetToneTicks(Default()));
        }

        [Fact]
        public void ToTicks_ExactMultiple()
        {
            Assert.Equal(2, MorseTiming.ToTicks(100f));
        }

        [Fact]
        public void ToTicks_BelowOneTick_ClampsToOne()
        {
            Assert.Equal(1, MorseTiming.ToTicks(49f));
            Assert.Equal(1, MorseTiming.ToTicks(0f));
        }

        [Fact]
        public void ToTicks_HalfValue_RoundsToEven()
        {
            Assert.Equal(18, MorseTiming.ToTicks(875f));
        }

        // F136: inconsistent config (sound longer than period) must not
        // silently produce a 1-tick silence that exceeds the period.
        [Fact]
        public void GetDotSilenceTicks_BadConfig_ClampsToOneTick()
        {
            var c = Default();
            c.DotSoundMs = 125f;
            c.ElementPeriodMs = 100f;
            Assert.Equal(1, MorseTiming.GetDotSilenceTicks(c));
        }

        [Fact]
        public void GetDashSilenceTicks_BadConfig_ClampsToOneTick()
        {
            var c = Default();
            c.DashSoundMs = 875f;
            c.ElementPeriodMs = 100f;
            Assert.Equal(1, MorseTiming.GetDashSilenceTicks(c));
        }

        [Fact]
        public void GetDotSilenceTicks_ConsistentConfig()
        {
            var c = Default();
            c.ElementPeriodMs = 1000f;
            Assert.Equal(16, MorseTiming.GetDotSilenceTicks(c));
        }

        [Fact]
        public void CallsignTiming_PreservesExistingProfile()
        {
            var c = Default();

            Assert.Equal(2, MorseTiming.GetCallsignDotSoundTicks(c));
            Assert.Equal(18, MorseTiming.GetCallsignDashSoundTicks(c));
            Assert.Equal(18, MorseTiming.GetCallsignDotSilenceTicks(c));
            Assert.Equal(2, MorseTiming.GetCallsignDashSilenceTicks(c));
        }
    }
}
