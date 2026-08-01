using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class StationSchedulerTests
    {
        [Fact]
        public void Constructor_StartsInPause()
        {
            var s = new StationScheduler("TEST");
            Assert.Equal(StationScheduler.Phase.Pause, s.CurrentPhase);
            Assert.Null(s.GetCurrentSequence());
            Assert.Null(s.GetCurrentConsolPattern());
        }

        [Fact]
        public void Tick_FromPause_EntersCallsign()
        {
            var s = new StationScheduler("TEST");
            s.Tick();
            Assert.Equal(StationScheduler.Phase.Callsign, s.CurrentPhase);
            Assert.NotNull(s.GetCurrentSequence());
        }

        [Fact]
        public void OnSequenceFinished_Callsign_EntersPreNavPause()
        {
            var s = new StationScheduler("TEST");
            s.Tick();
            s.OnSequenceFinished();
            Assert.Equal(StationScheduler.Phase.PreNavPause, s.CurrentPhase);
            Assert.Null(s.GetCurrentSequence());
        }

        [Fact]
        public void PreNavPause_Expires_EntersNavigation()
        {
            var s = new StationScheduler("TEST");
            s.Tick();
            s.OnSequenceFinished();
            for (int i = 0; i < 60; i++) s.Tick();
            Assert.Equal(StationScheduler.Phase.Navigation, s.CurrentPhase);
            Assert.NotNull(s.GetCurrentSequence());
        }

        [Fact]
        public void OnSequenceFinished_Navigation_EntersPostNavSilence()
        {
            var s = new StationScheduler("TEST");
            s.Tick();
            s.OnSequenceFinished();
            for (int i = 0; i < 60; i++) s.Tick();
            s.OnSequenceFinished();
            Assert.Equal(StationScheduler.Phase.PostNavSilence, s.CurrentPhase);
            Assert.Null(s.GetCurrentSequence());
        }

        [Fact]
        public void PostNavSilence_Expires_BackToPause()
        {
            var s = new StationScheduler("TEST");
            s.Tick();
            s.OnSequenceFinished();
            for (int i = 0; i < 60; i++) s.Tick();
            s.OnSequenceFinished();
            s.Tick();
            Assert.Equal(StationScheduler.Phase.Pause, s.CurrentPhase);
        }

        // F122 (pending): Reset() currently sets PostNavSilence, not Pause. Baseline pins current behavior.
        [Fact]
        public void Reset_SetsPostNavSilence()
        {
            var s = new StationScheduler("TEST");
            s.Reset();
            Assert.Equal(StationScheduler.Phase.PostNavSilence, s.CurrentPhase);
        }

        [Fact]
        public void GetCurrentConsolPattern_ActivePhases_ReturnsPattern()
        {
            var s = new StationScheduler("TEST");
            var pattern = new[] { NavigationElementType.Dot };
            s.CurrentConsolPattern = pattern;
            s.Tick();
            Assert.Same(pattern, s.GetCurrentConsolPattern());
        }

        [Fact]
        public void GetCurrentConsolPattern_InactivePhase_ReturnsNull()
        {
            var s = new StationScheduler("TEST");
            s.CurrentConsolPattern = new[] { NavigationElementType.Dot };
            Assert.Null(s.GetCurrentConsolPattern());
        }

        [Fact]
        public void Callsign_Exposed()
        {
            var s = new StationScheduler("TEST");
            Assert.Equal(16, s.Callsign.ElementCount);
        }
    }
}
