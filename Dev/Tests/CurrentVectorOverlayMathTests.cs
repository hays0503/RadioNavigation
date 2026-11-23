using System;
using UnityEngine;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class CurrentVectorOverlayMathTests
    {
        [Fact]
        public void HigherCameraElevationCoversLargerTerritoryWithFewerVectors()
        {
            CurrentVectorGridSpec low = CurrentVectorOverlayMath.ComputeGridSpec(2500f);
            CurrentVectorGridSpec mid = CurrentVectorOverlayMath.ComputeGridSpec(75000f);
            CurrentVectorGridSpec high = CurrentVectorOverlayMath.ComputeGridSpec(1000000f);

            Assert.True(mid.RadiusKm > low.RadiusKm);
            Assert.True(high.RadiusKm > mid.RadiusKm);
            Assert.True(mid.StepKm > low.StepKm);
            Assert.True(high.StepKm > mid.StepKm);
            Assert.True(high.HalfSteps < mid.HalfSteps);
            Assert.True(mid.HalfSteps < low.HalfSteps);

            var buffer = new Vector2[CurrentVectorOverlayMath.MaxPoolSlots];
            int countLow = CurrentVectorOverlayMath.BuildSampleGrid(Vector2.zero, low, buffer);
            int countHigh = CurrentVectorOverlayMath.BuildSampleGrid(Vector2.zero, high, buffer);
            Assert.True(countHigh < countLow, "Zoomed-out Atlantic view must show fewer arrows than tactical view");
            Assert.InRange(countLow, 60, CurrentVectorOverlayMath.MaxPoolSlots);
            Assert.InRange(countHigh, 20, 45);
        }

        [Fact]
        public void SampleGridSnapsToWorldCoordinatesDuringPanning()
        {
            CurrentVectorGridSpec spec = CurrentVectorOverlayMath.ComputeGridSpec(50000f);
            var bufA = new Vector2[CurrentVectorOverlayMath.MaxPoolSlots];
            var bufB = new Vector2[CurrentVectorOverlayMath.MaxPoolSlots];

            int countA = CurrentVectorOverlayMath.BuildSampleGrid(new Vector2(12.3f, -45.7f), spec, bufA);
            int countB = CurrentVectorOverlayMath.BuildSampleGrid(new Vector2(13.1f, -46.2f), spec, bufB);

            Assert.True(countA > 0 && countB > 0);
            for (int i = 0; i < countA; i++)
            {
                float remX = Math.Abs(bufA[i].x % spec.StepKm);
                Assert.True(remX < 1e-3f || Math.Abs(remX - spec.StepKm) < 1e-3f);
            }
            Assert.Contains(bufA[countA / 2], bufB);
        }

        [Fact]
        public void ArrowLengthHidesNegligibleFlowAndScalesBoundedBelowCellStep()
        {
            CurrentVectorGridSpec spec = CurrentVectorOverlayMath.ComputeGridSpec(50000f);
            Assert.Equal(0f, CurrentVectorOverlayMath.ComputeArrowLengthScene(0f, spec));
            Assert.Equal(0f, CurrentVectorOverlayMath.ComputeArrowLengthScene(0.005f, spec));

            float gentle = CurrentVectorOverlayMath.ComputeArrowLengthScene(0.25f, spec);
            float strong = CurrentVectorOverlayMath.ComputeArrowLengthScene(5.0f, spec);
            Assert.True(gentle > 0f);
            Assert.True(strong > gentle);
            Assert.True(strong <= spec.StepKm * 1000f * 0.75f);
        }

        [Fact]
        public void MapYawFollowsEastAsZeroAndNorthAsMinusNinety()
        {
            float eastYaw = CurrentVectorOverlayMath.ComputeMapYawDegrees(1f, 0f);
            float northYaw = CurrentVectorOverlayMath.ComputeMapYawDegrees(0f, 1f);
            Assert.InRange(Math.Abs(eastYaw), 0f, 1e-4f);
            Assert.InRange(Math.Abs(northYaw + 90f), 0f, 1e-4f);
        }

        [Fact]
        public void SpeedColorBucketsSeparateOceanShelfChannelAndRaces()
        {
            Color ocean = CurrentVectorOverlayMath.ComputeSpeedColor(0.15f);
            Color shelf = CurrentVectorOverlayMath.ComputeSpeedColor(0.6f);
            Color channel = CurrentVectorOverlayMath.ComputeSpeedColor(1.5f);
            Color race = CurrentVectorOverlayMath.ComputeSpeedColor(4.0f);

            Assert.True(ocean.b > ocean.r);
            Assert.True(shelf.g > shelf.r);
            Assert.True(channel.r > 0.9f && channel.g > 0.5f);
            Assert.True(race.r > 0.9f && race.g < 0.4f);
        }
    }
}
