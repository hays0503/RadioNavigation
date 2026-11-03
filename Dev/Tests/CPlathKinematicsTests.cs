using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class CPlathKinematicsTests
    {
        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(23.375, 11.6875, -135)]
        [InlineData(24, 12, 0)]
        [InlineData(130, 65, 0)]
        [InlineData(-1, 0, 0)]
        [InlineData(131, 65, 0)]
        public void ReadingUsesCalibratedAxesAndLimits(double degrees, double index, double drum)
        {
            Assert.Equal(index, CPlathKinematics.IndexDegrees(degrees), 8);
            Assert.Equal(drum, CPlathKinematics.DrumDegrees(degrees), 8);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void InvalidAnglesNeverReachTransform(double degrees)
        {
            Assert.Throws<ArgumentException>(() => CPlathKinematics.Clamp(degrees));
        }

        [Fact]
        public void MinuteCarryKeepsFullTurnEquivalent()
        {
            double before = 23 + 59.5 / 60;
            double after = before + 1.0 / 60;
            Assert.Equal((24 + .5 / 60) / 2, CPlathKinematics.IndexDegrees(after), 8);
            Assert.Equal(-3, CPlathKinematics.DrumDegrees(after), 8);
            Assert.Equal(-357, CPlathKinematics.DrumDegrees(before), 8);
        }

        [Fact]
        public void EveryShadeMovesIndependentlyAndCanBeReversed()
        {
            var state = new CPlathShadeState();
            for (int selected = 0; selected < 7; selected++)
            {
                state.Toggle(selected);
                for (int other = 0; other < 7; other++) Assert.Equal(other == selected, state.IsActive(other));
                Assert.Equal(selected < 4 ? 1 : 0, state.RightCount);
                Assert.Equal(selected >= 4 ? 1 : 0, state.LeftCount);
                state.Toggle(selected);
            }
            Assert.Equal(0, state.LeftCount + state.RightCount);
        }

        [Fact]
        public void SyncDoesNotReplaceNonContiguousCombinationWithSameCount()
        {
            var state = new CPlathShadeState(); state.Toggle(3); state.Toggle(6);
            state.SyncCounts(1, 1);
            Assert.True(state.IsActive(3)); Assert.True(state.IsActive(6));
            Assert.False(state.IsActive(0)); Assert.False(state.IsActive(4));
            state.SelectBank(1, false);
            Assert.True(state.IsActive(0)); Assert.False(state.IsActive(3)); Assert.True(state.IsActive(6));
        }

        [Fact]
        public void BothBanksHavePhysicalLimitsAndAllSevenCanBeActive()
        {
            var state = new CPlathShadeState(); state.SelectBank(99, false); state.SelectBank(99, true);
            Assert.Equal(4, state.RightCount); Assert.Equal(3, state.LeftCount);
            for (int i = 0; i < 7; i++) Assert.True(state.IsActive(i));
            state.SelectBank(-1, false);
            Assert.Equal(0, state.RightCount); Assert.Equal(3, state.LeftCount);
            Assert.Throws<ArgumentOutOfRangeException>(() => state.Toggle(7));
        }
    }
}
