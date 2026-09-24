using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// P1b (CELESTIAL_REALISM_PLAN): the generated StarCatalog's effective
    /// epoch is 1926.0, not the 1942.0 its header claims — the converter
    /// applied a 74-year shift from J2000. Runtime precession must start from
    /// CatalogEpochJd. Etalons below are independent: J2000 Hipparcos
    /// positions + proper motion precessed with a separate implementation
    /// (Python/Meeus 21.2-21.4); the catalog chain reproduces them to ≤0.002°.
    /// </summary>
    public sealed class StarPrecessionTests
    {
        private const double Deg2Rad = Math.PI / 180.0;

        private struct Etalon
        {
            public int Hip;
            public string Name;
            public double Ra1939, Dec1939;
            public double Ra1942, Dec1942;
            public double Ra1945, Dec1945;

            public Etalon(int hip, string name,
                double ra39, double dec39, double ra42, double dec42, double ra45, double dec45)
            {
                Hip = hip; Name = name;
                Ra1939 = ra39; Dec1939 = dec39;
                Ra1942 = ra42; Dec1942 = dec42;
                Ra1945 = ra45; Dec1945 = dec45;
            }
        }

        private static readonly Etalon[] Etalons =
        {
            new Etalon(32349, "Sirius",   100.6152, -16.6308, 100.6483, -16.6349, 100.6813, -16.6391),
            new Etalon(91262, "Vega",     278.7183,  38.7259, 278.7436,  38.7286, 278.7690,  38.7314),
            new Etalon(69673, "Arcturus", 213.2195,  19.4993, 213.2537,  19.4836, 213.2879,  19.4680),
            new Etalon(24608, "Capella",   78.0446,  45.9381,  78.1000,  45.9412,  78.1554,  45.9443),
            new Etalon(24436, "Rigel",     77.9012,  -8.2707,  77.9372,  -8.2672,  77.9733,  -8.2637),
            new Etalon(27989, "Betelgeuse",87.9670,   7.3973,  88.0076,   7.3979,  88.0482,   7.3985),
            new Etalon(11767, "Polaris",   25.4898,  88.9738,  25.9380,  88.9889,  26.3992,  89.0038),
            new Etalon(97649, "Altair",   296.9519,   8.7059, 296.9884,   8.7138, 297.0250,   8.7217),
            new Etalon(102098, "Deneb",   309.8379,  45.0615, 309.8635,  45.0722, 309.8890,  45.0830),
            new Etalon(65474, "Spica",    200.4940, -10.8435, 200.5335, -10.8592, 200.5730, -10.8748),
        };

        private static double JdOfYear(double year)
        {
            return 2451545.0 + (year - 2000.0) * 365.25;
        }

        private static double SeparationDeg(double ra1, double dec1, double ra2, double dec2)
        {
            double d1 = dec1 * Deg2Rad, d2 = dec2 * Deg2Rad;
            double da = (ra1 - ra2) * Deg2Rad;
            double c = Math.Sin(d1) * Math.Sin(d2) + Math.Cos(d1) * Math.Cos(d2) * Math.Cos(da);
            return Math.Acos(Math.Max(-1.0, Math.Min(1.0, c))) / Deg2Rad;
        }

        [Fact]
        public void Precess_MeeusExample21a_WithProperMotion()
        {
            // Meeus 2nd ed. Example 21.a: theta Persei, J2000 -> 2028 Nov 13.19.
            // Proper motion over 28.867 yr is applied by the test (the helper
            // precesses only): dRA = +0.03425 s/yr, dDec = -0.0895"/yr.
            double years = 28.867;
            double raDeg = 41.0499417 + (0.03425 * 15.0 / 3600.0) * years / Math.Cos(49.2284667 * Deg2Rad);
            double decDeg = 49.2284667 + (-0.0895 / 3600.0) * years;

            AstroMath.Precess(raDeg * Deg2Rad, decDeg * Deg2Rad, 2451545.0, 2462088.69,
                out double ra, out double dec);

            Assert.InRange(ra / Deg2Rad, 41.53721, 41.55721);
            Assert.InRange(dec / Deg2Rad, 49.33848, 49.35848);
        }

        [Fact]
        public void Precess_CatalogRows_MatchIndependentEtalons()
        {
            foreach (Etalon e in Etalons)
            {
                Assert.True(CelestialEphemeris.TryGetStarRaDec(e.Hip, out double rawRa, out double rawDec),
                    "HIP " + e.Hip + " missing from catalog");

                double rawRaDeg = rawRa / Deg2Rad;
                double rawDecDeg = rawDec / Deg2Rad;

                CheckChain(e, rawRaDeg, rawDecDeg, 1939.0, e.Ra1939, e.Dec1939);
                CheckChain(e, rawRaDeg, rawDecDeg, 1942.0, e.Ra1942, e.Dec1942);
                CheckChain(e, rawRaDeg, rawDecDeg, 1945.0, e.Ra1945, e.Dec1945);
            }
        }

        private static void CheckChain(Etalon e, double rawRaDeg, double rawDecDeg,
            double year, double expRa, double expDec)
        {
            CelestialEphemeris.PrecessCatalog(rawRaDeg, rawDecDeg, JdOfYear(year),
                out double ra, out double dec);
            double sep = SeparationDeg(ra / Deg2Rad, dec / Deg2Rad, expRa, expDec);
            Assert.True(sep <= 0.01, string.Format(
                "{0} {1:F0}: chain separation {2:F4} deg", e.Name, year, sep));
        }

        [Fact]
        public void Precess_CatalogEpoch_IsIdentity()
        {
            // Precessing from the measured catalog epoch must return the row
            // unchanged; if CatalogEpochJd were wrong this fails immediately.
            CelestialEphemeris.TryGetStarRaDec(32349, out double ra, out double dec);
            CelestialEphemeris.PrecessCatalog(ra / Deg2Rad, dec / Deg2Rad,
                CelestialEphemeris.CatalogEpochJd, out double ra2, out double dec2);
            Assert.Equal(ra, ra2, 9);
            Assert.Equal(dec, dec2, 9);
        }

        [Fact]
        public void Precess_Claimed1942Epoch_WouldBeOffByTensOfArcmin()
        {
            // Documents the defect this phase fixes: treating the catalog as
            // J1942.0 (its header's claim) leaves Sirius 0.17° = 10' off.
            CelestialEphemeris.TryGetStarRaDec(32349, out double ra, out double dec);
            double raDeg = ra / Deg2Rad, decDeg = dec / Deg2Rad;

            CelestialEphemeris.PrecessCatalog(raDeg, decDeg, JdOfYear(1942.0),
                out double correctRa, out double correctDec);
            AstroMath.Precess(raDeg * Deg2Rad, decDeg * Deg2Rad,
                2451545.0 + (1942.0 - 2000.0) * 365.25, JdOfYear(1942.0),
                out double wrongRa, out double wrongDec);

            double defect = SeparationDeg(correctRa / Deg2Rad, correctDec / Deg2Rad,
                wrongRa / Deg2Rad, wrongDec / Deg2Rad);
            Assert.InRange(defect, 0.10, 0.25);
        }

        [Fact]
        public void TryGetStarRaDec_JdDependent()
        {
            // Same star, different dates: 1926 vs 1945 must differ by ~0.21°
            // in RA (general precession in RA is ~46"/yr at the equator, less
            // the n·sinα·tanδ term for Sirius), not be identical.
            Assert.True(CelestialEphemeris.TryGetStarRaDec(32349,
                JdOfYear(1926.0), out double ra1926, out _));
            Assert.True(CelestialEphemeris.TryGetStarRaDec(32349,
                JdOfYear(1945.0), out double ra1945, out _));

            double delta = Math.Abs(ra1945 - ra1926) / Deg2Rad;
            Assert.InRange(delta, 0.18, 0.25);
        }
    }
}
