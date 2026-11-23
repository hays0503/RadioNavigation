using System;
using UnityEngine;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class CurrentWaterDragTests
    {
        [Fact]
        public void CurrentWaterDrag_TransverseFlowAcceleratesStationaryBodyTowardsTheWaterVelocity()
        {
            var water = new ConstantCurrentField(2, 0).Sample(0, 0, 50, DateTime.UnixEpoch);
            Vector3 velocity = Vector3.zero;
            float positionX = 0;

            // Closed-form terminal velocity after 3 s: 2 - 2/(1 + 0.5*2*3) = 1.5 m/s.
            for (int i = 0; i < 12; ++i)
            {
                velocity = CurrentWaterDrag.Advance(velocity, water, 0f, 0.5f, 0.25f);
                positionX += 0.25f * velocity.x;
            }

            Assert.Equal(1.5f, velocity.x, 5);
            Assert.Equal(0f, velocity.y);
            Assert.Equal(0f, velocity.z);
            // Exact integral is 6 - 2*ln(4) = 3.2274 m; end-of-step integration
            // of position overestimates it by at most one step times 1.5 m/s.
            Assert.InRange(positionX, 3.2274f, 3.6024f);
        }

        [Fact]
        public void CurrentWaterDrag_DoesNotAccelerateABodyMovingWithItsWater()
        {
            var water = new CurrentVelocity(-0.75f, 0.5f);
            var velocity = new Vector3(-0.75f, 0f, 0.5f);

            var after = CurrentWaterDrag.Advance(velocity, water, 0.5f, 3f, 600f);

            Assert.Equal(velocity.x, after.x);
            Assert.Equal(velocity.y, after.y);
            Assert.Equal(velocity.z, after.z);
        }

        [Fact]
        public void CurrentWaterDrag_StillWaterDampsWithoutChangingDirection()
        {
            // With linear drag alone, one half-life halves each component.
            var after = CurrentWaterDrag.Advance(new Vector3(3f, -2f, -4f), default(CurrentVelocity),
                1f, 0f, (float)Math.Log(2));

            Assert.Equal(1.5f, after.x, 5);
            Assert.Equal(-1f, after.y, 5);
            Assert.Equal(-2f, after.z, 5);
        }

        [Fact]
        public void CurrentWaterDrag_CombinedDragUsesRelativeSpeedAndDoesNotReverseIt()
        {
            // Water moving south at 1 m/s; body moving north at 2 m/s.
            // In one linear half-life, relative speed goes from +3 to +0.5 m/s
            // for q = 4/3 m^-1, so the body moves south but slower than the water.
            var after = CurrentWaterDrag.Advance(new Vector3(0f, 0f, 2f), new CurrentVelocity(0f, -1f),
                1f, 4f / 3f, (float)Math.Log(2));

            Assert.Equal(0f, after.x);
            Assert.Equal(0f, after.y);
            Assert.Equal(-0.5f, after.z, 5);
        }
    }
}
