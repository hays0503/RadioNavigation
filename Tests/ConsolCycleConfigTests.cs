using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class ConsolCycleConfigTests
    {
        [Fact]
        public void Cycle120s_ExpectedDefaults()
        {
            var c = ConsolCycleConfig.Cycle120s;
            Assert.Equal(125f, c.DotSoundMs);
            Assert.Equal(875f, c.DashSoundMs);
            Assert.Equal(1000f, c.ElementPeriodMs);
            Assert.Equal(1000f, c.EquisignalMs);
            Assert.Equal(3000f, c.PreNavPauseMs);
            Assert.Equal(1000f, c.PostNavSilenceMs);
            Assert.Equal(56000f, c.CallsignDurationMs);
            Assert.Equal(60000f, c.NavigationDurationMs);
            Assert.Equal(60, c.MaxNavigationCount);
            Assert.Equal(60, c.TotalSignals);
            Assert.Equal(5f, c.SectorWidthDeg);
            Assert.Equal(5f / 60f, c.DegPerSign, 1e-6f);
        }

        [Fact]
        public void GetForCycleDuration_120_ReturnsCycle120s()
        {
            var c = ConsolCycleConfig.GetForCycleDuration(120);
            Assert.Equal(ConsolCycleConfig.Cycle120s.DotSoundMs, c.DotSoundMs);
            Assert.Equal(ConsolCycleConfig.Cycle120s.MaxNavigationCount, c.MaxNavigationCount);
            Assert.Equal(ConsolCycleConfig.Cycle120s.NavigationDurationMs, c.NavigationDurationMs);
        }

        // F55/F86 (pending): GetForCycleDuration is a stub that always returns Cycle120s. Baseline pins current behavior.
        [Fact]
        public void GetForCycleDuration_OtherDuration_StillReturnsCycle120s()
        {
            var c = ConsolCycleConfig.GetForCycleDuration(60);
            Assert.Equal(ConsolCycleConfig.Cycle120s.NavigationDurationMs, c.NavigationDurationMs);
        }
    }
}
