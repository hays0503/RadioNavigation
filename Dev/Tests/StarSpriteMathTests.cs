using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// P3 sprite shape: the star quad samples a radial gaussian, so a wrong
    /// sigma or a normalisation slip shows up as a square or a halo.
    /// </summary>
    public sealed class StarSpriteMathTests
    {
        [Fact]
        public void Field_HasSizeSquaredSamples()
        {
            float[] field = StarSpriteMath.BuildAlphaField(16, 0.35f);
            Assert.Equal(16 * 16, field.Length);
        }

        [Fact]
        public void Field_CenterIsBrightestAndCornerIsCut()
        {
            int size = StarSpriteMath.SpriteSize;
            float[] field = StarSpriteMath.BuildAlphaField(size, StarSpriteMath.SpriteSigma);

            float center = field[(size / 2) * size + size / 2];
            float corner = field[0];

            Assert.InRange(center, 0.99f, 1.0f);
            Assert.Equal(0f, corner);
        }

        [Fact]
        public void Field_IsRadiallyMonotonic()
        {
            int size = StarSpriteMath.SpriteSize;
            float[] field = StarSpriteMath.BuildAlphaField(size, StarSpriteMath.SpriteSigma);
            float half = size * 0.5f;

            float previous = 1.0f;
            for (int i = 0; i <= 100; i++)
            {
                float r = i / 100f;
                float x = half + r * half - 0.5f;
                int xi = Math.Min(size - 1, (int)Math.Round(x));
                int yi = size / 2;
                float a = field[yi * size + xi];
                Assert.True(a <= previous + 1e-4f,
                    string.Format("alpha must not grow with radius (r={0:F2})", r));
                previous = a;
            }
        }

        [Fact]
        public void Field_SigmaIsPinnedAtHalfRadius()
        {
            // alpha(r) = exp(-r²/(2σ²)); at r = 0.5 and σ = 0.35 → 0.360.
            int size = 512; // dense grid so the sample lands on r≈0.5
            float[] field = StarSpriteMath.BuildAlphaField(size, 0.35f);
            float half = size * 0.5f;
            int xi = (int)Math.Round(half + 0.5f * half - 0.5f);
            float a = field[(size / 2) * size + xi];
            Assert.InRange(a, 0.350f, 0.370f);
        }

        [Fact]
        public void OpticalPsf_HasBrightCoreAndFaintHaloWithoutSquareCorners()
        {
            int size = StarSpriteMath.SpriteSize;
            float[] field = StarSpriteMath.BuildOpticalPsfField(size);
            float center = field[(size / 2) * size + size / 2];
            float corner = field[0];

            Assert.InRange(center, 0.99f, 1f);
            Assert.Equal(0f, corner);
            Assert.True(field[(size / 2) * size + size / 2 + size / 4] < 0.36f,
                "the core/halo PSF should be sharper at the mid-radius than the old Gaussian");
            Assert.True(field[(size / 2) * size + size - 1] > 0f,
                "the restrained scatter wing should survive near the quad edge");
        }

        [Fact]
        public void OpticalPsf_PreservesIntegratedAlphaNearTheLegacyGaussian()
        {
            int size = 128;
            float[] legacy = StarSpriteMath.BuildAlphaField(size, StarSpriteMath.SpriteSigma);
            float[] optical = StarSpriteMath.BuildOpticalPsfField(size);
            double legacySum = 0d;
            double opticalSum = 0d;
            for (int i = 0; i < legacy.Length; i++)
            {
                legacySum += legacy[i];
                opticalSum += optical[i];
            }

            double ratio = opticalSum / legacySum;
            Assert.InRange(ratio, 0.94d, 1.02d);
        }

        [Fact]
        public void OpticalPsf_IsRadiallyMonotonic()
        {
            int size = 128;
            float[] field = StarSpriteMath.BuildOpticalPsfField(size);
            float half = size * 0.5f;
            float previous = 1f;
            for (int i = 0; i <= 100; i++)
            {
                float r = i / 100f;
                int x = Math.Min(size - 1, (int)Math.Round(half + r * half - 0.5f));
                float alpha = field[(size / 2) * size + x];
                Assert.True(alpha <= previous + 1e-4f,
                    string.Format("optical PSF must not grow with radius (r={0:F2})", r));
                previous = alpha;
            }
        }

        [Fact]
        public void OpticalPsf_RejectsInvalidParameters()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => StarSpriteMath.BuildOpticalPsfField(1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => StarSpriteMath.BuildOpticalPsfField(16, 0f, 0.4f, 0.2f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => StarSpriteMath.BuildOpticalPsfField(16, 0.2f, 0.4f, 1.1f));
        }
    }
}
