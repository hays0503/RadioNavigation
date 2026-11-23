using System;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Semi-diurnal (M2 + S2) astronomical tidal current field spanning three
    /// tiers:
    /// 1) Narrow straits & races (Alderney Race, Pentland Firth, Dover, Ushant,
    ///    Gibraltar, Messina, Irish Sea channels, Great Belt, Biscay passages);
    /// 2) Named rotary continental-shelf seas (North Sea, Celtic Sea, Irish Sea,
    ///    Bay of Biscay shelf, Norwegian/Lofoten/Kola Arctic shelf, Hebrides,
    ///    Iceland, Iberia, Grand Banks, Nova Scotia/Fundy, US East Coast);
    /// 3) Universal coastal-shelf proximity field built from the ORA-20C land
    ///    mask plus weak abyssal barotropic tide in the deep open ocean.
    /// Zero per-query heap allocations, C# 7.0, pure time from CurrentQuery.Utc.
    /// </summary>
    internal sealed class CurrentTidalField : ICurrentField
    {
        private const double DegToRad = Math.PI / 180.0;
        private const double KmPerLatDeg = 111.195;
        private const double CutoffRatioSq = 2.25; // (1.5 R)^2 smooth support
        private const float M2Fraction = 0.76f;
        private const float S2Fraction = 0.24f;
        private const float CoastalShelfMaxMps = 0.35f; // ~0.68 kn along any coast
        private const float CoastalEllipticity = -0.45f;
        private const float AbyssalBarotropicMps = 0.025f; // ~0.05 kn deep ocean M2+S2
        private const float AbyssalEllipticity = -0.55f;
        private static readonly DateTime J2000Utc = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        private readonly TidalRegion[] regions;
        private readonly CoastProximityGrid coastMask;

        private CurrentTidalField(TidalRegion[] regions, CoastProximityGrid coastMask)
        {
            this.regions = regions ?? new TidalRegion[0];
            this.coastMask = coastMask;
        }

        public bool HasFlow => regions.Length > 0;

        internal static CurrentTidalField CreateDefault()
        {
            return new CurrentTidalField(BuildDefaultRegions(), null);
        }

        internal static CurrentTidalField CreateWithAtlas(CurrentAtlasField atlas)
        {
            CoastProximityGrid mask = atlas != null ? CoastProximityGrid.BuildFromAtlas(atlas) : null;
            return new CurrentTidalField(BuildDefaultRegions(), mask);
        }

        private static TidalRegion[] BuildDefaultRegions()
        {
            return new[]
            {
                // --- Tier 2: Broad rotary continental shelf seas (elliptical M2+S2) ---
                new TidalRegion("Southern North Sea & German Bight", 53.80, 4.50, 220.0, 160.0, 62.0, 310.0, 0.80f, -0.42f, 0.0f, 55f, 100f),
                new TidalRegion("Northern North Sea", 58.80, 2.20, 240.0, 170.0, 15.0, 285.0, 0.45f, -0.35f, 0.0f, 80f, 120f),
                new TidalRegion("Norwegian Coast & Lofoten", 66.80, 11.50, 480.0, 110.0, 28.0, 350.0, 0.65f, -0.30f, 0.0f, 80f, 120f),
                new TidalRegion("Kola / Murmansk Shelf", 69.40, 32.50, 260.0, 110.0, 105.0, 95.0, 0.75f, -0.30f, 0.0f, 75f, 120f),
                new TidalRegion("Celtic Sea & Western Approaches", 49.60, -7.20, 220.0, 160.0, 52.0, 130.0, 0.72f, -0.50f, 0.0f, 90f, 120f),
                new TidalRegion("Irish Sea Basin", 53.50, -4.80, 150.0, 85.0, 12.0, 210.0, 0.90f, -0.25f, 0.0f, 60f, 120f),
                new TidalRegion("Bay of Biscay French Shelf", 46.20, -2.80, 220.0, 95.0, 145.0, 100.0, 0.55f, -0.45f, 0.0f, 70f, 120f),
                new TidalRegion("Hebrides / NW Scotland Shelf", 57.20, -6.80, 180.0, 90.0, 25.0, 225.0, 0.85f, -0.35f, 0.0f, 75f, 120f),
                new TidalRegion("Icelandic Coastal Shelf", 64.60, -19.50, 320.0, 150.0, 80.0, 180.0, 0.60f, -0.40f, 0.0f, 75f, 120f),
                new TidalRegion("Iberian Atlantic Shelf", 39.80, -9.60, 360.0, 75.0, 5.0, 75.0, 0.35f, -0.35f, 0.0f, 70f, 120f),
                new TidalRegion("Nova Scotia / Fundy Approaches", 44.50, -62.50, 280.0, 150.0, 65.0, 250.0, 1.05f, -0.35f, 0.0f, 75f, 120f),
                new TidalRegion("Grand Banks & St. Lawrence", 46.00, -53.50, 360.0, 240.0, 55.0, 230.0, 0.45f, -0.45f, 0.0f, 80f, 120f),
                new TidalRegion("US East Coast Shelf", 41.20, -68.50, 380.0, 160.0, 40.0, 265.0, 0.85f, -0.42f, 0.0f, 75f, 120f),

                // --- Tier 1: Named straits, channels & races ---
                new TidalRegion("Central English Channel", 50.08, -2.50, 175.0, 45.0, 74.0, 155.0, 0.95f, -0.15f, 0.0f, 60f, 120f),
                new TidalRegion("Ushant / Fromveur", 48.45, -5.10, 28.0, 16.0, 26.0, 115.0, 2.00f, 0.0f, 0.0f, 60f, 120f),
                new TidalRegion("Alderney Race", 49.70, -2.08, 20.0, 12.0, 38.0, 165.0, 5.20f, 0.0f, 0.0f, 60f, 120f),
                new TidalRegion("Barfleur Narrows", 49.82, -1.15, 32.0, 18.0, 84.0, 185.0, 1.45f, 0.0f, 0.0f, 60f, 120f),
                new TidalRegion("Dover Strait", 51.03, 1.50, 48.0, 20.0, 42.0, 330.0, 1.75f, 0.0f, 0.0f, 60f, 120f),
                new TidalRegion("Pentland Firth", 58.71, -3.12, 22.0, 12.0, 105.0, 285.0, 5.20f, 0.0f, 0.0f, 60f, 120f),
                new TidalRegion("Orkney Approaches", 58.85, -2.65, 28.0, 18.0, 60.0, 295.0, 1.80f, 0.0f, 0.0f, 60f, 120f),
                new TidalRegion("North Channel", 55.12, -5.82, 42.0, 18.0, 335.0, 225.0, 2.05f, 0.0f, 0.0f, 60f, 120f),
                new TidalRegion("St George's Channel", 52.15, -5.60, 55.0, 24.0, 30.0, 165.0, 1.70f, 0.0f, 0.0f, 60f, 120f),
                new TidalRegion("Bristol Channel", 51.35, -4.00, 45.0, 20.0, 82.0, 175.0, 2.25f, 0.0f, 0.0f, 60f, 120f),
                new TidalRegion("Strait of Gibraltar", 35.97, -5.55, 36.0, 14.0, 86.0, 55.0, 0.95f, 0.0f, 1.05f, 60f, 110f),
                new TidalRegion("Strait of Messina", 38.23, 15.63, 16.0, 8.0, 20.0, 270.0, 2.40f, 0.0f, 0.0f, 60f, 120f),
                new TidalRegion("Great Belt", 55.65, 10.95, 38.0, 14.0, 5.0, 340.0, 1.25f, 0.0f, 0.0f, 45f, 90f),
                new TidalRegion("Pertuis d'Antioche", 46.08, -1.45, 26.0, 16.0, 115.0, 105.0, 0.90f, 0.0f, 0.0f, 50f, 100f)
            };
        }

        public bool TrySample(CurrentQuery query, out Vector3 velocity)
        {
            velocity = Vector3.zero;
            if (!query.HasPosition || query.Utc == default(DateTime))
                return false;
            if (double.IsNaN(query.Latitude) || double.IsNaN(query.Longitude))
                return false;

            double lon = NormalizeLon180(query.Longitude);
            double lat = query.Latitude;
            float depth = query.DepthMeters > 0f ? query.DepthMeters : 0f;
            ComputeAstronomicalAngles(query.Utc, out double phiM2Deg, out double phiS2Deg);

            float sumEast = 0f;
            float sumNorth = 0f;
            float maxRegionWeight = 0f;
            for (int i = 0; i < regions.Length; i++)
            {
                if (regions[i].TryEvaluate(lat, lon, depth, phiM2Deg, phiS2Deg,
                        out float e, out float n, out float weight))
                {
                    sumEast += e;
                    sumNorth += n;
                    if (weight > maxRegionWeight)
                        maxRegionWeight = weight;
                }
            }

            AddCoastalAndAbyssalBackground(lat, lon, depth, phiM2Deg, phiS2Deg,
                maxRegionWeight, ref sumEast, ref sumNorth);

            velocity = new Vector3(sumEast, 0f, sumNorth);
            return sumEast != 0f || sumNorth != 0f;
        }

        private void AddCoastalAndAbyssalBackground(double lat, double lon, float depth,
            double phiM2Deg, double phiS2Deg, float namedWeight, ref float sumEast, ref float sumNorth)
        {
            float unnamedShare = Mathf.Clamp01(1f - namedWeight);
            if (unnamedShare <= 0f)
                return;

            double lagDeg = NormalizeLon180(lon) * -4.0 + (lat - 45.0) * 6.0;
            EvaluateHarmonicPair(phiM2Deg - lagDeg, phiS2Deg - lagDeg, out float maj, out float min);
            float coastW = coastMask != null ? coastMask.Sample(lat, lon) : 0f;

            float depthTaper = Mathf.Lerp(1.0f, 0.72f, Mathf.Clamp01(depth / 80f));
            if (coastW > 0f)
            {
                float amp = CoastalShelfMaxMps * coastW * unnamedShare * depthTaper;
                sumEast += amp * maj;
                sumNorth += amp * CoastalEllipticity * min;
                return;
            }

            // Deep-ocean barotropic M2 + S2 tide (~0.05 kn everywhere in open ocean)
            float abyssAmp = AbyssalBarotropicMps * unnamedShare;
            sumEast += abyssAmp * maj;
            sumNorth += abyssAmp * AbyssalEllipticity * min;
        }

        internal static void EvaluateHarmonicPair(double angleM2Deg, double angleS2Deg,
            out float major, out float minor)
        {
            double m2R = angleM2Deg * DegToRad;
            double s2R = angleS2Deg * DegToRad;
            major = M2Fraction * (float)Math.Cos(m2R) + S2Fraction * (float)Math.Cos(s2R);
            minor = M2Fraction * (float)Math.Sin(m2R) + S2Fraction * (float)Math.Sin(s2R);
        }

        internal static void ComputeAstronomicalAngles(DateTime utc, out double phiM2Deg, out double phiS2Deg)
        {
            double daysSinceJ2000 = (utc.ToUniversalTime() - J2000Utc).TotalSeconds / 86400.0;
            double elongDeg = 297.8501921 + 12.19074912 * daysSinceJ2000;
            phiS2Deg = 720.0 * daysSinceJ2000;
            phiM2Deg = phiS2Deg - 2.0 * elongDeg;
        }

        private static double NormalizeLon180(double lon)
        {
            double normalized = lon;
            while (normalized > 180.0)
                normalized -= 360.0;
            while (normalized < -180.0)
                normalized += 360.0;
            return normalized;
        }

        private sealed class TidalRegion
        {
            internal readonly string Name;
            private readonly double latDeg;
            private readonly double lonDeg;
            private readonly double cosLat;
            private readonly double alongKm;
            private readonly double crossKm;
            private readonly double sinAxis;
            private readonly double cosAxis;
            private readonly double phaseLagDeg;
            private readonly float tidalMaxMps;
            private readonly float ellipticity;
            private readonly float steadyMps;
            private readonly float tidalDepthScaleM;
            private readonly float steadyDepthScaleM;
            private readonly double minLat;
            private readonly double maxLat;
            private readonly double minLon;
            private readonly double maxLon;

            internal TidalRegion(string name, double latDeg, double lonDeg, double alongKm, double crossKm,
                double axisAzimuthDeg, double phaseLagDeg, float tidalMaxMps, float ellipticity, float steadyMps,
                float tidalDepthScaleM, float steadyDepthScaleM)
            {
                Name = name;
                this.latDeg = latDeg;
                this.lonDeg = lonDeg;
                cosLat = Math.Max(0.15, Math.Cos(latDeg * DegToRad));
                this.alongKm = alongKm;
                this.crossKm = crossKm;
                double rad = axisAzimuthDeg * DegToRad;
                sinAxis = Math.Sin(rad);
                cosAxis = Math.Cos(rad);
                this.phaseLagDeg = phaseLagDeg;
                this.tidalMaxMps = tidalMaxMps;
                this.ellipticity = ellipticity;
                this.steadyMps = steadyMps;
                this.tidalDepthScaleM = tidalDepthScaleM;
                this.steadyDepthScaleM = steadyDepthScaleM;

                double maxRadiusKm = 1.5 * Math.Max(alongKm, crossKm);
                double latMargin = maxRadiusKm / KmPerLatDeg + 0.05;
                double lonMargin = maxRadiusKm / (KmPerLatDeg * cosLat) + 0.05;
                minLat = latDeg - latMargin;
                maxLat = latDeg + latMargin;
                minLon = lonDeg - lonMargin;
                maxLon = lonDeg + lonMargin;
            }

            internal bool TryEvaluate(double lat, double lon, float depthMeters, double phiM2Deg, double phiS2Deg,
                out float east, out float north, out float weight)
            {
                east = 0f;
                north = 0f;
                weight = 0f;
                if (lat < minLat || lat > maxLat || lon < minLon || lon > maxLon)
                    return false;

                double dNorthKm = (lat - latDeg) * KmPerLatDeg;
                double dEastKm = (lon - lonDeg) * KmPerLatDeg * cosLat;
                double uAlong = dEastKm * sinAxis + dNorthKm * cosAxis;
                double uCross = dEastKm * cosAxis - dNorthKm * sinAxis;
                double r2 = (uAlong * uAlong) / (alongKm * alongKm) + (uCross * uCross) / (crossKm * crossKm);
                if (r2 >= CutoffRatioSq)
                    return false;

                double edge = 1.0 - r2 / CutoffRatioSq;
                weight = (float)(Math.Exp(-1.8 * r2) * edge * edge);
                EvaluateHarmonicPair(phiM2Deg - phaseLagDeg, phiS2Deg - phaseLagDeg, out float maj, out float min);

                float tidalDepthFactor = Mathf.Lerp(1.0f, 0.72f, Mathf.Clamp01(depthMeters / Math.Max(1f, tidalDepthScaleM)));
                float steadyDepthFactor = Mathf.Lerp(1.0f, 0.15f, Mathf.Clamp01(depthMeters / Math.Max(1f, steadyDepthScaleM)));

                float alongSpeed = weight * (tidalMaxMps * maj * tidalDepthFactor + steadyMps * steadyDepthFactor);
                float crossSpeed = weight * (tidalMaxMps * ellipticity * min * tidalDepthFactor);

                east = alongSpeed * (float)sinAxis + crossSpeed * (float)cosAxis;
                north = alongSpeed * (float)cosAxis - crossSpeed * (float)sinAxis;
                return true;
            }
        }

        private sealed class CoastProximityGrid
        {
            private const double MinLat = 30.5;
            private const double MinLon = -64.5;
            private const int LatCount = 46;
            private const int LonCount = 91;
            private readonly float[] weights;

            private CoastProximityGrid(float[] weights)
            {
                this.weights = weights;
            }

            internal static CoastProximityGrid BuildFromAtlas(CurrentAtlasField atlas)
            {
                var isLand = new bool[LatCount * LonCount];
                for (int iy = 0; iy < LatCount; iy++)
                {
                    double lat = MinLat + iy;
                    for (int ix = 0; ix < LonCount; ix++)
                    {
                        double lon = MinLon + ix;
                        if (atlas.TryGetSurfaceLandMask(lat, lon, out bool land) && land)
                            isLand[iy * LonCount + ix] = true;
                    }
                }
                var coastWeights = new float[LatCount * LonCount];
                for (int iy = 0; iy < LatCount; iy++)
                {
                    for (int ix = 0; ix < LonCount; ix++)
                    {
                        if (!IsCoastlineCell(isLand, iy, ix))
                            continue;
                        StampProximity(coastWeights, iy, ix);
                    }
                }
                return new CoastProximityGrid(coastWeights);
            }

            private static bool IsCoastlineCell(bool[] isLand, int iy, int ix)
            {
                if (!isLand[iy * LonCount + ix])
                    return false;
                if (iy > 0 && !isLand[(iy - 1) * LonCount + ix]) return true;
                if (iy < LatCount - 1 && !isLand[(iy + 1) * LonCount + ix]) return true;
                if (ix > 0 && !isLand[iy * LonCount + ix - 1]) return true;
                return ix < LonCount - 1 && !isLand[iy * LonCount + ix + 1];
            }

            private static void StampProximity(float[] target, int cy, int cx)
            {
                for (int dy = -2; dy <= 2; dy++)
                {
                    int y = cy + dy;
                    if (y < 0 || y >= LatCount)
                        continue;
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int x = cx + dx;
                        if (x < 0 || x >= LonCount)
                            continue;
                        float d2 = dy * dy + dx * dx;
                        if (d2 > 4.5f)
                            continue;
                        float w = Mathf.Exp(-0.45f * d2);
                        int idx = y * LonCount + x;
                        if (w > target[idx])
                            target[idx] = w;
                    }
                }
            }

            internal float Sample(double lat, double lon)
            {
                double fy = lat - MinLat;
                double fx = lon - MinLon;
                if (fy < 0.0 || fy > LatCount - 1 || fx < 0.0 || fx > LonCount - 1)
                    return 0f;
                int y0 = Math.Min((int)fy, LatCount - 2);
                int x0 = Math.Min((int)fx, LonCount - 2);
                float ty = (float)(fy - y0);
                float tx = (float)(fx - x0);
                float c00 = weights[y0 * LonCount + x0];
                float c01 = weights[y0 * LonCount + x0 + 1];
                float c10 = weights[(y0 + 1) * LonCount + x0];
                float c11 = weights[(y0 + 1) * LonCount + x0 + 1];
                return Mathf.Lerp(Mathf.Lerp(c00, c01, tx), Mathf.Lerp(c10, c11, tx), ty);
            }
        }
    }

    /// <summary>
    /// Combines a background climatology/atlas field (e.g. ORA-20C) with an
    /// optional overlaid harmonic tidal layer. Missing atlas coverage near
    /// narrow straits still permits tidal flow cleanly.
    /// </summary>
    internal sealed class CompositeCurrentField : ICurrentField
    {
        private readonly ICurrentField primary;
        private readonly ICurrentField overlay;
        private readonly Func<double, double, bool> isLand;

        internal CompositeCurrentField(
            ICurrentField primary,
            ICurrentField overlay,
            Func<double, double, bool> isLand = null)
        {
            this.primary = primary;
            this.overlay = overlay;
            this.isLand = isLand;
        }

        public bool HasFlow => (primary?.HasFlow == true) || (overlay?.HasFlow == true);

        public bool TrySample(CurrentQuery query, out Vector3 velocity)
        {
            if (isLand != null && isLand(query.Latitude, query.Longitude))
            {
                velocity = Vector3.zero;
                return false;
            }
            Vector3 v1 = Vector3.zero;
            Vector3 v2 = Vector3.zero;
            bool ok1 = primary != null && primary.TrySample(query, out v1);
            bool ok2 = overlay != null && overlay.TrySample(query, out v2);
            if (!ok1 && !ok2)
            {
                velocity = Vector3.zero;
                return false;
            }
            velocity = new Vector3(v1.x + v2.x, v1.y + v2.y, v1.z + v2.z);
            return true;
        }
    }
}
