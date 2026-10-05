using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// StationDiagnosticsReport contract: the diagnostics snapshot behind
    /// SonneController.GetStationDiagnostics (hoisted top-level struct +
    /// pure assembly). Extracted so field mapping runs offline.
    /// </summary>
    public sealed class StationDiagnosticsReportTests
    {
        private static StationState State()
        {
            return new StationState
            {
                InBeam = true,
                IsDashSector = true,
                SectorNumber = 3,
                Gain = 0.9f,
                BeatFrequency = 1200f,
                TransitionIndex = 40,
                DotCount = 25,
                CurrentBearing = 100f,
                BearingToBoat = 280f,
                Config = ConsolCycleConfig.Cycle60s,
                Pattern = new NavigationPattern(new Group[0], 8),
            };
        }

        private static BeaconStation Station()
        {
            return new BeaconStation
            {
                Name = "TestStation",
                Latitude = 50.0,
                Longitude = 1.0,
            };
        }

        [Fact]
        public void Build_CopiesAllFields()
        {
            StationDiagnostics diag = StationDiagnosticsReport.Build(
                Station(), "TT", State(), StationScheduler.Phase.Navigation, 12.5f);

            Assert.Equal("TestStation", diag.Name);
            Assert.Equal("TT", diag.Callsign);
            Assert.True(diag.InBeam);
            Assert.True(diag.IsDashSector);
            Assert.Equal(3, diag.SectorNumber);
            Assert.Equal(0.9f, diag.Gain);
            Assert.Equal(1200f, diag.BeatFrequency);
            Assert.Equal(40, diag.TransitionIndex);
            Assert.Equal(25, diag.DotCount);
            Assert.Equal(100f, diag.CurrentBearing);
            Assert.Equal(280f, diag.BearingToBoat);
            Assert.Equal(StationScheduler.Phase.Navigation, diag.SchedulerPhase);
            Assert.Equal(12.5f, diag.NavigationElapsed);
        }

        [Fact]
        public void Build_DerivesDashCountAndEqSignIndex()
        {
            StationDiagnostics diag = StationDiagnosticsReport.Build(
                Station(), "TT", State(), StationScheduler.Phase.Pause, 0f);

            Assert.Equal(ConsolCycleConfig.Cycle60s.MaxNavigationCount - 25, diag.DashCount);
            Assert.Equal(40, diag.EqSignIndex);
        }
    }
}
