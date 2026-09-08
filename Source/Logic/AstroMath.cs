using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Чистая астрономическая математика для STAR_SKY (ADR-020).
    /// Без Unity, без IO — тестируется в Tests/. Формулы — Meeus гл.7,12,21,22.
    /// Best practice: double точность, без аллокаций, без кэша.
    /// </summary>
    public static class AstroMath
    {
        private const double Deg2Rad = Math.PI / 180.0;

        /// <summary>JD из UTC DateTime (Meeus гл.7). 2000-01-01 12:00 = 2451545.0</summary>
        public static double ToJulianDay(DateTime utc)
        {
            // Упрощённо через Ticks — достаточно для 1939-1945 (±0.5с)
            DateTime j2000 = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            double days = (utc.ToUniversalTime() - j2000).TotalDays;
            return 2451545.0 + days;
        }

        /// <summary>GMST градусы 0-360 (Meeus гл.12, 12.4).</summary>
        public static double GmstDeg(double jd)
        {
            double t = (jd - 2451545.0) / 36525.0;
            double gmst = 280.46061837 + 360.98564736629 * (jd - 2451545.0)
                          + 0.000387933 * t * t - t * t * t / 38710000.0;
            gmst %= 360.0;
            if (gmst < 0) gmst += 360.0;
            return gmst;
        }

        public static double LstDeg(double jd, double lonDeg) // восточная долгота
        {
            double lst = GmstDeg(jd) + lonDeg;
            lst %= 360.0;
            if (lst < 0) lst += 360.0;
            return lst;
        }

        /// <summary>RA/Dec (рад) → ENU + alt/az (рад, az 0=север +восток). Формула STAR_SKY_PLAN §7.3.</summary>
        public static void RaDecToEnu(double raRad, double decRad, double lstDeg, double latDeg,
            out double altRad, out double azRad, out double e, out double n, out double u)
        {
            double lst = lstDeg * Deg2Rad;
            double lat = latDeg * Deg2Rad;
            double h = lst - raRad;
            double sinDec = Math.Sin(decRad), cosDec = Math.Cos(decRad);
            double sinLat = Math.Sin(lat), cosLat = Math.Cos(lat);
            double sinH = Math.Sin(h), cosH = Math.Cos(h);
            e = -cosDec * sinH;
            n = sinDec * cosLat - cosDec * cosH * sinLat;
            u = sinDec * sinLat + cosDec * cosH * cosLat;
            altRad = Math.Asin(Math.Max(-1.0, Math.Min(1.0, u)));
            azRad = Math.Atan2(e, n);
            if (azRad < 0) azRad += 2 * Math.PI;
        }

        /// <summary>
        /// Геоцентрические RA/Dec планеты (Meeus гл.31, кеплеровы элементы).
        /// planetId: 0=Венера, 1=Марс, 2=Юпитер, 3=Сатурн.
        /// </summary>
        public static bool PlanetRaDec(int planetId, double jd, out double raRad, out double decRad)
        {
            raRad = 0;
            decRad = 0;
            if (planetId < 0 || planetId > 3) return false;

            double T = (jd - 2451545.0) / 36525.0; // столетия от J2000

            // Mean elements, J2000, valid close to 1800-2050 (Meeus/JPL form).
            double aEarth = 1.00000261;
            double eEarth = 0.01671123 - 0.00004392 * T;
            double lEarth = 100.466457 + 36000.769823 * T + 0.00030322 * T * T;
            double piEarth = 102.937348 + 1.7195269 * T + 0.00045962 * T * T;

            // Элементы планеты (Meeus Table 31.B для внешних, Table 31.C для внутренних)
            double a_p, e_p, L_p, pi_p, node_p, inclination_p;
            switch (planetId)
            {
                case 0: // Венера
                    a_p = 0.72333566 + 0.00000390 * T;
                    e_p = 0.00677672 - 0.00004107 * T;
                    L_p = 181.97909950 + 58517.81538729 * T;
                    pi_p = 131.60246718 + 0.00268329 * T;
                    node_p = 76.67984255 - 0.27769418 * T;
                    inclination_p = 3.39467605 - 0.00078890 * T;
                    break;
                case 1: // Марс
                    a_p = 1.52371034 + 0.00001847 * T;
                    e_p = 0.09339410 + 0.00007882 * T;
                    L_p = -4.55343205 + 19140.30268499 * T;
                    pi_p = -23.94362959 + 0.44441088 * T;
                    node_p = 49.55953891 - 0.29257343 * T;
                    inclination_p = 1.84969142 - 0.00813131 * T;
                    break;
                case 2: // Юпитер
                    a_p = 5.20288700 - 0.00011607 * T;
                    e_p = 0.04838624 - 0.00013253 * T;
                    L_p = 34.39644051 + 3034.74612775 * T;
                    pi_p = 14.72847983 + 0.21252668 * T;
                    node_p = 100.47390909 + 0.20469106 * T;
                    inclination_p = 1.30439695 - 0.00183714 * T;
                    break;
                case 3: // Сатурн
                    a_p = 9.53667594 - 0.00125060 * T;
                    e_p = 0.05386179 - 0.00050991 * T;
                    L_p = 49.95424423 + 1222.49362201 * T;
                    pi_p = 92.59887831 - 0.41897216 * T;
                    node_p = 113.66242448 - 0.28867794 * T;
                    inclination_p = 2.48599187 + 0.00193609 * T;
                    break;
                default:
                    return false;
            }

            // Наклонение эклиптики (Meeus 21.2)
            double eps = 23.439291 - 0.013004166 * T;
            eps *= Deg2Rad;

            SolveKepler(eEarth, lEarth - piEarth, out double vEarth);
            double rEarth = aEarth * (1 - eEarth * eEarth) / (1 + eEarth * Math.Cos(vEarth));

            // Гелиоцентрические координаты Земли
            double lonEarth = (piEarth * Deg2Rad) + vEarth;
            double xEarth = rEarth * Math.Cos(lonEarth);
            double yEarth = rEarth * Math.Sin(lonEarth);

            // Решаем уравнение Кеплера для планеты
            SolveKepler(e_p, L_p - pi_p, out double v_p);
            double r_p = a_p * (1 - e_p * e_p) / (1 + e_p * Math.Cos(v_p));

            // Orbital-plane coordinates rotated by argument of perihelion, node and inclination.
            double omega = (pi_p - node_p) * Deg2Rad;
            double node = node_p * Deg2Rad;
            double inclination = inclination_p * Deg2Rad;
            double argument = v_p + omega;
            double cosNode = Math.Cos(node), sinNode = Math.Sin(node);
            double cosArgument = Math.Cos(argument), sinArgument = Math.Sin(argument);
            double cosInclination = Math.Cos(inclination), sinInclination = Math.Sin(inclination);
            double x_p = r_p * (cosNode * cosArgument - sinNode * sinArgument * cosInclination);
            double y_p = r_p * (sinNode * cosArgument + cosNode * sinArgument * cosInclination);
            double z_p = r_p * (sinArgument * sinInclination);

            // Геоцентрические координаты
            double x_geo = x_p - xEarth;
            double y_geo = y_p - yEarth;
            double z_geo = z_p;

            // Ecliptic of date -> equatorial of date.
            double x_equ = x_geo;
            double y_equ = y_geo * Math.Cos(eps) - z_geo * Math.Sin(eps);
            double z_equ = y_geo * Math.Sin(eps) + z_geo * Math.Cos(eps);
            double ra = Math.Atan2(y_equ, x_equ);
            if (ra < 0) ra += 2 * Math.PI;
            double dec = Math.Atan2(z_equ, Math.Sqrt(x_equ * x_equ + y_equ * y_equ));

            raRad = ra;
            decRad = dec;
            return true;
        }

        /// <summary>
        /// Низкоточная геоцентрическая RA/Dec Луны (Meeus гл.47 orbital approximation).
        /// Даёт визуально пригодное положение, но не заменяет усечённый ELP2000.
        /// </summary>
        public static bool MoonRaDec(double jd, out double raRad, out double decRad)
        {
            raRad = 0;
            decRad = 0;
            if (double.IsNaN(jd) || double.IsInfinity(jd)) return false;

            double days = jd - 2451543.5;
            double node = (125.1228 - 0.0529538083 * days) * Deg2Rad;
            double inclination = 5.1454 * Deg2Rad;
            double periapsis = (318.0634 + 0.1643573223 * days) * Deg2Rad;
            double eccentricity = 0.0549;
            double meanAnomaly = NormalizeDegrees(115.3654 + 13.0649929509 * days) * Deg2Rad;
            double semiMajorAxis = 60.2666;

            double eccentricAnomaly = meanAnomaly;
            for (int i = 0; i < 8; i++)
                eccentricAnomaly -= (eccentricAnomaly - eccentricity * Math.Sin(eccentricAnomaly) - meanAnomaly)
                    / (1 - eccentricity * Math.Cos(eccentricAnomaly));

            double xv = semiMajorAxis * (Math.Cos(eccentricAnomaly) - eccentricity);
            double yv = semiMajorAxis * Math.Sqrt(1 - eccentricity * eccentricity) * Math.Sin(eccentricAnomaly);
            double trueAnomaly = Math.Atan2(yv, xv);
            double distance = Math.Sqrt(xv * xv + yv * yv);
            double argument = trueAnomaly + periapsis;
            double cosNode = Math.Cos(node), sinNode = Math.Sin(node);
            double cosArgument = Math.Cos(argument), sinArgument = Math.Sin(argument);
            double cosInclination = Math.Cos(inclination), sinInclination = Math.Sin(inclination);

            double xEcliptic = distance * (cosNode * cosArgument - sinNode * sinArgument * cosInclination);
            double yEcliptic = distance * (sinNode * cosArgument + cosNode * sinArgument * cosInclination);
            double zEcliptic = distance * (sinArgument * sinInclination);
            double obliquity = (23.439291 - 0.013004166 * ((jd - 2451545.0) / 36525.0)) * Deg2Rad;
            double xEquatorial = xEcliptic;
            double yEquatorial = yEcliptic * Math.Cos(obliquity) - zEcliptic * Math.Sin(obliquity);
            double zEquatorial = yEcliptic * Math.Sin(obliquity) + zEcliptic * Math.Cos(obliquity);

            raRad = Math.Atan2(yEquatorial, xEquatorial);
            if (raRad < 0) raRad += 2 * Math.PI;
            decRad = Math.Atan2(zEquatorial, Math.Sqrt(xEquatorial * xEquatorial + yEquatorial * yEquatorial));
            return true;
        }

        /// <summary>Approximate illuminated fraction, 0=new moon and 1=full moon.</summary>
        public static double MoonIllumination(double jd)
        {
            double synodicDays = 29.530588853;
            double phase = (jd - 2451550.25972) % synodicDays;
            if (phase < 0) phase += synodicDays;
            return 0.5 * (1.0 - Math.Cos(2 * Math.PI * phase / synodicDays));
        }

        /// <summary>
        /// Геоцентрические RA/Dec Солнца, низкоточная формула Meeus (гл.25,
        /// без нутации/аберрации высших порядков, точность ~0.01°). Введена
        /// для секстанта: TOD_Sky недоступен компилятору мода in-game
        /// (SEXTANT_PLAN v1.3).
        /// </summary>
        public static void SunRaDec(double jd, out double raRad, out double decRad)
        {
            double n = jd - 2451545.0;
            double meanLon = NormalizeDegrees(280.460 + 0.9856474 * n);
            double meanAnom = NormalizeDegrees(357.528 + 0.9856003 * n);
            double gRad = meanAnom * Deg2Rad;
            double eclLon = NormalizeDegrees(meanLon + 1.915 * Math.Sin(gRad) + 0.020 * Math.Sin(2 * gRad));
            double eclLonRad = eclLon * Deg2Rad;
            double obliquity = (23.439 - 0.0000004 * n) * Deg2Rad;

            raRad = Math.Atan2(Math.Cos(obliquity) * Math.Sin(eclLonRad), Math.Cos(eclLonRad));
            if (raRad < 0) raRad += 2 * Math.PI;
            decRad = Math.Asin(Math.Sin(obliquity) * Math.Sin(eclLonRad));
        }

        private static void SolveKepler(double e, double M_deg, out double v)
        {
            // Упрощённое решение уравнения Кеплера (итерация Ньютона)
            double M_rad = NormalizeDegrees(M_deg) * Deg2Rad;
            double E = M_rad;
            for (int i = 0; i < 10; i++)
            {
                double dE = (E - e * Math.Sin(E) - M_rad) / (1 - e * Math.Cos(E));
                E -= dE;
                if (Math.Abs(dE) < 1e-10) break;
            }
            // Истинная аномалия
            v = 2 * Math.Atan2(Math.Sqrt(1 + e) * Math.Sin(E / 2), Math.Sqrt(1 - e) * Math.Cos(E / 2));
        }

        internal static double NormalizeDegrees(double degrees)
        {
            degrees %= 360.0;
            return degrees < 0 ? degrees + 360.0 : degrees;
        }
    }
}
