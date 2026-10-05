using System.IO;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// StationNavigator contract: the per-station navigation update behind
    /// SonneController.UpdateStationNavigation (map bearings, in-beam /
    /// sector, bearing-change-gated table lookup, scheduler pattern feed).
    /// Extracted so the update runs offline against the real tables.
    /// </summary>
    public sealed class StationNavigatorTests
    {
        private static void EnsureTables()
        {
            DirectoryInfo directory = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (directory != null
                && !Directory.Exists(Path.Combine(directory.FullName, "Source", "Data")))
            {
                directory = directory.Parent;
            }

            Assert.NotNull(directory);
            Assert.True(
                SonneBeam.Initialize(Path.Combine(directory.FullName, "Source", "Data")),
                SonneBeam.LastLoadError);
        }

        private static BeaconStation Quimper()
        {
            return new BeaconStation
            {
                Name = "Quimper/Ploneis",
                Latitude = 47.9,
                Longitude = -4.3,
                BaseAngle = 130f,
                BeamHeadingDegTrue = 130f,
                Role = BeaconStationRole.DirectionalSonne,
            };
        }

        private static StationScheduler Scheduler(out StationState state)
        {
            state = new StationState
            {
                Config = ConsolCycleConfig.Cycle60s,
                TransitionIndex = ConsolCycleConfig.Cycle60s.MaxNavigationCount,
            };
            var scheduler = new StationScheduler("TT")
            {
                Config = ConsolCycleConfig.Cycle60s,
                CurrentConsolPattern = SonneBeam.BuildPattern(0, false),
            };
            return scheduler;
        }

        [Fact]
        public void DirectionalStation_SetsBearingsAndLooksUpPattern()
        {
            EnsureTables();
            StationScheduler scheduler = Scheduler(out StationState state);

            StationNavigator.UpdateStation(ref state, Quimper(), 50f, 0f, scheduler);

            Assert.True(state.HasNavigationBearing);
            Assert.InRange(state.CurrentBearing, 0f, 360f);
            Assert.InRange(state.BearingToBoat, 0f, 360f);
            Assert.InRange(state.DotCount, 0, ConsolCycleConfig.Cycle60s.MaxNavigationCount);
            Assert.NotNull(scheduler.CurrentConsolPattern);
            Assert.Equal(60, scheduler.CurrentConsolPattern.Length);
        }

        [Fact]
        public void DirectionalStation_SecondCallSamePosition_KeepsPattern()
        {
            EnsureTables();
            StationScheduler scheduler = Scheduler(out StationState state);

            StationNavigator.UpdateStation(ref state, Quimper(), 50f, 0f, scheduler);
            int dots = state.DotCount;
            StationNavigator.UpdateStation(ref state, Quimper(), 50f, 0f, scheduler);

            Assert.Equal(dots, state.DotCount);
            Assert.True(state.HasNavigationBearing);
        }

        [Fact]
        public void NonDirectionalStation_ZeroesNavigationState()
        {
            EnsureTables();
            StationScheduler scheduler = Scheduler(out StationState state);
            var station = Quimper();
            station.Role = BeaconStationRole.RadioBeaconOnly;

            StationNavigator.UpdateStation(ref state, station, 50f, 0f, scheduler);

            Assert.False(state.InBeam);
            Assert.False(state.IsDashSector);
            Assert.Equal(0, state.DotCount);
            Assert.Equal(0, state.SectorNumber);
            Assert.False(state.HasNavigationBearing);
            Assert.InRange(state.CurrentBearing, 0f, 360f);
        }
    }
}
