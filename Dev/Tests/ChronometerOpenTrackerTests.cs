using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class ChronometerOpenTrackerTests
    {
        [Fact]
        public void FirstFrame_F7WithButton_TogglesClosedToOpen()
        {
            ChronometerOpenTracker.Frame frame = ChronometerOpenTracker.Track(
                false, false, false, true, true);

            Assert.True(frame.EffectiveOpen);
            Assert.True(frame.ToggledByFallback);
        }

        [Fact]
        public void FirstFrame_F7WithButton_TogglesOpenToClosed()
        {
            ChronometerOpenTracker.Frame frame = ChronometerOpenTracker.Track(
                false, false, true, true, true);

            Assert.False(frame.EffectiveOpen);
            Assert.True(frame.ToggledByFallback);
        }

        [Fact]
        public void NativeChangedFrame_F7Suppressed()
        {
            ChronometerOpenTracker.Frame frame = ChronometerOpenTracker.Track(
                true, false, true, true, true);

            Assert.True(frame.EffectiveOpen);
            Assert.False(frame.ToggledByFallback);
        }

        [Fact]
        public void NoButton_NoToggle()
        {
            ChronometerOpenTracker.Frame frame = ChronometerOpenTracker.Track(
                true, true, true, true, false);

            Assert.True(frame.EffectiveOpen);
            Assert.False(frame.ToggledByFallback);
        }

        [Fact]
        public void NoF7_Passthrough()
        {
            ChronometerOpenTracker.Frame open = ChronometerOpenTracker.Track(
                true, true, true, false, true);
            ChronometerOpenTracker.Frame closed = ChronometerOpenTracker.Track(
                true, false, false, false, true);

            Assert.True(open.EffectiveOpen);
            Assert.False(open.ToggledByFallback);
            Assert.False(closed.EffectiveOpen);
            Assert.False(closed.ToggledByFallback);
        }

        [Fact]
        public void SameStateNoF7_NoToggle()
        {
            ChronometerOpenTracker.Frame frame = ChronometerOpenTracker.Track(
                true, true, true, false, true);

            Assert.False(frame.ToggledByFallback);
        }
    }
}
