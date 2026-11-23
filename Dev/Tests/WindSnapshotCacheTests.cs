using System;
using System.Collections.Generic;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class WindSnapshotCacheTests
    {
        private static WindSample Linear(double east, double north, double time)
        {
            return new WindSample
            {
                Available = true,
                EastMetersPerSecond = (float)(2.0 + 0.5 * east + 0.01 * time),
                NorthMetersPerSecond = (float)(-1.0 + 0.25 * north)
            };
        }

        private static WindPoint Point(double east, double north, double time)
        {
            return new WindPoint { East = east, North = north, Time = time };
        }

        private static WindSnapshotCache Build(WindSnapshotCache previous, int revision,
            IList<WindPoint> points, out int evaluations)
        {
            return WindSnapshotCache.Build(previous, revision, 10, 10, 100, points, Linear,
                out evaluations);
        }

        [Fact]
        public void LinearFieldIsReproducedExactlyInsideACell()
        {
            var cache = Build(null, 1, new[] { Point(3, 4, 50) }, out int evaluations);

            var sample = cache.Sample(3, 4, 50, 1);

            Assert.True(sample.Available);
            Assert.Equal(4.0f, sample.EastMetersPerSecond, 4);
            Assert.Equal(0.0f, sample.NorthMetersPerSecond, 4);
            Assert.Equal(8, evaluations);
            Assert.Equal(8, cache.AnchorCount);
        }

        [Fact]
        public void NegativeCoordinatesUseSignedCellsAndStayExact()
        {
            var cache = Build(null, 1, new[] { Point(-15, -25, -150) }, out int evaluations);

            var sample = cache.Sample(-15, -25, -150, 1);

            Assert.True(sample.Available);
            Assert.Equal(-7.0f, sample.EastMetersPerSecond, 4);
            Assert.Equal(-7.25f, sample.NorthMetersPerSecond, 4);
            Assert.Equal(8, evaluations);
        }

        [Fact]
        public void MissingAnchorIsUnavailableRatherThanStillAir()
        {
            var cache = Build(null, 1, new[] { Point(3, 4, 50) }, out int evaluations);

            var far = cache.Sample(300, 400, 5000, 1);

            Assert.False(far.Available);
            Assert.Equal(0f, far.EastMetersPerSecond);
            Assert.Equal(0f, far.NorthMetersPerSecond);
            Assert.Equal(8, evaluations);
        }

        [Fact]
        public void StaleRevisionIsUnavailableEvenWithAnchorsPresent()
        {
            var cache = Build(null, 7, new[] { Point(3, 4, 50) }, out int evaluations);

            Assert.True(cache.Sample(3, 4, 50, 7).Available);
            Assert.False(cache.Sample(3, 4, 50, 8).Available);
        }

        [Fact]
        public void WarmCellReusesAnchorsWithoutCallingTheEvaluator()
        {
            var first = Build(null, 1, new[] { Point(3, 4, 50) }, out int firstEvaluations);
            var warm = Build(first, 1, new[] { Point(1, 2, 20) }, out int warmEvaluations);

            Assert.Equal(8, firstEvaluations);
            Assert.Equal(0, warmEvaluations);
            Assert.True(warm.Sample(1, 2, 20, 1).Available);
            Assert.Equal(8, warm.AnchorCount);
        }

        [Fact]
        public void NewRevisionDoesNotReuseOldAnchorsAndKeepsOldSnapshotValid()
        {
            var first = Build(null, 1, new[] { Point(3, 4, 50) }, out int firstEvaluations);
            var revised = Build(first, 2, new[] { Point(3, 4, 50) }, out int revisedEvaluations);

            Assert.Equal(8, revisedEvaluations);
            Assert.Equal(8, revised.AnchorCount);
            Assert.Equal(8, first.AnchorCount);
            Assert.True(first.Sample(3, 4, 50, 1).Available);
            Assert.False(first.Sample(3, 4, 50, 2).Available);
            Assert.True(revised.Sample(3, 4, 50, 2).Available);
        }

        [Fact]
        public void TimeIsPartOfTheAnchorKey()
        {
            var cache = Build(null, 1, new[] { Point(3, 4, 50), Point(3, 4, 450) },
                out int evaluations);

            Assert.True(cache.Sample(3, 4, 50, 1).Available);
            Assert.True(cache.Sample(3, 4, 450, 1).Available);
            Assert.Equal(16, evaluations);
            Assert.Equal(-4.0f, cache.Sample(3, 4, 50, 1).EastMetersPerSecond
                - cache.Sample(3, 4, 450, 1).EastMetersPerSecond, 4);
        }

        [Fact]
        public void NonFinitePointOrQueryIsRejected()
        {
            var cache = Build(null, 1,
                new[] { Point(double.NaN, 4, 50), Point(0, 0, 0) }, out int evaluations);

            Assert.Equal(8, evaluations);
            Assert.False(cache.Sample(double.PositiveInfinity, 0, 0, 1).Available);
            Assert.False(cache.Sample(0, 0, double.NaN, 1).Available);
        }

        [Fact]
        public void EvaluatorThatLeavesAvailableUnsetIsStillCachedAsData()
        {
            // The evaluator contract is finite components only; the cache owns
            // the Available flag, and a by-value helper used to drop it.
            WindSnapshotCache cache = WindSnapshotCache.Build(null, 1, 10, 10, 100,
                new[] { Point(3, 4, 50) },
                (east, north, time) => new WindSample
                {
                    EastMetersPerSecond = (float)(2 + 0.5 * east + 0.01 * time),
                    NorthMetersPerSecond = (float)(-1 + 0.25 * north)
                },
                out int evaluations);

            var sample = cache.Sample(3, 4, 50, 1);

            Assert.True(sample.Available);
            Assert.Equal(4.0f, sample.EastMetersPerSecond, 4);
            Assert.Equal(0f, sample.NorthMetersPerSecond, 4);
            Assert.Equal(8, evaluations);
        }

        [Fact]
        public void InvalidSpacingIsRejectedAtBuildTime()
        {
            int evaluations;
            Assert.Throws<ArgumentOutOfRangeException>(() => WindSnapshotCache.Build(null, 1,
                0, 10, 100, new[] { Point(0, 0, 0) }, Linear, out evaluations));
            Assert.Throws<ArgumentNullException>(() => WindSnapshotCache.Build(null, 1,
                10, 10, 100, null, Linear, out evaluations));
        }
    }
}
