using System;
using System.Collections.Generic;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>One pure world kernel. Reuses AstroMath; never reads legacy calibration.</summary>
    internal static class CelestialSkyMath
    {
        internal const double R = Math.PI / 180;
        private const double AuKm = 149597870.7;
        private const double SolarRadiusKm = 695700;
        private const double LunarRadiusKm = 1737.4;
        private const double CatalogEpochJd = 2424516.5;

        internal static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        internal static double Wrap(double value) { value %= 360; return value < 0 ? value + 360 : value; }

        internal static CelestialSkyNotReady Validate(CelestialSkyInput input)
        {
            if (input == null) return CelestialSkyNotReady.MissingInput;
            if (input.WorldRevision <= 0) return CelestialSkyNotReady.WrongWorld;
            if (!ValidTime(input)) return CelestialSkyNotReady.GameTime;
            if (!input.WorldUtcKnown || input.WorldUtcOffset != 0) return CelestialSkyNotReady.WorldUtc;
            if (!ValidObserver(input)) return CelestialSkyNotReady.Observer;
            if (!ValidHeight(input)) return CelestialSkyNotReady.HeightDatum;
            if (!ValidAtmosphere(input)) return CelestialSkyNotReady.Atmosphere;
            return CelestialSkyNotReady.None;
        }

        private static bool ValidTime(CelestialSkyInput input)
        {
            return input.TimeTrusted && input.GmtUt1.Kind == DateTimeKind.Utc
                && input.GmtUt1.Year >= 1939 && input.GmtUt1.Year <= 1945;
        }

        private static bool ValidObserver(CelestialSkyInput input)
        {
            return input.ObserverTrusted && Finite(input.LatitudeDeg) && Finite(input.LongitudeDeg)
                && Math.Abs(input.LatitudeDeg) <= 90 && Math.Abs(input.LongitudeDeg) <= 180;
        }

        private static bool ValidHeight(CelestialSkyInput input)
        {
            return input.HeightTrusted && Finite(input.EllipsoidHeightM) && Finite(input.EyeAboveSeaM)
                && input.EllipsoidHeightM >= -500 && input.EllipsoidHeightM <= 10000
                && input.EyeAboveSeaM >= 0 && input.EyeAboveSeaM <= 10000;
        }

        private static bool ValidAtmosphere(CelestialSkyInput input)
        {
            return input.AtmosphereAccepted && Finite(input.PressureHpa) && Finite(input.TemperatureC)
                && input.PressureHpa >= 300 && input.PressureHpa <= 1100
                && input.TemperatureC >= -80 && input.TemperatureC <= 60;
        }

        internal static double DeltaT(DateTime ut1)
        {
            double year = ut1.Year + (ut1 - new DateTime(ut1.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalDays
                / (DateTime.IsLeapYear(ut1.Year) ? 366 : 365);
            double t = year - (year < 1941 ? 1920 : 1950);
            return year < 1941 ? 21.20 + 0.84493 * t - 0.076100 * t * t + 0.0020936 * t * t * t
                : 29.07 + 0.407 * t - t * t / 233 + t * t * t / 2547;
        }

        internal static double Refract(double altitude, double pressure, double temperature)
        {
            if (!Finite(altitude) || altitude < -1 || altitude > 90
                || !Finite(pressure) || pressure < 300 || pressure > 1100
                || !Finite(temperature) || temperature < -80 || temperature > 60) return double.NaN;
            if (altitude == 90) return 90;
            double correction = Math.Max(0, 1.02 / Math.Tan((altitude + 10.3 / (altitude + 5.11)) * R));
            return altitude + correction * pressure / 1010 * 283 / (273 + temperature) / 60;
        }

        internal static CelestialSkyState Compute(CelestialSkyInput input, long snapshotVersion)
        {
            CelestialSkyNotReady error = Validate(input);
            if (error != CelestialSkyNotReady.None || snapshotVersion <= 0)
                throw new ArgumentException("Invalid world input or snapshot version: " + error);
            double ut = AstroMath.ToJulianDay(input.GmtUt1);
            double dt = DeltaT(input.GmtUt1), tt = ut + dt / 86400;
            double t = (tt - 2451545) / 36525;
            double dp, de;
            AstroMath.NutationLowPrecision(t, Wrap(280.46646 + 36000.76983 * t + 0.0003032 * t * t) * R, out dp, out de);
            double eps = (23.4392911 - 0.0130042 * t - 0.000000164 * t * t + 0.000000504 * t * t * t) * R;
            double gmst = AstroMath.GmstDeg(ut);
            double gast = Wrap(gmst + dp * Math.Cos(eps + de / 3600 * R) / 3600);
            CelestialSkyVector observer = Observer(input, gast);
            CelestialSkyBody sun = Sun(input, tt, observer, gast);
            CelestialSkyBody moon = Moon(input, tt, observer, gast);
            List<CelestialSkyBody> stars = Stars(input, tt, gast, eps, dp, de);
            return new CelestialSkyState(snapshotVersion, input, ut, tt, dt, gmst, gast, sun, moon, stars);
        }

        private static CelestialSkyBody Sun(CelestialSkyInput input, double tt, CelestialSkyVector observer, double gast)
        {
            double ra, dec;
            AstroMath.SunRaDec(tt, out ra, out dec);
            // Invert the existing physical-size API to recover its distance law;
            // do not duplicate its Meeus solar-distance algorithm or change legacy SD.
            double legacySd = SunDiscScale.AngularDiameterDeg(tt) / 2 * R;
            double distance = Math.Sin(959.63 / 3600 * R) * AuKm / Math.Sin(legacySd);
            return FiniteBody(input, ra, dec, distance, SolarRadiusKm, observer, gast);
        }

        private static CelestialSkyBody Moon(CelestialSkyInput input, double tt, CelestialSkyVector observer, double gast)
        {
            double ra, dec;
            if (!AstroMath.MoonRaDecComplete(tt, out ra, out dec))
                throw new ArithmeticException("Lunar ephemeris unavailable");
            return FiniteBody(input, ra, dec, AstroMath.MoonDistanceKmComplete(tt), LunarRadiusKm, observer, gast);
        }

        private static CelestialSkyBody FiniteBody(CelestialSkyInput input, double ra, double dec,
            double distance, double radius, CelestialSkyVector observer, double gast)
        {
            CelestialSkyVector topocentric = FromRaDec(ra, dec) * distance - observer;
            double diameter = 2 * Math.Asin(radius / topocentric.Length) / R;
            return new CelestialSkyBody(0, ToEnu(topocentric.Unit, input, gast), ra, dec, topocentric.Length, diameter, input);
        }

        private static List<CelestialSkyBody> Stars(CelestialSkyInput input, double tt, double gast,
            double eps, double dp, double de)
        {
            var result = new List<CelestialSkyBody>();
            var seen = new HashSet<int>();
            foreach (StarEntry star in StarCatalog.Stars)
            {
                if (!star.IsNav || star.Hip <= 0 || !seen.Add(star.Hip)) continue;
                double ra, dec;
                AstroMath.Precess(star.RaDeg * R, star.DecDeg * R, CatalogEpochJd, tt, out ra, out dec);
                CelestialSkyVector eq = FromRaDec(ra, dec);
                eq = RotateX(RotateZ(RotateX(eq, -eps), dp / 3600 * R), eps + de / 3600 * R);
                result.Add(new CelestialSkyBody(star.Hip, ToEnu(eq, input, gast),
                    Math.Atan2(eq.Y, eq.X), Math.Asin(eq.Z), double.PositiveInfinity, 0, input));
            }
            return result;
        }

        internal static CelestialSkyVector Observer(CelestialSkyInput input, double gast)
        {
            const double a = 6378.137, f = 1 / 298.257223563;
            double phi = input.LatitudeDeg * R, theta = (gast + input.LongitudeDeg) * R;
            double e2 = f * (2 - f), h = input.EllipsoidHeightM / 1000;
            double n = a / Math.Sqrt(1 - e2 * Math.Sin(phi) * Math.Sin(phi));
            return new CelestialSkyVector((n + h) * Math.Cos(phi) * Math.Cos(theta),
                (n + h) * Math.Cos(phi) * Math.Sin(theta), (n * (1 - e2) + h) * Math.Sin(phi));
        }

        private static CelestialSkyVector ToEnu(CelestialSkyVector eq, CelestialSkyInput input, double gast)
        {
            double phi = input.LatitudeDeg * R, theta = (gast + input.LongitudeDeg) * R;
            return new CelestialSkyVector(-Math.Sin(theta) * eq.X + Math.Cos(theta) * eq.Y,
                -Math.Sin(phi) * Math.Cos(theta) * eq.X - Math.Sin(phi) * Math.Sin(theta) * eq.Y + Math.Cos(phi) * eq.Z,
                Math.Cos(phi) * Math.Cos(theta) * eq.X + Math.Cos(phi) * Math.Sin(theta) * eq.Y + Math.Sin(phi) * eq.Z);
        }

        private static CelestialSkyVector FromRaDec(double ra, double dec)
        { return new CelestialSkyVector(Math.Cos(dec) * Math.Cos(ra), Math.Cos(dec) * Math.Sin(ra), Math.Sin(dec)); }
        private static CelestialSkyVector RotateX(CelestialSkyVector v, double a)
        { return new CelestialSkyVector(v.X, Math.Cos(a) * v.Y - Math.Sin(a) * v.Z, Math.Sin(a) * v.Y + Math.Cos(a) * v.Z); }
        private static CelestialSkyVector RotateZ(CelestialSkyVector v, double a)
        { return new CelestialSkyVector(Math.Cos(a) * v.X - Math.Sin(a) * v.Y, Math.Sin(a) * v.X + Math.Cos(a) * v.Y, v.Z); }
    }
}
