using System.Collections.Generic;
using System.Globalization;
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
        public void StarCatalog_IsNav_MatchesCuratedNavStarNames()
        {
            // Defect fix: IsNav was generated as "V<2" (48) while the curated
            // navigation set (NavStarNames) is 57 — so 20 named stars (incl.
            // Polaris, HIP 11767) were excluded from the bodies readout and the
            // star-mesh highlight. The flag must now equal the curated set.
            var named = new System.Collections.Generic.HashSet<int>();
            foreach (var kv in NavStarNames.Names)
                named.Add(kv.Key);

            foreach (var s in StarCatalog.Stars)
            {
                if (s.IsNav)
                    Assert.True(named.Contains(s.Hip),
                        "IsNav star HIP " + s.Hip + " has no curated name");
            }

            foreach (int hip in named)
            {
                bool found = false;
                foreach (var s in StarCatalog.Stars)
                {
                    if (s.Hip == hip && s.IsNav)
                    {
                        found = true;
                        break;
                    }
                }
                Assert.True(found, "curated nav star HIP " + hip + " is not flagged IsNav");
            }
        }

        [Fact]
        public void StarCatalog_Polaris_IsNavigational()
        {
            bool found = false;
            foreach (var s in StarCatalog.Stars)
            {
                if (s.Hip == 11767)
                {
                    Assert.True(s.IsNav, "Polaris (HIP 11767) must be a navigation star");
                    found = true;
                }
            }
            Assert.True(found, "Polaris missing from catalog");
        }

        [Fact]
        public void StarCatalog_NoExactDuplicateRows()
        {
            // F226: shared HIPs are legitimate (multiple-system components with
            // distinct magnitudes), but two byte-identical rows would be a
            // generation defect. ConstellationLines indexes into this array, so
            // rows must never be removed — dedupe happens at the consumer.
            var seen = new HashSet<string>();
            foreach (var s in StarCatalog.Stars)
            {
                string key = s.Hip.ToString(CultureInfo.InvariantCulture) + "|"
                    + s.RaDeg.ToString("R", CultureInfo.InvariantCulture) + "|"
                    + s.DecDeg.ToString("R", CultureInfo.InvariantCulture) + "|"
                    + s.Vmag.ToString("R", CultureInfo.InvariantCulture);
                Assert.True(seen.Add(key), "exact duplicate star row: " + key);
            }
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
