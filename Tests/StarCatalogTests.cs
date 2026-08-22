using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class StarCatalogTests
    {
        [Fact]
        public void StarCatalog_HasStars()
        {
            Assert.True(StarCatalog.Stars.Length > 0);
        }

        [Fact]
        public void StarCatalog_NavigationStars_Exist()
        {
            int navCount = 0;
            foreach (var s in StarCatalog.Stars)
                if (s.IsNav) navCount++;
            Assert.InRange(navCount, 40, 60); // ~48 expected (V<2)
        }

        [Fact]
        public void StarCatalog_CoordinatesInRange()
        {
            foreach (var s in StarCatalog.Stars)
            {
                Assert.InRange(s.RaDeg, 0f, 360f);
                Assert.InRange(s.DecDeg, -90f, 90f);
                Assert.InRange(s.Vmag, -2f, 7f);
                Assert.InRange(s.Bv, -0.5f, 6.0f); // extreme red dwarfs/giants can have B-V up to ~6
            }
        }

        [Fact]
        public void StarCatalog_Sirius_IsBrightest()
        {
            // Sirius HIP 32349 should be first (brightest, V=-1.46)
            var brightest = StarCatalog.Stars[0];
            Assert.Equal(32349, brightest.Hip);
            Assert.True(brightest.Vmag < -1.0f);
            Assert.True(brightest.IsNav);
        }

        [Fact]
        public void StarCatalog_Canopus_NotVisibleAt50N()
        {
            // Canopus HIP 30438 Dec~-52.7° — never visible at lat 50°N
            bool found = false;
            foreach (var s in StarCatalog.Stars)
            {
                if (s.Hip == 30438)
                {
                    Assert.True(s.DecDeg < -50f);
                    found = true;
                    break;
                }
            }
            Assert.True(found);
        }
    }
}
