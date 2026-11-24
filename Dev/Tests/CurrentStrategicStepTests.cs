using System;
using UnityEngine;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class CurrentStrategicStepTests
    {
        [Theory]
        [InlineData(0.5f, 1)]
        [InlineData(1f, 1)]
        [InlineData(600f, 1)]
        [InlineData(1799.9f, 1)]
        [InlineData(CurrentStrategicStep.MaxIntervalSeconds, 1)]
        [InlineData(CurrentStrategicStep.MaxIntervalSeconds + 0.1f, 2)]
        [InlineData(CurrentStrategicStep.MaxIntervalSeconds + 0.5f, 2)]
        [InlineData(3600f, 2)]
        [InlineData(14400f, 8)]
        [InlineData(115201f, 65)]
        [InlineData(172800f, 96)]
        [InlineData(CurrentStrategicStep.MaxSupportedSeconds, CurrentStrategicStep.MaxSteps)]
        [InlineData(CurrentStrategicStep.MaxSupportedSeconds + 1f, 0)]
        public void SplitsLongIntervalsIntoBoundedSteps(float seconds, int expected)
        {
            Assert.Equal(expected, CurrentStrategicStep.StepCount(seconds));
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(-1f)]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(CurrentStrategicStep.MaxIntervalSeconds * (CurrentStrategicStep.MaxSteps + 1))]
        public void RejectsUnusableIntervals(float seconds)
        {
            Assert.Equal(0, CurrentStrategicStep.StepCount(seconds));
            Assert.False(CurrentStrategicStep.TryOffsetKm(seconds, new Vector3(1f, 0f, 0f), out _));
        }

        [Fact]
        public void ConvertsMetresPerSecondAndSecondsToEncounterKilometres()
        {
            Assert.True(CurrentStrategicStep.TryOffsetKm(100f, new Vector3(1f, 0f, 0f), out var east));
            Assert.Equal(0.1f, east.x, 6);
            Assert.Equal(0f, east.y, 6);

            Assert.True(CurrentStrategicStep.TryOffsetKm(600f, new Vector3(0f, 0f, -0.5f), out var north));
            Assert.Equal(0f, north.x, 6);
            Assert.Equal(-0.3f, north.y, 6);

            Assert.True(CurrentStrategicStep.TryOffsetKm(30f, Vector3.zero, out var still));
            Assert.Equal(Vector2.zero, still);
        }

        [Theory]
        [InlineData(float.NaN, 0f)]
        [InlineData(0f, float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity, 0f)]
        public void RejectsNonFiniteWater(float east, float north)
        {
            Assert.False(CurrentStrategicStep.TryOffsetKm(60f, new Vector3(east, 0f, north), out _));
        }

        [Fact]
        public void OneStepOffsetTimesStepsEqualsTheWholeInterval()
        {
            const float seconds = 3600f;
            int steps = CurrentStrategicStep.StepCount(seconds);
            Assert.True(CurrentStrategicStep.TryOffsetKm(seconds / steps, new Vector3(1f, 0f, 0f), out var step));
            Assert.Equal(3.6f, step.x * steps, 4);          // 1 m/s for 3600 s = 3.6 km
            Assert.Equal(3600f, step.x * steps * 1000f, 1); // and 3600 m
        }

        [Fact]
        public void SamplesTheNativeSegmentAtEachStepTimeInsteadOfReusingTheFinalSample()
        {
            var end = new DateTime(1941, 11, 30, 0, 0, 0, DateTimeKind.Utc);
            Assert.Equal(end.AddHours(-36), CurrentStrategicStep.MidpointUtc(end, TimeSpan.TicksPerDay * 2, 0, 2));
            Assert.Equal(end.AddHours(-12), CurrentStrategicStep.MidpointUtc(end, TimeSpan.TicksPerDay * 2, 1, 2));
            var p = CurrentStrategicStep.Midpoint(Vector2.zero, new Vector2(8f, 0f), new Vector2(0f, 1f), 1, 2);
            Assert.Equal(new Vector2(6f, 1f), p);
        }

        [Fact]
        public void WorldVelocityAddsTheWaterVectorWithoutChangingThePropulsionSpeed()
        {
            var stock = new Vector2(0f, 0.003f);
            var current = new Vector3(1f, 20f, -0.25f);
            var ground = CurrentStrategicStep.WorldVelocity(stock, current);
            Assert.Equal(0.001f, ground.x, 7);
            Assert.Equal(0.00275f, ground.y, 7);
            Assert.Equal(stock, CurrentStrategicStep.WorldVelocity(stock, Vector3.zero));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-2)]
        public void RejectsNonPositiveStepCountWithoutDividingByZero(int steps)
        {
            var start = new Vector2(12f, -4f);
            var end = new DateTime(1941, 11, 30, 0, 0, 0, DateTimeKind.Utc);
            Assert.Equal(start, CurrentStrategicStep.Midpoint(start, new Vector2(20f, 0f), Vector2.zero, 0, steps));
            Assert.Equal(default(DateTime), CurrentStrategicStep.MidpointUtc(end, TimeSpan.TicksPerHour, 0, steps));
            Assert.Equal(default(DateTime), CurrentStrategicStep.MidpointUtc(end, -1L, 0, 2));
        }
    }
}
