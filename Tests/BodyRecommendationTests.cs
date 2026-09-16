using System.Collections.Generic;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// BodyRecommendation contract (SEXTANT_PLAN v2.2): the second sight is
    /// the candidate whose azimuth is closest to 90° from the first, ignoring
    /// bodies below the reliable-altitude floor.
    /// </summary>
    public sealed class BodyRecommendationTests
    {
        private static BodyRecommendation.Candidate C(string name, double alt, double az)
        {
            return new BodyRecommendation.Candidate
            {
                Name = name,
                AltDeg = alt,
                AzDeg = az
            };
        }

        [Fact]
        public void Separation_IsUndirected_0To90()
        {
            Assert.Equal(90.0, BodyRecommendation.SeparationDeg(0.0, 90.0), 9);
            Assert.Equal(10.0, BodyRecommendation.SeparationDeg(5.0, 175.0), 9);
            Assert.Equal(20.0, BodyRecommendation.SeparationDeg(350.0, 10.0), 9);
            Assert.Equal(0.0, BodyRecommendation.SeparationDeg(123.0, 123.0), 9);
        }

        [Fact]
        public void Recommend_PicksClosestToPerpendicular()
        {
            var candidates = new List<BodyRecommendation.Candidate>
            {
                C("Along", 40.0, 5.0),
                C("Good", 30.0, 95.0),
                C("Back", 35.0, 200.0)
            };

            BodyRecommendation.Result r = BodyRecommendation.Recommend(0.0, candidates);

            Assert.True(r.Found);
            Assert.Equal("Good", r.Name);
            Assert.Equal(85.0, r.SeparationDeg, 6);
        }

        [Fact]
        public void Recommend_SkipsLowAltitude()
        {
            var candidates = new List<BodyRecommendation.Candidate>
            {
                C("Low", 3.0, 90.0),
                C("High", 25.0, 30.0)
            };

            BodyRecommendation.Result r = BodyRecommendation.Recommend(0.0, candidates);

            Assert.True(r.Found);
            Assert.Equal("High", r.Name);
        }

        [Fact]
        public void Recommend_NoCandidateAboveFloor_NotFound()
        {
            var candidates = new List<BodyRecommendation.Candidate>
            {
                C("Low1", 1.0, 90.0),
                C("Low2", 9.9, 45.0)
            };

            BodyRecommendation.Result r = BodyRecommendation.Recommend(0.0, candidates);

            Assert.False(r.Found);
            Assert.Null(r.Name);
        }

        [Fact]
        public void Recommend_NullOrEmpty_NotFound()
        {
            Assert.False(BodyRecommendation.Recommend(0.0, null).Found);
            Assert.False(BodyRecommendation.Recommend(0.0,
                new List<BodyRecommendation.Candidate>()).Found);
        }

        [Fact]
        public void Recommend_ExcludesFirstBody()
        {
            var candidates = new List<BodyRecommendation.Candidate>
            {
                C("Sun", 40.0, 10.0),
                C("Vega", 35.0, 80.0)
            };

            BodyRecommendation.Result r = BodyRecommendation.Recommend(10.0, candidates, "Sun");

            Assert.True(r.Found);
            Assert.Equal("Vega", r.Name);
        }

        [Fact]
        public void Recommend_ExcludedIsOnlyCandidate_NotFound()
        {
            var candidates = new List<BodyRecommendation.Candidate>
            {
                C("Sun", 40.0, 10.0)
            };

            Assert.False(BodyRecommendation.Recommend(10.0, candidates, "Sun").Found);
        }
    }
}
