using System.Linq;
using UnityEngine;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    // Pins the pure cursor-art renderer extracted in CH-1. The glyphs are
    // deterministic 32x32 buffers: transparent background, opaque-white arrow
    // strokes. No Unity runtime needed - only the Color32/Texture2D stubs.
    public class ChronometerCursorArtTests
    {
        private static int CountOpaque(Color32[] pixels)
        {
            return pixels.Count(p => p.a == 255 && p.r == 255 && p.g == 255 && p.b == 255);
        }

        [Fact]
        public void Render_Is32x32()
        {
            var pixels = ChronometerCursorArt.RenderScaleCursorPixels(false);
            Assert.Equal(32 * 32, pixels.Length);
        }

        [Fact]
        public void Render_HasTransparentBackground()
        {
            var pixels = ChronometerCursorArt.RenderScaleCursorPixels(false);
            // Corners of the buffer are far from every arrow stroke -> transparent.
            Assert.Equal(0, pixels[0].a);      // top-left
            Assert.Equal(0, pixels[31].a);     // top-right
            Assert.Equal(0, pixels[32 * 31].a); // bottom-left
            Assert.Equal(0, pixels[32 * 32 - 1].a); // bottom-right
        }

        [Fact]
        public void Render_DrawsOpaqueArrowStrokes()
        {
            var pixels = ChronometerCursorArt.RenderScaleCursorPixels(false);
            Assert.True(CountOpaque(pixels) > 0,
                "expected at least one opaque arrow pixel");
        }

        [Fact]
        public void ExpandAndReduce_GlyphsDiffer()
        {
            var expand = ChronometerCursorArt.RenderScaleCursorPixels(false);
            var reduce = ChronometerCursorArt.RenderScaleCursorPixels(true);

            bool identical = true;
            for (int i = 0; i < expand.Length; i++)
            {
                if (expand[i].r != reduce[i].r || expand[i].a != reduce[i].a)
                {
                    identical = false;
                    break;
                }
            }
            Assert.False(identical, "inward and outward arrow glyphs must differ");
        }

        [Fact]
        public void CreateScaleCursor_Wraps32x32PointClampTexture()
        {
            var texture = ChronometerCursorArt.CreateScaleCursor(false);
            Assert.Equal(32, texture.width);
            Assert.Equal(32, texture.height);
            Assert.Equal(FilterMode.Point, texture.filterMode);
            Assert.Equal(TextureWrapMode.Clamp, texture.wrapMode);
            Assert.NotNull(texture.pixels);
        }
    }
}
