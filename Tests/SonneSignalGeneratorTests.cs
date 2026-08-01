using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class SonneSignalGeneratorTests
    {
        private const float Tol = 0.01f;

        [Fact]
        public void CalculateSector_OnBaseAngle_ZeroDotsDotSector()
        {
            var r = SonneSignalGenerator.CalculateSector(310.2f, 310.2f);
            Assert.Equal(0, r.DotCount);
            Assert.False(r.IsDashSector);
            Assert.Equal(0f, r.LocalAngle, Tol);
        }

        [Fact]
        public void CalculateSector_MidSector_HalfDots()
        {
            var r = SonneSignalGenerator.CalculateSector(312.7f, 310.2f);
            Assert.Equal(30, r.DotCount);
            Assert.False(r.IsDashSector);
            Assert.Equal(2.5f, r.LocalAngle, Tol);
        }

        [Fact]
        public void CalculateSector_DashSector_CountsReversed()
        {
            var r = SonneSignalGenerator.CalculateSector(315.2f, 310.2f);
            Assert.True(r.IsDashSector);
            Assert.Equal(60, r.DotCount);
        }

        [Fact]
        public void CalculateSector_FullRotation_WrapsToZero()
        {
            var r = SonneSignalGenerator.CalculateSector(670.2f, 310.2f);
            Assert.Equal(0, r.DotCount);
            Assert.False(r.IsDashSector);
        }

        [Fact]
        public void RestoreBearingFromDotCount_DotSector()
        {
            Assert.Equal(312.7f, SonneSignalGenerator.RestoreBearingFromDotCount(30, false, 310.2f), Tol);
        }

        [Fact]
        public void RestoreBearingFromDotCount_DashSector()
        {
            Assert.Equal(317.7f, SonneSignalGenerator.RestoreBearingFromDotCount(30, true, 315.2f), Tol);
        }

        [Fact]
        public void DegPerSign_IsFiveSixtiethsOfDegree()
        {
            Assert.Equal(5f / 60f, SonneSignalGenerator.DegPerSign, 1e-6f);
        }

        [Fact]
        public void GetTransitionCount_CountsOnlyDots()
        {
            var pattern = new[] { NavigationElementType.Dot, NavigationElementType.Dot, NavigationElementType.Dash, NavigationElementType.Equisignal };
            Assert.Equal(2, SonneSignalGenerator.GetTransitionCount(pattern));
        }

        [Fact]
        public void GenerateBearingTable_LengthAndFirstEntry()
        {
            var table = SonneSignalGenerator.GenerateBearingTable(BeaconData.Stations[0]);
            Assert.Equal(61, table.Length);
            Assert.Equal(310, table[0]);
            Assert.Equal(315, table[60]);
        }

        [Fact]
        public void LookupBearing_ClampsOutOfRange()
        {
            var table = SonneSignalGenerator.GenerateBearingTable(BeaconData.Stations[0]);
            Assert.Equal(315, SonneSignalGenerator.LookupBearing(75, table));
            Assert.Equal(310, SonneSignalGenerator.LookupBearing(-5, table));
        }

        [Fact]
        public void GenerateConsolPattern_LetterB_Structure()
        {
            var pattern = SonneSignalGenerator.GenerateConsolPattern("B", 0, ConsolCycleConfig.Cycle120s);
            Assert.Equal(65, pattern.Length);
            Assert.Equal(NavigationElementType.Dash, pattern[0]);
            Assert.Equal(NavigationElementType.Equisignal, pattern[4]);
            Assert.Equal(NavigationElementType.Dash, pattern[5]);
        }

        [Fact]
        public void GetCallsignElementCount_Empty_Zero()
        {
            Assert.Equal(0, SonneSignalGenerator.GetCallsignElementCount(""));
        }
    }
}
