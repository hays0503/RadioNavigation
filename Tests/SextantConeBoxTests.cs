using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>P7 aiming window: the box side equals the identification cone
    /// (fixed 1.0° since 2026-11-04, so the window grows with the zoom). The
    /// side is the exact perspective projection tan(cone)/tan(pipFov/2) of
    /// the cone through the PIP FOV onto the live ocular.</summary>
    public sealed class SextantConeBoxTests
    {
        private const double MainFov = 65.0;

        // The live ocular on the documented stand (1920x1080, uiScale 1.59):
        // 0.88 * (1080/1.59) = 597.7 units (SextantOcularSizeTests pins it).
        private static float LiveOcular()
        {
            return SextantOcularSize.UnitsForScreen(1920f, 1080f, 1.59f,
                SextantOcularSize.DefaultFraction, SextantOverlayLayout.SideColumnsUnits);
        }

        [Fact]
        public void BoxSide_MatchesTheConeThroughTheLiveOcular()
        {
            float ocular = LiveOcular();
            Assert.Equal(597.7f, ocular, 1);
            // tan(1°)/tan(32.5°)*597.7 = 16.38; ×6 = 110.03. The linear form
            // (18.39 / 110.35) must fail these ranges — that was the M2 bug.
            Assert.InRange(SextantConeBox.BoxUnits(1.0, MainFov, ocular), 16.1f, 16.7f);
            Assert.InRange(SextantConeBox.BoxUnits(1.0, MainFov / 6.0, ocular), 109.4f, 110.7f);
        }

        [Fact]
        public void BoxSide_MapsBackToTheConeAngle()
        {
            float ocular = LiveOcular();
            float side = SextantConeBox.BoxUnits(1.0, MainFov, ocular);
            double drawnDeg = Math.Atan(side / ocular
                * Math.Tan(MainFov * 0.5 * Math.PI / 180.0)) * 180.0 / Math.PI;
            Assert.InRange(drawnDeg, 0.99, 1.01);
        }

        [Fact]
        public void BoxSide_GrowsWithZoom()
        {
            float ocular = LiveOcular();
            double[] divisors = { 1.0, 1.5, 2.0, 3.0, 4.5, 6.0 };
            float previous = 0f;
            foreach (double divisor in divisors)
            {
                double cone = SightabilityGate.EffectiveConeDeg();
                float side = SextantConeBox.BoxUnits(cone, MainFov / divisor, ocular);
                Assert.True(side > previous, "the aiming window must grow with the zoom");
                previous = side;
            }
        }

        [Fact]
        public void BoxSide_DegenerateInputs_AreZero()
        {
            Assert.Equal(0f, SextantConeBox.BoxUnits(0.5, 0.0, 600f));
            Assert.Equal(0f, SextantConeBox.BoxUnits(0.0, 65.0, 600f));
        }
    }
}
