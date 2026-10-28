using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Physical lunar diameter, using the same distance/observer
    /// sphere as MoonTopocentricRaDec. No aesthetic ×2 magnification.</summary>
    public static class MoonDiscScale
    {
        private const double LunarRadiusKm = 1737.4;
        private const double EarthRadiusKm = 6378.14;

        public static double AngularDiameterDeg(double jd, double topocentricAltitudeRad)
        {
            double distance = ObserverDistanceKm(AstroMath.MoonDistanceKm(jd),
                topocentricAltitudeRad);
            return distance > LunarRadiusKm
                ? 2.0 * Math.Asin(LunarRadiusKm / distance) * 180.0 / Math.PI : 0.0;
        }

        public static double ObserverDistanceKm(double geocentricDistanceKm,
            double topocentricAltitudeRad)
        {
            if (double.IsNaN(geocentricDistanceKm) || double.IsInfinity(geocentricDistanceKm)
                || geocentricDistanceKm <= EarthRadiusKm
                || double.IsNaN(topocentricAltitudeRad) || double.IsInfinity(topocentricAltitudeRad))
                return 0.0;
            double sinAlt = Math.Sin(topocentricAltitudeRad);
            return Math.Sqrt(geocentricDistanceKm * geocentricDistanceKm
                - EarthRadiusKm * EarthRadiusKm * (1.0 - sinAlt * sinAlt))
                - EarthRadiusKm * sinAlt;
        }
    }
}
