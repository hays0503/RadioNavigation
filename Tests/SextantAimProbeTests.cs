using System;
using UBOAT.Mods.RadioNavigation;
using Xunit;

namespace Tests
{
    /// <summary>
    /// The debug aim probe (author's request, 2026-10-02): a reproducible angular
    /// acceleration so a motion-locked defect can be reproduced without a mouse.
    ///
    /// The tests are weighted towards the ways a probe could misbehave rather
    /// than towards the arithmetic, because the arithmetic cannot hurt anyone
    /// while a probe that keeps running can: it moves the player's view.
    /// </summary>
    public class SextantAimProbeTests
    {
        private static SextantAimProbe.State Run(SextantAimProbe.State state,
            double yawAccel, double pitchAccel, int steps, double dt)
        {
            for (int i = 0; i < steps; i++)
                state = SextantAimProbe.Advance(state, yawAccel, pitchAccel, dt);
            return state;
        }

        [Fact]
        public void AProbeIsInertUnlessArmed()
        {
            Assert.Equal(0d, SextantAimProbe.AccelerationFrom(0), 9);
            // Negative means "turn that way", NOT off. This was a live defect:
            // lateral probing was impossible, and the test below used to assert
            // the buggy behaviour, i.e. it locked the bug in.
            Assert.Equal(-5d, SextantAimProbe.AccelerationFrom(-5), 9);
            // The unit is plain deg/s^2 now; the thousandths version made "120"
            // mean 0.12 deg/s^2, which was invisible in the telemetry and made
            // six seconds of swing produce 0.93 degrees.
            Assert.Equal(120d, SextantAimProbe.AccelerationFrom(120), 9);
            Assert.Equal((double)SextantAimProbe.MaxAccelerationUnits,
                SextantAimProbe.AccelerationFrom(999999), 9);
            Assert.Equal(-(double)SextantAimProbe.MaxAccelerationUnits,
                SextantAimProbe.AccelerationFrom(-999999), 9);
            Assert.Equal(0d, SextantAimProbe.SecondsFrom(0), 9);
            Assert.Equal(0d, SextantAimProbe.SecondsFrom(-3), 9);

            // No acceleration, or no time, means no probe at all - a preference
            // left at its default must never move the view. NOTE: a negative
            // acceleration is armed, not off.
            Assert.Equal(0d, SextantAimProbe.Armed(0d, 0d, 4d).SecondsLeft, 9);
            Assert.Equal(0d, SextantAimProbe.Armed(90d, 0d, 0d).SecondsLeft, 9);
            Assert.False(SextantAimProbe.Armed(0d, 0d, 0d).Moving);
            Assert.True(SextantAimProbe.Armed(-90d, 0d, 4d).SecondsLeft > 0d,
                "a leftward probe must be armable");
        }

        [Fact]
        public void TheAccelerationBuildsRealAngularMotion()
        {
            SextantAimProbe.State state = SextantAimProbe.Armed(90d, 0d, 4d);
            Assert.True(state.SecondsLeft > 0d);

            state = Run(state, 90d, 0d, 10, 0.016);
            Assert.True(state.YawRate > 0.5, "the probe must actually accelerate, rate " + state.YawRate);
            Assert.True(state.Yaw > 0d, "the probe must actually move the view");
            Assert.False(state.Moving == false, "a running probe must report itself as moving");
        }

        [Fact]
        public void TheViewCannotRunAway()
        {
            // Two independent stops, because either alone is enough to hurt: the
            // angle clamp (so the view cannot wrap through the poles) and the
            // rate clamp (so the shadow cannot saturate on a single frame).
            SextantAimProbe.State state = SextantAimProbe.Armed(4000d, 4000d, 30d);
            state = Run(state, 4000d, 4000d, 2000, 1.0 / 60.0);
            Assert.True(Math.Abs(state.Yaw) <= SextantAimProbe.MaxAngleDegrees + 1e-9,
                "yaw ran past the clamp: " + state.Yaw);
            Assert.True(Math.Abs(state.Pitch) <= SextantAimProbe.MaxAngleDegrees + 1e-9,
                "pitch ran past the clamp: " + state.Pitch);
            Assert.True(Math.Abs(state.YawRate) <= SextantAimProbe.MaxRateDegPerSec + 1e-9);
            Assert.True(Math.Abs(state.PitchRate) <= SextantAimProbe.MaxRateDegPerSec + 1e-9);
        }

