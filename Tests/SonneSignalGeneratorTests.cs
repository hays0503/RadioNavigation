using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class SonneSignalGeneratorTests
    {
        private const float Tol = 0.01f;

        [Fact]
        public void CalculateSector_OnBaseAngle_ZeroDotsDotSector()
        {
            var result = SonneSignalGenerator.CalculateSector(310.2f, 310.2f);

            Assert.Equal(0, result.DotCount);
            Assert.False(result.IsDashSector);
            Assert.Equal(0, result.SectorNumber);
            Assert.Equal(0f, result.LocalAngle, Tol);
        }

        [Fact]
        public void CalculateSector_MidSector_HalfDots()
        {
            var result = SonneSignalGenerator.CalculateSector(312.7f, 310.2f);

            Assert.Equal(30, result.DotCount);
            Assert.False(result.IsDashSector);
            Assert.Equal(0, result.SectorNumber);
            Assert.Equal(2.5f, result.LocalAngle, Tol);
        }

        [Fact]
        public void CalculateSector_MidSector_DashZone()
        {
            var result = SonneSignalGenerator.CalculateSector(317.5f, 310f);

            Assert.Equal(30, result.DotCount);
            Assert.True(result.IsDashSector);
            Assert.Equal(1, result.SectorNumber);
            Assert.Equal(2.5f, result.LocalAngle, Tol);
        }

        [Theory]
        [InlineData(310f, 310f, true)]
        [InlineData(312.4f, 310f, true)]
        [InlineData(312.6f, 310f, false)]
        [InlineData(307.6f, 310f, true)]
        [InlineData(307.4f, 310f, false)]
        [InlineData(357f, 0f, false)]
        [InlineData(358f, 0f, true)]
        [InlineData(307.5f, 310f, true)]
        public void CalculateInBeam_BearingVsBaseAngle(float bearing, float baseAngle, bool expected)
        {
            Assert.Equal(expected, SonneSignalGenerator.CalculateInBeam(
                bearing,
                baseAngle,
                SonneSignalGenerator.SectorWidthDeg));
        }

        [Fact]
        public void CalculateSectorNumber_WrapsAroundZero()
        {
            Assert.Equal(71, SonneSignalGenerator.CalculateSectorNumber(359f, 0f));
            Assert.Equal(0, SonneSignalGenerator.CalculateSectorNumber(1f, 0f));
        }

        [Fact]
        public void AngularDistance_UsesShortestCircularPath()
        {
            Assert.Equal(2f, SonneSignalGenerator.AngularDistance(359f, 1f), Tol);
            Assert.Equal(180f, SonneSignalGenerator.AngularDistance(0f, 180f), Tol);
        }

        [Fact]
        public void ToNavigationPattern_PacksSixtyElementPattern()
        {
            var elements = SonneBeam.BuildPattern(38, false);
            var pattern = SonneSignalGenerator.ToNavigationPattern(elements);

            Assert.Equal(60, elements.Length);
            Assert.Equal(64, pattern.ElementCount);
            Assert.Equal(4, pattern.TransitionIndex);
            Assert.Contains("=", NavigationPattern.FormatPattern(pattern));
        }

        [Fact]
        public void ToNavigationPattern_Empty_ReturnsEmptyPattern()
        {
            var pattern = SonneSignalGenerator.ToNavigationPattern(null);

            Assert.Empty(pattern.Groups);
            Assert.Equal(0, pattern.ElementCount);
        }

        [Fact]
        public void CalculateSector_FullRotation_WrapsToZero()
        {
            var result = SonneSignalGenerator.CalculateSector(670.2f, 310.2f);

            Assert.Equal(0, result.DotCount);
            Assert.False(result.IsDashSector);
        }

        [Fact]
        public void RestoreBearingFromDotCount_DotSector()
        {
            Assert.Equal(312.7f,
                SonneSignalGenerator.RestoreBearingFromDotCount(30, false, 310.2f),
                Tol);
        }

        [Fact]
        public void RestoreBearingFromDotCount_DashSector()
        {
            Assert.Equal(317.7f,
                SonneSignalGenerator.RestoreBearingFromDotCount(30, true, 315.2f),
                Tol);
        }

        [Fact]
        public void DegPerSign_IsFiveSixtiethsOfDegree()
        {
            Assert.Equal(5f / 60f, SonneSignalGenerator.DegPerSign, 1e-6f);
        }

        [Fact]
        public void GetCallsignElementCount_Empty_Zero()
        {
            Assert.Equal(0, SonneSignalGenerator.GetCallsignElementCount(""));
        }
    }
}
