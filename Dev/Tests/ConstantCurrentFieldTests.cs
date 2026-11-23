using System;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class ConstantCurrentFieldTests
    {
        [Fact]
        public void ConstantCurrentField_ZeroIsValidStillWater()
        {
            var field = new ConstantCurrentField(0, 0);
            var water = field.Sample(48.259, -15.742, 80, new DateTime(1941, 11, 5, 0, 0, 0, DateTimeKind.Utc));

            Assert.Equal(0f, water.EastMetersPerSecond);
            Assert.Equal(0f, water.NorthMetersPerSecond);
        }

        [Fact]
        public void ConstantCurrentField_PreservesSignedEastNorthAtDifferentPlacesDepthsAndDates()
        {
            var field = new ConstantCurrentField(-0.75f, 0.25f);

            var first = field.Sample(48.259, -15.742, 0, new DateTime(1941, 11, 5, 0, 0, 0, DateTimeKind.Utc));
            var second = field.Sample(36, -5.5, 80, new DateTime(1944, 6, 6, 0, 0, 0, DateTimeKind.Utc));

            Assert.Equal(-0.75f, first.EastMetersPerSecond);
            Assert.Equal(0.25f, first.NorthMetersPerSecond);
            Assert.Equal(first.EastMetersPerSecond, second.EastMetersPerSecond);
            Assert.Equal(first.NorthMetersPerSecond, second.NorthMetersPerSecond);
        }

        [Fact]
        public void ConstantCurrentField_RejectsAnInvalidAtlasQuery()
        {
            var field = new ConstantCurrentField(1, 0);
            var when = new DateTime(1941, 11, 5, 0, 0, 0, DateTimeKind.Utc);

            Assert.Throws<ArgumentException>(() => field.Sample(double.NaN, 0, 10, when));
            Assert.Throws<ArgumentException>(() => field.Sample(0, double.PositiveInfinity, 10, when));
            Assert.Throws<ArgumentException>(() => field.Sample(0, 0, double.NaN, when));
            Assert.Throws<ArgumentException>(() => field.Sample(0, 0, 10, default(DateTime)));
        }
    }
}