        [Fact]
        public void HittingTheAngleStopDropsTheVelocity()
        {
            // Otherwise the probe would grind against the clamp for the rest of
            // its life, and the "swing" would be a press against a wall - which
            // is not the motion the artefact needs reproduced.
            // The window has to stay inside the run time: a probe that outlives
            // its timer returns to rest and the test would pass or fail for the
            // wrong reason.
            SextantAimProbe.State state = SextantAimProbe.Armed(3000d, 0d,
                SextantAimProbe.MaxSeconds);
            state = Run(state, 3000d, 0d, 400, 1.0 / 60.0);
            Assert.True(state.SecondsLeft > 0d, "the probe expired mid-test, so the clamp was never exercised");
            Assert.True(Math.Abs(state.Yaw) >= SextantAimProbe.MaxAngleDegrees - 1e-6, "the clamp was never reached");
            Assert.Equal(0d, state.YawRate, 9);
        }

        [Fact]
        public void TheRateFollowsTheBleedBalanceRatherThanRunningFree()
        {
            // With bleed b and acceleration a the rate settles at a/b. Measured
            // against the closed form (a/b)(1 - e^(-b t)) so that DELETING the
            // bleed is caught: without it the rate would be a*t, and at t=1 s that
            // is 30 deg/s instead of 25.3 - outside the tolerance below.
            const double accel = 30d;
            const double seconds = 1.0;
            const int steps = 60;
            SextantAimProbe.State state = SextantAimProbe.Armed(accel, 0d,
                SextantAimProbe.MaxSeconds);
            state = Run(state, accel, 0d, steps, seconds / steps);

            double expected = (accel / SextantAimProbe.VelocityBleedPerSecond)
                * (1d - Math.Exp(-SextantAimProbe.VelocityBleedPerSecond * seconds));
            Assert.True(Math.Abs(state.YawRate - expected) < expected * 0.15,
                "rate " + state.YawRate + " does not match the bleed balance " + expected
                    + " - the velocity bleed is gone or the integration is wrong");
            // And it must never overshoot the balance, whatever the preference says.
            Assert.True(state.YawRate <= accel / SextantAimProbe.VelocityBleedPerSecond + 1e-9);
        }

        [Fact]
        public void TheProbeBleedsOffWhenTheVectorIsReleased()
        {
            // "Hold the vector" has to read as a swing, not a press: when the
            // vector goes away the rate has to fall on its own, and the offset
            // has to stop growing. Without the bleed the rate would be carried to
            // the angle stop and stay there.
            SextantAimProbe.State state = SextantAimProbe.Armed(30d, 0d,
                SextantAimProbe.MaxSeconds);
            state = Run(state, 30d, 0d, 60, 1.0 / 60.0);
            double driven = state.YawRate;
            Assert.True(driven > 1d, "the probe never got going, so there is nothing to bleed");

            double previous = driven;
            for (int i = 0; i < 30; i++)
            {
                state = SextantAimProbe.Advance(state, 0d, 0d, 1.0 / 60.0);
                Assert.True(state.YawRate < previous,
                    "the rate did not fall after the vector was released: "
                        + previous + " -> " + state.YawRate);
                previous = state.YawRate;
            }
            // 30 frames is 0.5 s, and the bleed time constant is 1/b ~ 2.9 s, so
            // the expected decay is (1 - b*dt)^30 ~ 0.84 - NOT a halving. Checked
            // against the closed form so the constant stays meaningful.
            double expected = driven
                * Math.Pow(1d - SextantAimProbe.VelocityBleedPerSecond / 60.0, 30d);
            Assert.True(Math.Abs(previous - expected) < expected * 0.1,
                "decay " + driven + " -> " + previous + " does not match the bleed "
                    + expected + " - the rate is being held up by something");
        }

        [Fact]
        public void TheProbeStopsItselfWhenItsTimeIsUp()
        {
            // The single most important property: a forgotten preference must not
            // leave the view moving. The state has to come back to rest, not just
            // to a small offset.
            SextantAimProbe.State state = SextantAimProbe.Armed(120d, 0d, 1.0);
            state = Run(state, 120d, 0d, 1000, 1.0 / 60.0);
            Assert.Equal(0d, state.SecondsLeft, 9);
            Assert.Equal(0d, state.Yaw, 9);
            Assert.Equal(0d, state.YawRate, 9);
            Assert.False(state.Moving);
        }

