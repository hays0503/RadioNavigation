using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class SextantOpticsProfileTests
    {
        [Fact]
        public void GrimeIsAbsentInSightCentreAndFadesBeforeApertureRim()
        {
            Assert.Equal(0f, SextantOpticsProfile.GrimeCoverage(0f));
            Assert.Equal(0f, SextantOpticsProfile.GrimeCoverage(0.34f));
            Assert.True(SextantOpticsProfile.GrimeCoverage(0.60f) > 0f);
            Assert.Equal(0f, SextantOpticsProfile.GrimeCoverage(0.91f));
            Assert.Equal(0f, SextantOpticsProfile.GrimeCoverage(float.NaN));
        }

        [Fact]
        public void GrimeNoiseIsDeterministicAndBounded()
        {
            float a = SextantOpticsProfile.Hash01(91, 37, 191);
            Assert.Equal(a, SextantOpticsProfile.Hash01(91, 37, 191));
            Assert.InRange(a, 0f, 1f);
            Assert.NotEqual(a, SextantOpticsProfile.Hash01(92, 37, 191));
        }

        [Fact]
        public void MotesDriftSlowlyInsideTheOcular()
        {
            for (int i = 0; i < SextantOpticsProfile.MoteCount; i++)
            {
                float x, y;
                SextantOpticsProfile.MotePosition(i, 12f, out x, out y);
                Assert.InRange(x * x + y * y, 0.14f, 0.66f);
            }
        }
    }
}
