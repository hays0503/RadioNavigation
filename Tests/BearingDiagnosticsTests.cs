using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// BearingDiagnostics contract: the [RadioNav][BEARING] report lines
    /// behind SonneController.LogBearingDiagnostics. Extracted so the
    /// reciprocal-delta math and line format are pinned offline; the
    /// controller keeps only the emit-once-per-position gate.
    /// </summary>
    public sealed class BearingDiagnosticsTests
    {
        private static BeaconStation NorthStation()
        {
            return new BeaconStation
            {
                Name = "TestNorth",
                Latitude = 51.0,
                Longitude = 0.0,
                BaseAngle = 0f,
                BeamHeadingDegTrue = 0f,
                History = new[]
                {
                    new BeaconHistoryEntry { Callsign = "TT" },
                },
            };
        }

        private static StationState[] States(float mapBoatToStation, float mapStationToBoat)
        {
            return new[]
            {
                new StationState
                {
                    CurrentBearing = mapBoatToStation,
                    BearingToBoat = mapStationToBoat,
                },
            };
        }

        [Fact]
        public void BuildLines_HeaderPlusOneLinePerStation()
        {
            var lines = BearingDiagnostics.BuildLines(
                50f, 0f, new[] { NorthStation() }, States(0f, 180f));

            Assert.Equal(2, lines.Count);
            Assert.Contains("lat=50.000000", lines[0]);
            Assert.Contains("lon=0.000000", lines[0]);
        }

        [Fact]
        public void BuildLines_NorthGeometry_ZeroReciprocalDeltas()
        {
            var lines = BearingDiagnostics.BuildLines(
                50f, 0f, new[] { NorthStation() }, States(0f, 180f));

            Assert.Contains("name=TestNorth", lines[1]);
            Assert.Contains("callsign=TT", lines[1]);
            Assert.Contains("mapBoatToStation=0.0", lines[1]);
            Assert.Contains("trueBoatToStation=0.0", lines[1]);
            Assert.Contains("trueReciprocalDelta=0.0", lines[1]);
            Assert.Contains("mapReciprocalDelta=0.0", lines[1]);
            Assert.Contains("distanceKm=111.2", lines[1]);
        }

        [Fact]
        public void BuildLines_NullHistory_CallsignUnknown()
        {
            var station = NorthStation();
            station.History = null;

            var lines = BearingDiagnostics.BuildLines(
                50f, 0f, new[] { station }, States(0f, 180f));

            Assert.Contains("callsign=?", lines[1]);
        }
    }
}
