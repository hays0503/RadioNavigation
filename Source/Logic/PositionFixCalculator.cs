using System;
using System.Collections.Generic;

namespace UBOAT.Mods.RadioNavigation
{
    public static class PositionFixCalculator
    {
        public struct Measurement
        {
            public int StationIndex;
            public float Latitude;
            public float Longitude;
            public float Bearing;
            public float Weight;
        }

        public struct FixResult
        {
            public float Latitude;
            public float Longitude;
            public float ErrorKm;
            public bool Valid;
        }

        public static FixResult Calculate(List<Measurement> measurements)
        {
            if (measurements.IsNull() || measurements.Count < 2)
                return new FixResult { Valid = false };

            var work = new List<Measurement>(measurements);

            var rawIntersections = new List<GeoPoint>();

            for (int i = 0; i < work.Count; i++)
            {
                for (int j = i + 1; j < work.Count; j++)
                {
                    var m1 = work[i];
                    var m2 = work[j];
                    var pt = FindIntersection(
                        m1.Latitude, m1.Longitude, m1.Bearing,
                        m2.Latitude, m2.Longitude, m2.Bearing);
                    if (pt.Valid)
                        rawIntersections.Add(pt);
                }
            }

            if (rawIntersections.Count.IsZero())
                return new FixResult { Valid = false };

            float roughLat = 0f;
            float roughLon = 0f;
            float roughX = 0f;
            float roughY = 0f;
            for (int i = 0; i < rawIntersections.Count; i++)
            {
                roughLat += rawIntersections[i].Latitude;
                double lonRad = GeoMath.ToRadians(rawIntersections[i].Longitude);
                roughX += (float)Math.Cos(lonRad);
                roughY += (float)Math.Sin(lonRad);
            }
            roughLat /= rawIntersections.Count;
            // F204: average longitudes as unit vectors so a fix straddling the
            // antimeridian (+179.5°/-179.5°) lands near ±180° instead of ~0°.
            roughLon = (float)(Math.Atan2(roughY, roughX) * (180.0 / Math.PI));

            for (int i = 0; i < work.Count; i++)
            {
                var m = work[i];
                float dist = GeoMath.HaversineKm(m.Latitude, m.Longitude, roughLat, roughLon);
                m.Weight = dist > 0.1f ? 1.0f / (dist / 100.0f) : 10f;
                // F64: struct copy must be written back via the indexer; a
                // future `foreach` (or an early `continue`) would drop the
                // Weight update silently.
                work[i] = m;
            }

            var weightedIntersections = new List<WeightedPoint>();

            for (int i = 0; i < work.Count; i++)
            {
                for (int j = i + 1; j < work.Count; j++)
                {
                    var m1 = work[i];
                    var m2 = work[j];
                    var pt = FindIntersection(
                        m1.Latitude, m1.Longitude, m1.Bearing,
                        m2.Latitude, m2.Longitude, m2.Bearing);
                    if (pt.Valid)
                    {
                        float pairWeight = (m1.Weight + m2.Weight) * 0.5f;
                        weightedIntersections.Add(new WeightedPoint
                        {
                            Latitude = pt.Latitude,
                            Longitude = pt.Longitude,
                            Weight = pairWeight
                        });
                    }
                }
            }

            if (weightedIntersections.Count.IsZero())
                return new FixResult { Valid = false };

            float totalLat = 0f;
            float totalW = 0f;
            float lonX = 0f;
            float lonY = 0f;

            for (int i = 0; i < weightedIntersections.Count; i++)
            {
                var wp = weightedIntersections[i];
                totalLat += wp.Latitude * wp.Weight;
                totalW += wp.Weight;
                double lonRad = GeoMath.ToRadians(wp.Longitude);
                lonX += (float)(Math.Cos(lonRad) * wp.Weight);
                lonY += (float)(Math.Sin(lonRad) * wp.Weight);
            }

            if (totalW < 1e-6f)
                return new FixResult { Valid = false };

            float calcLat = totalLat / totalW;
            // F204: circular mean (see roughLon above); also keeps the error
            // distance computed from calcLon meaningful near the dateline.
            float calcLon = (float)(Math.Atan2(lonY, lonX) * (180.0 / Math.PI));

            double sqErr = 0.0;
            double errW = 0.0;
            for (int i = 0; i < weightedIntersections.Count; i++)
            {
                var wp = weightedIntersections[i];
                double d = GeoMath.HaversineKm(calcLat, calcLon, wp.Latitude, wp.Longitude);
                sqErr += d * d * wp.Weight;
                errW += wp.Weight;
            }
            float errorKm = errW > 0.0 ? (float)Math.Sqrt(sqErr / errW) : 0f;

            return new FixResult
            {
                Latitude = calcLat,
                Longitude = calcLon,
                ErrorKm = errorKm,
                Valid = true
            };
        }

