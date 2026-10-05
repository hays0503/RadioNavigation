using System;
using System.Collections.Generic;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>P5 night planning: twilight windows, moon crossings, best pair
    /// and the three-star series objective.</summary>
    public sealed class CelestialForecastTests
    {
        private static BodyRecommendation.Candidate Star(string name, double azDeg)
        {
            return new BodyRecommendation.Candidate
            {
                Name = name,
                AltDeg = 40.0,
                AzDeg = azDeg
            };
        }

        [Fact]
        public void EquinoxSunrise_AtTheEquator_IsAboutSixUtc()
        {
            // 1942-03-21 (equinox), lat 0, lon 0: the geometric sunrise is
            // ≈ 06:00 UT; ±20 min absorbs the ΔT and the low-precision series.
            DateTime from = new DateTime(1942, 3, 20, 20, 0, 0, DateTimeKind.Utc);
            Assert.True(CelestialForecast.TryFindSunCrossing(0.0, 0.0, from,
                90.0, true, 24.0, out DateTime sunrise));
            Assert.InRange(sunrise.TimeOfDay.TotalHours, 5.6, 6.4);
        }

        [Fact]
        public void TwilightOrdering_DawnsRunCivilThenNauticalThenAstronomical()
        {
            DateTime from = new DateTime(1942, 3, 20, 0, 0, 0, DateTimeKind.Utc);
            Assert.True(CelestialForecast.TryFindSunCrossing(50.0, 0.0, from,
                CelestialForecast.CivilZenithDeg, true, 24.0, out DateTime civil));
            Assert.True(CelestialForecast.TryFindSunCrossing(50.0, 0.0, from,
                CelestialForecast.NauticalZenithDeg, true, 24.0, out DateTime nautical));
            Assert.True(CelestialForecast.TryFindSunCrossing(50.0, 0.0, from,
                CelestialForecast.AstronomicalZenithDeg, true, 24.0, out DateTime astronomical));

            // At dawn the order is reversed: the astronomical window opens
            // first (the Sun is deepest), civil last.
            Assert.True(astronomical < nautical, "astronomical dawn comes first");
            Assert.True(nautical < civil, "civil dawn comes last");
        }

        [Fact]
        public void PolarDay_HasNoAstronomicalNight()
        {
            DateTime from = new DateTime(1942, 6, 20, 0, 0, 0, DateTimeKind.Utc);
            Assert.False(CelestialForecast.TryFindSunCrossing(80.0, 0.0, from,
                CelestialForecast.AstronomicalZenithDeg, true, 48.0, out _));
        }

        [Fact]
        public void MoonRisesOrSetsWithinADay()
        {
            DateTime from = new DateTime(1942, 6, 15, 0, 0, 0, DateTimeKind.Utc);
            bool rises = CelestialForecast.TryFindMoonCrossing(50.0, 0.0, from, true, 24.0,
                out DateTime rise);
            bool sets = CelestialForecast.TryFindMoonCrossing(50.0, 0.0, from, false, 24.0,
                out DateTime set);
            Assert.True(rises || sets, "the Moon must cross the horizon within a day at 50N");
            if (rises)
                Assert.InRange(rise, from, from.AddHours(24.0));
            if (sets)
                Assert.InRange(set, from, from.AddHours(24.0));
        }

        [Fact]
        public void BestPair_IsNearNinetyDegreesWithBothStarsUp()
        {
            DateTime from = new DateTime(1942, 6, 15, 22, 0, 0, DateTimeKind.Utc);
            Assert.True(CelestialForecast.TryFindBestPair(50.0, 0.0, from, 6.0, 10.0,
                out string nameA, out string nameB, out double separation, out DateTime when));

            Assert.False(string.IsNullOrEmpty(nameA));
            Assert.False(string.IsNullOrEmpty(nameB));
            Assert.NotEqual(nameA, nameB);
            Assert.InRange(separation, 30.0, 90.0);
            Assert.InRange(when, from, from.AddHours(6.0));
        }

        [Fact]
        public void SunZenith_IsNinetyAtTheHorizonCrossing()
        {
            DateTime from = new DateTime(1942, 3, 20, 20, 0, 0, DateTimeKind.Utc);
            Assert.True(CelestialForecast.TryFindSunCrossing(0.0, 0.0, from,
                90.0, true, 24.0, out DateTime sunrise));
            Assert.InRange(CelestialForecast.SunZenithDeg(0.0, 0.0, sunrise), 89.9, 90.1);
        }

        [Fact]
        public void WaitForCrossing_SunAzimuthChangesWithTime()
        {
            // Morning at 50N: the Sun swings east→south, so waiting widens the
            // crossing of two same-body sights.
            DateTime from = new DateTime(1942, 6, 15, 6, 0, 0, DateTimeKind.Utc);
            bool found = CelestialForecast.TryWaitMinutesForCrossing(
                CelestialBody.Sun(), 50.0, 0.0, from, 30.0, 6.0,
                out double minutes, out double cross);
            Assert.True(found, "the Sun's azimuth must change by 30° in six hours");
            Assert.InRange(minutes, 10.0, 360.0);
            Assert.InRange(cross, 30.0, 45.0);
        }

        [Fact]
        public void TryBodyAzimuth_RejectsBelowHorizon()
        {
            // Local midnight at 50N in June: the Sun is below the horizon.
            DateTime midnight = new DateTime(1942, 6, 15, 0, 0, 0, DateTimeKind.Utc);
            Assert.False(CelestialForecast.TryBodyAzimuth(
                CelestialBody.Sun(), 50.0, 0.0, midnight, out _));
        }

        [Fact]
        public void TripleObjective_MaxMinBeatsPerfectPair()
        {
            // The 0/60/120 triple spreads 60° three ways; the 0/90/170 triple
            // contains a near-perfect 90° pair but its third leg collapses to
            // 10°, so its smallest separation (10°) loses to the even spread.
            var bodies = new List<BodyRecommendation.Candidate>
            {
                Star("A", 0.0), Star("B", 60.0), Star("C", 120.0),
                Star("P", 90.0), Star("Q", 170.0)
            };
            double[] scratch = new double[bodies.Count * bodies.Count];

            Assert.True(CelestialForecast.TryBestTriple(bodies, scratch,
                out string a, out string b, out string c, out double minSep));

            Assert.Equal("A", a);
            Assert.Equal("B", b);
            Assert.Equal("C", c);
            Assert.Equal(60.0, minSep, 6);
        }

        [Fact]
        public void TripleObjective_AcceptBoundary()
        {
            Assert.True(CelestialForecast.IsTripleAcceptable(30.0));
            Assert.False(CelestialForecast.IsTripleAcceptable(29.999));
        }

        [Fact]
        public void TripleObjective_FewerThanThree_NotFound()
        {
            var bodies = new List<BodyRecommendation.Candidate>
            {
                Star("A", 0.0), Star("B", 90.0)
            };
            double[] scratch = new double[4];

            Assert.False(CelestialForecast.TryBestTriple(bodies, scratch,
                out _, out _, out _, out _));
        }

        [Fact]
        public void TripleObjective_Tie_PrefersFirstCatalogOrder()
        {
            // Two evenly spread triples share the 60° minimum: the strict `>`
            // keeps the first in catalog order, and a second call is identical.
            var bodies = new List<BodyRecommendation.Candidate>
            {
                Star("A", 0.0), Star("B", 60.0), Star("C", 120.0),
                Star("D", 10.0), Star("E", 70.0), Star("F", 130.0)
            };
            double[] scratch = new double[bodies.Count * bodies.Count];

            Assert.True(CelestialForecast.TryBestTriple(bodies, scratch,
                out string a1, out string b1, out string c1, out double min1));
            Assert.True(CelestialForecast.TryBestTriple(bodies, scratch,
                out string a2, out string b2, out string c2, out double min2));

            Assert.Equal("A", a1);
            Assert.Equal("B", b1);
            Assert.Equal("C", c1);
            Assert.Equal(a1, a2);
            Assert.Equal(b1, b2);
            Assert.Equal(c1, c2);
            Assert.Equal(min1, min2, 9);
        }

        [Fact]
        public void TripleSearch_50N_FindsTripleAtTheDarkStep()
        {
            DateTime from = new DateTime(1942, 6, 15, 22, 0, 0, DateTimeKind.Utc);
            Assert.True(CelestialForecast.TryFindBestTriple(50.0, 0.0, from, 6.0, 10.0,
                out CelestialForecast.TripleResult triple));

            Assert.True(triple.Found);
            Assert.False(string.IsNullOrEmpty(triple.NameA));
            Assert.False(string.IsNullOrEmpty(triple.NameB));
            Assert.False(string.IsNullOrEmpty(triple.NameC));
            Assert.NotEqual(triple.NameA, triple.NameB);
            Assert.NotEqual(triple.NameB, triple.NameC);
            Assert.NotEqual(triple.NameA, triple.NameC);
            Assert.True(triple.MinSeparationDeg >= CelestialForecast.TripleAcceptMinSepDeg);
            Assert.InRange(triple.WhenUtc, from, from.AddHours(6.0));
        }

        [Fact]
        public void TripleSearch_PolarDay_NoDarkStep_False()
        {
            // 80N in late June: the Sun never reaches the star-dark gate.
            DateTime from = new DateTime(1942, 6, 20, 12, 0, 0, DateTimeKind.Utc);
            Assert.False(CelestialForecast.TryFindBestTriple(80.0, 0.0, from, 24.0, 10.0,
                out CelestialForecast.TripleResult triple));
            Assert.False(triple.Found);
        }

        [Fact]
        public void TripleSearch_ZeroStepMinutes_FallsBackToTen()
        {
            DateTime from = new DateTime(1942, 6, 15, 22, 0, 0, DateTimeKind.Utc);
            Assert.True(CelestialForecast.TryFindBestTriple(50.0, 0.0, from, 6.0, 0.0,
                out CelestialForecast.TripleResult fallback));
            Assert.True(CelestialForecast.TryFindBestTriple(50.0, 0.0, from, 6.0, 10.0,
                out CelestialForecast.TripleResult tenMinutes));

            Assert.Equal(tenMinutes.NameA, fallback.NameA);
            Assert.Equal(tenMinutes.NameB, fallback.NameB);
            Assert.Equal(tenMinutes.NameC, fallback.NameC);
            Assert.Equal(tenMinutes.WhenUtc, fallback.WhenUtc);
        }

        [Fact]
        public void StarDarkZenith_Boundary()
        {
            Assert.True(CelestialForecast.IsStarDarkZenith(91.0));
            Assert.False(CelestialForecast.IsStarDarkZenith(90.99));
            Assert.False(CelestialForecast.IsStarDarkZenith(double.NaN));
            Assert.Equal(91.0, CelestialForecast.StarDarkZenithDeg, 9);
        }
    }
}
