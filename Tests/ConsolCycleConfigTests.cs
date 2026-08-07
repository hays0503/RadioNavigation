using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class ConsolCycleConfigTests
    {
        [Fact]
        public void Cycle120s_ExpectedDefaults()
        {
            var c = ConsolCycleConfig.Cycle120s;
            Assert.Equal(120f, c.CycleDurationSeconds);
            Assert.Equal(200f, c.DotSoundMs);
            Assert.Equal(400f, c.DashSoundMs);
            Assert.Equal(1000f, c.ElementPeriodMs);
            Assert.Equal(1000f, c.EquisignalMs);
            Assert.Equal(125f, c.CallsignDotSoundMs);
            Assert.Equal(875f, c.CallsignDashSoundMs);
            Assert.Equal(1000f, c.CallsignElementPeriodMs);
            Assert.Equal(3000f, c.PreNavPauseMs);
            Assert.Equal(3000f, c.PostNavSilenceMs);
            Assert.Equal(60, c.MaxNavigationCount);
            Assert.Equal(5f, c.SectorWidthDeg);
            Assert.Equal(5f / 60f, c.DegPerSign, 1e-6f);
        }

        [Fact]
        public void GetForCycleDuration_40_ReturnsHalfSecondProfile()
        {
            var c = ConsolCycleConfig.GetForCycleDuration(40);
            Assert.Equal(40f, c.CycleDurationSeconds);
            Assert.Equal(1000f, c.ElementPeriodMs);
            Assert.Equal(200f, c.DotSoundMs);
            Assert.Equal(400f, c.DashSoundMs);
            Assert.Equal(500f, c.CallsignElementPeriodMs);
            Assert.Equal(62.5f, c.CallsignDotSoundMs);
            Assert.Equal(437.5f, c.CallsignDashSoundMs);
            Assert.Equal(ConsolCycleConfig.Cycle120s.MaxNavigationCount, c.MaxNavigationCount);
        }

        [Fact]
        public void GetForCycleDuration_60_ReturnsOneSecondProfile()
        {
            var c = ConsolCycleConfig.GetForCycleDuration(60);
            Assert.Equal(ConsolCycleConfig.Cycle60s.CycleDurationSeconds, c.CycleDurationSeconds);
            Assert.Equal(ConsolCycleConfig.Cycle60s.ElementPeriodMs, c.ElementPeriodMs);
        }

        [Fact]
        public void Cycle120s_IsValid()
        {
            Assert.True(ConsolCycleConfig.Cycle120s.IsValid());
        }

        // F126: fields must satisfy the validity contract.
        [Theory]
        [InlineData(0f, 875f, 1000f, "DotSoundMs")]
        [InlineData(125f, 875f, 100f, "ElementPeriodMs")]
        [InlineData(125f, 875f, -1f, "ElementPeriodMs")]
        [InlineData(125f, 0f, 1000f, "EquisignalMs")]
        [InlineData(125f, 875f, 1000f, "PreNavPauseMs=-1")]
        [InlineData(125f, 875f, 1000f, "MaxNavigationCount=0")]
        public void IsValid_ReturnsFalse_ForBrokenConfig(float dot, float dash, float period, string scenario)
        {
            var c = ConsolCycleConfig.Cycle120s;
            if (scenario == "DotSoundMs") c.DotSoundMs = dot;
            if (scenario == "ElementPeriodMs") c.ElementPeriodMs = period;
            if (scenario == "EquisignalMs") c.EquisignalMs = dash;
            if (scenario == "PreNavPauseMs=-1") c.PreNavPauseMs = -1f;
            if (scenario == "MaxNavigationCount=0") c.MaxNavigationCount = 0;
            Assert.False(c.IsValid());
        }
    }
}
