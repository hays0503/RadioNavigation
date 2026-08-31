using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Locks the dot/streak overlay layout math extracted from
    /// LighthouseBeamGlow.UpdateDot (P5.4 LBG-2). Reference points come from
    /// the lighthouse visual model spec (dot ≤10px / streak ≤13px beyond
    /// 2 km, ×1.1 perceptual size scale, layer alphas).
    /// </summary>
    public sealed class LighthouseDotLayoutTests
    {
        [Fact]
        public void CenterOffset_IsScreenMinusHalfCanvas()
        {
            var m = LighthouseDotLayout.Compute(
                2000f, 960f, 540f, 1920f, 1080f, 1f, 0f);
            Assert.Equal(0f, m.CenterOffset.x, 3);
            Assert.Equal(0f, m.CenterOffset.y, 3);
        }

        [Fact]
        public void CenterOffset_OffCenter_Screen()
        {
            var m = LighthouseDotLayout.Compute(
                2000f, 100f, 200f, 1920f, 1080f, 1f, 0f);
            Assert.Equal(100f - 960f, m.CenterOffset.x, 3);
            Assert.Equal(200f - 540f, m.CenterOffset.y, 3);
        }

        [Fact]
        public void Size_FarDistance_CapsAtAbout11Px()
        {
            // DotSizePx caps near 10px beyond 2 km; ×1.1 scale → ~11px.
            var m = LighthouseDotLayout.Compute(
                2000f, 960f, 540f, 1920f, 1080f, 1f, 0f);
            Assert.InRange(m.Size, 10.4f, 11.1f);
        }

        [Fact]
        public void StreakLength_FarDistance_CappedByModel()
        {
            // StreakLengthCapPx ≈ 13px beyond 2 km; the raw multiplier
            // (size×6.5) would be ~71px, so the model cap wins.
            var m = LighthouseDotLayout.Compute(
                2000f, 960f, 540f, 1920f, 1080f, 1f, 0f);
            Assert.InRange(m.StreakLength, 12.5f, 13.5f);
        }

        [Fact]
        public void StreakThickness_NearDistance_HasMinimumOf2()
        {
            // At 100 m size is small; thickness floor is 2px.
            var m = LighthouseDotLayout.Compute(
                100f, 960f, 540f, 1920f, 1080f, 1f, 0f);
            Assert.True(m.StreakThickness >= 2f);
        }

        [Fact]
        public void LayerAlphas_ScaleWithIVisual()
        {
            var m = LighthouseDotLayout.Compute(
                2000f, 960f, 540f, 1920f, 1080f, 0.5f, 0f);
            Assert.Equal(LighthouseDotLayout.CoreDotAlpha * 0.5f, m.CoreAlpha, 3);
            Assert.Equal(LighthouseDotLayout.HaloMaxAlpha * 0.5f, m.HaloAlpha, 3);
        }

        [Fact]
        public void StreakFade_ZeroNear_FullFar()
        {
            // f ramps 0→1 between 70 m and 200 m; far distance → full alpha.
            var near = LighthouseDotLayout.Compute(
                50f, 960f, 540f, 1920f, 1080f, 1f, 0f);
            var far = LighthouseDotLayout.Compute(
                500f, 960f, 540f, 1920f, 1080f, 1f, 0f);
            Assert.True(near.StreakAlphaFinal < far.StreakAlphaFinal);
            Assert.Equal(LighthouseDotLayout.StreakAlpha, far.StreakAlphaFinal, 3);
        }

        [Fact]
        public void HorizonAngle_PassedThrough()
        {
            var m = LighthouseDotLayout.Compute(
                2000f, 960f, 540f, 1920f, 1080f, 1f, 42f);
            Assert.Equal(42f, m.HorizonAngleDeg, 3);
        }
    }
}
