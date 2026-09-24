using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>P5 hold continuity: one body, grace, decay, rising edge.</summary>
    public sealed class HoldTrackerTests
    {
        private const float Tick = HoldTracker.TickSeconds;

        private static int TicksFor(float seconds)
        {
            return (int)(seconds / Tick + 0.5f);
        }

        [Fact]
        public void SameKey_ConfirmsAfterHoldSeconds()
        {
            var tracker = new HoldTracker();
            bool confirmed = false;
            for (int i = 0; i < TicksFor(HoldTracker.HoldSeconds); i++)
                confirmed |= tracker.Tick("star:A", Tick);

            Assert.True(confirmed);
            Assert.True(tracker.Confirmed);
            Assert.Equal(1f, tracker.Progress);
        }

        [Fact]
        public void Confirmed_IsARisingEdgeOnly()
        {
            var tracker = new HoldTracker();
            int hits = 0;
            for (int i = 0; i < TicksFor(HoldTracker.HoldSeconds) + 5; i++)
            {
                if (tracker.Tick("star:A", Tick))
                    hits++;
            }
            Assert.Equal(1, hits);
        }

        [Fact]
        public void SingleMissedTick_DecaysInsteadOfSnapping()
        {
            var tracker = new HoldTracker();
            for (int i = 0; i < TicksFor(1.0f); i++)
                tracker.Tick("star:A", Tick);
            float progressBefore = tracker.Progress;

            tracker.Tick("", Tick); // one jitter frame

            Assert.Equal("star:A", tracker.Key);
            Assert.True(tracker.Progress < progressBefore);
            Assert.True(tracker.Progress > 0f);
        }

        [Fact]
        public void LongMiss_ClearsTheKey()
        {
            var tracker = new HoldTracker();
            for (int i = 0; i < TicksFor(1.0f); i++)
                tracker.Tick("star:A", Tick);

            for (int i = 0; i < TicksFor(0.6f); i++)
                tracker.Tick("", Tick);

            Assert.Equal("", tracker.Key);
            Assert.Equal(0f, tracker.Progress);
        }

        [Fact]
        public void JitterUnderGrace_KeepsTheOriginalKey()
        {
            var tracker = new HoldTracker();
            for (int i = 0; i < TicksFor(1.0f); i++)
                tracker.Tick("star:A", Tick);
            float held = tracker.Progress;

            // A different body for less than the grace window: no switch.
            tracker.Tick("star:B", Tick);
            tracker.Tick("star:B", Tick);
            Assert.Equal("star:A", tracker.Key);
            Assert.Equal(held, tracker.Progress);

            // Back on A: the hold resumes rather than restarting.
            tracker.Tick("star:A", Tick);
            Assert.True(tracker.Progress > held);
        }

        [Fact]
        public void DifferentKeyBeyondGrace_SwitchesAndRestarts()
        {
            var tracker = new HoldTracker();
            for (int i = 0; i < TicksFor(1.0f); i++)
                tracker.Tick("star:A", Tick);

            for (int i = 0; i < TicksFor(HoldTracker.GraceSeconds) + 1; i++)
                tracker.Tick("star:B", Tick);

            Assert.Equal("star:B", tracker.Key);
            Assert.True(tracker.Progress < 1f);
            Assert.False(tracker.Confirmed);
        }

        [Fact]
        public void Reset_ClearsEverything()
        {
            var tracker = new HoldTracker();
            for (int i = 0; i < TicksFor(HoldTracker.HoldSeconds); i++)
                tracker.Tick("star:A", Tick);
            tracker.Reset();

            Assert.Equal("", tracker.Key);
            Assert.Equal(0f, tracker.Progress);
            Assert.False(tracker.Confirmed);
        }

        [Fact]
        public void NonPositiveDt_IsIgnored()
        {
            var tracker = new HoldTracker();
            Assert.False(tracker.Tick("star:A", 0f));
            Assert.Equal("", tracker.Key);
        }
    }
}
