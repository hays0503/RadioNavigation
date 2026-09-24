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

        /// <summary>Earth equatorial radius (km) — parallax and topocentric
        /// corrections (P2).</summary>
        private const double EarthRadiusKm = 6378.14;

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
        /// Геоцентрические RA/Dec Луны (Meeus гл.47, усечённый ряд): основные
        /// фундаменты L′, D, M, M′, F и старшие члены таблиц 47.A/47.B.
        /// Точность ~0.05° (против ~1–2° у прежней кеплеровой базы, которая
        /// теряла Evection 76′ и Variation 40′). Вход — TT/UT jd.
        /// </summary>
        public static bool MoonRaDec(double jd, out double raRad, out double decRad)
        {
            raRad = 0;
            decRad = 0;
            if (double.IsNaN(jd) || double.IsInfinity(jd)) return false;

            double t = (jd - 2451545.0) / 36525.0;
            double lp, d, m, mp, f;
            MoonFundamentals(t, out lp, out d, out m, out mp, out f);
            double e = 1.0 - 0.002516 * t - 0.0000074 * t * t;

            double lambda = lp + MoonLongitudeSum(d, m, mp, f, e) / 1000000.0;
            double beta = MoonLatitudeSum(d, m, mp, f, e) / 1000000.0;

            // Meeus 47 additive terms (planetary + figure effects), degrees.
            double a1 = 119.75 + 131.849 * t;
            double a2 = 53.09 + 479264.290 * t;
            double a3 = 313.45 + 481266.484 * t;
            lambda += (3958.0 * Math.Sin(a1 * Deg2Rad)
                     + 1962.0 * Math.Sin((lp - f) * Deg2Rad)
                     + 318.0 * Math.Sin(a2 * Deg2Rad)) / 1000000.0;
            beta += (-2235.0 * Math.Sin(lp * Deg2Rad)
                   + 382.0 * Math.Sin(a3 * Deg2Rad)
                   + 175.0 * Math.Sin((a1 - f) * Deg2Rad)
                   + 175.0 * Math.Sin((a1 + f) * Deg2Rad)
                   + 127.0 * Math.Sin((lp - mp) * Deg2Rad)
                   - 115.0 * Math.Sin((lp + mp) * Deg2Rad)) / 1000000.0;

            // Видимое место: нутация в долготе и истинный наклон эклиптики.
            double dPsi, dEps;
            NutationLowPrecision(t, NormalizeDegrees(280.46646 + 36000.76983 * t) * Deg2Rad,
                out dPsi, out dEps);
            lambda += dPsi / 3600.0;
            double eps = (23.4392911 - 0.0130042 * t + dEps / 3600.0) * Deg2Rad;

            double lamRad = lambda * Deg2Rad;
            double betRad = beta * Deg2Rad;
            double cosBet = Math.Cos(betRad);
            double sinBet = Math.Sin(betRad);
            double cosEps = Math.Cos(eps);
            double sinEps = Math.Sin(eps);

            raRad = Math.Atan2(Math.Sin(lamRad) * cosEps - Math.Tan(betRad) * sinEps,
                Math.Cos(lamRad));
            if (raRad < 0) raRad += 2 * Math.PI;
            decRad = Math.Asin(Math.Sin(betRad) * cosEps
                + cosBet * sinEps * Math.Sin(lamRad));
            return true;
        }

        /// <summary>Fundamentals of Meeus ch.47 (degrees).</summary>
        private static void MoonFundamentals(double t, out double lp, out double d,
            out double m, out double mp, out double f)
        {
            lp = NormalizeDegrees(218.3164477 + 481267.88123421 * t - 0.0015786 * t * t
                + t * t * t / 538841.0 - t * t * t * t / 65194000.0);
            d = NormalizeDegrees(297.8501921 + 445267.1114034 * t - 0.0018819 * t * t
                + t * t * t / 545868.0 - t * t * t * t / 113065000.0);
            m = NormalizeDegrees(357.5291092 + 35999.0502909 * t - 0.0001536 * t * t
                + t * t * t / 24490000.0);
            mp = NormalizeDegrees(134.9633964 + 477198.8675055 * t + 0.0087414 * t * t
                + t * t * t / 69699.0 - t * t * t * t / 14712000.0);
            f = NormalizeDegrees(93.2720950 + 483202.0175233 * t - 0.0036539 * t * t
                - t * t * t / 3526000.0 + t * t * t * t / 863310000.0);
        }

        private struct MoonTerm
        {
            public readonly int D;
            public readonly int M;
            public readonly int Mp;
            public readonly int F;
            public readonly double Coeff;

            public MoonTerm(int d, int m, int mp, int f, double coeff)
            {
                D = d; M = m; Mp = mp; F = f; Coeff = coeff;
            }
        }

        /// <summary>Meeus table 47.A (longitude, units 1e-6 degrees), top terms.
        /// The eccentricity factor E multiplies terms containing M.</summary>
        private static readonly MoonTerm[] MoonLTerms =
        {
            new MoonTerm(0, 0, 1, 0, 6288774),
            new MoonTerm(2, 0, -1, 0, 1274027),
            new MoonTerm(2, 0, 0, 0, 658314),
            new MoonTerm(0, 0, 2, 0, 213618),
            new MoonTerm(0, 1, 0, 0, -185116),
            new MoonTerm(0, 0, 0, 2, -114332),
            new MoonTerm(2, 0, -2, 0, 58793),
            new MoonTerm(2, -1, -1, 0, 57066),
            new MoonTerm(2, 0, 1, 0, 53322),
            new MoonTerm(2, -1, 0, 0, 45758),
            new MoonTerm(0, 1, -1, 0, -40923),
            new MoonTerm(1, 0, 0, 0, -34720),
            new MoonTerm(0, 1, 1, 0, -30383),
            new MoonTerm(2, 0, 0, -2, 15327),
            new MoonTerm(0, 0, 1, 2, -12528),
            new MoonTerm(0, 0, 1, -2, 10980),
            new MoonTerm(4, 0, -1, 0, 10675),
            new MoonTerm(0, 0, 3, 0, 10034),
            new MoonTerm(4, 0, -2, 0, 8548),
            new MoonTerm(2, 1, -1, 0, -7888),
        };

        /// <summary>Meeus table 47.B (latitude, units 1e-6 degrees), top terms.</summary>
        private static readonly MoonTerm[] MoonBTerms =
        {
            new MoonTerm(0, 0, 0, 1, 5128122),
            new MoonTerm(0, 0, 1, 1, 280602),
            new MoonTerm(0, 0, 1, -1, 277693),
            new MoonTerm(2, 0, 0, -1, 173237),
            new MoonTerm(2, 0, -1, 1, 55413),
            new MoonTerm(2, 0, -1, -1, 46271),
            new MoonTerm(2, 0, 0, 1, 32573),
            new MoonTerm(0, 0, 2, 1, 17198),
            new MoonTerm(2, 0, 1, -1, 9266),
            new MoonTerm(0, 0, 2, -1, 8822),
            new MoonTerm(2, -1, 0, -1, 8216),
            new MoonTerm(2, 0, -2, -1, 4324),
            new MoonTerm(2, 0, 1, 1, 4200),
            new MoonTerm(2, 1, 0, -1, -3359),
        };

        /// <summary>Meeus table 47.C (distance, units 1e-3 km), top terms.
        /// NOTE: this table is summed with the COSINE (Meeus 47.6).</summary>
        private static readonly MoonTerm[] MoonRTerms =
        {
            new MoonTerm(0, 0, 1, 0, -20905355),
            new MoonTerm(2, 0, -1, 0, -3699111),
            new MoonTerm(2, 0, 0, 0, -2955968),
            new MoonTerm(0, 0, 2, 0, -569925),
            new MoonTerm(0, 1, 0, 0, 48888),
            new MoonTerm(0, 0, 0, 2, -3149),
            new MoonTerm(2, 0, -2, 0, 246158),
            new MoonTerm(2, -1, -1, 0, -152138),
            new MoonTerm(2, 0, 1, 0, -170733),
            new MoonTerm(2, -1, 0, 0, -204586),
            new MoonTerm(0, 1, -1, 0, -129620),
            new MoonTerm(1, 0, 0, 0, 108743),
            new MoonTerm(0, 1, 1, 0, 104755),
            new MoonTerm(2, 0, 0, -2, 10321),
            new MoonTerm(0, 0, 1, -2, 79661),
            new MoonTerm(4, 0, -1, 0, -34782),
            new MoonTerm(0, 0, 3, 0, -23210),
            new MoonTerm(4, 0, -2, 0, -21636),
            new MoonTerm(2, 1, -1, 0, 24208),
            new MoonTerm(2, 1, 0, 0, 30824),
            new MoonTerm(1, 0, -1, 0, -8379),
            new MoonTerm(1, 1, 0, 0, -16675),
            new MoonTerm(2, -1, 1, 0, -12831),
            new MoonTerm(2, 0, 2, 0, -10445),
            new MoonTerm(4, 0, 0, 0, -11650),
            new MoonTerm(2, 0, -3, 0, 14403),
            new MoonTerm(0, 1, -2, 0, -7003),
            new MoonTerm(2, -1, -2, 0, 10056),
            new MoonTerm(1, 0, 1, 0, 6322),
            new MoonTerm(2, -2, 0, 0, -9884),
            new MoonTerm(0, 1, 2, 0, 5751),
            new MoonTerm(2, -2, -1, 0, -4950),
            new MoonTerm(2, 0, 1, -2, 4130),
        };

        private static double MoonSeriesSum(MoonTerm[] terms, double d, double m,
            double mp, double f, double e, bool useCosine)
        {
            double sum = 0.0;
            for (int i = 0; i < terms.Length; i++)
            {
                MoonTerm term = terms[i];
                double argument = (term.D * d + term.M * m + term.Mp * mp + term.F * f) * Deg2Rad;
                double factor = 1.0;
                if (term.M == 1 || term.M == -1)
                    factor = e;
                else if (term.M == 2 || term.M == -2)
                    factor = e * e;
                sum += factor * term.Coeff
                    * (useCosine ? Math.Cos(argument) : Math.Sin(argument));
            }
            return sum;
        }

        private static double MoonLongitudeSum(double d, double m, double mp, double f, double e)
        {
            return MoonSeriesSum(MoonLTerms, d, m, mp, f, e, false);
        }

        private static double MoonLatitudeSum(double d, double m, double mp, double f, double e)
        {
            return MoonSeriesSum(MoonBTerms, d, m, mp, f, e, false);
        }

        /// <summary>Geocentric distance in km (Meeus 47.6 + table 47.C).
        /// NOTE: 47.C is a COSINE series (the table's arguments are arranged
        /// that way) — using sine puts the distance ~19 000 km off.</summary>
        public static double MoonDistanceKm(double jd)
        {
            double t = (jd - 2451545.0) / 36525.0;
            double lp, d, m, mp, f;
            MoonFundamentals(t, out lp, out d, out m, out mp, out f);
            double e = 1.0 - 0.002516 * t - 0.0000074 * t * t;
            return 385000.56 + MoonSeriesSum(MoonRTerms, d, m, mp, f, e, true) / 1000.0;
        }

        /// <summary>Horizontal parallax of the Moon (degrees), from the
        /// distance: sin HP = a_earth / Δ (mean HP ≈ 57′).</summary>
        public static double MoonParallaxDeg(double jd)
        {
            double distance = MoonDistanceKm(jd);
            if (distance <= 0.0)
                return 0.0;
            return Math.Asin(EarthRadiusKm / distance) / Deg2Rad;
        }

        /// <summary>
        /// Topocentric RA/Dec of the Moon (P2/R5): corrects the geocentric
        /// place for the observer's parallax (up to ~1° at the horizon, zero
        /// at the zenith). Vector method in the equatorial frame (x = vernal
        /// equinox, θ = local sidereal time): the observer sits at
        /// [cosφ·cosθ, cosφ·sinθ, sinφ] Earth radii. Render AND reduction must
        /// both use this, or the measured arc disagrees with Hc by up to 57′.
        /// </summary>
        public static bool MoonTopocentricRaDec(double jd, double latDeg, double lstDeg,
            out double raRad, out double decRad)
        {
            raRad = 0.0;
            decRad = 0.0;
            if (!MoonRaDec(jd, out double geoRa, out double geoDec))
                return false;

            double distanceKm = MoonDistanceKm(jd);
            if (distanceKm <= 0.0)
                return false;
            double delta = distanceKm / EarthRadiusKm;

            double theta = lstDeg * Deg2Rad;
            double phi = latDeg * Deg2Rad;
            double cosPhi = Math.Cos(phi);

            double gx = delta * Math.Cos(geoDec) * Math.Cos(geoRa);
            double gy = delta * Math.Cos(geoDec) * Math.Sin(geoRa);
            double gz = delta * Math.Sin(geoDec);

            double ox = cosPhi * Math.Cos(theta);
            double oy = cosPhi * Math.Sin(theta);
            double oz = Math.Sin(phi);

            double tx = gx - ox;
            double ty = gy - oy;
            double tz = gz - oz;
            double len = Math.Sqrt(tx * tx + ty * ty + tz * tz);
            if (len < 1e-9)
                return false;

            raRad = Math.Atan2(ty, tx);
            if (raRad < 0) raRad += 2 * Math.PI;
            decRad = Math.Asin(Math.Max(-1.0, Math.Min(1.0, tz / len)));
            return true;
        }

        /// <summary>
        /// Illuminated fraction (0 = new, 1 = full) from the Moon–Sun
        /// elongation: k = (1 − cos ψ)/2 with ψ the geocentric elongation
        /// (Meeus ch.48, parallax term omitted — ~1% near quarter).
        /// </summary>
        public static double MoonIllumination(double jd)
        {
            if (!MoonRaDec(jd, out double raMoon, out double decMoon))
                return 0.0;
            SunRaDec(jd, out double raSun, out double decSun);

            double cosPsi = Math.Sin(decMoon) * Math.Sin(decSun)
                + Math.Cos(decMoon) * Math.Cos(decSun) * Math.Cos(raMoon - raSun);
            if (cosPsi > 1.0) cosPsi = 1.0;
            if (cosPsi < -1.0) cosPsi = -1.0;
            return 0.5 * (1.0 - cosPsi);
        }

        /// <summary>
        /// Геоцентрические RA/Dec Солнца, видимое место (Meeus гл.25 + гл.22):
        /// средняя долгота/аномалия с T², уравнение центра (3 члена), нутация
        /// (4 главных члена), аберрация −20.496″ и истинный наклон эклиптики.
        /// Точность ~0.002–0.005° против видимого места.
        /// Вход — TT (террестриальное время): вызывающий обязан передать
        /// jd + ΔT (CELESTIAL_REALISM_PLAN P1; ΔT даёт ~6′ GHA, если забыть).
        /// </summary>
        public static void SunRaDec(double jdTt, out double raRad, out double decRad)
        {
            double t = (jdTt - 2451545.0) / 36525.0;
            double l0 = NormalizeDegrees(280.46646 + 36000.76983 * t + 0.0003032 * t * t);
            double m = NormalizeDegrees(357.52911 + 35999.05029 * t - 0.0001537 * t * t);
            double mRad = m * Deg2Rad;
            double c = (1.914602 - 0.004817 * t - 0.000014 * t * t) * Math.Sin(mRad)
                     + (0.019993 - 0.000101 * t) * Math.Sin(2.0 * mRad)
                     + 0.000289 * Math.Sin(3.0 * mRad);
            double lambdaTrue = l0 + c;

            NutationLowPrecision(t, l0 * Deg2Rad, out double dPsiArcsec, out double dEpsArcsec);

            double eps0 = 23.4392911 - 0.0130042 * t - 0.000000164 * t * t
                          + 0.000000504 * t * t * t;
            double epsTrue = eps0 + dEpsArcsec / 3600.0;
            // Видимая долгота: нутация + годичная аберрация (Meeus 25.9/25.11).
            double lambdaApp = lambdaTrue + dPsiArcsec / 3600.0 - 0.005693;
            double lambdaRad = lambdaApp * Deg2Rad;
            double epsRad = epsTrue * Deg2Rad;

            raRad = Math.Atan2(Math.Cos(epsRad) * Math.Sin(lambdaRad), Math.Cos(lambdaRad));
            if (raRad < 0) raRad += 2 * Math.PI;
            decRad = Math.Asin(Math.Sin(epsRad) * Math.Sin(lambdaRad));
        }

        /// <summary>
        /// Усечённая нутация (Meeus гл.22, 4 главных члена; STAR_SKY_PLAN §7.4):
        /// Δψ ≈ −17.20″sinΩ − 1.32″sin2L☉ − 0.23″sin2L☾ + 0.21″sin2Ω,
        /// Δε ≈ +9.20″cosΩ + 0.57″cos2L☉ + 0.10″cos2L☾ − 0.09″cos2Ω.
        /// Точность ~0.5″; L☾ (средняя долгота Луны) считается здесь, а не
        /// подменяется аномалией Солнца (ревизия v3, m1).
        /// </summary>
        internal static void NutationLowPrecision(double t, double lSunRad,
            out double dPsiArcsec, out double dEpsArcsec)
        {
            double omegaRad = NormalizeDegrees(125.04452 - 1934.136261 * t) * Deg2Rad;
            double lMoonRad = NormalizeDegrees(218.3165 + 481267.8813 * t) * Deg2Rad;
            double sinOmega = Math.Sin(omegaRad);
            double cosOmega = Math.Cos(omegaRad);
            double sin2Sun = Math.Sin(2.0 * lSunRad);
            double cos2Sun = Math.Cos(2.0 * lSunRad);
            double sin2Moon = Math.Sin(2.0 * lMoonRad);
            double cos2Moon = Math.Cos(2.0 * lMoonRad);
            double sin2Omega = Math.Sin(2.0 * omegaRad);
            double cos2Omega = Math.Cos(2.0 * omegaRad);

            dPsiArcsec = -17.20 * sinOmega - 1.32 * sin2Sun - 0.23 * sin2Moon
                         + 0.21 * sin2Omega;
            dEpsArcsec = 9.20 * cosOmega + 0.57 * cos2Sun + 0.10 * cos2Moon
                         - 0.09 * cos2Omega;
        }

        /// <summary>
        /// Прецессия IAU-1976 (Meeus гл.21, формулы 21.2–21.4) из эпохи
        /// jdFrom в jdTo. Работает для произвольных эпох (T считается от
        /// J2000, t — от jdFrom). Проверено на примере Meeus 21.a.
        /// </summary>
        public static void Precess(double raRad, double decRad,
            double jdFrom, double jdTo, out double raRadOut, out double decRadOut)
        {
            double t0 = (jdFrom - 2451545.0) / 36525.0;
            double t = (jdTo - jdFrom) / 36525.0;
            double zeta = ((2306.2181 + 1.39656 * t0 - 0.000139 * t0 * t0) * t
                         + (0.30188 - 0.000344 * t0) * t * t
                         + 0.017998 * t * t * t) / 3600.0;
            double z = ((2306.2181 + 1.39656 * t0 - 0.000139 * t0 * t0) * t
                      + (1.09468 + 0.000066 * t0) * t * t
                      + 0.018203 * t * t * t) / 3600.0;
            double theta = ((2004.3109 - 0.85330 * t0 - 0.000217 * t0 * t0) * t
                          - (0.42665 + 0.000217 * t0) * t * t
                          - 0.041833 * t * t * t) / 3600.0;

            double zetaRad = zeta * Deg2Rad;
            double thetaRad = theta * Deg2Rad;
            double cosDec = Math.Cos(decRad);
            double sinDec = Math.Sin(decRad);
            double cosA = Math.Cos(raRad + zetaRad);
            double sinA = Math.Sin(raRad + zetaRad);

            double a = cosDec * sinA;
            double b = Math.Cos(thetaRad) * cosDec * cosA - Math.Sin(thetaRad) * sinDec;
            double c = Math.Sin(thetaRad) * cosDec * cosA + Math.Cos(thetaRad) * sinDec;

            raRadOut = Math.Atan2(a, b) + z * Deg2Rad;
            if (raRadOut < 0) raRadOut += 2 * Math.PI;
            if (raRadOut >= 2 * Math.PI) raRadOut -= 2 * Math.PI;
            decRadOut = Math.Asin(Math.Max(-1.0, Math.Min(1.0, c)));
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