        private struct GeoPoint
        {
            public float Latitude;
            public float Longitude;
            public bool Valid;
        }

        private struct WeightedPoint
        {
            public float Latitude;
            public float Longitude;
            public float Weight;
        }

        private static GeoPoint FindIntersection(
            float lat1, float lon1, float brng1,
            float lat2, float lon2, float brng2)
        {
            double phi1 = GeoMath.ToRadians(lat1);
            double lam01 = GeoMath.ToRadians(lon1);
            double phi2 = GeoMath.ToRadians(lat2);
            double lam02 = GeoMath.ToRadians(lon2);
            double brng13 = GeoMath.ToRadians(brng1);
            double brng23 = GeoMath.ToRadians(brng2);

            double deltaPhi = phi2 - phi1;
            double deltaLam = lam02 - lam01;
            while (deltaLam > Math.PI) deltaLam -= 2.0 * Math.PI;
            while (deltaLam < -Math.PI) deltaLam += 2.0 * Math.PI;

            // F190: float->double accumulation can push the Haversine sum
            // above 1.0 (e.g. 1.0000000000000002); Math.Asin(NaN) would poison
            // every downstream bearing/fix computation.
            double havA = Math.Sin(deltaPhi / 2.0) * Math.Sin(deltaPhi / 2.0) +
                          Math.Cos(phi1) * Math.Cos(phi2) *
                          Math.Sin(deltaLam / 2.0) * Math.Sin(deltaLam / 2.0);
            havA = Math.Max(0.0, Math.Min(1.0, havA));
            double dist12 = 2.0 * Math.Asin(Math.Sqrt(havA));

            if (Math.Abs(dist12) < 1e-9)
                return new GeoPoint { Latitude = lat1, Longitude = lon1, Valid = true };

            double cosBrngA = (Math.Sin(phi2) - Math.Sin(phi1) * Math.Cos(dist12)) /
                              (Math.Sin(dist12) * Math.Cos(phi1));
            cosBrngA = Math.Max(-1.0, Math.Min(1.0, cosBrngA));
            double brngA = Math.Acos(cosBrngA);
            if (Math.Sin(deltaLam).IsNegative())
                brngA = 2.0 * Math.PI - brngA;

            double cosBrngB = (Math.Sin(phi1) - Math.Sin(phi2) * Math.Cos(dist12)) /
                              (Math.Sin(dist12) * Math.Cos(phi2));
            cosBrngB = Math.Max(-1.0, Math.Min(1.0, cosBrngB));
            double brngB = Math.Acos(cosBrngB);
            if (Math.Sin(deltaLam).IsPositive())
                brngB = 2.0 * Math.PI - brngB;

            double alpha1 = brng13 - brngA;
            double alpha2 = brngB - brng23;

            if (Math.Sin(alpha1).IsZero() && Math.Sin(alpha2).IsZero())
                return new GeoPoint { Valid = false };
            if ((Math.Sin(alpha1) * Math.Sin(alpha2)).IsNegative())
                return new GeoPoint { Valid = false };

            double cosAlpha3 = -Math.Cos(alpha1) * Math.Cos(alpha2) +
                               Math.Sin(alpha1) * Math.Sin(alpha2) * Math.Cos(dist12);
            double alpha3 = Math.Acos(Math.Max(-1.0, Math.Min(1.0, cosAlpha3)));

            double dist13 = Math.Atan2(
                Math.Sin(dist12) * Math.Sin(alpha1) * Math.Sin(alpha2),
                Math.Cos(alpha2) + Math.Cos(alpha1) * Math.Cos(alpha3));

            // F191: the spherical sine law argument can exceed [-1, 1] by
            // rounding; Math.Asin would return NaN for the fix latitude.
            double sinPhi3 = Math.Sin(phi1) * Math.Cos(dist13) +
                             Math.Cos(phi1) * Math.Sin(dist13) * Math.Cos(brng13);
            double phi3 = Math.Asin(Math.Max(-1.0, Math.Min(1.0, sinPhi3)));

            double deltaLam13 = Math.Atan2(
                Math.Sin(brng13) * Math.Sin(dist13) * Math.Cos(phi1),
                Math.Cos(dist13) - Math.Sin(phi1) * Math.Sin(phi3));

            double lam3 = lam01 + deltaLam13;

            float lat3 = (float)(phi3 * (180.0 / Math.PI));
            float lon3 = (float)((lam3 * (180.0 / Math.PI) + 540.0) % 360.0 - 180.0);

            return new GeoPoint { Latitude = lat3, Longitude = lon3, Valid = true };
        }
    }
}
