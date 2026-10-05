using UnityEngine;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Backdrop pixel contract for map markers: soft radial falloff on a
    /// white sprite (opaque core fading to transparent rim), computed as
    /// pure pixel math so it runs offline. Pins the rendering BEHAVIOR
    /// (dimensions, center/corner alpha, radial symmetry, monotonic
    /// falloff) — the Sprite assembly stays in NavMapMarkerBase.
    /// </summary>
    public sealed class MarkerArtPixelsTests
    {
        [Fact]
        public void RenderBackdropPixels_Size64_LengthMatches()
        {
            Color32[] pixels = MarkerArtPixels.RenderBackdropPixels(64);

            Assert.Equal(64 * 64, pixels.Length);
        }

        [Fact]
        public void RenderBackdropPixels_CenterOpaqueCornersTransparent()
        {
            Color32[] pixels = MarkerArtPixels.RenderBackdropPixels(64);

            // Center 2x2 (even size, no exact middle pixel) near-opaque.
            Assert.True(pixels[31 * 64 + 31].a > 240);
            Assert.True(pixels[31 * 64 + 32].a > 240);
            // Corners outside the unit circle: fully transparent.
            Assert.Equal(0, pixels[0].a);
            Assert.Equal(0, pixels[63].a);
            Assert.Equal(0, pixels[63 * 64].a);
            Assert.Equal(0, pixels[63 * 64 + 63].a);
            // Core color channel is white everywhere.
            Assert.Equal(255, pixels[31 * 64 + 31].r);
            Assert.Equal(255, pixels[31 * 64 + 31].g);
            Assert.Equal(255, pixels[31 * 64 + 31].b);
        }

        [Fact]
        public void RenderBackdropPixels_RadiallySymmetric()
        {
            Color32[] pixels = MarkerArtPixels.RenderBackdropPixels(64);

            // Mirror pairs across both axes share alpha.
            Assert.Equal(pixels[10 * 64 + 20].a, pixels[10 * 64 + 43].a);
            Assert.Equal(pixels[10 * 64 + 20].a, pixels[53 * 64 + 20].a);
            Assert.Equal(pixels[20 * 64 + 10].a, pixels[43 * 64 + 10].a);
        }

        [Fact]
        public void RenderBackdropPixels_MiddleRow_DecreasesOutward()
        {
            Color32[] pixels = MarkerArtPixels.RenderBackdropPixels(64);

            byte prev = 255;
            for (int x = 32; x < 64; x++)
            {
                byte a = pixels[32 * 64 + x].a;
                Assert.True(a <= prev);
                prev = a;
            }
            Assert.Equal(0, pixels[32 * 64 + 63].a);
        }

        [Fact]
        public void RenderBackdropPixels_Size1_SingleOpaquePixel()
        {
            Color32[] pixels = MarkerArtPixels.RenderBackdropPixels(1);

            Assert.Equal(1, pixels.Length);
            Assert.True(pixels[0].a > 200);
        }

        [Fact]
        public void RenderIconPixels_HasACoreAndARing()
        {
            const int size = 64;
            Color32[] pixels = MarkerArtPixels.RenderIconPixels(size);
            Assert.Equal(size * size, pixels.Length);

            // Center: the opaque core.
            Assert.True(pixels[32 * size + 32].a > 200);
            // r ≈ 0.45: the transparent gap between core and ring.
            Assert.True(pixels[32 * size + 46].a < 40);
            // r ≈ 0.7: the ring.
            Assert.True(pixels[32 * size + 54].a > 200);
            // Corner: outside the glyph.
            Assert.Equal(0, pixels[0].a);
        }
    }
}
