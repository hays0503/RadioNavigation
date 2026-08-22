using System;
using System.IO;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class SonneBeamTests
    {
        [Fact]
        public void BuildPattern_DotSector_UsesSixtyElementsAndFourEquisignals()
        {
            var pattern = SonneBeam.BuildPattern(38, false);

            Assert.Equal(60, pattern.Length);
            AssertRepeated(pattern, 0, 36, NavigationElementType.Dot);
            AssertRepeated(pattern, 36, 4, NavigationElementType.Equisignal);
            AssertRepeated(pattern, 40, 20, NavigationElementType.Dash);
        }

        [Fact]
        public void BuildPattern_DashSector_UsesHistoricalOrder()
        {
            var pattern = SonneBeam.BuildPattern(38, true);

            Assert.Equal(60, pattern.Length);
            AssertRepeated(pattern, 0, 20, NavigationElementType.Dash);
            AssertRepeated(pattern, 20, 4, NavigationElementType.Equisignal);
            AssertRepeated(pattern, 24, 36, NavigationElementType.Dot);
        }

        [Theory]
        [InlineData(0, false, 2)]
        [InlineData(1, false, 3)]
        [InlineData(2, false, 4)]
        [InlineData(60, false, 2)]
        public void BuildPattern_UsesSonneBeamLossCorrection(int dotCount, bool isDashSector, int expectedEquisignals)
        {
            var pattern = SonneBeam.BuildPattern(dotCount, isDashSector);
            int equisignals = 0;

            for (int i = 0; i < pattern.Length; i++)
                if (pattern[i] == NavigationElementType.Equisignal)
                    equisignals++;

            Assert.Equal(60, pattern.Length);
            Assert.Equal(expectedEquisignals, equisignals);
        }

        [Fact]
        public void StationTable_UsesCircularNearestAngle()
        {
            var station = SonneBeamStation.Parse(
                "{\"station\":\"Test\",\"dashes\":{\"359\":60},\"dots\":{\"0\":0}}");

            SonneBeamSignal signal;
            Assert.True(station.TryGetSignal(360.1, out signal));
            Assert.Equal(0, signal.DotCount);
            Assert.Equal(60, signal.DashCount);
            Assert.False(signal.IsDashSector);
        }

        [Fact]
        public void Initialize_LoadsAllStationTables()
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null
                && !Directory.Exists(Path.Combine(directory.FullName, "Source", "Data")))
            {
                directory = directory.Parent;
            }

            Assert.NotNull(directory);
            Assert.True(
                SonneBeam.Initialize(Path.Combine(directory.FullName, "Source", "Data")),
                SonneBeam.LastLoadError);

            NavigationElementType[] pattern;
            SonneBeamSignal signal;
            Assert.True(SonneBeam.TryGetNavigationPattern(
                "Quimper/Ploneis",
                130.2,
                out pattern,
                out signal));
            Assert.Equal(60, pattern.Length);
            Assert.Equal(60, signal.DotCount + signal.DashCount);
        }

        private static void AssertRepeated(
            NavigationElementType[] pattern,
            int start,
            int count,
            NavigationElementType expected)
        {
            for (int i = start; i < start + count; i++)
                Assert.Equal(expected, pattern[i]);
        }
    }
}
