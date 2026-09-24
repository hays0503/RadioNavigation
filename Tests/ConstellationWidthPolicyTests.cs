using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Ocular-aware constellation width policy (2026-09-22): the line width
    /// must follow the view that matters — the ocular while the sextant is
    /// open, the main camera otherwise.
    /// </summary>
    public sealed class ConstellationWidthPolicyTests
    {
        [Fact]
        public void Capture_NormalisesInvalidInputs()
        {
            ConstellationViewKey zero = ConstellationWidthPolicy.Capture(true, 0f, 0f);
            Assert.Equal(1f, zero.FovDivisor);
            Assert.Equal(1f, zero.UiScale);
            Assert.True(zero.Open);

            ConstellationViewKey nan = ConstellationWidthPolicy.Capture(false, float.NaN, float.NaN);
            Assert.Equal(1f, nan.FovDivisor);
            Assert.Equal(1f, nan.UiScale);
            Assert.False(nan.Open);
        }

        [Fact]
        public void KeyChanged_OpenEdgeBothWays()
        {
            ConstellationViewKey closed = ConstellationWidthPolicy.Capture(false, 1f, 1f);
            ConstellationViewKey open = ConstellationWidthPolicy.Capture(true, 1f, 1f);
            Assert.True(ConstellationWidthPolicy.KeyChanged(open, closed));
            Assert.True(ConstellationWidthPolicy.KeyChanged(closed, open));
        }

        [Fact]
        public void KeyChanged_ZoomStepAndUiScale()
        {
            ConstellationViewKey one = ConstellationWidthPolicy.Capture(true, 1f, 1f);
            ConstellationViewKey six = ConstellationWidthPolicy.Capture(true, 6f, 1f);
            Assert.True(ConstellationWidthPolicy.KeyChanged(six, one));

            ConstellationViewKey scaled = ConstellationWidthPolicy.Capture(true, 1f, 1.15f);
            Assert.True(ConstellationWidthPolicy.KeyChanged(scaled, one));
        }

        [Fact]
        public void KeyChanged_IdenticalAndSubEpsilon_AreStable()
        {
            ConstellationViewKey a = ConstellationWidthPolicy.Capture(true, 4.5f, 1.15f);
            ConstellationViewKey b = ConstellationWidthPolicy.Capture(true, 4.5f, 1.1500001f);
            Assert.False(ConstellationWidthPolicy.KeyChanged(a, b));
        }

        [Fact]
        public void NeedsRebuild_KeyAndCameraWinTheThrottle()
        {
            Assert.True(ConstellationWidthPolicy.NeedsRebuild(true, false, false, false));
            Assert.True(ConstellationWidthPolicy.NeedsRebuild(false, true, false, false));
            Assert.False(ConstellationWidthPolicy.NeedsRebuild(false, false, true, false));
            Assert.True(ConstellationWidthPolicy.NeedsRebuild(false, false, true, true));
            Assert.False(ConstellationWidthPolicy.NeedsRebuild(false, false, false, false));
        }

        [Fact]
        public void WidthInputs_Open_UsesOcularFovAndOnScreenHeight()
        {
            double fovRad, pixels;
            ConstellationWidthPolicy.WidthInputs(true, 65.0, 4.5, 1.15, 1080.0,
                out fovRad, out pixels);

            Assert.Equal(65.0 / 4.5 * System.Math.PI / 180.0, fovRad, 12);
            Assert.Equal(600.0 * 1.15, pixels, 9);
        }

        [Fact]
        public void WidthInputs_Closed_UsesMainCamera()
        {
            double fovRad, pixels;
            ConstellationWidthPolicy.WidthInputs(false, 65.0, 4.5, 1.15, 1080.0,
                out fovRad, out pixels);

            Assert.Equal(65.0 * System.Math.PI / 180.0, fovRad, 12);
            Assert.Equal(1080.0, pixels, 9);
        }

        [Fact]
        public void WidthInputs_Open_InvalidDivisorFallsBackToOne()
        {
            double fovRad, pixels;
            ConstellationWidthPolicy.WidthInputs(true, 65.0, 0.0, 0.0, 1080.0,
                out fovRad, out pixels);

            Assert.Equal(65.0 * System.Math.PI / 180.0, fovRad, 12);
            Assert.Equal(600.0, pixels, 9);
        }

        [Fact]
        public void OcularUnits_MatchTheOverlayLayout()
        {
            // The legacy constant is still the fallback; the live size comes
            // from SextantOcularSize (fraction of the unit height).
            Assert.Equal(600.0, ConstellationWidthPolicy.OcularSizeUnits, 6);
            Assert.Equal(600.0, (double)SextantOcularSize.Units(0f, 0f, 0.88f, 0f), 6);
        }

        [Fact]
        public void WidthInputs_Open_UsesTheLiveOcularSize()
        {
            // A 900-unit ocular at uiScale 1.5 -> 1350 px.
            ConstellationWidthPolicy.WidthInputs(true, 65.0, 1.0, 1.5, 1080.0, 900.0,
                out double fovRad, out double pixels);

            Assert.Equal(65.0 * System.Math.PI / 180.0, fovRad, 12);
            Assert.Equal(1350.0, pixels, 9);
        }

        [Fact]
        public void WidthInputs_Open_InvalidOcular_FallsBackToLegacy()
        {
            ConstellationWidthPolicy.WidthInputs(true, 65.0, 1.0, 1.0, 1080.0, 0.0,
                out _, out double pixels);

            Assert.Equal(600.0, pixels, 9);
        }
    }
}
