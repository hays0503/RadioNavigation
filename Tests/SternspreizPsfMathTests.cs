using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Pins the parameterized, uniform horizontal line-spread math only.
    /// These tests do not claim that the simplified profile is a measured
    /// optical model or authorize a shader/PIP integration.
    /// </summary>
    public sealed class SternspreizPsfMathTests
    {
        [Fact]
        public void ZeroLength_IsCenteredImpulse()
        {
            float[] kernel = SternspreizPsfMath.BuildHorizontalLineSpreadKernel(0f, 0);

            Assert.Single(kernel);
            Assert.Equal(1f, kernel[0]);
        }

        [Fact]
        public void FourPixelLine_UsesPixelCellOverlapWeights()
        {
            float[] kernel = SternspreizPsfMath.BuildHorizontalLineSpreadKernel(4f, 4);

            Assert.Equal(5, kernel.Length);
            Assert.Equal(0.125f, kernel[0], 6);
            Assert.Equal(0.25f, kernel[1], 6);
            Assert.Equal(0.25f, kernel[2], 6);
            Assert.Equal(0.25f, kernel[3], 6);
            Assert.Equal(0.125f, kernel[4], 6);
        }

        [Fact]
        public void Kernel_IsSymmetric()
        {
            float[] kernel = SternspreizPsfMath.BuildHorizontalLineSpreadKernel(7.3f, 8);

            for (int i = 0; i < kernel.Length; i++)
                Assert.Equal(kernel[i], kernel[kernel.Length - 1 - i], 6);
        }

        [Fact]
        public void Kernel_PreservesUnitEnergyAcrossSubpixelAndFractionalLengths()
        {
            float[] lengths = { 0.1f, 0.99f, 1f, 1.1f, 4f, 7.3f, 24.75f };
            for (int lengthIndex = 0; lengthIndex < lengths.Length; lengthIndex++)
            {
                float[] kernel = SternspreizPsfMath.BuildHorizontalLineSpreadKernel(
                    lengths[lengthIndex], 32);
                double sum = 0d;
                for (int i = 0; i < kernel.Length; i++)
                    sum += kernel[i];

                Assert.InRange(sum, 0.999999d, 1.000001d);
            }
        }

        [Fact]
        public void Kernel_RejectsNonFiniteOrNegativeLength()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SternspreizPsfMath.BuildHorizontalLineSpreadKernel(-1f, 8));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SternspreizPsfMath.BuildHorizontalLineSpreadKernel(float.NaN, 8));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SternspreizPsfMath.BuildHorizontalLineSpreadKernel(float.PositiveInfinity, 8));
        }

        [Fact]
        public void Kernel_RequiresNonnegativeSufficientRadius()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SternspreizPsfMath.BuildHorizontalLineSpreadKernel(0f, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SternspreizPsfMath.BuildHorizontalLineSpreadKernel(10f, 4));
        }

        [Fact]
        public void Convolution_SpreadsImpulseHorizontallyAndPadsBothSides()
        {
            int resultWidth;
            float[] result = SternspreizPsfMath.ConvolveHorizontalField(
                new[] { 0f, 1f, 0f }, 3, 1, 2f, 1, out resultWidth);

            Assert.Equal(5, resultWidth);
            Assert.Equal(new[] { 0f, 0.25f, 0.5f, 0.25f, 0f }, result);
        }

        [Fact]
        public void Convolution_PreservesTotalFieldEnergy()
        {
            float[] source = { 0.1f, 0.8f, 0.2f, 0.4f, 0.05f, 0.3f };
            int resultWidth;
            float[] result = SternspreizPsfMath.ConvolveHorizontalField(
                source, 3, 2, 5.7f, 8, out resultWidth);
            double sourceSum = 0d;
            double resultSum = 0d;
            for (int i = 0; i < source.Length; i++)
                sourceSum += source[i];
            for (int i = 0; i < result.Length; i++)
                resultSum += result[i];

            Assert.InRange(resultSum, sourceSum - 0.000001d, sourceSum + 0.000001d);
            Assert.Equal(9, resultWidth);
        }

        [Fact]
        public void Convolution_ZeroLengthCopiesFieldWithoutChangingDimensions()
        {
            float[] source = { 0.2f, 0.7f, 0.1f, 0.4f };
            int resultWidth;
            float[] result = SternspreizPsfMath.ConvolveHorizontalField(
                source, 2, 2, 0f, 0, out resultWidth);

            Assert.Equal(2, resultWidth);
            Assert.Equal(source, result);
        }

        [Fact]
        public void Convolution_RejectsInvalidSourceShape()
        {
            int resultWidth;
            Assert.Throws<ArgumentNullException>(() =>
                SternspreizPsfMath.ConvolveHorizontalField(
                    null, 1, 1, 1f, 1, out resultWidth));
            Assert.Throws<ArgumentException>(() =>
                SternspreizPsfMath.ConvolveHorizontalField(
                    new[] { 1f, 0f }, 2, 2, 1f, 1, out resultWidth));
            Assert.Throws<ArgumentException>(() =>
                SternspreizPsfMath.ConvolveHorizontalField(
                    new[] { 1f }, 0, 1, 1f, 1, out resultWidth));
        }
    }
}
