using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// DecodedDisplayTracker contract: the decoded-callsign display text +
    /// log-once latch behind InformationPanelHandler.UpdateSignalDisplay.
    /// Extracted so text selection and latch transitions run offline.
    /// </summary>
    public sealed class DecodedDisplayTrackerTests
    {
        [Fact]
        public void DisplayText_Empty_ShowsPlaceholder()
        {
            Assert.Equal("Decoded: ", DecodedDisplayTracker.DisplayText(null));
            Assert.Equal("Decoded: ", DecodedDisplayTracker.DisplayText(string.Empty));
        }

        [Fact]
        public void DisplayText_Decoded_ShowsValue()
        {
            Assert.Equal("Decoded: TT", DecodedDisplayTracker.DisplayText("TT"));
        }

        [Fact]
        public void ShouldLog_FirstSight_TrueThenFalse()
        {
            var tracker = new DecodedDisplayTracker();

            Assert.True(tracker.ShouldLog("TT"));
            tracker.MarkLogged("TT");
            Assert.False(tracker.ShouldLog("TT"));
        }

        [Fact]
        public void ShouldLog_NewValue_TrueAgain()
        {
            var tracker = new DecodedDisplayTracker();
            tracker.MarkLogged("TT");

            Assert.True(tracker.ShouldLog("MW"));
        }

        [Fact]
        public void ShouldLog_Empty_Never()
        {
            var tracker = new DecodedDisplayTracker();

            Assert.False(tracker.ShouldLog(null));
            Assert.False(tracker.ShouldLog(string.Empty));
        }

        [Fact]
        public void Reset_RearmsLatch()
        {
            var tracker = new DecodedDisplayTracker();
            tracker.MarkLogged("TT");

            tracker.Reset();

            Assert.True(tracker.ShouldLog("TT"));
        }
    }
}
