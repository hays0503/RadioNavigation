using System;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// One recorded sight, frozen at the moment the player pressed the
    /// record key (F-Tafel Beobachtungsblatt block). Holds the dead-reckoned
    /// position the navigator believed he was at, plus the full ephemeris
    /// chain of the German worksheet: gamma (GHA Aries), SHA, GHA, lambda_g,
    /// LHA, delta, h_b (measured), h1 (computed Hc), Az and the intercept
    /// a = Ho - Hc. Ephemeris fields come from AstroMath (Meeus) through
    /// CelestialEphemeris, which routes the Sun (calibrated) and the
    /// Moon/stars (plain UTC) to the right source.
    /// </summary>
    public sealed class SextantObservation
    {
        public DateTime Utc;
        public double HoDeg;

        public double DrLatDeg;
        public double DrLonDeg;

        // Body identity (SEXTANT_PLAN v2.2): the sight may be the Sun, the
        // Moon or a catalog star. BodyRaDeg/BodyDecDeg freeze the ephemeris
        // used for the fix refinement, so RecomputeHcAz is body-agnostic.
        public CelestialBodyKind BodyKind;
        public string BodyName;
        public int BodyHip;
        public double BodyRaDeg;
        public double BodyDecDeg;

        public double GhaAriesDeg;
        public double ShaBodyDeg;
        public double GhaBodyDeg;
        public double LhaDeg;
        public double DecDeg;

        public double HcDeg;
        public double AzDeg;
        public double InterceptNm;

        /// <summary>LHA quadrant I..IV (1..4) of the F-Tafel compass rose.</summary>
        public int Quadrant;

        /// <summary>F-Tafel gleichnam.t rule: LHA I,II -> West; III,IV -> Ost.</summary>
        public bool QuadrantWest;

        /// <summary>Reconstructs the body identity for display (map tooltip,
        /// card) from the frozen observation fields.</summary>
        public CelestialBody ToBody()
        {
            switch (BodyKind)
            {
                case CelestialBodyKind.Moon:
                    return CelestialBody.Moon();
                case CelestialBodyKind.Star:
                    return CelestialBody.Star(BodyHip, BodyName);
                default:
                    return CelestialBody.Sun();
            }
        }
    }

    /// <summary>
    /// A position line (Standlinie): straight line through the tangent point
    /// perpendicular to the body's azimuth. Stored as tangent point (lat/lon)
    /// plus azimuth, so Versetzung is a plain shift of the tangent point.
    /// </summary>
    public struct LineOfPosition
    {
        public double TangentLatDeg;
        public double TangentLonDeg;
        public double AzDeg;
    }

    /// <summary>
    /// Two-observation running fix (F-Tafel "two sights with Versetzung"):
    /// LOP1 -> transfer by course/speed over the sight interval -> intersect
    /// with LOP2 -> FIX. The two sights may be any bodies (Sun, Moon, star)
    /// or the same body twice; simultaneous star sights have dt≈0 and no
    /// transfer. Because Marcq St. Hilaire is a linearization around the
    /// assumed position, the fix is refined by recomputing Hc/Az at the
    /// previous fix (default 2 iterations). Pure math, no Unity -
    /// unit-tested offline.
    /// </summary>
    public sealed class RunningFixCalculator
    {
        public SextantObservation Observation1 { get; private set; }
        public SextantObservation Observation2 { get; private set; }

        public bool CanSolve
        {
            get { return Observation1 != null && Observation2 != null; }
        }

        /// <summary>First record -> observation 1, second -> observation 2,
        /// any later record replaces observation 2.</summary>
        public void Record(SextantObservation observation)
        {
            if (observation == null)
                return;
            if (Observation1 == null)
                Observation1 = observation;
            else
                Observation2 = observation;
        }

        public void Reset()
        {
            Observation1 = null;
            Observation2 = null;
        }

        /// <summary>
        /// Full chain: build both Standlinien, transfer LOP1 by the ship's
        /// movement between the sights, intersect, then refine. Returns false
        /// when fewer than two sights are recorded or the lines are parallel.
        /// </summary>
        public bool CalculateFix(double courseDeg, double speedKn, int iterations,
            out double fixLatDeg, out double fixLonDeg)
        {
            fixLatDeg = 0.0;
            fixLonDeg = 0.0;
            if (!CanSolve)
                return false;

            double dtHours = (Observation2.Utc - Observation1.Utc).TotalHours;
            if (dtHours < 0.0)
                dtHours = 0.0;
            double transferNm = speedKn * dtHours;

            LineOfPosition lop1 = TranslateLop(BuildLop(Observation1), courseDeg, transferNm);
            LineOfPosition lop2 = BuildLop(Observation2);

            if (!IntersectLops(lop1, lop2, out fixLatDeg, out fixLonDeg))
                return false;

            // Marcq St. Hilaire refinement: recompute Hc/Az at the current
            // fix for both sight times, rebuild the intercepts and intersect
            // again. Sight 1 must be evaluated at the ship's position at t1
            // (fix minus the run between the sights) and its Standlinie then
            // transferred forward again; sight 2 is evaluated at the fix.
            for (int i = 1; i < iterations; i++)
            {
                double backLat = fixLatDeg -
                    transferNm * Math.Cos(courseDeg * Deg2Rad) / 60.0;
                double backLon = fixLonDeg -
                    transferNm * Math.Sin(courseDeg * Deg2Rad) /
                    (60.0 * Math.Cos(fixLatDeg * Deg2Rad));

                double hc1, az1, hc2, az2;
                RecomputeHcAz(Observation1, backLat, backLon, out hc1, out az1);
                RecomputeHcAz(Observation2, fixLatDeg, fixLonDeg, out hc2, out az2);
                LineOfPosition refined1 = TranslateLop(BuildLopFromSight(
                    Observation1.HoDeg, hc1, az1, backLat, backLon),
                    courseDeg, transferNm);
                LineOfPosition refined2 = BuildLopFromSight(
                    Observation2.HoDeg, hc2, az2, fixLatDeg, fixLonDeg);
                double lat, lon;
                if (!IntersectLops(refined1, refined2, out lat, out lon))
                    break;
                fixLatDeg = lat;
                fixLonDeg = lon;
            }
            return true;
        }

        public bool CalculateFix(double courseDeg, double speedKn,
            out double fixLatDeg, out double fixLonDeg)
        {
            return CalculateFix(courseDeg, speedKn, 2, out fixLatDeg, out fixLonDeg);
        }

        /// <summary>Standlinie from a recorded observation (intercept around
        /// the dead-reckoned position).</summary>
        public static LineOfPosition BuildLop(SextantObservation observation)
        {
            return BuildLopAt(observation, observation.DrLatDeg, observation.DrLonDeg);
        }

        /// <summary>Standlinie with the intercept taken around an arbitrary
        /// assumed position (used by the fix refinement pass).</summary>
        public static LineOfPosition BuildLopAt(SextantObservation observation,
            double assumedLatDeg, double assumedLonDeg)
        {
            return BuildLopFromSight(observation.HoDeg, observation.HcDeg,
                observation.AzDeg, assumedLatDeg, assumedLonDeg);
        }

        /// <summary>Standlinie from a sight pair (Ho, Hc, Az) around an
        /// assumed position: tangent point = assumed + a·û(a=Ho−Hc).</summary>
        private static LineOfPosition BuildLopFromSight(double hoDeg, double hcDeg,
            double azDeg, double assumedLatDeg, double assumedLonDeg)
        {
            double aNm = (hoDeg - hcDeg) * 60.0;
            double azRad = azDeg * Deg2Rad;
            double dNorthNm = aNm * Math.Cos(azRad);
            double dEastNm = aNm * Math.Sin(azRad);

            LineOfPosition lop;
            lop.TangentLatDeg = assumedLatDeg + dNorthNm / 60.0;
            lop.TangentLonDeg = assumedLonDeg +
                dEastNm / (60.0 * Math.Cos(assumedLatDeg * Deg2Rad));
            lop.AzDeg = azDeg;
            return lop;
        }

        /// <summary>Versetzung: shift a Standlinie by the run between the
        /// sights (course over ground, distance in nautical miles).</summary>
        public static LineOfPosition TranslateLop(LineOfPosition lop,
            double courseDeg, double distanceNm)
        {
            double courseRad = courseDeg * Deg2Rad;
            double dNorthNm = distanceNm * Math.Cos(courseRad);
            double dEastNm = distanceNm * Math.Sin(courseRad);

            lop.TangentLatDeg += dNorthNm / 60.0;
            lop.TangentLonDeg += dEastNm /
                (60.0 * Math.Cos(lop.TangentLatDeg * Deg2Rad));
            return lop;
        }

        /// <summary>
        /// Intersection of two Standlinien in the local (East, North) nautical
        /// mile plane, referenced to the mean latitude of the tangent points.
        /// Returns false for (near-)parallel lines.
        /// </summary>
        public static bool IntersectLops(LineOfPosition lop1, LineOfPosition lop2,
            out double fixLatDeg, out double fixLonDeg)
        {
            fixLatDeg = 0.0;
            fixLonDeg = 0.0;

            double refLat = 0.5 * (lop1.TangentLatDeg + lop2.TangentLatDeg);
            double refCos = Math.Cos(refLat * Deg2Rad);
            if (Math.Abs(refCos) < 1e-9)
                return false;

            // Local plane, anchor = tangent point of LOP2.
            double e1 = (lop1.TangentLonDeg - lop2.TangentLonDeg) * 60.0 * refCos;
            double n1 = (lop1.TangentLatDeg - lop2.TangentLatDeg) * 60.0;

            double az1 = lop1.AzDeg * Deg2Rad;
            double az2 = lop2.AzDeg * Deg2Rad;
            // Azimuth unit vector u = (sinAz, cosAz); the line runs along the
            // perpendicular t = (cosAz, -sinAz).
            double t1x = Math.Cos(az1), t1y = -Math.Sin(az1);
            double t2x = Math.Cos(az2), t2y = -Math.Sin(az2);

            double cross = t1x * t2y - t1y * t2x;
            if (Math.Abs(cross) < 1e-9)
                return false;

            // P1 + s*t1 = P2 + r*t2 with P2 = (0,0): solve for s.
            double s = (n1 * t2x - e1 * t2y) / cross;

            double fixE = e1 + s * t1x;
            double fixN = n1 + s * t1y;

            fixLatDeg = lop2.TangentLatDeg + fixN / 60.0;
            fixLonDeg = lop2.TangentLonDeg + fixE / (60.0 * refCos);
            return true;
        }

        /// <summary>
        /// Angle between the two Standlinien (0..90°). Lines are undirected
        /// and perpendicular to their azimuths, so this is the normalized
        /// azimuth difference. Shallow crossings amplify small intercept
        /// errors by roughly 1/sin(θ) — the fix error indicator on the card.
        /// </summary>
        public static double CrossingAngleDeg(SextantObservation o1, SextantObservation o2)
        {
            double d = Math.Abs(Normalize360(o1.AzDeg - o2.AzDeg));
            if (d > 180.0)
                d = 360.0 - d;
            if (d > 90.0)
                d = 180.0 - d;
            return d;
        }

        /// <summary>Below this crossing angle the LOP intersection amplifies
        /// the intercept errors too much to publish a fix (1/sin(1°) ≈ 57);
        /// the card still warns "слабо" between 1° and 15°.</summary>
        public const double MinUsableCrossingAngleDeg = 1.0;

        public static bool IsCrossingUsable(double crossingAngleDeg)
        {
            return crossingAngleDeg >= MinUsableCrossingAngleDeg;
        }

        /// <summary>Great-circle distance fix -> reference, in NM (error from
        /// the dead-reckoned position shown on the panel).</summary>
        public static double FixErrorNm(double fixLatDeg, double fixLonDeg,
            double refLatDeg, double refLonDeg)
        {
            double lat1 = fixLatDeg * Deg2Rad, lat2 = refLatDeg * Deg2Rad;
            double dLat = lat2 - lat1;
            double dLon = (refLonDeg - fixLonDeg) * Deg2Rad;
            double a = Math.Sin(dLat / 2.0) * Math.Sin(dLat / 2.0) +
                       Math.Cos(lat1) * Math.Cos(lat2) *
                       Math.Sin(dLon / 2.0) * Math.Sin(dLon / 2.0);
            double c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
            return NauticalMilePerRadian * c;
        }

        /// <summary>
        /// Builds a full worksheet observation from the body ephemeris
        /// (AstroMath, Meeus): gamma = GHA Aries, GHA body = gamma - RA,
        /// SHA = GHA - gamma, LHA = GHA + lambda_g, Hc/Az at the
        /// dead-reckoned position, intercept a = (Ho - Hc) * 60.
        /// </summary>
        public static SextantObservation BuildObservation(DateTime utc,
            double hoDeg, double drLatDeg, double drLonDeg)
        {
            return BuildObservation(utc, hoDeg, drLatDeg, drLonDeg, CelestialBody.Sun());
        }

        /// <summary>Body-aware overload (SEXTANT_PLAN v2.2): the Sun is
        /// calibrated, the Moon and stars use plain UTC — see
        /// CelestialEphemeris.</summary>
        public static SextantObservation BuildObservation(DateTime utc,
            double hoDeg, double drLatDeg, double drLonDeg, CelestialBody body)
        {
            double jd = CelestialEphemeris.JdFor(body, utc);
            double ghaAries = Normalize360(AstroMath.GmstDeg(jd));

            double raRad, decRad;
            if (!CelestialEphemeris.Compute(body, utc, out raRad, out decRad))
            {
                // Defensive: an unknown body falls back to the sun ephemeris
                // so the record still carries a self-consistent chain.
                AstroMath.SunRaDec(jd, out raRad, out decRad);
            }
            double raDeg = Normalize360(raRad * Rad2Deg);
            double decDeg = decRad * Rad2Deg;

            double ghaBody = Normalize360(ghaAries - raDeg);
            double shaBody = Normalize360(ghaBody - ghaAries);
            double lha = Normalize360(ghaBody + drLonDeg);

            double lstDeg = AstroMath.LstDeg(jd, drLonDeg);
            double altRad, azRad, e, n, u;
            AstroMath.RaDecToEnu(raRad, decRad, lstDeg, drLatDeg,
                out altRad, out azRad, out e, out n, out u);
            double hcDeg = altRad * Rad2Deg;
            double azDeg = azRad * Rad2Deg;

            SextantObservation obs = new SextantObservation();
            obs.Utc = utc;
            obs.HoDeg = hoDeg;
            obs.DrLatDeg = drLatDeg;
            obs.DrLonDeg = drLonDeg;
            obs.BodyKind = body.Kind;
            obs.BodyName = body.Name;
            obs.BodyHip = body.Hip;
            obs.BodyRaDeg = raDeg;
            obs.BodyDecDeg = decDeg;
            obs.GhaAriesDeg = ghaAries;
            obs.ShaBodyDeg = shaBody;
            obs.GhaBodyDeg = ghaBody;
            obs.LhaDeg = lha;
            obs.DecDeg = decDeg;
            obs.HcDeg = hcDeg;
            obs.AzDeg = azDeg;
            obs.InterceptNm = (hoDeg - hcDeg) * 60.0;
            obs.Quadrant = (int)(lha / 90.0) + 1;
            if (obs.Quadrant > 4)
                obs.Quadrant = 4;
            obs.QuadrantWest = obs.Quadrant <= 2;
            return obs;
        }

        /// <summary>Hc/Az of the recorded body at an arbitrary position and
        /// the observation's frozen time (fix refinement pass). Uses the
        /// frozen BodyRa/BodyDec, so it works for any body; the ephemeris
        /// time is calibrated only for the Sun.</summary>
        public static void RecomputeHcAz(SextantObservation observation,
            double latDeg, double lonDeg, out double hcDeg, out double azDeg)
        {
            double jd = CelestialEphemeris.JdForKind(observation.BodyKind, observation.Utc);
            double lstDeg = AstroMath.LstDeg(jd, lonDeg);
            double raRad = observation.BodyRaDeg * Deg2Rad;
            double decRad = observation.BodyDecDeg * Deg2Rad;
            double altRad, azRad, e, n, u;
            AstroMath.RaDecToEnu(raRad, decRad, lstDeg, latDeg,
                out altRad, out azRad, out e, out n, out u);
            hcDeg = altRad * Rad2Deg;
            azDeg = azRad * Rad2Deg;
        }

        public static double Normalize360(double deg)
        {
            double v = deg % 360.0;
            if (v < 0.0)
                v += 360.0;
            return v;
        }

        private const double Deg2Rad = Math.PI / 180.0;
        private const double Rad2Deg = 180.0 / Math.PI;
        private const double NauticalMilePerRadian = 60.0 * 180.0 / Math.PI;
    }
}
