using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// CourseSampler contract: course/speed over ground from a position
    /// track (10 s window, 0.5 kn transfer threshold, anchored = speed 0
    /// with last course kept). Extracted from SextantWorksheet so the math
    /// runs offline on synthetic tracks.
    /// </summary>
    public sealed class CourseSamplerTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);

        private static void SailEast(CourseSampler sampler, double knots, int seconds, DateTime start)
        {
            double degPerHour = knots * 1.852 / 111.32;
            for (int s = 0; s <= seconds; s += 2)
            {
                sampler.AddSample(start.AddSeconds(s), 0.0, degPerHour * s / 3600.0);
            }
        }

        [Fact]
        public void NoSamples_CourseZeroSpeedZero()
        {
            var sampler = new CourseSampler();

            Assert.Equal(0f, sampler.CourseDeg);
            Assert.Equal(0f, sampler.SpeedKn);
        }

        [Fact]
        public void SingleSample_NoOutput()
        {
            var sampler = new CourseSampler();
            sampler.AddSample(T0, 50.0, 0.0);

            Assert.Equal(0f, sampler.CourseDeg);
            Assert.Equal(0f, sampler.SpeedKn);
        }

        [Fact]
        public void ShortBaseline_BelowSixSeconds_Ignored()
        {
            var sampler = new CourseSampler();
            sampler.AddSample(T0, 0.0, 0.0);
            sampler.AddSample(T0.AddSeconds(2), 0.0, 0.001);

            Assert.Equal(0f, sampler.CourseDeg);
            Assert.Equal(0f, sampler.SpeedKn);
        }

        [Fact]
        public void SteadyEastTrack_ReportsCourse90AndSpeed()
        {
            var sampler = new CourseSampler();
            SailEast(sampler, 10.0, 12, T0);

            Assert.InRange(sampler.CourseDeg, 89.0, 91.0);
            Assert.InRange(sampler.SpeedKn, 9.0, 11.0);
        }

        [Fact]
        public void Anchored_SpeedZeroCourseKept()
        {
            var sampler = new CourseSampler();
            SailEast(sampler, 10.0, 12, T0);
            float course = sampler.CourseDeg;

            for (int s = 14; s <= 30; s += 2)
                sampler.AddSample(T0.AddSeconds(s), 0.0, 10.0 * 1.852 / 111.32 * 12 / 3600.0);

            Assert.Equal(0f, sampler.SpeedKn);
            Assert.Equal(course, sampler.CourseDeg);
        }

        [Fact]
        public void StaleTrack_Pruned_ThenAnchored()
        {
            var sampler = new CourseSampler();
            SailEast(sampler, 10.0, 12, T0);
            float course = sampler.CourseDeg;

            for (int s = 60; s <= 70; s += 2)
                sampler.AddSample(T0.AddSeconds(s), 0.0, 0.05);

            Assert.Equal(0f, sampler.SpeedKn);
            Assert.Equal(course, sampler.CourseDeg);
        }

        [Fact]
        public void Reset_ClearsOutput()
        {
            var sampler = new CourseSampler();
            SailEast(sampler, 10.0, 12, T0);

            sampler.Reset();

            Assert.Equal(0f, sampler.CourseDeg);
            Assert.Equal(0f, sampler.SpeedKn);
        }
    }
}
