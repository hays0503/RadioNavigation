using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class SonneSignalGeneratorTests
    {
        private const float Tol = 0.01f;

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
        }

        [Fact]
        public void ToNavigationPattern_Empty_ReturnsEmptyPattern()
        {
            var pattern = SonneSignalGenerator.ToNavigationPattern(null);

            Assert.Empty(pattern.Groups);
            Assert.Equal(0, pattern.ElementCount);
        }

        [Fact]
        public void GetCallsignElementCount_Empty_Zero()
        {
            Assert.Equal(0, SonneSignalGenerator.GetCallsignElementCount(""));
        }
    }
}
