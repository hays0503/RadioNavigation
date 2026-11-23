using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class CurrentRouteSteeringTests
    {
        [Theory]
        [InlineData(3f, -0.3f)]
        [InlineData(-3f, 0.3f)]
        [InlineData(0f, 0f)]
        public void NorthboundHeadingCancelsTheCrossCurrent(float eastFlow, float headingEast)
        {
            Assert.True(CurrentRouteSteering.TrySolve(0, 1000, 10, eastFlow, 0, out var solution));
            Assert.Equal(headingEast, solution.HeadingEast, 6);
            Assert.Equal((float)Math.Sqrt(100 - eastFlow * eastFlow), solution.GroundMetersPerSecond, 5);
            Assert.Equal(0f, 10 * solution.HeadingEast + eastFlow, 5);
        }

        [Theory]
        [InlineData(10, 10, 0)]
        [InlineData(10, 11, 0)]
        [InlineData(10, 0, -10)]
        [InlineData(0, 1, 0)]
        public void RejectsAHeadingThatCannotMakeForwardProgress(int throughWater, int eastFlow, int northFlow)
        {
            Assert.False(CurrentRouteSteering.TrySolve(0, 1, throughWater, eastFlow, northFlow, out _));
        }

        [Fact]
        public void BeamFlowEqualToSpeedLeavesNoGroundProgress()
        {
            Assert.False(CurrentRouteSteering.TrySolve(0, 1, 5, 5, 0, out _));
            Assert.False(CurrentRouteSteering.TrySolve(0, 1, 5, 0, -5, out _));
        }

        [Fact]
        public void NonFiniteTrackIsRejected()
        {
            Assert.False(CurrentRouteSteering.TrySolve(double.PositiveInfinity, 1, 5, 0, 0, out _));
            Assert.False(CurrentRouteSteering.TrySolve(double.NaN, 1, 5, 0, 0, out _));
        }

        [Fact]
        public void DiagonalTrackUsesBothVelocityComponents()
        {
            Assert.False(CurrentRouteSteering.TrySolve(3, 4, 5, -4, 3, out var solution));
            Assert.True(CurrentRouteSteering.TrySolve(3, 4, 6, -4, 3, out solution));
            var east = solution.HeadingEast * 6 - 4;
            var north = solution.HeadingNorth * 6 + 3;
            Assert.Equal(east * 4, north * 3, 4);
            Assert.Equal((float)Math.Sqrt(11), solution.GroundMetersPerSecond, 5);
        }
    }
}
