using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class SolarSightAuditTests
    {
        [Theory]
        [InlineData(0.0)]
        [InlineData(-6.47)]
        [InlineData(10.0)]
        public void SolarAudit_ExplicitShiftMatchesProductionWithoutChangingCalibration(double shift)
        {
            DateTime utc = new DateTime(1939, 9, 2, 10, 3, 7, DateTimeKind.Utc);
            double old = SunEphemerisCalibration.DeltaMinutes;
            try
            {
                SunEphemerisCalibration.SetDelta(shift);
                var production = SunEphemeris.Compute(55.0, 5.75, utc);
                var audit = SolarSightAudit.Compute(utc, 55.0, 5.75, shift);
                Assert.Equal((double)production.AltDeg, audit.AltDeg, 5);
                Assert.Equal((double)production.AzDeg, audit.AzDeg, 4);
                Assert.Equal(SunEphemerisCalibration.JdUt(utc), audit.JdUt);
                Assert.Equal(SunEphemerisCalibration.JdTt(utc), audit.JdTt);
                SolarSightAudit.Compute(utc, 55.0, 5.75, 0.0);
                Assert.Equal(shift, SunEphemerisCalibration.DeltaMinutes);
                Assert.InRange(audit.GhaDeg, 0.0, 359.999999);
                Assert.InRange(audit.DiameterDeg * 30.0, 15.8, 16.0);
            }
            finally
            {
                SunEphemerisCalibration.SetDelta(old);
            }
        }

        [Fact]
        public void SolarAudit_RejectsAmbiguousTimeAndInvalidCoordinates()
        {
            DateTime utc = new DateTime(1940, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            Assert.Throws<ArgumentException>(() => SolarSightAudit.Compute(DateTime.SpecifyKind(utc, DateTimeKind.Unspecified), 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => SolarSightAudit.Compute(utc, double.NaN, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => SolarSightAudit.Compute(utc, 91, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => SolarSightAudit.Compute(utc, 0, 181, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => SolarSightAudit.Compute(utc, 0, 0, double.PositiveInfinity));
        }

        [Fact]
        public void SolarAudit_JsonIsCultureInvariantAndAbsentDirectionIsNull()
        {
            CultureInfo old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
                var sb = new StringBuilder();
                SolarSightAudit.AppendModel(sb, SolarSightAudit.Compute(new DateTime(1939, 9, 2, 12, 0, 0, DateTimeKind.Utc), 55, 5, 0));
                using (JsonDocument json = JsonDocument.Parse(sb.ToString()))
                    Assert.InRange(json.RootElement.GetProperty("ghaDeg").GetDouble(), 0, 360);
                foreach (double value in new[] { 0.0, double.NaN, double.PositiveInfinity })
                {
                    sb.Clear();
                    SolarSightAudit.AppendDirection(sb, value, 0, 0);
                    Assert.Equal("null", sb.ToString());
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = old;
            }
        }

        [Theory]
        [InlineData(0.0, 1.0, 0.0, 90.0, 0.0)]
        [InlineData(2.0, 0.0, 0.0, 0.0, 90.0)]
        [InlineData(0.0, 0.0, -3.0, 0.0, 180.0)]
        public void SolarAudit_UsesProvenEnuWorldAxes(double x, double y, double z, double alt, double az)
        {
            var sb = new StringBuilder();
            SolarSightAudit.AppendDirection(sb, x, y, z);
            using (JsonDocument json = JsonDocument.Parse(sb.ToString()))
            {
                Assert.Equal(alt, json.RootElement.GetProperty("altDeg").GetDouble(), 10);
                Assert.Equal(az, json.RootElement.GetProperty("azDeg").GetDouble(), 10);
            }
        }
    }
}
