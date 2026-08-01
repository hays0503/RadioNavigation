using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class MorseTimingTests
    {
        private static ConsolCycleConfig Default() => ConsolCycleConfig.Cycle120s;

        // F81 (truncation): (int) casts 2.5 -> 2. Value is the current behavior; will become 3 after Round() fix.
        [Fact]
        public void GetDotSoundTicks_DefaultConfig_Truncates()
        {
            Assert.Equal(2, MorseTiming.GetDotSoundTicks(Default()));
        }

        [Fact]
        public void GetDashSoundTicks_DefaultConfig_Truncates()
        {
            Assert.Equal(17, MorseTiming.GetDashSoundTicks(Default()));
        }

        [Fact]
        public void GetDotSilenceTicks_DefaultConfig()
        {
            Assert.Equal(17, MorseTiming.GetDotSilenceTicks(Default()));
        }

        [Fact]
        public void GetDashSilenceTicks_DefaultConfig()
        {
            Assert.Equal(2, MorseTiming.GetDashSilenceTicks(Default()));
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
        public void ToTicks_Truncates()
        {
            Assert.Equal(17, MorseTiming.ToTicks(875f));
        }
    }
}
