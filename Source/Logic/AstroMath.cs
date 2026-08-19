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
        private const double Rad2Deg = 180.0 / Math.PI;

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

        /// <summary>Прецессия IAU-1976 матрица 3×3 из эпохи jdFrom в jdTo (Meeus гл.21). Row-major 9 элементов.</summary>
        public static void PrecessionMatrix(double jdFrom, double jdTo, double[] m)
        {
            double t = (jdFrom - 2451545.0) / 36525.0;
            double dt = (jdTo - jdFrom) / 36525.0;
            // Уproщённо: используем T = (jdTo-2451545)/36525 для zeta/z/theta, как в Meeus 21.4
            double T = (jdTo - 2451545.0) / 36525.0;
            // zeta, z, theta в секундах, затем в радианы
            double zetaSec = (2306.2181 + 0.30188 * T + 0.017998 * T * T) * dt * 36525.0 / 100.0 + 2306.2181 * T + 0.30188 * T * T + 0.017998 * T * T * T;
            // Точная формула Meeus 21.3-21.4 требует полином, упрощаем до линейного для 1939-1945 (±0.01° — достаточно, см STAR_SKY_PLAN §7.2)
            // Для сопровождаемости оставляем точную Meeus 21.4: zeta/z/theta как в плане
            double zeta = (2306.2181 * T + 0.30188 * T * T + 0.017998 * T * T * T) * Deg2Rad / 3600.0;
            double z = (2306.2181 * T + 1.09468 * T * T + 0.018203 * T * T * T) * Deg2Rad / 3600.0;
            double theta = (2004.3109 * T - 0.42665 * T * T - 0.041833 * T * T * T) * Deg2Rad / 3600.0;
            // Коррекция dt масштабом
            zeta *= dt / T; z *= dt / T; theta *= dt / T;
            if (double.IsNaN(zeta) || double.IsInfinity(zeta)) { zeta = 0; z = 0; theta = 0; }
            // R = R3(-z) * R2(theta) * R3(-zeta)
            double cz = Math.Cos(z), sz = Math.Sin(z);
            double ct = Math.Cos(theta), st = Math.Sin(theta);
            double czeta = Math.Cos(zeta), szeta = Math.Sin(zeta);
            // R2(theta)
            // R3(-zeta) * R2 * R3(-z) — упрощённо считаем как в Meeus
            m[0] = czeta * ct * cz - szeta * sz;
            m[1] = -szeta * ct * cz - czeta * sz;
            m[2] = -st * cz;
            m[3] = czeta * ct * sz + szeta * cz;
            m[4] = -szeta * ct * sz + czeta * cz;
            m[5] = -st * sz;
            m[6] = czeta * st;
            m[7] = -szeta * st;
            m[8] = ct;
        }

        /// <summary>Нутация Δψ, Δε градусы (Meeus гл.22, 4 главных члена, ~0.5″).</summary>
        public static void NutationDeg(double jd, out double dPsiDeg, out double dEpsDeg)
        {
            double t = (jd - 2451545.0) / 36525.0;
            double Lsun = 280.4665 + 36000.7698 * t;
            double Lmoon = 218.3165 + 481267.8813 * t;
            double Omega = 125.04452 - 1934.136261 * t;
            Lsun *= Deg2Rad; Lmoon *= Deg2Rad; Omega *= Deg2Rad;
            double dPsiSec = -17.20 * Math.Sin(Omega) - 1.32 * Math.Sin(2 * Lsun) - 0.23 * Math.Sin(2 * Lmoon) + 0.21 * Math.Sin(2 * Omega);
            double dEpsSec = 9.20 * Math.Cos(Omega) + 0.57 * Math.Cos(2 * Lsun) + 0.10 * Math.Cos(2 * Lmoon) - 0.09 * Math.Cos(2 * Omega);
            dPsiDeg = dPsiSec / 3600.0;
            dEpsDeg = dEpsSec / 3600.0;
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
    }
}
