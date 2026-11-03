using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class SextantShadeFilterTests
    {
        [Theory]
        [InlineData(0.125, true)]
        [InlineData(1.0, true)]
        [InlineData(4.0, true)]
        [InlineData(64.0, true)]
        [InlineData(0.1249, false)]
        [InlineData(0.0, false)]
        [InlineData(-1.0, false)]
        [InlineData(64.01, false)]
        [InlineData(double.NaN, false)]
        [InlineData(double.PositiveInfinity, false)]
        [InlineData(double.NegativeInfinity, false)]
        public void ProbeRadianceIsFinitePositiveAndBounded(double value, bool accepted)
        {
            Assert.Equal(accepted, SextantShadeFilter.IsProbeRadiance(value));
        }

        [Fact]
        public void SolarRadianceIsFixedAndThreeGlassesTransmitTheTextureReference()
        {
            Assert.InRange(SextantShadeFilter.SolarSceneRadiance, 23.0, 24.0);
            Assert.Equal(1.0, SextantShadeFilter.SolarSceneRadiance * SextantShadeFilter.Transmission(3), 10);
            Assert.True(SextantShadeFilter.SolarSceneRadiance * SextantShadeFilter.Transmission(2) > 1.0);
            Assert.True(SextantShadeFilter.SolarSceneRadiance * SextantShadeFilter.Transmission(1) >
                SextantShadeFilter.SolarSceneRadiance * SextantShadeFilter.Transmission(2));
        }

        [Fact]
        public void NoGlass_IsIdentity_NotAnExposureOrColourChange()
        {
            Assert.Equal(1.0, SextantShadeFilter.Transmission(0));
            Assert.Equal(0.0, SextantShadeFilter.ExposureEv(0));
            Assert.Equal(1.0, SextantShadeFilter.Green(0));
            Assert.Equal(1.0, SextantShadeFilter.Blue(0));
        }

        [Theory]
        [InlineData(1, 0.35)]
        [InlineData(2, 0.1225)]
        [InlineData(3, 0.042875)]
        [InlineData(4, 0.01500625)]
        public void GlassesMultiplyLight_BeforeTheTonemapper(int count, double transmission)
        {
            Assert.Equal(transmission, SextantShadeFilter.Transmission(count), 10);
            Assert.Equal(transmission, Math.Pow(2.0, SextantShadeFilter.ExposureEv(count)), 10);
            Assert.True(SextantShadeFilter.ExposureEv(count) < 0);
        }

        [Fact]
        public void GlassesAreAmber_NotGreyOrAWhiteOverlay()
        {
            double previousGreen = 1.0, previousBlue = 1.0;
            for (int count = 1; count <= 4; count++)
            {
                double green = SextantShadeFilter.Green(count);
                double blue = SextantShadeFilter.Blue(count);
                Assert.True(green < previousGreen && blue < previousBlue);
                Assert.True(green > blue && blue > 0.0);
                previousGreen = green;
                previousBlue = blue;
            }
            Assert.Equal(0.614125, SextantShadeFilter.Green(3), 8);
            Assert.Equal(0.125, SextantShadeFilter.Blue(3), 8);
        }

        [Theory]
        [InlineData(int.MinValue, 0)]
        [InlineData(-1, 0)]
        [InlineData(4, 4)]
        [InlineData(5, 4)]
        [InlineData(int.MaxValue, 4)]
        public void InvalidCountsAreBounded(int input, int expected)
        {
            Assert.Equal(expected, SextantShadeFilter.ClampCount(input));
            Assert.Equal(SextantShadeFilter.Transmission(expected), SextantShadeFilter.Transmission(input));
            Assert.Equal(SextantShadeFilter.ExposureEv(expected), SextantShadeFilter.ExposureEv(input));
            Assert.Equal(SextantShadeFilter.Green(expected), SextantShadeFilter.Green(input));
            Assert.Equal(SextantShadeFilter.Blue(expected), SextantShadeFilter.Blue(input));
        }

        [Fact]
        public void GlareAndImageUseTheSameOpticalTransmission()
        {
            for (int count = 0; count <= 4; count++)
                Assert.Equal(SextantShadeFilter.Transmission(count),
                    SextantGlare.Glare(0.0, 60.0, count, 65.0), 10);
        }
    }
}