        [Fact]
        public void AStoppedProbeCannotBeRestartedByItsOwnLeftovers()
        {
            SextantAimProbe.State state = SextantAimProbe.Armed(120d, 0d, 0.5);
            state = Run(state, 120d, 0d, 200, 1.0 / 60.0);
            Assert.False(state.Moving);

            // Further steps with the acceleration still applied must do nothing:
            // only PollAimProbe may re-arm, and only from a fresh preference read.
            state = Run(state, 120d, 0d, 200, 1.0 / 60.0);
            Assert.Equal(0d, state.Yaw, 9);
            Assert.Equal(0d, state.SecondsLeft, 9);
        }

        [Theory]
        [InlineData(1, 0, true)]
        [InlineData(2, 1, true)]
        [InlineData(1, 1, false)]
        [InlineData(5, 5, false)]
        [InlineData(0, 0, false)]
        [InlineData(0, 3, false)]
        [InlineData(-1, 0, false)]
        public void TheProbeFiresOnAFreshRunCounterOnly(int requested, int consumed, bool expected)
        {
            // The timer alone is not a safety property: an expired probe is armed
            // again by the next poll unless something else stops it. Requiring the
            // counter to CHANGE is what makes one bump mean one swing.
            Assert.Equal(expected, SextantAimProbe.IsFreshRun(requested, consumed));
        }

        [Fact]
        public void AnExpiredProbeIsNotRearmedByTheSameRun()
        {
            // The failure this pins: poll -> armed -> runs -> expires -> poll sees
            // an idle state with an armed preference -> arms again, forever.
            int consumed = 0;
            int polls = 0;
            int swings = 0;
            // 20 poll cycles of 1 Hz, each advancing the probe for its own 4 s.
            for (int cycle = 0; cycle < 20; cycle++)
            {
                polls++;
                if (SextantAimProbe.IsFreshRun(7, consumed))
                {
                    consumed = 7;
                    SextantAimProbe.State state = SextantAimProbe.Armed(90d, 0d, 4d);
                    state = Run(state, 90d, 0d, 300, 1.0 / 60.0);
                    Assert.False(state.Moving, "the probe should have expired inside its own window");
                    swings++;
                }
            }
            // Twenty polls of the same armed request must produce exactly ONE swing.
            Assert.Equal(20, polls);
            Assert.Equal(1, swings);
        }

        [Fact]
        public void TheSpinTurnsForFiveFullRevolutions()
        {
            // The author's request, as a test: continuous rotation, 5 x 360 deg.
            // This is the assertion the swing mode could never satisfy, because
            // its offset is clamped at 80 deg and returns to the player's aim.
            SextantAimProbe.State state = SextantAimProbe.ArmedSpin(60d);
            for (int i = 0; i < 600; i++)     // 10 s at 60 fps
                state = SextantAimProbe.AdvanceSpin(state, 180d, 1.0 / 60.0);
            // 180 deg/s for 10 s = 1800 deg = five turns.
            Assert.True(Math.Abs(state.SpinTotal - 1800d) < 1d,
                "expected ~1800 deg of continuous rotation, got " + state.SpinTotal);
        }

        [Fact]
        public void TheSpinKeepsTurningWhereTheSwingStops()
        {
            // The difference that matters: the swing clamps its offset and would
            // be pinned at 80 deg, the spin's view angle wraps instead, so the
            // total keeps growing and the motion never ends.
            SextantAimProbe.State swing = SextantAimProbe.Armed(4000d, 0d, 60d);
            for (int i = 0; i < 600; i++)
                swing = SextantAimProbe.Advance(swing, 4000d, 0d, 1.0 / 60.0);
            Assert.True(Math.Abs(swing.Yaw) <= SextantAimProbe.MaxAngleDegrees + 1e-9,
                "the swing is bounded by construction");

            SextantAimProbe.State spin = SextantAimProbe.ArmedSpin(60d);
            for (int i = 0; i < 600; i++)
                spin = SextantAimProbe.AdvanceSpin(spin, 180d, 1.0 / 60.0);
            Assert.True(spin.SpinTotal > 1000d, "the spin must keep accumulating");
            // The view angle itself stays in range, which is why the total is
            // reported separately.
            Assert.True(Math.Abs(spin.Yaw) <= 180.0001d);
        }

