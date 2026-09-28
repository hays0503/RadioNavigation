using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Pins the shared sky-condition model (limiting magnitude + Moon altitude)
    /// that the point-star mesh and the Sternspreiz streak field both use, so
    /// lines and points agree on which stars the night can show.
    /// </summary>
    public sealed class StarSkyConditionsTests
    {
        private static readonly DateTime NightUtc = new DateTime(1941, 11, 4, 22, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime NoonUtc = new DateTime(1941, 11, 4, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void NightShowsFainterStarsThanNoon()
        {
            double night = StarSkyConditions.LimitingMagnitude(NightUtc, 48f, -15.7f);
            double noon = StarSkyConditions.LimitingMagnitude(NoonUtc, 48f, -15.7f);

            Assert.False(double.IsNaN(night));
            Assert.False(double.IsNaN(noon));
            Assert.True(night > noon, "A dark sky must reach fainter magnitudes than daylight.");
            Assert.True(night > 3d, "A moonless night sky must show at least 3rd magnitude stars.");
        }

        [Fact]
        public void LimitingMagnitude_IsDeterministic()
        {
            double first = StarSkyConditions.LimitingMagnitude(NightUtc, 48f, -15.7f);
            double second = StarSkyConditions.LimitingMagnitude(NightUtc, 48f, -15.7f);
            Assert.Equal(first, second, 12);
        }

        [Fact]
        public void MoonAltitude_StaysInRange()
        {
            for (int hour = 0; hour < 24; hour += 3)
            {
                var utc = new DateTime(1941, 11, 4, hour, 0, 0, DateTimeKind.Utc);
                double alt = StarSkyConditions.MoonAltitudeDeg(utc, 48f, -15.7f);
                Assert.False(double.IsNaN(alt));
                Assert.InRange(alt, -90d, 90d);
            }
        }

        [Fact]
        public void SouthernObserver_GetsADifferentSkyThanNorthern()
        {
            double north = StarSkyConditions.LimitingMagnitude(NightUtc, 48f, -15.7f);
            double south = StarSkyConditions.LimitingMagnitude(NightUtc, -35f, -15.7f);
            Assert.NotEqual(north, south);
        }
    }
}
