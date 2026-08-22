using System;
using System.Linq;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class BeaconDataTests
    {
        [Fact]
        public void Stations_FiveCap59Stations()
        {
            Assert.Equal(5, BeaconData.Stations.Length);
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
        public void Stations_Cap59CycleDurations()
        {
            Assert.Equal(40, BeaconData.Stations[0].CycleDurationSeconds);
            Assert.Equal(40, BeaconData.Stations[1].CycleDurationSeconds);
            Assert.Equal(60, BeaconData.Stations[2].CycleDurationSeconds);
            Assert.Equal(60, BeaconData.Stations[3].CycleDurationSeconds);
            Assert.Equal(60, BeaconData.Stations[4].CycleDurationSeconds);
        }

        [Fact]
        public void Bushmills_FirstStation_Coordinates()
        {
            var s = BeaconData.Stations[0];
            Assert.Equal("Bushmills", s.Name);
            Assert.Equal(55.205556, s.Latitude, 5);
            Assert.Equal(-6.467222, s.Longitude, 5);
            Assert.Equal("MWN", s.History[0].Callsign);
            Assert.Equal(266f, s.History[0].FrequencyKHz);
            Assert.Equal(130.2f, s.NormalBearingForwardDegTrue, 2);
            Assert.Equal(310.2f, s.NormalBearingBackwardDegTrue, 2);
        }

        [Fact]
        public void Stations_Cap59IdentityAndRoles()
        {
            Assert.Equal("Quimper/Ploneis", BeaconData.Stations[1].Name);
            Assert.Equal("FRQ", BeaconData.Stations[1].History[0].Callsign);
            Assert.Equal(257f, BeaconData.Stations[1].History[0].FrequencyKHz);
            Assert.Equal(BeaconStationRole.DirectionalSonne, BeaconData.Stations[1].Role);

            Assert.Equal("Stavanger", BeaconData.Stations[2].Name);
            Assert.Equal("LEC", BeaconData.Stations[2].History[0].Callsign);
            Assert.Equal(319f, BeaconData.Stations[2].History[0].FrequencyKHz);

            Assert.Equal("Lugo", BeaconData.Stations[3].Name);
            Assert.Equal("LG", BeaconData.Stations[3].History[0].Callsign);
            Assert.Equal(285f, BeaconData.Stations[3].History[0].FrequencyKHz);

            Assert.Equal("Seville", BeaconData.Stations[4].Name);
            Assert.Equal("SL", BeaconData.Stations[4].History[0].Callsign);
            Assert.Equal(315f, BeaconData.Stations[4].History[0].FrequencyKHz);
            Assert.Equal(BeaconStationRole.RadioBeaconOnly, BeaconData.Stations[4].Role);
        }

        [Fact]
        public void Stations_Cap59Schedules_RespectPublishedWindows()
        {
            var bushmills = BeaconData.Stations[0].Schedule;
            Assert.True(bushmills.IsOperationalAt(new DateTime(2026, 8, 3, 15, 59, 0)));
            Assert.False(bushmills.IsOperationalAt(new DateTime(2026, 8, 3, 16, 0, 0)));
            Assert.False(bushmills.IsOperationalAt(new DateTime(2026, 8, 5, 11, 30, 0)));

            var ploneis = BeaconData.Stations[1].Schedule;
            Assert.False(ploneis.IsOperationalAt(new DateTime(2026, 8, 4, 7, 30, 0)));
            Assert.True(ploneis.IsOperationalAt(new DateTime(2026, 8, 4, 7, 45, 0)));

            var seville = BeaconData.Stations[4].Schedule;
            Assert.False(seville.IsOperationalAt(new DateTime(2026, 8, 4, 5, 59, 0)));
            Assert.True(seville.IsOperationalAt(new DateTime(2026, 8, 4, 6, 0, 0)));
            Assert.False(seville.IsOperationalAt(new DateTime(2026, 8, 4, 20, 0, 0)));
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
