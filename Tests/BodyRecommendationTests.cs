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

        private static BodyRecommendation.Candidate H(string name, double alt, double az,
            int hip)
        {
            return new BodyRecommendation.Candidate
            {
                Name = name,
                AltDeg = alt,
                AzDeg = az,
                Hip = hip
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

        [Fact]
        public void RecommendThird_MaximizesMinToBothRecorded()
        {
            // Recorded azimuths 0 and 80: "Balanced" sits 40° from each,
            // "Wide" is 80° from one but only 20° from the other.
            var candidates = new List<BodyRecommendation.Candidate>
            {
                H("Balanced", 40.0, 40.0, 1),
                H("Wide", 40.0, 100.0, 2)
            };

            BodyRecommendation.Result r =
                BodyRecommendation.RecommendThird(0.0, 80.0, candidates, 0, 0);

            Assert.True(r.Found);
            Assert.Equal("Balanced", r.Name);
            Assert.Equal(40.0, r.SeparationDeg, 6);
        }

        [Fact]
        public void RecommendThird_ExcludesRecordedHips()
        {
            var candidates = new List<BodyRecommendation.Candidate>
            {
                H("Recorded1", 40.0, 40.0, 111),
                H("Recorded2", 40.0, 30.0, 222),
                H("Fresh", 40.0, 20.0, 333)
            };

            BodyRecommendation.Result r =
                BodyRecommendation.RecommendThird(0.0, 80.0, candidates, 111, 222);

            Assert.True(r.Found);
            Assert.Equal("Fresh", r.Name);
        }

        [Fact]
        public void RecommendThird_BelowAccept_NotFound()
        {
            var candidates = new List<BodyRecommendation.Candidate>
            {
                H("NearlySame", 40.0, 85.0, 1)
            };

            Assert.False(BodyRecommendation.RecommendThird(0.0, 80.0, candidates, 0, 0).Found);
        }

        [Fact]
        public void RecommendThird_NullEmpty_NotFound()
        {
            Assert.False(BodyRecommendation.RecommendThird(0.0, 80.0, null, 0, 0).Found);
            Assert.False(BodyRecommendation.RecommendThird(0.0, 80.0,
                new List<BodyRecommendation.Candidate>(), 0, 0).Found);
        }
    }
}
