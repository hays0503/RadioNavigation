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

        [Fact]
        public void Cadence_FastFeed_StillDerivesTheSpeed()
        {
            // P7: the caller may feed at 20 Hz; the sampler accepts a point
            // every SampleIntervalSeconds of GAME time, so the derived speed
            // is unchanged (the fast feed must not corrupt the track).
            var sampler = new CourseSampler();
            double degPerHour = 10.0 * 1.852 / 111.32;
            for (int i = 0; i <= 24; i++)
            {
                double gameSeconds = i * 0.5;
                sampler.AddSample(T0.AddMilliseconds(i * 500), 0.0,
                    degPerHour * gameSeconds / 3600.0);
            }

            Assert.InRange(sampler.CourseDeg, 89.0, 91.0);
            Assert.InRange(sampler.SpeedKn, 9.0, 11.0);
        }

        [Fact]
        public void Cadence_SubIntervalFeed_DoesNotAddSamples()
        {
            // A stationary boat fed every 0.5 s stays anchored: no phantom
            // speed from the sub-interval points.
            var sampler = new CourseSampler();
            for (int i = 0; i <= 40; i++)
                sampler.AddSample(T0.AddMilliseconds(i * 500), 50.0, 10.0);

            Assert.Equal(0f, sampler.SpeedKn);
        }

        [Fact]
        public void Compression_EveryFrameButGameTimeSteps_StillSamples()
        {
            // ×30: the caller feeds every frame, game time advances 3 s a step.
            var sampler = new CourseSampler();
            double degPerHour = 10.0 * 1.852 / 111.32;
            for (int i = 0; i <= 4; i++)
            {
                double gameSeconds = i * 3.0;
                sampler.AddSample(T0.AddSeconds(gameSeconds), 0.0, degPerHour * gameSeconds / 3600.0);
            }

            Assert.InRange(sampler.CourseDeg, 89.0, 91.0);
            Assert.InRange(sampler.SpeedKn, 9.0, 11.0);
        }

        [Fact]
        public void Pause_NonPositiveGameTime_IsIgnored()
        {
            var sampler = new CourseSampler();
            SailEast(sampler, 10.0, 12, T0);
            float speed = sampler.SpeedKn;

            // Paused: the same game timestamp repeats (unscaled dt keeps ticking).
            for (int i = 0; i < 10; i++)
                sampler.AddSample(T0.AddSeconds(12), 0.0, 0.1);

            Assert.Equal(speed, sampler.SpeedKn);
            Assert.True(sampler.IsFresh(T0.AddSeconds(12)));
        }

        [Fact]
        public void Skip_GapAboveTwelveSeconds_RestartsTheTrack()
        {
            var sampler = new CourseSampler();
            SailEast(sampler, 10.0, 12, T0);

            // A 3 h time skip: the track must restart, not compute 10 kn over it.
            sampler.AddSample(T0.AddHours(3), 0.0, 0.1);
            Assert.False(sampler.IsFresh(T0.AddHours(3).AddSeconds(20)));
            Assert.False(sampler.IsFresh(T0.AddSeconds(30)));

            // Fresh samples after the skip rebuild a sane speed.
            double degPerHour = 10.0 * 1.852 / 111.32;
            DateTime t = T0.AddHours(3);
            for (int s = 2; s <= 14; s += 2)
                sampler.AddSample(t.AddSeconds(s), 0.0, 0.1 + degPerHour * s / 3600.0);

            Assert.InRange(sampler.SpeedKn, 9.0, 11.0);
        }

        [Fact]
        public void Teleport_ImpliedSpeed_IsRejectedAndReseeded()
        {
            var sampler = new CourseSampler();
            sampler.AddSample(T0, 0.0, 0.0);
            sampler.AddSample(T0.AddSeconds(2), 0.0, 0.0001);

            // 1° east in 2 s ≈ 900 kn: a teleport, not a run.
            sampler.AddSample(T0.AddSeconds(4), 0.0, 1.0);

            // The absurd step seeded a new track, so no 900 kn speed leaks out.
            Assert.True(sampler.SpeedKn < 1f);
        }

        [Fact]
        public void IsFresh_TracksTheGameClock()
        {
            var sampler = new CourseSampler();
            Assert.False(sampler.IsFresh(T0));

            sampler.AddSample(T0, 0.0, 0.0);
            Assert.True(sampler.IsFresh(T0.AddSeconds(5)));
            Assert.False(sampler.IsFresh(T0.AddSeconds(20)));
            Assert.Equal(double.MaxValue, new CourseSampler().SecondsSinceLastSample(T0));
        }
    }
}
