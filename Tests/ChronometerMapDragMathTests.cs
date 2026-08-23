using UnityEngine;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    // Mirrors ShipControlUI's native override normalization
    // (InverseLerp over the 20px screen margins) used by the expanded
    // chronometer map-drag persist path.
    public class ChronometerMapDragMathTests
    {
        private const float W = 2560f;
        private const float H = 1440f;

        [Fact]
        public void ScreenCenter_IsHalfHalf()
        {
            var v = ChronometerMapDragMath.ToNormalizedOverride(new Vector2(W / 2f, H / 2f), W, H);
            Assert.Equal(0.5f, v.x, 4);
            Assert.Equal(0.5f, v.y, 4);
        }

        [Fact]
        public void Margins_MapToZeroAndOne()
        {
            var left = ChronometerMapDragMath.ToNormalizedOverride(new Vector2(20f, 20f), W, H);
            Assert.Equal(0f, left.x, 4);
            Assert.Equal(0f, left.y, 4);

            var right = ChronometerMapDragMath.ToNormalizedOverride(new Vector2(W - 20f, H - 20f), W, H);
            Assert.Equal(1f, right.x, 4);
            Assert.Equal(1f, right.y, 4);
        }

        [Fact]
        public void OutsideMargins_ClampToUnitRange()
        {
            var v = ChronometerMapDragMath.ToNormalizedOverride(new Vector2(-500f, H + 500f), W, H);
            Assert.Equal(0f, v.x, 4);
            Assert.Equal(1f, v.y, 4);
        }
    }
}
