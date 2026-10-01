using UBOAT.Mods.RadioNavigation;
using Xunit;

namespace Tests
{
    /// <summary>
    /// The eye shadow's rate contract (author 2026-10-02). Every case here is a
    /// way the shipped version could have been silently wrong: a wrong sign
    /// puts the crescent on the wrong edge, an unwrapped difference spikes by
    /// 360 deg/s once per revolution, an unanchored first frame reads as a
    /// jump, and a missing discontinuity guard turns a camera transition into a
    /// full-strength shadow for one frame.
    /// </summary>
    public class SextantAimRateTests
    {
        [Fact]
        public void FirstFrameHasNoRate()
        {
            SextantAimRateSample ignored;
            SextantAimRate.State state = SextantAimRate.Advance(
                SextantAimRate.State.Fresh, 120d, -30d, 0.016, out ignored);
            Assert.True(state.Anchored);
            Assert.Equal(0d, ignored.Yaw, 9);
            Assert.Equal(0d, ignored.Pitch, 9);
        }

        [Fact]
        public void SteadyPoseGivesNoRateSoAStillViewHasNoShadow()
        {
            // The author's rule: the cursor is locked, the camera cannot turn,
            // therefore the ocular cannot turn, therefore no lune. A steady pose
            // must read exactly zero, not "small enough to ignore".
            SextantAimRate.State state = SextantAimRate.Reset();
            for (int i = 0; i < 50; i++)
            {
                SextantAimRateSample rate;
                state = SextantAimRate.Advance(state, 137.5d, -12.25d, 0.016, out rate);
                Assert.Equal(0d, rate.Yaw, 9);
                Assert.Equal(0d, rate.Pitch, 9);
            }
        }

        [Fact]
        public void YawAndPitchRatesHaveTheExpectedSign()
        {
            SextantAimRateSample rate;
            var state = SextantAimRate.Advance(SextantAimRate.State.Fresh, 0d, 0d, 0.016, out rate);

            // Yaw grows to the right in Unity's euler space.
            state = SextantAimRate.Advance(state, 2d, 0d, 0.1, out rate);
            Assert.Equal(20d, rate.Yaw, 6);
            Assert.Equal(0d, rate.Pitch, 6);

            // Pitch grows looking DOWN.
            state = SextantAimRate.Advance(state, 2d, 3d, 0.1, out rate);
            Assert.Equal(0d, rate.Yaw, 6);
            Assert.Equal(30d, rate.Pitch, 6);

            state = SextantAimRate.Advance(state, 2d, 1d, 0.1, out rate);
            Assert.Equal(-20d, rate.Pitch, 6);
        }

        [Fact]
        public void EulerWrap359To0IsNotAJump()
        {
            // Unity's eulerAngles wraps 360 -> 0. Unwrapped this reads +0.2 deg
            // per frame at 10 fps, i.e. a 20 deg/s phantom, and once per
            // revolution a 360 deg/s spike that saturates the shadow.
            SextantAimRateSample rate;
            var state = SextantAimRate.Advance(SextantAimRate.State.Fresh, 359.9d, 0d, 0.1, out rate);
            state = SextantAimRate.Advance(state, 0.1d, 0d, 0.1, out rate);
            Assert.Equal(2d, rate.Yaw, 6);
            Assert.True(rate.Magnitude < SextantAimRate.RejectRateDegPerSec);

            // The same on the vertical axis, which also comes back 0..360.
            state = SextantAimRate.Advance(SextantAimRate.State.Fresh, 0d, 359.5d, 0.1, out rate);
            state = SextantAimRate.Advance(state, 0d, 0.5d, 0.1, out rate);
            Assert.Equal(10d, rate.Pitch, 6);
        }

        [Fact]
        public void WrapBelowZeroIsHandled()
        {
            SextantAimRateSample rate;
            var state = SextantAimRate.Advance(SextantAimRate.State.Fresh, 0.2d, 0d, 0.1, out rate);
            state = SextantAimRate.Advance(state, 359.9d, 0d, 0.1, out rate);
            Assert.Equal(-3d, rate.Yaw, 6);
        }

