using System;

namespace UBOAT.Mods.RadioNavigation
{
    // F12/F73: single home for spherical geo math; SonneController and
    // PositionFixCalculator previously each carried a private ToRadians and
    // the Haversine distance was duplicated across classes.
    public static class GeoMath
    {
        public const double EarthRadiusKm = 6371.0;

        public static double ToRadians(float degrees)
        {
            return degrees * Math.PI / 180.0;
        }

        // Great-circle distance in km (Haversine formula).
        // F80: rounding can push `a` a hair above 1.0 for (nearly) identical
        // points; Math.Sqrt(1.0 - a) would then be NaN.
        public static float HaversineKm(float lat1, float lon1, float lat2, float lon2)
        {
            double phi1 = ToRadians(lat1);
            double phi2 = ToRadians(lat2);
            double deltaPhi = ToRadians(lat2 - lat1);
            double deltaLam = ToRadians(lon2 - lon1);

            double a = Math.Sin(deltaPhi / 2.0) * Math.Sin(deltaPhi / 2.0) +
                       Math.Cos(phi1) * Math.Cos(phi2) *
                       Math.Sin(deltaLam / 2.0) * Math.Sin(deltaLam / 2.0);
            a = Math.Max(0.0, Math.Min(1.0, a));
            double c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));

            return (float)(EarthRadiusKm * c);
        }

        // Initial great-circle bearing from point 1 to point 2, degrees [0, 360).
        public static float InitialBearingDeg(float lat1, float lon1, float lat2, float lon2)
        {
            double dLon = ToRadians(lon2 - lon1);
            double lat1R = ToRadians(lat1);
            double lat2R = ToRadians(lat2);

            double y = Math.Sin(dLon) * Math.Cos(lat2R);
            double x = Math.Cos(lat1R) * Math.Sin(lat2R) -
                       Math.Sin(lat1R) * Math.Cos(lat2R) * Math.Cos(dLon);

            float bearing = (float)(Math.Atan2(y, x) * (180.0 / Math.PI));
            return (bearing + 360f) % 360f;
        }

        // Bearing used by UBOAT's flat encounter-space map and protractor.
        // The game maps latitude and longitude to world axes with the same
        // scale, so the visual map angle is not the spherical true bearing.
        public static float MapBearingDeg(float lat1, float lon1, float lat2, float lon2)
        {
            float deltaLatitude = lat2 - lat1;
            float deltaLongitude = lon2 - lon1;
            float bearing = (float)(Math.Atan2(deltaLongitude, deltaLatitude) * (180.0 / Math.PI));
            return (bearing + 360f) % 360f;
        }
    }
}
