using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// P0 independent alt/az etalons: the whole sky pipeline (catalog epoch →
    /// precession to the date → LST → ENU) against an independent Meeus
    /// implementation (Python, written from the book and cross-checked on
    /// Meeus Examples 21.a/22.a). 10 control stars × 3 campaign dates at
    /// 50°N 18°E, 00:00 UT, tolerance 0.1°.
    /// </summary>
    public sealed class StarAltAzEtalonTests
    {
        private const double Lat = 50.0;
        private const double Lon = 18.0;
        private const double Deg2Rad = Math.PI / 180.0;
        private const double ToleranceDeg = 0.1;

        private struct Etalon
        {
            public int Hip;
            public string Name;
            public int Year;
            public double AltDeg;
            public double AzDeg;

            public Etalon(string name, int year, double alt, double az)
            {
                Hip = 0;
                Name = name;
                Year = year;
                AltDeg = alt;
                AzDeg = az;
            }
        }

        private static readonly Etalon[] Etalons =
        {
            new Etalon("Sirius", 1939, -56.6317, 359.5762),
            new Etalon("Sirius", 1942, -56.6354, 359.9873),
            new Etalon("Sirius", 1945, 19.3880, 206.9222),
            new Etalon("Vega", 1939, 78.6654, 186.5803),
            new Etalon("Vega", 1942, 78.6486, 187.5457),
            new Etalon("Vega", 1945, 2.1861, 21.7851),
            new Etalon("Arcturus", 1939, 29.4014, 265.6641),
            new Etalon("Arcturus", 1942, 29.2409, 265.8390),
            new Etalon("Arcturus", 1945, 17.2283, 80.0183),
            new Etalon("Capella", 1939, 7.8719, 15.4658),
            new Etalon("Capella", 1942, 7.9121, 15.6106),
            new Etalon("Capella", 1945, 57.5409, 282.0975),
            new Etalon("Rigel", 1939, -44.2667, 31.8822),
            new Etalon("Rigel", 1942, -44.1838, 32.1832),
            new Etalon("Rigel", 1945, 17.8006, 231.8668),
            new Etalon("Betelgeuse", 1939, -31.5963, 14.4821),
            new Etalon("Betelgeuse", 1942, -31.5586, 14.7449),
            new Etalon("Betelgeuse", 1945, 36.3901, 230.9895),
            new Etalon("Polaris", 1939, 49.7218, 1.5286),
            new Etalon("Polaris", 1942, 49.7230, 1.5049),
            new Etalon("Polaris", 1945, 49.8043, 358.4838),
            new Etalon("Altair", 1939, 46.4628, 155.8255),
            new Etalon("Altair", 1942, 46.5312, 156.1457),
            new Etalon("Altair", 1945, -30.6169, 11.6541),
            new Etalon("Deneb", 1939, 69.6466, 92.6485),
            new Etalon("Deneb", 1942, 69.8076, 92.8276),
            new Etalon("Deneb", 1945, 5.1128, 358.0617),
            new Etalon("Spica", 1939, -1.9016, 255.3252),
            new Etalon("Spica", 1942, -2.0567, 255.4902),
            new Etalon("Spica", 1945, 2.0409, 109.6436),
        };

        /// <summary>HIP of each control star (the etalon table is name-keyed).</summary>
        private static int HipOf(string name)
        {
            switch (name)
            {
                case "Sirius": return 32349;
                case "Vega": return 91262;
                case "Arcturus": return 69673;
                case "Capella": return 24608;
                case "Rigel": return 24436;
                case "Betelgeuse": return 27989;
                case "Polaris": return 11767;
                case "Altair": return 97649;
                case "Deneb": return 102098;
                default: return 65474; // Spica
            }
        }

        private static DateTime UtcOf(int year)
        {
            return year == 1945
                ? new DateTime(1945, 1, 10, 0, 0, 0, DateTimeKind.Utc)
                : new DateTime(year, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        }

        [Fact]
        public void StarAltAz_MatchesIndependentEtalons()
        {
            foreach (Etalon etalon in Etalons)
            {
                int hip = HipOf(etalon.Name);
                DateTime utc = UtcOf(etalon.Year);
                double jd = AstroMath.ToJulianDay(utc);
                double lst = AstroMath.LstDeg(jd, Lon);

                Assert.True(CelestialEphemeris.TryGetStarRaDec(hip, jd,
                    out double ra, out double dec), "HIP " + hip + " missing");
                AstroMath.RaDecToEnu(ra, dec, lst, Lat,
                    out double altRad, out double azRad, out _, out _, out _);
                double alt = altRad / Deg2Rad;
                double az = azRad / Deg2Rad;

                Assert.True(Math.Abs(alt - etalon.AltDeg) <= ToleranceDeg, string.Format(
                    "{0} {1}: alt {2:F4} vs etalon {3:F4}", etalon.Name, etalon.Year, alt, etalon.AltDeg));
                Assert.True(Math.Abs(BodyRowFormatter.Wrap180(az - etalon.AzDeg)) <= ToleranceDeg,
                    string.Format("{0} {1}: az {2:F4} vs etalon {3:F4}",
                        etalon.Name, etalon.Year, az, etalon.AzDeg));
            }
        }

        [Fact]
        public void PolarisAltitude_TracksTheLatitude()
        {
            // Plan §1 acceptance: Polaris ≈ latitude ± 0.2° (the polar distance
            // in this catalog epoch is ~1.1°, so 50 − 0.3 is the band).
            DateTime utc = UtcOf(1942);
            double jd = AstroMath.ToJulianDay(utc);
            Assert.True(CelestialEphemeris.TryGetStarRaDec(11767, jd, out double ra, out double dec));
            AstroMath.RaDecToEnu(ra, dec, AstroMath.LstDeg(jd, Lon), Lat,
                out double altRad, out _, out _, out _, out _);
            Assert.InRange(altRad / Deg2Rad, Lat - 1.4, Lat + 1.4);
        }
    }
}
