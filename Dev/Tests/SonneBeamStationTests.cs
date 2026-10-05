using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class SonneBeamStationTests
    {
        private const string Json =
            "{\"station\": \"T\", \"dashes\": {\"10\": 30}, \"dots\": {\"190\": 20}}";

        [Fact]
        public void Parse_ValidJson()
        {
            SonneBeamStation station = SonneBeamStation.Parse(Json);

            Assert.Equal("T", station.Name);
            Assert.Equal(30, station.Dashes[10.0]);
            Assert.Equal(20, station.Dots[190.0]);
        }

        [Fact]
        public void Parse_MissingStation_Throws()
        {
            Assert.Throws<FormatException>(() =>
                SonneBeamStation.Parse("{\"dashes\": {}, \"dots\": {}}"));
        }

        [Fact]
        public void Parse_CountOutOfRange_Throws()
        {
            Assert.Throws<FormatException>(() => SonneBeamStation.Parse(
                "{\"station\": \"T\", \"dashes\": {\"10\": 999}, \"dots\": {}}"));
        }

        [Fact]
        public void TryGetSignal_EmptyTables_False()
        {
            var station = new SonneBeamStation();
            SonneBeamSignal signal;

            Assert.False(station.TryGetSignal(10.0, out signal));
        }

        [Fact]
        public void TryGetSignal_NaN_False()
        {
            SonneBeamStation station = SonneBeamStation.Parse(Json);
            SonneBeamSignal signal;

            Assert.False(station.TryGetSignal(double.NaN, out signal));
        }

        [Fact]
        public void TryGetSignal_Tie_GoesToDashes()
        {
            // Dashes at 10, dots at 190: angle 100 is equidistant (90 each).
            SonneBeamStation station = SonneBeamStation.Parse(Json);
            SonneBeamSignal signal;

            Assert.True(station.TryGetSignal(100.0, out signal));
            Assert.True(signal.IsDashSector);
        }

        [Fact]
        public void TryGetSignal_NearestWins()
        {
            SonneBeamStation station = SonneBeamStation.Parse(Json);
            SonneBeamSignal signal;

            Assert.True(station.TryGetSignal(12.0, out signal));
            Assert.True(signal.IsDashSector);
            Assert.Equal(10.0, signal.MatchedAngle);
        }

        [Fact]
        public void TryGetSignal_WrapsAroundZero()
        {
            // 359 is 11 away from dashes-at-10 across the 0 boundary.
            SonneBeamStation station = SonneBeamStation.Parse(Json);
            SonneBeamSignal signal;

            Assert.True(station.TryGetSignal(359.0, out signal));
            Assert.True(signal.IsDashSector);
            Assert.Equal(10.0, signal.MatchedAngle);
        }
    }
}
