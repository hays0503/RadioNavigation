using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    // SEXTANT_PLAN §8: gaussian peak/falloff, filter transmission, night zero, clamp01.
    public class SextantGlareTests
    {
        private const double PipFov = 20.0;

        [Fact]
        public void Peak_AtZero_EqualsSunIntensity()
        {
            // sunAltitude 30° → intensity 0.5, no filters → glare = 0.5
            Assert.Equal(0.5, SextantGlare.Glare(0.0, 30.0, 0, PipFov), 6);
        }

        [Fact]
        public void Falloff_TwoSigma_BelowTenPercent()
        {
            // σ = 20/4 = 5°, at 2σ: exp(-4) ≈ 0.018 < 0.1
            double g = SextantGlare.Glare(10.0, 60.0, 0, PipFov);
            Assert.True(g < 0.1, "glare@2sigma=" + g);
        }

        [Fact]
        public void Filters_OneToThree_AttenuateMonotonically()
        {
            double g0 = SextantGlare.Glare(3.0, 40.0, 0, PipFov);
            double g1 = SextantGlare.Glare(3.0, 40.0, 1, PipFov);
            double g2 = SextantGlare.Glare(3.0, 40.0, 2, PipFov);
            double g3 = SextantGlare.Glare(3.0, 40.0, 3, PipFov);
            Assert.True(g0 > g1 && g1 > g2 && g2 > g3, g0 + "/" + g1 + "/" + g2 + "/" + g3);
            Assert.Equal(g0 * 0.35, g1, 6);
        }

        [Fact]
        public void Night_SunBelowHorizon_Zero()
        {
            Assert.Equal(0.0, SextantGlare.Glare(0.0, -5.0, 0, PipFov));
        }

        [Fact]
        public void Intensity_ClampedToOne()
        {
            // sunAltitude 120° would give intensity 2.0 — must clamp to 1.0
            Assert.Equal(1.0, SextantGlare.Glare(0.0, 120.0, 0, PipFov), 6);
        }
    }
}
