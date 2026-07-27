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

            var rawIntersections = new List<GeoPoint>();

            for (int i = 0; i < measurements.Count; i++)
            {
                for (int j = i + 1; j < measurements.Count; j++)
                {
                    var m1 = measurements[i];
                    var m2 = measurements[j];
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
            for (int i = 0; i < rawIntersections.Count; i++)
            {
                roughLat += rawIntersections[i].Latitude;
                roughLon += rawIntersections[i].Longitude;
            }
            roughLat /= rawIntersections.Count;
            roughLon /= rawIntersections.Count;

            for (int i = 0; i < measurements.Count; i++)
            {
                var m = measurements[i];
                float dist = GetDistanceKm(m.Latitude, m.Longitude, roughLat, roughLon);
                m.Weight = dist > 0.1f ? 1.0f / (dist / 100.0f) : 10f;
                measurements[i] = m;
            }

            var weightedIntersections = new List<WeightedPoint>();

            for (int i = 0; i < measurements.Count; i++)
            {
                for (int j = i + 1; j < measurements.Count; j++)
                {
                    var m1 = measurements[i];
                    var m2 = measurements[j];
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
            float totalLon = 0f;
            float totalW = 0f;

            for (int i = 0; i < weightedIntersections.Count; i++)
            {
                var wp = weightedIntersections[i];
                totalLat += wp.Latitude * wp.Weight;
                totalLon += wp.Longitude * wp.Weight;
                totalW += wp.Weight;
            }

            if (totalW < 1e-6f)
                return new FixResult { Valid = false };

            float calcLat = totalLat / totalW;
            float calcLon = totalLon / totalW;

            return new FixResult
            {
                Latitude = calcLat,
                Longitude = calcLon,
                ErrorKm = 0f,
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
            double phi1 = ToRadians(lat1);
            double lam01 = ToRadians(lon1);
            double phi2 = ToRadians(lat2);
            double lam02 = ToRadians(lon2);
            double brng13 = ToRadians(brng1);
            double brng23 = ToRadians(brng2);

            double deltaPhi = phi2 - phi1;
            double deltaLam = lam02 - lam01;

            double dist12 = 2.0 * Math.Asin(Math.Sqrt(
                Math.Sin(deltaPhi / 2.0) * Math.Sin(deltaPhi / 2.0) +
                Math.Cos(phi1) * Math.Cos(phi2) *
                Math.Sin(deltaLam / 2.0) * Math.Sin(deltaLam / 2.0)));

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

            double alpha3 = Math.Acos(
                -Math.Cos(alpha1) * Math.Cos(alpha2) +
                Math.Sin(alpha1) * Math.Sin(alpha2) * Math.Cos(dist12));

            double dist13 = Math.Atan2(
                Math.Sin(dist12) * Math.Sin(alpha1) * Math.Sin(alpha2),
                Math.Cos(alpha2) + Math.Cos(alpha1) * Math.Cos(alpha3));

            double phi3 = Math.Asin(
                Math.Sin(phi1) * Math.Cos(dist13) +
                Math.Cos(phi1) * Math.Sin(dist13) * Math.Cos(brng13));

            double deltaLam13 = Math.Atan2(
                Math.Sin(brng13) * Math.Sin(dist13) * Math.Cos(phi1),
                Math.Cos(dist13) - Math.Sin(phi1) * Math.Sin(phi3));

            double lam3 = lam01 + deltaLam13;

            float lat3 = (float)(phi3 * (180.0 / Math.PI));
            float lon3 = (float)((lam3 * (180.0 / Math.PI) + 540.0) % 360.0 - 180.0);

            return new GeoPoint { Latitude = lat3, Longitude = lon3, Valid = true };
        }

        public static float GetDistanceKm(float lat1, float lon1, float lat2, float lon2)
        {
            const double R = 6371.0;
            double phi1 = ToRadians(lat1);
            double phi2 = ToRadians(lat2);
            double deltaPhi = ToRadians(lat2 - lat1);
            double deltaLam = ToRadians(lon2 - lon1);

            double a = Math.Sin(deltaPhi / 2.0) * Math.Sin(deltaPhi / 2.0) +
                       Math.Cos(phi1) * Math.Cos(phi2) *
                       Math.Sin(deltaLam / 2.0) * Math.Sin(deltaLam / 2.0);
            double c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));

            return (float)(R * c);
        }

        private static double ToRadians(float degrees)
        {
            return degrees * Math.PI / 180.0;
        }
    }
}
