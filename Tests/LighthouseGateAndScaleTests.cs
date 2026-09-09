using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class LighthouseDistanceGateTests
    {
        [Theory]
        [InlineData(30.0, LighthouseGateAction.Spawn)]   // exactly on the rim
        [InlineData(10.0, LighthouseGateAction.Spawn)]
        [InlineData(0.0, LighthouseGateAction.Spawn)]
        [InlineData(30.1, LighthouseGateAction.Keep)]    // hysteresis band
        [InlineData(33.0, LighthouseGateAction.Keep)]
        public void Decide_NotSpawned(double distanceKm, LighthouseGateAction expected)
        {
            Assert.Equal(expected, LighthouseDistanceGate.Decide(false, distanceKm));
        }

        [Theory]
        [InlineData(10.0, LighthouseGateAction.Keep)]
        [InlineData(30.0, LighthouseGateAction.Keep)]
        [InlineData(33.0, LighthouseGateAction.Keep)]    // despawn strictly beyond
        [InlineData(33.1, LighthouseGateAction.Despawn)]
        [InlineData(100.0, LighthouseGateAction.Despawn)]
        public void Decide_Spawned(double distanceKm, LighthouseGateAction expected)
        {
            Assert.Equal(expected, LighthouseDistanceGate.Decide(true, distanceKm));
        }

        [Fact]
        public void Decide_NaN_SpawnedDespawns()
        {
            Assert.Equal(LighthouseGateAction.Despawn,
                LighthouseDistanceGate.Decide(true, double.NaN));
        }

        [Fact]
        public void Decide_NaN_NotSpawnedKeeps()
        {
            Assert.Equal(LighthouseGateAction.Keep,
                LighthouseDistanceGate.Decide(false, double.NaN));
        }

        [Fact]
        public void DistanceKm_Pythagoras()
        {
            Assert.Equal(5.0, LighthouseDistanceGate.DistanceKm(0, 0, 3, 4), 6);
            Assert.Equal(0.0, LighthouseDistanceGate.DistanceKm(7, 7, 7, 7), 6);
        }

        [Fact]
        public void Hysteresis_NoThrash_AtBoundaryCrossing()
        {
            // Sail out: spawn at 30, keep until past 33.
            Assert.Equal(LighthouseGateAction.Spawn, LighthouseDistanceGate.Decide(false, 29.9));
            Assert.Equal(LighthouseGateAction.Keep, LighthouseDistanceGate.Decide(true, 31.0));
            Assert.Equal(LighthouseGateAction.Keep, LighthouseDistanceGate.Decide(true, 32.9));
            Assert.Equal(LighthouseGateAction.Despawn, LighthouseDistanceGate.Decide(true, 33.1));
            // Sail back: no re-spawn until inside 30 again.
            Assert.Equal(LighthouseGateAction.Keep, LighthouseDistanceGate.Decide(false, 31.0));
            Assert.Equal(LighthouseGateAction.Spawn, LighthouseDistanceGate.Decide(false, 29.9));
        }
    }

    public class LighthouseScaleTests
    {
        [Fact]
        public void Compute_Wangerooge63m()
        {
            Assert.Equal(0.550f, LighthouseScale.Compute(63f, LighthouseScale.DefaultLampHeightM), 3);
        }

        [Fact]
        public void Compute_Pier8m()
        {
            // 8 m pier light would be 0.070 — floored to keep the tower a
            // visible seamark (sub-pixel sliver otherwise).
            Assert.Equal(0.35f, LighthouseScale.Compute(8f, LighthouseScale.DefaultLampHeightM), 3);
        }

        [Fact]
        public void Compute_ClampsLow()
        {
            // 5 m pier light would be 0.044 — clamped to the floor.
            Assert.Equal(LighthouseScale.MinScale,
                LighthouseScale.Compute(5f, LighthouseScale.DefaultLampHeightM));
        }

        [Fact]
        public void Compute_ClampsHigh()
        {
            Assert.Equal(LighthouseScale.MaxScale, LighthouseScale.Compute(500f, 10f));
        }

        [Fact]
        public void Compute_BadInputs_ReturnOne()
        {
            Assert.Equal(1f, LighthouseScale.Compute(0f, 114.47f));
            Assert.Equal(1f, LighthouseScale.Compute(float.NaN, 114.47f));
            Assert.Equal(1f, LighthouseScale.Compute(63f, 0f));
            Assert.Equal(1f, LighthouseScale.Compute(63f, float.NaN));
            Assert.Equal(1f, LighthouseScale.Compute(-5f, 114.47f));
        }

        [Fact]
        public void Compute_ExactReference_IsOne()
        {
            Assert.Equal(1f, LighthouseScale.Compute(114.47f, 114.47f), 4);
        }
    }
}
