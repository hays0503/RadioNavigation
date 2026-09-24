using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>P7 aiming reticle: the crosshair GAP equals the identification
    /// cone (fixed 1.0° since 2026-11-04) — the diameter is the exact
    /// perspective projection tan(cone)/tan(pipFov/2) of the cone through the
    /// PIP FOV onto the live ocular; the arms are short (1.5 × the gap radius,
    /// floored) so the reticle stays compact at ×6.</summary>
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
        public void GapDiameter_MatchesTheConeThroughTheLiveOcular()
        {
            float ocular = LiveOcular();
            Assert.Equal(597.7f, ocular, 1);
            // tan(1°)/tan(32.5°)*597.7 = 16.38; ×6 = 110.03. The linear form
            // (18.39 / 110.35) must fail these ranges — that was the M2 bug.
            Assert.InRange(SextantConeBox.DiameterUnits(1.0, MainFov, ocular), 16.1f, 16.7f);
            Assert.InRange(SextantConeBox.DiameterUnits(1.0, MainFov / 6.0, ocular), 109.4f, 110.7f);
        }

        [Fact]
        public void GapRadius_IsHalfTheDiameter()
        {
            float ocular = LiveOcular();
            Assert.InRange(SextantConeBox.RadiusUnits(1.0, MainFov, ocular), 8.1f, 8.3f);
            Assert.InRange(SextantConeBox.RadiusUnits(1.0, MainFov / 6.0, ocular), 54.6f, 55.4f);
        }

        [Fact]
        public void GapDiameter_MapsBackToTheConeAngle()
        {
            float ocular = LiveOcular();
            float side = SextantConeBox.DiameterUnits(1.0, MainFov, ocular);
            double drawnDeg = Math.Atan(side / ocular
                * Math.Tan(MainFov * 0.5 * Math.PI / 180.0)) * 180.0 / Math.PI;
            Assert.InRange(drawnDeg, 0.99, 1.01);
        }

        [Fact]
        public void GapDiameter_GrowsWithZoom()
        {
            float ocular = LiveOcular();
            double[] divisors = { 1.0, 1.5, 2.0, 3.0, 4.5, 6.0 };
            float previous = 0f;
            foreach (double divisor in divisors)
            {
                double cone = SightabilityGate.EffectiveConeDeg();
                float side = SextantConeBox.DiameterUnits(cone, MainFov / divisor, ocular);
                Assert.True(side > previous, "the aiming gap must grow with the zoom");
                previous = side;
            }
        }

        [Fact]
        public void Arm_ReachesTheOcularEdge()
        {
            float ocular = LiveOcular();
            // ×1: 597.7/2 − 8.19 = 290.7; ×6: 597.7/2 − 55.01 = 243.8 — the arm
            // plus the gap is exactly half the ocular (the arms meet the rim).
            float arm1 = SextantConeBox.ArmLengthUnits(
                SextantConeBox.RadiusUnits(1.0, MainFov, ocular), ocular);
            float arm6 = SextantConeBox.ArmLengthUnits(
                SextantConeBox.RadiusUnits(1.0, MainFov / 6.0, ocular), ocular);
            Assert.InRange(arm1, 290.0f, 291.5f);
            Assert.InRange(arm6, 243.0f, 244.5f);
            Assert.Equal(ocular * 0.5f,
                arm1 + SextantConeBox.RadiusUnits(1.0, MainFov, ocular), 2);
        }

        [Fact]
        public void Arm_DegenerateGap_IsZero()
        {
            // A gap wider than the ocular must hide the arms, never invert them.
            Assert.Equal(0f, SextantConeBox.ArmLengthUnits(400f, 600f));
            Assert.Equal(0f, SextantConeBox.ArmLengthUnits(float.NaN, 600f));
            Assert.Equal(300f, SextantConeBox.ArmLengthUnits(0f, 600f));
        }

        [Fact]
        public void GapDiameter_DegenerateInputs_AreZero()
        {
            Assert.Equal(0f, SextantConeBox.DiameterUnits(0.5, 0.0, 600f));
            Assert.Equal(0f, SextantConeBox.DiameterUnits(0.0, 65.0, 600f));
        }
    }
}
