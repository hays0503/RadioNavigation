using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>Independent raw-DXBC fixture → production public API. The tests
    /// prove ideal shader geometry, not active mesh or presented water pixels.</summary>
    public sealed class SolarSeaHorizonTests
    {
        [Fact]
        public void Horizon_InstalledShaderBitsNotEditorCopyAreTheAuthority()
        {
            double[] drop = ReadInstalledDrop();
            Assert.Equal(81, drop.Length);
            Assert.Equal(0.004905038978904486, drop[1]);
            Assert.Equal(6.356928825378418, drop[36]);
            // At 14 m the tangent is beyond 9 km: every supported knot is the
            // finite far edge. This exposes EVERY used table value through the
            // actual horizon computation, not reflection of an unused array.
            for (int i = 1; i <= 36; i++)
            {
                double altitude;
                Assert.True(SolarSeaHorizon.TryAltitude(14, i * 250, true, out altitude));
                Assert.Equal(Math.Atan((-14 - drop[i]) / (i * 250)) * 180 / Math.PI,
                    altitude, 12);
            }
        }

        [Fact]
        public void Horizon_EnvelopeIncludesKnotsAndTheInterpolatedFiniteClip()
        {
            double[] drop = ReadInstalledDrop();
            // Covers fractional clips, transition from far edge to tangent,
            // zero eye, and both curvature states. Reference is independently
            // built from the external DXBC fixture, never from mod constants.
            foreach (double eye in new[] { 0, 0.1, 0.5, 1, 2, 3, 4, 5.5, 7, 10, 14 })
                foreach (double far in new[] { 250, 251, 375, 1499, 4001, 8125, 8250, 8623, 8999, 9000 })
                    foreach (bool curved in new[] { false, true })
                    {
                        double actual;
                        Assert.True(SolarSeaHorizon.TryAltitude(eye, far, curved, out actual));
                        int low = (int)(far / 250);
                        double fraction = far / 250 - low;
                        double endDrop = drop[low] + fraction * (drop[low + 1] - drop[low]);
                        double maximum = (-eye - (curved ? endDrop : 0)) / far;
                        for (int i = 1; i * 250 < far; i++)
                            maximum = Math.Max(maximum, (-eye - (curved ? drop[i] : 0)) / (i * 250));
                        Assert.Equal(Math.Atan(maximum) * 180 / Math.PI, actual, 12);
                    }
        }

        [Fact]
        public void Horizon_InstalledMeanSeaMovesTheDiscNotTheReducedHs()
        {
            string root = Directory.GetCurrentDirectory();
            Assert.True(Sh5SunAlmanac.Initialize(Path.Combine(root, "Source", "Data")), Sh5SunAlmanac.LastError);
            DateTime utc = new DateTime(1939, 9, 2, 10, 3, 7, DateTimeKind.Utc);
            double horizon, alt, az, lower, hc, trueAz, ho, correction;
            Assert.True(SolarSeaHorizon.TryAltitude(5.5, 9000, true, out horizon));
            Assert.True(Sh5SolarNavigation.TryCentre(utc, 55, 5.75, out hc, out trueAz));
            Assert.True(SolarHeightReduction.TryLowerLimb(hc, 5.5, 9, out lower));
            Assert.True(Sh5SolarNavigation.TryApparentCentre(utc, 55, 5.75, 5.5,
                horizon, 0.53, out alt, out az));
            Assert.Equal(horizon, alt - lower - 0.53 / 2, 12);
            Assert.Equal(trueAz, az);
            Assert.True(SolarHeightReduction.TryReduce(lower, 0, 5.5, 9,
                SunSightLimb.LowerLimb, out ho, out correction));
            Assert.Equal(hc, ho, 10);
            // No extra 2.6-minute correction in the reduction: only the render
            // centre changes when the independently sourced horizon changes.
            const double historicalEditorHorizon = -0.11827238574173227;
            double oldAlt, oldAz;
            Assert.True(Sh5SolarNavigation.TryApparentCentre(utc, 55, 5.75, 5.5,
                historicalEditorHorizon, 0.53, out oldAlt, out oldAz));
            Assert.Equal(2.578693908703302, (alt - oldAlt) * 60, 10);
        }

        [Fact]
        public void Horizon_FrozenOldModelIsDecodedWithoutBeingRecomputed()
        {
            string root = Directory.GetCurrentDirectory();
            Assert.True(Sh5SunAlmanac.Initialize(Path.Combine(root, "Source", "Data")), Sh5SunAlmanac.LastError);
            DateTime utc = new DateTime(1939, 9, 2, 10, 3, 7, DateTimeKind.Utc);
            var disc = new SunDiscSighting
            {
                TableRequested = true, TableCompatible = true,
                ConfiguredUtc = utc, ObserverLatDeg = 55, ObserverLonDeg = 5.75,
                ObservedDiameterDeg = 0.53, Limb = SunSightLimb.LowerLimb,
                EyeHeightM = 5.5, HorizonAltitudeDeg = -0.11827238574173227
            };
            SextantObservation original;
            Assert.True(Sh5SolarNavigation.TryObservation(utc, 39.4, 0.4, 55, 5.75, disc, out original));
            string payload = SextantSaveCodec.Encode(new SextantSaveSnapshot { Observation1 = original });
            SextantSaveSnapshot decoded;
            string error;
            Assert.True(SextantSaveCodec.TryDecode(payload, out decoded, out error), error);
            foreach (var field in typeof(SextantObservation).GetFields())
                Assert.Equal(field.GetValue(original), field.GetValue(decoded.Observation1));
            double current;
            Assert.True(SolarSeaHorizon.TryAltitude(5.5, 9000, true, out current));
            Assert.NotEqual(current, decoded.Observation1.SolarHorizonAltitudeDeg);
        }

        [Theory]
        [InlineData(double.NaN, 9000)]
        [InlineData(double.PositiveInfinity, 9000)]
        [InlineData(double.NegativeInfinity, 9000)]
        [InlineData(-0.01, 9000)]
        [InlineData(14.01, 9000)]
        [InlineData(5.5, double.NaN)]
        [InlineData(5.5, double.PositiveInfinity)]
        [InlineData(5.5, double.NegativeInfinity)]
        [InlineData(5.5, 249.99)]
        [InlineData(5.5, 9000.01)]
        public void Horizon_InvalidGeometryIsRefusedRatherThanExtrapolated(double eye, double far)
        {
            double altitude;
            Assert.False(SolarSeaHorizon.TryAltitude(eye, far, true, out altitude));
            Assert.Equal(0, altitude);
        }

        private static double[] ReadInstalledDrop()
        {
            using (var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                Directory.GetCurrentDirectory(), "Tools", "solar-sight", "water-curvature-verified.json"))))
            {
                Assert.Equal("ce26da620ea3f4a377bb47538192640743ab79d32be2ffc7b5c198620c8ccc9c",
                    fixture.RootElement.GetProperty("bundle_sha256").GetString());
                Assert.Equal(250, fixture.RootElement.GetProperty("step_m").GetInt32());
                Assert.Equal(79, fixture.RootElement.GetProperty("max_index").GetInt32());
                var bits = fixture.RootElement.GetProperty("signed_x_bits").EnumerateArray()
                    .Select(value => uint.Parse(value.GetString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture))
                    .ToArray();
                byte[] bytes = bits.SelectMany(value => new[] { (byte)(value >> 24),
                    (byte)(value >> 16), (byte)(value >> 8), (byte)value }).ToArray();
                using (var sha = SHA256.Create())
                    Assert.Equal("121655da0afa2d37dcb185ba99492c1935f22ab83f0093d05db69f20594fe23c",
                        Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant());
                return bits.Select(value => -(double)BitConverter.ToSingle(BitConverter.GetBytes(value), 0)).ToArray();
            }
        }
    }
}
