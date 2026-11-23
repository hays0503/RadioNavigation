using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class CurrentTrack2DTests
    {
        private static long T(int seconds) => seconds * TimeSpan.TicksPerSecond;

        private static CurrentTrack2D Strategic(double east, double north, double velocityEast)
        {
            var track = new CurrentTrack2D(0, east, north, velocityEast, 0);
            Assert.True(track.Handoff(0, CurrentOwner.Physical, CurrentOwner.Strategic));
            return track;
        }

        [Fact]
        public void HandoffKeepsWorldVelocityAndRejectsWrongEpochOrOwner()
        {
            var track = new CurrentTrack2D(10, 5, -3, 2, 1);

            Assert.False(track.Handoff(11, CurrentOwner.Physical, CurrentOwner.Strategic));
            Assert.False(track.Handoff(10, CurrentOwner.Strategic, CurrentOwner.Physical));
            Assert.True(track.Handoff(10, CurrentOwner.Physical, CurrentOwner.Strategic));
            Assert.False(track.Handoff(10, CurrentOwner.Physical, CurrentOwner.Strategic));
            Assert.Equal(2, track.VelocityEastMetersPerSecond);
            Assert.Equal(1, track.VelocityNorthMetersPerSecond);
            Assert.Equal(CurrentOwner.Strategic, track.Owner);
        }

        [Fact]
        public void ExactRelaxationMatchesTheClosedForm()
        {
            var track = Strategic(0, 0, 2);

            Assert.True(track.AdvanceStrategic(0, T(100), 0, 0, 100));

            Assert.Equal(2 * Math.Exp(-1), track.VelocityEastMetersPerSecond, 10);
            Assert.Equal(200 * (1 - Math.Exp(-1)), track.EastMeters, 8);
        }

        [Fact]
        public void ChunkingAndFrameBudgetDoNotChangeTheResult()
        {
            var whole = Strategic(0, 0, 2);
            var split = Strategic(0, 0, 2);

            Assert.True(whole.AdvanceStrategic(0, T(220), 0.4, -0.2, 100));
            Assert.True(split.AdvanceStrategic(0, T(107), 0.4, -0.2, 100));
            Assert.True(split.AdvanceStrategic(T(107), T(150), 0.4, -0.2, 100));
            Assert.True(split.AdvanceStrategic(T(150), T(220), 0.4, -0.2, 100));

            Assert.Equal(whole.EastMeters, split.EastMeters, 9);
            Assert.Equal(whole.NorthMeters, split.NorthMeters, 9);
            Assert.Equal(whole.VelocityEastMetersPerSecond, split.VelocityEastMetersPerSecond, 12);
            Assert.Equal(whole.VelocityNorthMetersPerSecond, split.VelocityNorthMetersPerSecond, 12);
        }

        [Fact]
        public void OneTickAtCampaignEpochIsNotRoundedAwayOrTreatedAsASecond()
        {
            long epoch = new DateTime(1941, 11, 5).Ticks;
            var track = new CurrentTrack2D(epoch, 0, 0, 2, -1);
            Assert.True(track.Handoff(epoch, CurrentOwner.Physical, CurrentOwner.Strategic));
            Assert.True(track.AdvanceStrategic(epoch, epoch + 1, 2, -1, 100));
            Assert.Equal(2e-7, track.EastMeters, 13);
            Assert.Equal(-1e-7, track.NorthMeters, 13);
        }

        [Fact]
        public void AnEventAlreadyDueAtTheStartBlocksBeforeAnyMotion()
        {
            var track = Strategic(0, 0, 2);
            Assert.False(track.AcknowledgeEvent(-1));
            Assert.True(track.AdvanceUntilEvent(0, T(1), 0, 0, 100, 0, out long stopped));
            Assert.Equal(0, stopped);
            Assert.Equal(0, track.Epoch);
            Assert.Equal(0, track.EastMeters);
            Assert.False(track.AdvanceStrategic(0, T(1), 0, 0, 100));
            Assert.True(track.AcknowledgeEvent(0));
            Assert.True(track.AdvanceStrategic(0, T(1), 0, 0, 100));
        }

        [Fact]
        public void AnOverdueEventRejectsTheInterval()
        {
            var track = Strategic(0, 0, 2);
            Assert.True(track.AdvanceStrategic(0, T(1), 0, 0, 100));
            Assert.False(track.AdvanceUntilEvent(T(1), T(2), 0, 0, 100, 0, out _));
            Assert.Equal(T(1), track.Epoch);
        }

        [Fact]
        public void EventBarrierStopsMotionAndRefusesFurtherProgress()
        {
            var whole = Strategic(0, 0, 2);
            var barrier = Strategic(0, 0, 2);
            Assert.True(whole.AdvanceStrategic(0, 10, 0, 0, 100));

            long stopped;
            Assert.True(barrier.AdvanceUntilEvent(0, 10, 0, 0, 100, 3, out stopped));
            Assert.Equal(3, stopped);
            Assert.Equal(3, barrier.PendingEventEpoch);
            Assert.False(barrier.AdvanceStrategic(3, 10, 0, 0, 100));
            Assert.False(barrier.AcknowledgeEvent(4));
            Assert.True(barrier.AcknowledgeEvent(3));
            Assert.True(barrier.AdvanceStrategic(3, 10, 0, 0, 100));

            Assert.Equal(whole.EastMeters, barrier.EastMeters, 10);
            Assert.Equal(whole.VelocityEastMetersPerSecond, barrier.VelocityEastMetersPerSecond, 12);
            Assert.Equal(-1, barrier.PendingEventEpoch);
        }

        [Fact]
        public void EventOutsideTheIntervalDoesNotCreateABarrier()
        {
            var track = Strategic(0, 0, 2);
            long stopped;

            Assert.True(track.AdvanceUntilEvent(0, 10, 0, 0, 100, 11, out stopped));
            Assert.Equal(-1, stopped);
            Assert.Equal(-1, track.PendingEventEpoch);
            Assert.Equal(10, track.Epoch);
        }

        [Fact]
        public void EventAtTheIntervalEndIsHandledBeforeCrossingIt()
        {
            var track = Strategic(0, 0, 2);
            long stopped;

            Assert.True(track.AdvanceUntilEvent(0, 10, 0, 0, 100, 10, out stopped));
            Assert.Equal(10, stopped);
            Assert.Equal(10, track.PendingEventEpoch);
            Assert.Equal(10, track.Epoch);
            Assert.False(track.AdvanceStrategic(10, 20, 0, 0, 100));
        }

        [Fact]
        public void EquilibriumChangeAfterAnEventIsIntegratedOnce()
        {
            var track = Strategic(0, 0, 2);
            long stopped;
            Assert.True(track.AdvanceUntilEvent(0, T(10), 0, 0, 100, T(5), out stopped));
            Assert.True(track.AcknowledgeEvent(stopped));
            Assert.True(track.AdvanceStrategic(T(5), T(10), 1, 0, 100));

            // Independent fine-step integration of dv/dt = (eq - v)/tau, dx/dt = v.
            double v = 2, x = 0;
            const double dt = 0.0005;
            for (int i = 0; i < 20000; i++)
            {
                double time = i * dt;
                double equilibrium = time < 5 ? 0 : 1;
                x += v * dt;
                v += (equilibrium - v) / 100 * dt;
            }
            x += v * dt;

            Assert.InRange(Math.Abs(track.EastMeters - x), 0, 0.02);
            Assert.InRange(track.VelocityEastMetersPerSecond, 1.80, 1.95);
        }

        [Fact]
        public void PayloadRoundTripPreservesEpochOwnerPositionAndVelocity()
        {
            var track = Strategic(12.5, -3.25, 1.5);
            Assert.True(track.AdvanceStrategic(0, 42, 0.5, 0.25, 60));

            CurrentTrack2D restored;
            Assert.True(CurrentTrack2D.TryRestore(track.ToPayload(), out restored));

            Assert.Equal(track.Epoch, restored.Epoch);
            Assert.Equal(track.Owner, restored.Owner);
            Assert.Equal(track.EastMeters, restored.EastMeters, 12);
            Assert.Equal(track.NorthMeters, restored.NorthMeters, 12);
            Assert.Equal(track.VelocityEastMetersPerSecond, restored.VelocityEastMetersPerSecond, 12);
            Assert.Equal(track.VelocityNorthMetersPerSecond, restored.VelocityNorthMetersPerSecond, 12);
        }

        [Fact]
        public void PendingEventBarrierSurvivesThePayloadRoundTrip()
        {
            var track = Strategic(0, 0, 2);
            long stopped;
            Assert.True(track.AdvanceUntilEvent(0, 10, 0, 0, 100, 5, out stopped));

            CurrentTrack2D restored;
            Assert.True(CurrentTrack2D.TryRestore(track.ToPayload(), out restored));

            Assert.Equal(5, restored.PendingEventEpoch);
            Assert.False(restored.AdvanceStrategic(5, 10, 0, 0, 100));
            Assert.True(restored.AcknowledgeEvent(5));
            Assert.True(restored.AdvanceStrategic(5, 10, 0, 0, 100));
        }

        [Fact]
        public void RestoreRejectsAPendingEventThatIsNotTheCurrentEpoch()
        {
            var payload = Strategic(0, 0, 1).ToPayload();
            payload.PendingEventEpoch = 3;
            CurrentTrack2D restored;

            Assert.False(CurrentTrack2D.TryRestore(payload, out restored));
        }

        [Fact]
        public void HandoffRejectsAnUndefinedOwner()
        {
            var track = new CurrentTrack2D(0, 0, 0, 1, 0);

            Assert.False(track.Handoff(0, CurrentOwner.Physical, (CurrentOwner)7));
            Assert.Equal(CurrentOwner.Physical, track.Owner);
        }

        [Fact]
        public void RestoreRejectsBadVersionOwnerAndNonFiniteState()
        {
            var payload = Strategic(0, 0, 1).ToPayload();
            CurrentTrack2D restored;

            payload.Version = CurrentTrack2D.PayloadVersion + 1;
            Assert.False(CurrentTrack2D.TryRestore(payload, out restored));

            payload.Version = CurrentTrack2D.PayloadVersion;
            payload.Owner = 7;
            Assert.False(CurrentTrack2D.TryRestore(payload, out restored));

            payload.Owner = (int)CurrentOwner.Strategic;
            payload.VelocityNorthMetersPerSecond = double.NaN;
            Assert.False(CurrentTrack2D.TryRestore(payload, out restored));
        }

        [Fact]
        public void NonFiniteInputsAndPhysicalPublishAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new CurrentTrack2D(0, double.NaN, 0, 0, 0));
            var track = Strategic(0, 0, 1);
            Assert.False(track.AdvanceStrategic(0, 10, double.PositiveInfinity, 0, 100));
            Assert.False(track.PublishPhysical(0, 10, 1, 1, 1, 1));
        }
    }
}
