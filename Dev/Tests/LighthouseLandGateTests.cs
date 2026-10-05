using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Pure-decision tests for the land/water curation gate: the shore
    /// band boundary, NaN handling and the game-projection conversion.
    /// The SIGN convention itself (negative = land) is pinned separately
    /// by CoastQuadTreePortTests against the real game data file.
    /// </summary>
    public class LighthouseLandGateTests
    {
        [Theory]
        [InlineData(double.NaN, false)]
        [InlineData(double.PositiveInfinity, false)]
        [InlineData(double.NegativeInfinity, false)]
        [InlineData(-4600.0, false)]
        [InlineData(-0.31, false)]
        [InlineData(-0.3, false)]
        [InlineData(0.0, false)]
        [InlineData(0.3, false)]
        [InlineData(0.300001, true)]
        [InlineData(0.5, true)]
        [InlineData(12.75, true)]
        public void IsWater_BandBoundary_MatchesSpec(double distanceKm, bool expected)
        {
            Assert.Equal(expected, LighthouseLandGate.IsWater(distanceKm));
        }

        [Theory]
        [InlineData(double.NaN, "unknown")]
        [InlineData(double.PositiveInfinity, "unknown")]
        [InlineData(-4600.0, "land")]
        [InlineData(-0.31, "land")]
        [InlineData(-0.3, "borderline")]
        [InlineData(0.0, "borderline")]
        [InlineData(0.3, "borderline")]
        [InlineData(0.300001, "water")]
        [InlineData(4600.0, "water")]
        public void VerdictLabel_ClassifiesSignedDistance(double distanceKm, string expected)
        {
            Assert.Equal(expected, LighthouseLandGate.VerdictLabel(distanceKm));
        }

        [Fact]
        public void LatitudeToKm_FullCircle_MatchesGameEquator()
        {
            // The game divides the 40075.16 km equator into 360 degrees.
            Assert.Equal(40075.16, LighthouseLandGate.LatitudeToKm(360.0), 3);
            Assert.Equal(40075.16, LighthouseLandGate.LongitudeToKm(360.0), 3);
        }

        [Fact]
        public void LatitudeToKm_GermanBight_MatchesGameProjection()
        {
            // Coordinates.cs:54 uses 111.3198888888889 km per degree.
            Assert.Equal(53.519973 * 111.3198888888889, LighthouseLandGate.LatitudeToKm(53.519973), 2);
            Assert.Equal(8.127701 * 111.3198888888889, LighthouseLandGate.LongitudeToKm(8.127701), 2);
        }

        [Fact]
        public void ConversionHelpers_AreSymmetric()
        {
            double latKm = LighthouseLandGate.LatitudeToKm(54.5);
            double lonKm = LighthouseLandGate.LongitudeToKm(7.0);
            Assert.Equal(latKm, LighthouseLandGate.LatitudeToKm(54.5), 9);
            Assert.NotEqual(latKm, lonKm, 3);
        }
    }
}