        [Fact]
        public void CameraTeleportIsDroppedAndTheLatchSurvivesIt()
        {
            // FPP <-> periscope, a scene jump, or the R reset that zeroes the
            // aim trim: a real discontinuity of the aim, not an eye motion.
            // One frame with a full-strength shadow would be a visible flash.
            SextantAimRateSample rate;
            var state = SextantAimRate.Advance(SextantAimRate.State.Fresh, 10d, 0d, 0.016, out rate);
            state = SextantAimRate.Advance(state, 190d, 0d, 0.016, out rate);
            Assert.Equal(0d, rate.Yaw, 9);
            Assert.Equal(0d, rate.Pitch, 9);

            // ...and the NEXT frame is a normal, small rate again: one bad frame
            // must not poison the rest.
            state = SextantAimRate.Advance(state, 190.5d, 0d, 0.1, out rate);
            Assert.Equal(5d, rate.Yaw, 6);
        }

        [Fact]
        public void RapidButRealFlicksAreNotRejected()
        {
            // 200 deg/s is a hard whip; the guard must be loose enough to keep it
            // even at 10 fps, which is 20 deg per frame.
            SextantAimRateSample rate;
            var state = SextantAimRate.Advance(SextantAimRate.State.Fresh, 0d, 0d, 0.1, out rate);
            state = SextantAimRate.Advance(state, 20d, 0d, 0.1, out rate);
            Assert.Equal(200d, rate.Yaw, 6);

            // The threshold itself is a rate, not a per-frame step: at 60 fps a
            // 900 deg/s rejection would be a 15 deg step, which a real flick can
            // exceed. A 1000 deg/s hand must be dropped.
            state = SextantAimRate.Advance(state, 120d, 0d, 0.1, out rate);
            Assert.Equal(0d, rate.Yaw, 9);
        }

        [Theory]
        [InlineData(0d)]
        [InlineData(-1d)]
        [InlineData(1e-6)]
        [InlineData(0.3)]
        [InlineData(5d)]
        [InlineData(double.NaN)]
        public void BadDeltaSecondsYieldsNoRateAndKeepsTheAnchor(double dt)
        {
            SextantAimRateSample rate;
            var state = SextantAimRate.Advance(SextantAimRate.State.Fresh, 0d, 0d, 0.016, out rate);
            state = SextantAimRate.Advance(state, 30d, 0d, dt, out rate);
            Assert.Equal(0d, rate.Magnitude, 9);
            // Re-anchored at 30, so the next valid frame measures from there.
            state = SextantAimRate.Advance(state, 30.5d, 0d, 0.1, out rate);
            Assert.Equal(5d, rate.Yaw, 6);
        }

        [Fact]
        public void NaNPoseIsRefusedAndDoesNotAnchor()
        {
            SextantAimRateSample rate;
            var state = SextantAimRate.Advance(SextantAimRate.State.Fresh, double.NaN, 0d, 0.016, out rate);
            Assert.False(state.Anchored);
            Assert.Equal(0d, rate.Magnitude, 9);

            // A real pose afterwards still behaves as a first frame.
            state = SextantAimRate.Advance(state, 45d, 0d, 0.016, out rate);
            Assert.Equal(0d, rate.Magnitude, 9);
            state = SextantAimRate.Advance(state, 46d, 0d, 0.1, out rate);
            Assert.Equal(10d, rate.Yaw, 6);
        }

        [Fact]
        public void ResetForgetsTheAnchor()
        {
            // The open edge must not read as "the view jumped".
            SextantAimRateSample rate;
            var state = SextantAimRate.Advance(SextantAimRate.State.Fresh, 10d, 0d, 0.016, out rate);
            state = SextantAimRate.Reset();
            state = SextantAimRate.Advance(state, 300d, 0d, 0.016, out rate);
            Assert.Equal(0d, rate.Magnitude, 9);
        }
    }
}
