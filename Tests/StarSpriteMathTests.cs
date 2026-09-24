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
    }
}
