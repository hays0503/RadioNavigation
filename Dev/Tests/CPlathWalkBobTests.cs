using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class CPlathWalkBobTests
    {
        [Fact]
        public void IdleProducesZeroPoseAndDecaysSmoothlyOnStop()
        {
            var bob = new CPlathWalkBob();
            CPlathWalkBobPose stationary = bob.Step(0.02f, false, false, 1f);
            Assert.Equal(0f, stationary.OffsetX);
            Assert.Equal(0f, stationary.OffsetY);
            Assert.Equal(0f, stationary.RollDeg);

            CPlathWalkBobPose peak = new CPlathWalkBobPose();
            for (int i = 0; i < 30; i++)
                peak = bob.Step(0.02f, true, false, 1f);

            float walkingMag = Magnitude(peak);
            Assert.True(walkingMag > 0.002f, "Expected audible/visible gait offset while walking");

            CPlathWalkBobPose settled = peak;
            for (int i = 0; i < 120; i++)
                settled = bob.Step(0.02f, false, false, 1f);

            Assert.Equal(0f, settled.OffsetX);
            Assert.Equal(0f, settled.OffsetY);
            Assert.Equal(0f, settled.RollDeg);
        }

        [Fact]
        public void SprintIncreasesCadenceAndAmplitudeWithoutExceedingSafeBounds()
        {
            var walk = new CPlathWalkBob();
            var sprint = new CPlathWalkBob();
            float maxWalkX = 0f;
            float maxSprintX = 0f;
            float maxSprintRoll = 0f;

            for (int i = 0; i < 120; i++)
            {
                CPlathWalkBobPose w = walk.Step(0.016f, true, false, 1f);
                CPlathWalkBobPose r = sprint.Step(0.016f, true, true, 1f);
                maxWalkX = Math.Max(maxWalkX, Math.Abs(w.OffsetX));
                maxSprintX = Math.Max(maxSprintX, Math.Abs(r.OffsetX));
                maxSprintRoll = Math.Max(maxSprintRoll, Math.Abs(r.RollDeg));
            }

            Assert.True(maxSprintX > maxWalkX * 1.3f, "Sprint should amplify lateral sway");
            Assert.InRange(maxSprintX, 0.008f, 0.02f);
            Assert.InRange(maxSprintRoll, 1.0f, 3.5f);
        }

        [Fact]
        public void AttenuationScalesMotionDownForReadAndOptics()
        {
            var full = new CPlathWalkBob();
            var read = new CPlathWalkBob();
            var suppressed = new CPlathWalkBob();

            CPlathWalkBobPose pFull = new CPlathWalkBobPose();
            CPlathWalkBobPose pRead = new CPlathWalkBobPose();
            CPlathWalkBobPose pZero = new CPlathWalkBobPose();
            for (int i = 0; i < 40; i++)
            {
                pFull = full.Step(0.02f, true, false, 1f);
                pRead = read.Step(0.02f, true, false, 0.35f);
                pZero = suppressed.Step(0.02f, true, false, 0f);
            }

            Assert.Equal(0.35f * pFull.OffsetX, pRead.OffsetX, 5);
            Assert.Equal(0.35f * pFull.RollDeg, pRead.RollDeg, 4);
            Assert.Equal(0f, Magnitude(pZero));
        }

        [Fact]
        public void InvalidDtAndResetNeverProduceNaNOrLeftoverPhase()
        {
            var bob = new CPlathWalkBob();
            bob.Step(float.NaN, true, true, float.NaN);
            CPlathWalkBobPose afterSpike = bob.Step(10f, true, false, 1f);
            Assert.False(float.IsNaN(afterSpike.OffsetX));
            Assert.True(Math.Abs(afterSpike.OffsetX) <= 0.02f);

            bob.Reset();
            CPlathWalkBobPose afterReset = bob.Step(0f, false, false, 1f);
            Assert.Equal(0f, afterReset.OffsetX);
            Assert.Equal(0f, afterReset.PitchDeg);
        }

        private static float Magnitude(CPlathWalkBobPose p)
        {
            return Math.Abs(p.OffsetX) + Math.Abs(p.OffsetY) + Math.Abs(p.OffsetZ)
                + Math.Abs(p.RollDeg);
        }
    }
}
