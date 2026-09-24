using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>P4 dark adaptation: τ=10 s ramp, shield holds, flash resets.</summary>
    public sealed class DarkAdaptationTests
    {
        private static DarkAdaptationState Ramp(double seconds, double step)
        {
            var state = new DarkAdaptationState();
            for (double t = 0.0; t < seconds - 1e-9; t += step)
                state.Step(step, true, false);
            return state;
        }

        [Fact]
        public void Step_Reaches63PercentInTenSeconds()
        {
            Assert.InRange(Ramp(10.0, 0.05).Value, 0.60, 0.66);
        }

        [Fact]
        public void Step_Reaches92PercentInTwentyFiveSeconds()
        {
            Assert.InRange(Ramp(25.0, 0.05).Value, 0.89, 0.95);
        }

        [Fact]
        public void Step_IsMonotonicAndClamped()
        {
            DarkAdaptationState state = Ramp(120.0, 0.05);
            Assert.InRange(state.Value, 0.999, 1.0);
            Assert.False(state.IsRamping);
        }

        [Fact]
        public void Step_ShieldedOrBright_HoldsValue()
        {
            DarkAdaptationState state = Ramp(5.0, 0.05);
            double held = state.Value;
            state.Step(10.0, true, true);   // shielded
            Assert.Equal(held, state.Value);
            state.Step(10.0, false, false); // not dark (inside boat / day)
            Assert.Equal(held, state.Value);
        }

        [Fact]
        public void Reset_ZeroesTheRamp()
        {
            DarkAdaptationState state = Ramp(20.0, 0.05);
            state.Reset();
            Assert.Equal(0.0, state.Value);
            Assert.True(state.IsRamping);
        }

        [Fact]
        public void Step_FrameSpike_IsClamped()
        {
            // A 5 s hitch must not jump the ramp: it counts as 0.25 s.
            var state = new DarkAdaptationState();
            state.Step(5.0, true, false);
            Assert.InRange(state.Value, 0.020, 0.030);
        }

        [Fact]
        public void Step_NonPositiveDt_IsIgnored()
        {
            var state = new DarkAdaptationState();
            state.Step(0.0, true, false);
            state.Step(-1.0, true, false);
            Assert.Equal(0.0, state.Value);
        }
    }
}
