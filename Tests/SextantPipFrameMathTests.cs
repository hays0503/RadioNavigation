using System;
using UBOAT.Mods.RadioNavigation;
using UnityEngine;
using Xunit;

namespace Tests
{
    public class SextantPipFrameMathTests
    {
        [Fact]
        public void WorldAim_ConvertsToCameraParentFrame()
        {
            Quaternion parent = AxisAngle(0f, 1f, 0f, 63f)
                * AxisAngle(1f, 0f, 0f, 90f)
                * AxisAngle(0f, 0f, 1f, -17f);
            Quaternion localAim = AxisAngle(0f, 1f, 0f, 76f)
                * AxisAngle(1f, 0f, 0f, 13f)
                * AxisAngle(0f, 0f, 1f, 4f);
            Quaternion worldAim = parent * localAim;

            Quaternion mapped = SextantPipFrameMath.ToParentFrame(parent, worldAim);

            AssertSameRotation(localAim, mapped);
        }

        [Fact]
        public void ApplyingOneFrameTransformToBothBeams_PreservesAlidadeGap()
        {
            Quaternion parent = AxisAngle(0f, 1f, 0f, -71f)
                * AxisAngle(1f, 0f, 0f, 90f)
                * AxisAngle(0f, 0f, 1f, 8f);
            Quaternion horizonLocal = AxisAngle(0f, 1f, 0f, 76f)
                * AxisAngle(1f, 0f, 0f, 13f);
            Quaternion indexLocal = horizonLocal * AxisAngle(1f, 0f, 0f, -11f);
            Quaternion horizonWorld = parent * horizonLocal;
            Quaternion indexWorld = parent * indexLocal;

            Quaternion horizonMapped = SextantPipFrameMath.ToParentFrame(parent, horizonWorld);
            Quaternion indexMapped = SextantPipFrameMath.ToParentFrame(parent, indexWorld);
            Quaternion originalGap = Quaternion.Inverse(horizonWorld) * indexWorld;
            Quaternion mappedGap = Quaternion.Inverse(horizonMapped) * indexMapped;

            AssertSameRotation(horizonLocal, horizonMapped);
            AssertSameRotation(indexLocal, indexMapped);
            AssertSameRotation(originalGap, mappedGap);
        }

        private static Quaternion AxisAngle(float x, float y, float z, float degrees)
        {
            double length = Math.Sqrt(x * x + y * y + z * z);
            double halfAngle = degrees * Math.PI / 360d;
            float sine = (float)(Math.Sin(halfAngle) / length);
            return new Quaternion(x * sine, y * sine, z * sine,
                (float)Math.Cos(halfAngle));
        }

        private static void AssertSameRotation(Quaternion expected, Quaternion actual)
        {
            double dot = expected.x * actual.x + expected.y * actual.y
                + expected.z * actual.z + expected.w * actual.w;
            double expectedLength = Math.Sqrt(expected.x * expected.x + expected.y * expected.y
                + expected.z * expected.z + expected.w * expected.w);
            double actualLength = Math.Sqrt(actual.x * actual.x + actual.y * actual.y
                + actual.z * actual.z + actual.w * actual.w);
            double alignment = Math.Abs(dot / (expectedLength * actualLength));
            Assert.True(1d - alignment < 1e-6,
                "expected the same orientation; normalized quaternion dot=" + alignment);
        }
    }
}
