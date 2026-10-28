using System;
using UBOAT.Mods.RadioNavigation;
using UnityEngine;
using Xunit;

namespace RadioNavigation.Tests
{
    public class CelestialDiscSizeTests
    {
        [Fact]
        public void SolarSemidiameterMatchesTheUsers1939To1945Almanac()
        {
            // [1] Nautical Almanac SH5 1939-1945.pdf: pp.32/54/57.
            // Rounded table SDs, NOT expectations derived from our constants.
            for (int year = 1939; year <= 1945; year++)
            {
                AssertSolarRadius(year, 1, 3, 16.3);
                AssertSolarRadius(year, 7, 3, 15.7);
                AssertSolarRadius(year, 9, 2, 15.9);
            }
        }

        private static void AssertSolarRadius(int year, int month, int day, double tableArcMin)
        {
            double jd = AstroMath.ToJulianDay(new DateTime(year, month, day, 12, 0, 0, DateTimeKind.Utc))
                + 24.325 / 86400.0;
            double radiusArcMin = SunDiscScale.AngularDiameterDeg(jd) * 30.0;
            Assert.InRange(radiusArcMin, tableArcMin - 0.06, tableArcMin + 0.06);
        }

        [Fact]
        public void SolarDiameterNeverRetainsTheOldMagnification()
        {
            double start = AstroMath.ToJulianDay(new DateTime(1939, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            for (int day = 0; day < 2557; day++)
                Assert.InRange(SunDiscScale.AngularDiameterDeg(start + day), 0.523, 0.543);
            Assert.Equal(0.0, SunDiscScale.AngularDiameterDeg(double.NaN));
            Assert.Equal(0.0, SunDiscScale.AngularDiameterDeg(double.PositiveInfinity));
        }

        [Fact]
        public void PhysicalRadiusAndRasterHalfCoverageAreTheSameAngle()
        {
            foreach (double diameter in new[] { 0.523, 0.53, 0.543, 0.49, 0.57 })
            {
                foreach (double distance in new[] { 50.0, 500.0, 3825.0, 20000.0 })
                {
                    double side = CelestialDiscRaster.QuadSide(distance, diameter);
                    double radius = side * 0.5 * CelestialDiscRaster.LimbRadius;
                    double actualAngle = 2.0 * Math.Atan(radius / distance) * 180.0 / Math.PI;
                    Assert.Equal(diameter, actualAngle, 10);
                    Assert.Equal(0.5, CelestialDiscRaster.Coverage(
                        CelestialDiscRaster.LimbRadius, 256), 12);
                }
            }
        }

        [Fact]
        public void LimbRampIsOneTexelNotSevenPercentOfTheRadius()
        {
            const int size = 256;
            double radius = CelestialDiscRaster.LimbRadius;
            Assert.Equal(1.0, CelestialDiscRaster.Coverage(radius - 1.0 / size, size));
            Assert.Equal(0.0, CelestialDiscRaster.Coverage(radius + 1.0 / size, size));
            // An independent angle budget: the entire AA ramp <0.15' radius.
            double inner = Math.Atan(Math.Tan(0.53 * Math.PI / 360.0)
                * (radius - 1.0 / size) / radius) * 180.0 / Math.PI * 60.0;
            Assert.InRange(15.9 - inner, 0.0, 0.15);
        }

        [Fact]
        public void NewSightsHaveOnlyIndexCorrectionNotAnObsoleteRenderOffset()
        {
            const double raw = 37 + 59.8 / 60.0;
            Assert.Equal(raw, SunDiscSighting.ApplyMeasuredChain(raw, 0.0), 12);
            Assert.Equal(raw + 2.0 / 60.0, SunDiscSighting.ApplyMeasuredChain(raw, 2.0), 12);
            Assert.Equal(raw - 3.0 / 60.0, SunDiscSighting.ApplyMeasuredChain(raw, -3.0), 12);
        }

        [Fact]
        public void NonSolarSightsCannotStoreASolarDiscOrLimb()
        {
            var disc = new SunDiscSighting { ObservedDiameterDeg = 2.153125, Limb = SunSightLimb.UpperLimb };
            foreach (CelestialBody body in new[] { CelestialBody.Moon(), CelestialBody.Star(12345, "Test"), CelestialBody.None() })
            {
                var snapshot = disc.ForBody(body);
                Assert.Equal(0.0, snapshot.ObservedDiameterDeg);
                Assert.Equal(SunSightLimb.LowerLimb, snapshot.Limb);
            }
            Assert.Equal(disc.ObservedDiameterDeg, disc.ForBody(CelestialBody.Sun()).ObservedDiameterDeg);
        }

        [Fact]
        public void DiameterSnapshotIsNotRecomputedWhenTheDateOrFilterChanges()
        {
            SunDiscSighting recorded = SunDiscSighting.TrueSize(0.543);
            SunDiscSighting later = SunDiscSighting.TrueSize(0.523);
            Assert.Equal(0.543, recorded.ObservedDiameterDeg);
            Assert.NotEqual(recorded.ObservedDiameterDeg, later.ObservedDiameterDeg);
        }

        [Fact]
        public void LunarDistanceIsTopocentricAndDiameterIsNotDoubled()
        {
            const double distance = 384400.0;
            Assert.Equal(distance - 6378.14, MoonDiscScale.ObserverDistanceKm(distance, Math.PI / 2), 8);
            Assert.Equal(distance + 6378.14, MoonDiscScale.ObserverDistanceKm(distance, -Math.PI / 2), 8);
            Assert.Equal(Math.Sqrt(distance * distance - 6378.14 * 6378.14),
                MoonDiscScale.ObserverDistanceKm(distance, 0.0), 8);
            double start = AstroMath.ToJulianDay(new DateTime(1939, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            for (int day = 0; day < 2557; day += 3)
            {
                foreach (double altitude in new[] { 0.0, Math.PI / 4, Math.PI / 2 })
                    Assert.InRange(MoonDiscScale.AngularDiameterDeg(start + day, altitude), 0.48, 0.58);
            }
        }

        [Theory]
        // SH5 Moon 1939-1945 every 4hours.pdf, p.31, 2 September at 08/12 GMT.
        // Printed HP columns, not values computed from our series. Against JPL
        // they run low by 0.020..0.104' (also with our series): allow one printed
        // 0.1' quantum + 0.01', not an unjustified nearest-rounding half quantum.
        [InlineData(1939, 8, 54.7)]
        [InlineData(1939, 12, 54.8)]
        [InlineData(1940, 8, 60.9)]
        [InlineData(1940, 12, 60.9)]
        [InlineData(1941, 8, 57.8)]
        [InlineData(1941, 12, 57.7)]
        [InlineData(1942, 8, 54.5)]
        [InlineData(1942, 12, 54.4)]
        [InlineData(1943, 8, 55.0)]
        [InlineData(1943, 12, 55.1)]
        [InlineData(1944, 8, 61.2)]
        [InlineData(1944, 12, 61.2)]
        [InlineData(1945, 8, 57.1)]
        [InlineData(1945, 12, 57.0)]
        public void LunarDistanceMatchesTheUsersCampaignParallaxTable(int year, int hour, double hpArcMin)
        {
            double jd = AstroMath.ToJulianDay(new DateTime(year, 9, 2, hour, 0, 0, DateTimeKind.Utc));
            Assert.InRange(AstroMath.MoonParallaxDeg(jd) * 60, hpArcMin - 0.11, hpArcMin + 0.11);
        }

        [Theory]
        // Skyfield 1.55 / DE421, apparent topocentric place, UT1, no refraction.
        // Literal expectations from retained jpl-comparison.json; WGS84 vs the
        // mod's spherical observer accounts for <0.006' diameter in the sweep.
        [InlineData(1939, 9, 10, 55.1666666666667, 5.6666666666667, 29.7564545647)]
        [InlineData(1940, 9, 10, 55.1666666666667, 5.6666666666667, 33.5451184726)]
        [InlineData(1941, 9, 10, 55.1666666666667, 5.6666666666667, 31.0955529197)]
        [InlineData(1942, 9, 10, 55.1666666666667, 5.6666666666667, 29.8747832044)]
        [InlineData(1943, 9, 10, 55.1666666666667, 5.6666666666667, 30.2110301982)]
        [InlineData(1944, 9, 10, 55.1666666666667, 5.6666666666667, 32.9624405423)]
        [InlineData(1945, 9, 10, 55.1666666666667, 5.6666666666667, 31.5484143568)]
        [InlineData(1943, 8, 22, -30, 60, 29.0352063961)]
        [InlineData(1944, 9, 22, -30, 60, 33.9442818965)]
        public void LunarDiameterMatchesIndependentJplCampaignReferences(
            int year, int month, int hour, double lat, double lon, double expectedArcMin)
        {
            double jd = AstroMath.ToJulianDay(new DateTime(year, month, 2, hour, 3, 7, DateTimeKind.Utc));
            double lst = AstroMath.LstDeg(jd, lon);
            Assert.True(AstroMath.MoonTopocentricRaDec(jd, lat, lst, out double ra, out double dec));
            AstroMath.RaDecToEnu(ra, dec, lst, lat, out double alt, out _, out _, out _, out _);
            Assert.InRange(MoonDiscScale.AngularDiameterDeg(jd, alt) * 60,
                expectedArcMin - 0.008, expectedArcMin + 0.008);
        }

        [Fact]
        public void LunarOpacityIsIndependentOfPhaseAndHidesTheDarkHemisphere()
        {
            const int size = 128;
            float[] coverage = MoonPhaseDisc.BuildCoverageField(size);
            foreach (double phase in new[] { 0.0, Math.PI / 2, Math.PI })
            {
                float[] lit = MoonPhaseDisc.BuildAlphaField(size, phase, 0.0);
                Assert.Equal(1f, coverage[size / 2 * size + size / 2 - 32]);
                for (int i = 0; i < lit.Length; i++)
                    Assert.InRange(lit[i], 0f, coverage[i]);
            }
            Assert.Equal(0f, coverage[0]);
        }

        [Fact]
        public void NativeRendererStateIsRestoredEvenOnAnException()
        {
            var sun = new Renderer { enabled = true };
            var moon = new Renderer { enabled = false };
            CelestialRenderState state = CelestialRenderState.Begin(sun, true, moon, true);
            try
            {
                Assert.False(sun.enabled);
                Assert.False(moon.enabled);
                throw new InvalidOperationException();
            }
            catch (InvalidOperationException) { }
            finally { state.Restore(); }
            Assert.True(sun.enabled);
            Assert.False(moon.enabled);
        }

        [Fact]
        public void PartialNativeSuppressionRollsBackBeforeTheCallerReceivesAToken()
        {
            var sun = new Renderer { enabled = true };
            var moon = new Renderer { enabled = true, ThrowOnDisable = true };
            Assert.Throws<InvalidOperationException>(() => CelestialRenderState.Begin(sun, true, moon, true));
            Assert.True(sun.enabled);
            Assert.True(moon.enabled);
        }

        [Fact]
        public void NoReplacementAndNestedRendersNeverInventEnablement()
        {
            var sun = new Renderer { enabled = true };
            var moon = new Renderer { enabled = true };
            CelestialRenderState untouched = CelestialRenderState.Begin(sun, false, moon, false);
            Assert.True(sun.enabled);
            untouched.Restore();
            CelestialRenderState outer = CelestialRenderState.Begin(sun, true, moon, true);
            CelestialRenderState inner = CelestialRenderState.Begin(sun, true, moon, true);
            inner.Restore();
            Assert.False(sun.enabled);
            Assert.False(moon.enabled);
            outer.Restore();
            Assert.True(sun.enabled);
            Assert.True(moon.enabled);
        }
    }
}
