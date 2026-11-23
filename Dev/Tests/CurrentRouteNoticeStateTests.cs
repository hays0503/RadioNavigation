using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class CurrentRouteNoticeStateTests
    {
        private const long Second = TimeSpan.TicksPerSecond;

        [Fact]
        public void RaisesOneNoticePerTransition()
        {
            var state = new CurrentRouteNoticeState();
            Assert.Equal(RouteNotice.Unreachable, state.Observe(true, 1000 * Second));
            Assert.Equal(RouteNotice.None, state.Observe(true, 2000 * Second));
            Assert.Equal(RouteNotice.None, state.Observe(true, 3000 * Second));
            Assert.Equal(RouteNotice.Restored, state.Observe(false, 4000 * Second));
            Assert.Equal(RouteNotice.None, state.Observe(false, 5000 * Second));
            Assert.Equal(RouteNotice.Unreachable, state.Observe(true, 6000 * Second));
        }

        [Fact]
        public void ANewRouteAnnouncesTheCorrectionOnce()
        {
            var state = new CurrentRouteNoticeState();
            state.Reset();
            Assert.Equal(RouteNotice.Restored, state.Observe(false, 1000 * Second));
            Assert.Equal(RouteNotice.None, state.Observe(false, 1001 * Second));
            Assert.Equal(RouteNotice.None, state.Observe(false, 2000 * Second));
        }

        [Fact]
        public void RecoveryWithoutATransitionStaysSilent()
        {
            var state = new CurrentRouteNoticeState();
            Assert.Equal(RouteNotice.None, state.Observe(false, 1000 * Second));
            Assert.Equal(RouteNotice.None, state.Observe(false, 2000 * Second));
        }

        [Fact]
        public void FlappingInsideTheMinimumIntervalIsSuppressed()
        {
            var state = new CurrentRouteNoticeState();
            Assert.Equal(RouteNotice.Unreachable, state.Observe(true, 1000 * Second));
            // A recovery inside the interval is suppressed and does not change
            // the stored state, so it cannot masquerade as a fresh transition.
            Assert.Equal(RouteNotice.None, state.Observe(false, 1030 * Second));
            Assert.Equal(RouteNotice.None, state.Observe(true, 1060 * Second));
            Assert.Equal(RouteNotice.Restored, state.Observe(false, 1070 * Second));
        }

        [Fact]
        public void ClockGoingBackwardsForgetsTheStateSilently()
        {
            var state = new CurrentRouteNoticeState();
            Assert.Equal(RouteNotice.Unreachable, state.Observe(true, 5000 * Second));
            // A save older than the session restores an earlier clock.
            Assert.Equal(RouteNotice.None, state.Observe(true, 1000 * Second));
            Assert.Equal(RouteNotice.Unreachable, state.Observe(true, 1001 * Second));
        }

        [Fact]
        public void ResetForgetsTheStateWithoutANotice()
        {
            var state = new CurrentRouteNoticeState();
            Assert.Equal(RouteNotice.Unreachable, state.Observe(true, 1000 * Second));
            state.Reset();
            Assert.Equal(RouteNotice.Restored, state.Observe(false, 1001 * Second));
            Assert.Equal(RouteNotice.Unreachable, state.Observe(true, 1100 * Second));
        }

        [Theory]
        [InlineData(124.4f, "Течение: поправка курса возобновлена, до точки ~2 мин")]
        [InlineData(20f, "Течение: поправка курса возобновлена, до точки ~1 мин")]
        [InlineData(0f, "Течение: поправка курса возобновлена")]
        [InlineData(-1f, "Течение: поправка курса возобновлена")]
        [InlineData(float.NaN, "Течение: поправка курса возобновлена")]
        [InlineData(float.PositiveInfinity, "Течение: поправка курса возобновлена")]
        public void CorrectedTextCarriesARoundedEta(float seconds, string expected)
        {
            Assert.Equal(expected, CurrentRouteNoticeText.Corrected(seconds));
        }
    }
}