        [Fact]
        public void TheSpinStopsItselfAndIgnoresRubbish()
        {
            SextantAimProbe.State state = SextantAimProbe.ArmedSpin(1.0);
            for (int i = 0; i < 200; i++)
                state = SextantAimProbe.AdvanceSpin(state, 180d, 1.0 / 60.0);
            Assert.Equal(0d, state.SecondsLeft, 9);
            Assert.False(state.Moving);

            // NaN/negative time steps freeze rather than teleport; a NaN rate is
            // refused outright so the view cannot be driven to NaN.
            SextantAimProbe.State a = SextantAimProbe.ArmedSpin(30d);
            for (int i = 0; i < 30; i++)
                a = SextantAimProbe.AdvanceSpin(a, 180d, 1.0 / 60.0);
            SextantAimProbe.State before = a;
            Assert.Equal(before.Yaw, SextantAimProbe.AdvanceSpin(a, 180d, 0d).Yaw, 9);
            Assert.Equal(before.Yaw, SextantAimProbe.AdvanceSpin(a, 180d, double.NaN).Yaw, 9);
            Assert.Equal(before.Yaw, SextantAimProbe.AdvanceSpin(a, double.NaN, 1.0 / 60.0).Yaw, 9);
            Assert.Equal(before.SpinTotal,
                SextantAimProbe.AdvanceSpin(a, double.NaN, 1.0 / 60.0).SpinTotal, 9);

            Assert.Equal(0d, SextantAimProbe.SpinRateFrom(0), 9);
            Assert.Equal(45d, SextantAimProbe.SpinRateFrom(450), 9);
            Assert.Equal(-45d, SextantAimProbe.SpinRateFrom(-450), 9);
            Assert.Equal(SextantAimProbe.MaxRateDegPerSec,
                SextantAimProbe.SpinRateFrom(99999), 9);
        }

        [Fact]
        public void TheRunTimeIsCappedWhateverThePreferenceSays()
        {
            Assert.Equal(SextantAimProbe.MaxSeconds, SextantAimProbe.SecondsFrom(9999), 9);
            Assert.Equal(3d, SextantAimProbe.SecondsFrom(3), 9);
            Assert.Equal(SextantAimProbe.DefaultSeconds, 4d, 9);
        }

        [Theory]
        [InlineData(0d)]
        [InlineData(-1d)]
        [InlineData(double.NaN)]
        public void ABadTimeStepFreezesTheProbeInsteadOfTeleportingIt(double dt)
        {
            // A hitch (dt huge is handled by the clamps) or two frames inside a
            // millisecond must not teleport the view: the state is returned
            // unchanged so the motion stays continuous.
            SextantAimProbe.State state = SextantAimProbe.Armed(90d, 0d, 4d);
            state = Run(state, 90d, 0d, 10, 0.016);
            SextantAimProbe.State before = state;
            SextantAimProbe.State after = SextantAimProbe.Advance(state, 90d, 0d, dt);
            Assert.Equal(before.Yaw, after.Yaw, 9);
            Assert.Equal(before.YawRate, after.YawRate, 9);
        }

        [Fact]
        public void GarbageAccelerationsCannotPoisonTheState()
        {
            SextantAimProbe.State state = SextantAimProbe.Armed(90d, 0d, 4d);
            state = Run(state, double.NaN, double.PositiveInfinity, 30, 0.016);
            Assert.False(double.IsNaN(state.Yaw) || double.IsInfinity(state.Yaw), "yaw went non-finite");
            Assert.False(double.IsNaN(state.YawRate) || double.IsInfinity(state.YawRate), "rate went non-finite");
            // NaN acceleration is treated as "no acceleration", so the bleed
            // still applies and the probe coasts down rather than freezing.
            Assert.True(Math.Abs(state.YawRate) < 90d, "the probe should be coasting, got " + state.YawRate);
        }

        [Fact]
        public void TheTwoAxesAreIndependent()
        {
            SextantAimProbe.State state = SextantAimProbe.Armed(60d, -25d, 4d);
            state = Run(state, 60d, -25d, 20, 0.016);
            Assert.True(state.Yaw > 0d, "yaw must follow its own acceleration");
            Assert.True(state.Pitch < 0d, "a negative pitch acceleration must turn down");
            Assert.True(state.Pitch < 0d, "pitch must follow its own acceleration, got " + state.Pitch);
        }
    }
}
