using System;
using System.Linq;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class BeaconDataTests
    {
        [Fact]
        public void Stations_FourteenStations()
        {
            Assert.Equal(14, BeaconData.Stations.Length);
        }

        [Fact]
        public void Stations_AllHaveNameAndValidCoordinates()
        {
            foreach (var s in BeaconData.Stations)
            {
                Assert.False(string.IsNullOrEmpty(s.Name));
                Assert.InRange(s.Latitude, -90.0, 90.0);
                Assert.InRange(s.Longitude, -180.0, 180.0);
            }
        }

        [Fact]
        public void Stations_AllHaveHistoryWithFromDate()
        {
            foreach (var s in BeaconData.Stations)
            {
                Assert.NotNull(s.History);
                Assert.NotEmpty(s.History);
                Assert.NotNull(s.History[0].From);
            }
        }

        [Fact]
        public void Stations_AllCycleDuration120()
        {
            foreach (var s in BeaconData.Stations)
                Assert.Equal(120, s.CycleDurationSeconds);
        }

        [Fact]
        public void GetDotCountToBearingTable_LengthAndRange()
        {
            var table = BeaconData.Stations[0].GetDotCountToBearingTable();
            Assert.Equal(61, table.Length);
            foreach (int v in table)
                Assert.InRange(v, 0, 359);
        }

        [Fact]
        public void Bushmills_FirstStation_Coordinates()
        {
            var s = BeaconData.Stations[0];
            Assert.Equal("Bushmills", s.Name);
            Assert.Equal(55.2028, s.Latitude, 3);
            Assert.Equal(-6.5, s.Longitude, 3);
        }

        [Fact]
        public void Stations_AllHistoryCallsigns_TwoOrThreeLettersWhenNonNull()
        {
            foreach (var s in BeaconData.Stations)
            {
                string cs = s.History[0].Callsign;
                if (cs != null)
                    Assert.InRange(cs.Length, 2, 3);
            }
        }

        [Fact]
        public void Stations_AllPowerPositive()
        {
            foreach (var s in BeaconData.Stations)
                Assert.True(s.PowerW > 0);
        }

        [Fact]
        public void Stations_BeamHalfWidth_Is90Degrees()
        {
            foreach (var s in BeaconData.Stations)
                Assert.Equal(90f, s.BeamHalfWidthDeg);
        }
    }
}
