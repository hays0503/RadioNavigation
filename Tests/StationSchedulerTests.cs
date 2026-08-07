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
        public void Constructor_EmptyCallsign_SkipsCallsignPhase()
        {
            // F87: an empty callsign sequence never finishes (MorseKeyer
            // stays idle), so the scheduler must skip the Callsign phase
            // or the cycle would stall forever.
            var s = new StationScheduler("");
            Assert.Equal(StationScheduler.Phase.PreNavPause, s.CurrentPhase);
            Assert.Null(s.GetCurrentSequence());
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

        [Fact]
        public void PostNavSilence_WithConfiguredCycle_LastsThreeSeconds()
        {
            var s = new StationScheduler("TEST")
            {
                Config = ConsolCycleConfig.Cycle120s
            };
            s.Tick();
            s.OnSequenceFinished();
            for (int i = 0; i < 60; i++) s.Tick();
            Assert.Equal(StationScheduler.Phase.Navigation, s.CurrentPhase);

            s.OnSequenceFinished();
            for (int i = 0; i < 59; i++) s.Tick();
            Assert.Equal(StationScheduler.Phase.PostNavSilence, s.CurrentPhase);
            s.Tick();
            Assert.Equal(StationScheduler.Phase.Pause, s.CurrentPhase);
        }

        // F122: Reset() must start the cycle over at Pause (one-tick), not PostNavSilence.
        [Fact]
        public void Reset_SetsPausePhase()
        {
            var s = new StationScheduler("TEST");
            s.Reset();
            Assert.Equal(StationScheduler.Phase.Pause, s.CurrentPhase);
        }

        [Fact]
        public void Reset_ThenTick_EntersCallsign()
        {
            var s = new StationScheduler("TEST");
            s.Reset();
            s.Tick();
            Assert.Equal(StationScheduler.Phase.Callsign, s.CurrentPhase);
            Assert.NotNull(s.GetCurrentSequence());
        }

        // F201: the full consol pattern must only be exposed during the
        // Navigation phase — in Callsign it would transmit the pattern twice
        // per cycle and silence the callsign-only phase.
        [Fact]
        public void GetCurrentConsolPattern_Navigation_ReturnsPattern()
        {
            var s = new StationScheduler("TEST");
            var pattern = new[] { NavigationElementType.Dot };
            s.CurrentConsolPattern = pattern;
            s.Tick();
            s.OnSequenceFinished();
            for (int i = 0; i < 60; i++) s.Tick();
            Assert.Equal(StationScheduler.Phase.Navigation, s.CurrentPhase);
            Assert.Same(pattern, s.GetCurrentConsolPattern());
        }

        [Fact]
        public void GetCurrentConsolPattern_Navigation_PreservesHistoricalDashOrder()
        {
            var s = new StationScheduler("TEST");
            var pattern = SonneBeam.BuildPattern(38, true);
            s.CurrentConsolPattern = pattern;
            s.Tick();
            s.OnSequenceFinished();
            for (int i = 0; i < 60; i++) s.Tick();

            Assert.Equal(StationScheduler.Phase.Navigation, s.CurrentPhase);
            Assert.Same(pattern, s.GetCurrentConsolPattern());
            Assert.Equal(NavigationElementType.Dash, pattern[0]);
            Assert.Equal(NavigationElementType.Equisignal, pattern[20]);
            Assert.Equal(NavigationElementType.Equisignal, pattern[23]);
            Assert.Equal(NavigationElementType.Dot, pattern[24]);
        }

        [Fact]
        public void GetCurrentConsolPattern_Callsign_ReturnsNull()
        {
            var s = new StationScheduler("TEST");
            s.CurrentConsolPattern = new[] { NavigationElementType.Dot };
            s.Tick();
            Assert.Equal(StationScheduler.Phase.Callsign, s.CurrentPhase);
            Assert.Null(s.GetCurrentConsolPattern());
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

        // F135: unexpected phase (Pause) must be ignored, not crash.
        [Fact]
        public void OnSequenceFinished_DuringPause_KeepsPhase()
        {
            var s = new StationScheduler("TEST");
            s.OnSequenceFinished();
            Assert.Equal(StationScheduler.Phase.Pause, s.CurrentPhase);
        }

        // F135: unexpected phase (PreNavPause) must be ignored.
        [Fact]
        public void OnSequenceFinished_DuringPreNavPause_KeepsPhase()
        {
            var s = new StationScheduler("TEST");
            s.Tick();
            s.OnSequenceFinished();
            s.OnSequenceFinished();
            Assert.Equal(StationScheduler.Phase.PreNavPause, s.CurrentPhase);
        }
    }
}
