using System;
using System.IO;
using UnityEngine;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public sealed class CurrentTidalFieldTests
    {
        private sealed class FakePrimaryField : ICurrentField
        {
            private readonly bool covered;
            private readonly Vector3 value;

            public FakePrimaryField(bool covered, Vector3 value)
            {
                this.covered = covered;
                this.value = value;
            }

            public bool HasFlow => covered && (value.x != 0f || value.z != 0f);

            public bool TrySample(CurrentQuery query, out Vector3 velocity)
            {
                velocity = covered ? value : Vector3.zero;
                return covered;
            }
        }

        private static float PeakSpeedAcrossTwoDays(CurrentTidalField field, double lat, double lon, float depth,
            DateTime startUtc, out Vector3 minVec, out Vector3 maxVec)
        {
            float maxSpeed = 0f;
            float maxDot = float.NegativeInfinity;
            float minDot = float.PositiveInfinity;
            minVec = Vector3.zero;
            maxVec = Vector3.zero;
            for (int h = 0; h < 48 * 4; h++)
            {
                var q = new CurrentQuery
                {
                    HasPosition = true,
                    Latitude = lat,
                    Longitude = lon,
                    DepthMeters = depth,
                    Utc = startUtc.AddMinutes(h * 15)
                };
                if (!field.TrySample(q, out Vector3 v))
                    continue;
                float spd = Mathf.Sqrt(v.x * v.x + v.z * v.z);
                if (spd > maxSpeed)
                    maxSpeed = spd;
                float proj = v.x + v.z;
                if (proj > maxDot) { maxDot = proj; maxVec = v; }
                if (proj < minDot) { minDot = proj; minVec = v; }
            }
            return maxSpeed;
        }

        [Fact]
        public void AlderneyRaceAndPentlandFirthReachTenToElevenKnotsAtSpringTideAndReverseOnEbb()
        {
            var field = CurrentTidalField.CreateDefault();
            var springWindow = new DateTime(1941, 11, 1, 0, 0, 0, DateTimeKind.Utc);

            float alderneySpringMax = 0f;
            float bestDayMax = 0f;
            float worstDayMax = float.MaxValue;
            for (int day = 0; day < 16; day++)
            {
                float dayPeak = PeakSpeedAcrossTwoDays(field, 49.70, -2.08, 0f,
                    springWindow.AddDays(day), out Vector3 minV, out Vector3 maxV);
                if (dayPeak > alderneySpringMax)
                    alderneySpringMax = dayPeak;
                if (dayPeak > bestDayMax)
                    bestDayMax = dayPeak;
                if (dayPeak < worstDayMax)
                    worstDayMax = dayPeak;
                Assert.True(minV.x * maxV.x + minV.z * maxV.z < -5f);
            }

            Assert.InRange(alderneySpringMax, 4.7f, 6.0f);
            Assert.InRange(worstDayMax / bestDayMax, 0.45f, 0.65f);

            float pentlandPeak = 0f;
            for (int day = 0; day < 16; day++)
            {
                float dayPeak = PeakSpeedAcrossTwoDays(field, 58.71, -3.12, 0f,
                    springWindow.AddDays(day), out _, out _);
                if (dayPeak > pentlandPeak)
                    pentlandPeak = dayPeak;
            }
            Assert.InRange(pentlandPeak, 4.5f, 5.6f);
        }

        [Fact]
        public void ShelfSeasCoverNorthSeaBiscayNorwayKolaAndRotateThroughFullEllipse()
        {
            var field = CurrentTidalField.CreateDefault();
            var start = new DateTime(1941, 11, 3, 0, 0, 0, DateTimeKind.Utc);

            // Southern North Sea (German Bight / Dogger): rotary ellipse ~1.1-1.7 kn (0.55 - 0.90 m/s)
            float northSeaPeak = PeakSpeedAcrossTwoDays(field, 53.80, 4.50, 0f, start, out _, out _);
            Assert.InRange(northSeaPeak, 0.50f, 1.00f);

            // Verify rotary ellipse does NOT hit zero slack along the minor axis (~90 deg turn after 3.1h)
            float minNorthSeaSpeed = float.MaxValue;
            for (int i = 0; i < 48; i++)
            {
                field.TrySample(new CurrentQuery { HasPosition = true, Latitude = 53.80, Longitude = 4.50, Utc = start.AddMinutes(i * 15) }, out Vector3 v);
                float spd = Mathf.Sqrt(v.x * v.x + v.z * v.z);
                if (spd < minNorthSeaSpeed)
                    minNorthSeaSpeed = spd;
            }
            Assert.True(minNorthSeaSpeed > 0.12f, "Rotary shelf tide rotates ~360 deg without dropping to zero");

            // Bay of Biscay French shelf (Lorient / La Pallice offshore ~0.8 - 1.2 kn)
            float biscayPeak = PeakSpeedAcrossTwoDays(field, 46.20, -2.80, 0f, start, out _, out _);
            Assert.InRange(biscayPeak, 0.40f, 0.85f);

            // Norwegian coast / Lofoten & Kola / Murmansk approaches
            float norwayPeak = PeakSpeedAcrossTwoDays(field, 66.80, 11.50, 0f, start, out _, out _);
            float kolaPeak = PeakSpeedAcrossTwoDays(field, 69.40, 32.50, 0f, start, out _, out _);
            Assert.InRange(norwayPeak, 0.45f, 0.85f);
            Assert.InRange(kolaPeak, 0.50f, 0.95f);

            // Nova Scotia / Fundy & US East Coast
            float novaScotiaPeak = PeakSpeedAcrossTwoDays(field, 44.50, -62.50, 0f, start, out _, out _);
            float usEastPeak = PeakSpeedAcrossTwoDays(field, 41.20, -68.50, 0f, start, out _, out _);
            Assert.InRange(novaScotiaPeak, 0.70f, 1.30f);
            Assert.InRange(usEastPeak, 0.55f, 1.10f);
        }

        [Fact]
        public void DoverAndGibraltarProduceRealisticChannelAndInflowSpeeds()
        {
            var field = CurrentTidalField.CreateDefault();
            var start = new DateTime(1941, 11, 3, 0, 0, 0, DateTimeKind.Utc);

            float doverPeak = PeakSpeedAcrossTwoDays(field, 51.03, 1.50, 0f, start, out _, out _);
            Assert.InRange(doverPeak, 1.4f, 2.6f);

            PeakSpeedAcrossTwoDays(field, 35.97, -5.55, 0f, start, out Vector3 gibMin, out Vector3 gibMax);
            Assert.InRange(gibMax.x, 1.6f, 2.3f);
            Assert.True(gibMin.x >= 0f, "Gibraltar upper inflow should not strongly reverse westward");
        }

        [Fact]
        public void DeepOpenOceanHasOnlyWeakBarotropicTideAndEmptyTimeReturnsFalse()
        {
            var field = CurrentTidalField.CreateDefault();
            var midAtlantic = new CurrentQuery
            {
                HasPosition = true,
                Latitude = 36.0,
                Longitude = -35.0,
                DepthMeters = 0f,
                Utc = new DateTime(1941, 11, 4, 12, 0, 0, DateTimeKind.Utc)
            };
            Assert.True(field.TrySample(midAtlantic, out Vector3 v));
            float spd = Mathf.Sqrt(v.x * v.x + v.z * v.z);
            Assert.InRange(spd, 0.005f, 0.05f);

            var noTime = new CurrentQuery
            {
                HasPosition = true,
                Latitude = 49.70,
                Longitude = -2.08,
                DepthMeters = 0f,
                Utc = default(DateTime)
            };
            Assert.False(field.TrySample(noTime, out _));
        }

        [Fact]
        public void TidalFlowAttenuatesWithDepthAndNormalizes360Longitudes()
        {
            var field = CurrentTidalField.CreateDefault();
            var start = new DateTime(1941, 11, 4, 0, 0, 0, DateTimeKind.Utc);
            float surface = PeakSpeedAcrossTwoDays(field, 49.70, -2.08, 0f, start, out _, out _);
            float deep = PeakSpeedAcrossTwoDays(field, 49.70, -2.08, 60f, start, out _, out _);
            Assert.True(deep < surface);
            Assert.InRange(deep / surface, 0.68f, 0.78f);

            var qNeg = new CurrentQuery { HasPosition = true, Latitude = 49.70, Longitude = -2.08, Utc = start };
            var q360 = new CurrentQuery { HasPosition = true, Latitude = 49.70, Longitude = 357.92, Utc = start };
            Assert.True(field.TrySample(qNeg, out Vector3 vNeg));
            Assert.True(field.TrySample(q360, out Vector3 v360));
            Assert.InRange(Math.Abs(vNeg.x - v360.x), 0f, 1e-4f);
            Assert.InRange(Math.Abs(vNeg.z - v360.z), 0f, 1e-4f);
        }

        [Fact]
        public void CompositeFieldOverlaysCoastalTidesEvenOverMaskedCoastalCells()
        {
            var maskedPrimary = new FakePrimaryField(false, Vector3.zero);
            var openPrimary = new FakePrimaryField(true, new Vector3(0.2f, 0f, -0.1f));
            var tidal = CurrentTidalField.CreateDefault();

            var coastalQuery = new CurrentQuery
            {
                HasPosition = true,
                Latitude = 49.70,
                Longitude = -2.08,
                DepthMeters = 5f,
                Utc = new DateTime(1941, 11, 4, 6, 0, 0, DateTimeKind.Utc)
            };

            var compMasked = new CompositeCurrentField(maskedPrimary, tidal);
            Assert.True(compMasked.TrySample(coastalQuery, out Vector3 coastalFlow));
            Assert.True(Mathf.Sqrt(coastalFlow.x * coastalFlow.x + coastalFlow.z * coastalFlow.z) > 0.5f);

            var openOceanQuery = new CurrentQuery
            {
                HasPosition = true,
                Latitude = 36.0,
                Longitude = -35.0,
                DepthMeters = 5f,
                Utc = new DateTime(1941, 11, 4, 6, 0, 0, DateTimeKind.Utc)
            };
            var compOpen = new CompositeCurrentField(openPrimary, tidal);
            Assert.True(compOpen.TrySample(openOceanQuery, out Vector3 openFlow));
            // Primary + tiny deep-ocean barotropic tide (~0.025 m/s)
            Assert.InRange(Math.Abs(openFlow.x - 0.2f), 0f, 0.04f);
            Assert.InRange(Math.Abs(openFlow.z + 0.1f), 0f, 0.04f);
        }

        [Fact]
        public void ShippedAtlasBuildsUniversalCoastalTidalMaskOutsideNamedBasins()
        {
            string atlasDir = null;
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "Source", "Data", "CurrentAtlas");
                if (Directory.Exists(candidate))
                {
                    atlasDir = candidate;
                    break;
                }
            }
            Assert.NotNull(atlasDir);
            Assert.True(CurrentAtlasField.TryLoad(atlasDir, out CurrentAtlasField atlas, out string err), err);

            CurrentTidalField field = CurrentTidalField.CreateWithAtlas(atlas);
            var start = new DateTime(1941, 11, 4, 0, 0, 0, DateTimeKind.Utc);

            // Moroccan/Atlantic coast (~32.5N, -9.5E) is outside named northern straits/basins,
            // so it must still receive coastal shelf tidal flow from the ORA-20C land mask.
            float coastalPeak = PeakSpeedAcrossTwoDays(field, 32.5, -9.5, 0f, start, out _, out _);
            Assert.InRange(coastalPeak, 0.12f, 0.45f);

            // Deep Mid-Atlantic (36.0N, -35.0E) remains abyssal barotropic only.
            float abyssPeak = PeakSpeedAcrossTwoDays(field, 36.0, -35.0, 0f, start, out _, out _);
            Assert.InRange(abyssPeak, 0.005f, 0.05f);
        }
    }
}
