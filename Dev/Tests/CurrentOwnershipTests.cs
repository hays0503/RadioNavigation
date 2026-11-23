using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class CurrentOwnershipTests
    {
        [Fact]
        public void HandoffKeepsWorldVelocityAndOnlyOneOwnerCanAdvanceEachInterval()
        {
            var track = new CurrentOwnership(10, 7.0, 3.0);
            Assert.False(track.AdvanceStrategic(10, 11, 5.0, 2.0));
            Assert.True(track.Handoff(10, CurrentOwner.Physical, CurrentOwner.Strategic));
            Assert.Equal(3.0, track.WorldSpeed);
            Assert.False(track.PublishPhysical(10, 11, 9.0, 99.0));
            Assert.False(track.AdvanceStrategic(11, 12, 5.0, 2.0));
            Assert.True(track.AdvanceStrategic(10, 12, 5.0, 2.0));
            Assert.False(track.AdvanceStrategic(10, 12, 5.0, 2.0));
            Assert.Equal(14.47151776468577, track.Position, 10);
            Assert.Equal(4.264241117657115, track.WorldSpeed, 10);
            Assert.False(track.Handoff(11, CurrentOwner.Strategic, CurrentOwner.Physical));
            Assert.True(track.Handoff(12, CurrentOwner.Strategic, CurrentOwner.Physical));
            Assert.Equal(4.264241117657115, track.WorldSpeed, 10);
            Assert.True(track.PublishPhysical(12, 13, 19.0, 4.7));
            Assert.Equal(19.0, track.Position);
        }

        [Fact]
        public void SameElapsedIntervalIsIndependentOfChunking()
        {
            var whole = new CurrentOwnership(100, 0, 2);
            var split = new CurrentOwnership(100, 0, 2);
            Assert.True(whole.Handoff(100, CurrentOwner.Physical, CurrentOwner.Strategic));
            Assert.True(split.Handoff(100, CurrentOwner.Physical, CurrentOwner.Strategic));
            Assert.True(whole.AdvanceStrategic(100, 220, -1, 30));
            Assert.True(split.AdvanceStrategic(100, 107, -1, 30));
            Assert.True(split.AdvanceStrategic(107, 220, -1, 30));
            Assert.Equal(whole.WorldSpeed, split.WorldSpeed, 12);
            Assert.Equal(whole.Position, split.Position, 12);
        }
    }
}
