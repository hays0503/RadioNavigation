using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Right-side verification readout of the sextant (SEXTANT_PLAN §13,
    /// v2.2): the eye height, the Sun and the Moon (each with its
    /// sightability status), the nav stars currently above the horizon (top N
    /// by altitude) with their azimuths, and — once a first sight is recorded
    /// — the recommended second body for a good crossing angle. The Sun row
    /// replaced the old wide "Высота солнца…" line (2026-11-04): the block's
    /// rows are ~231 units wide and always fit the column, while the separate
    /// 291-unit line ran off the screen edge. Throttled to 1 s. Extracted from
    /// SextantController (SRP: readout formatting is not device logic).
    /// </summary>
    internal sealed class SextantBodiesReadout
    {
        private const string MoonLabel = "\u041B\u0443\u043D\u0430";
        private const string EyeHeightLabel = "\u0413\u043B\u0430\u0437";
        private const string SunBelow =
            "\u0421\u043E\u043B\u043D\u0446\u0435: \u043F\u043E\u0434 \u0433\u043E\u0440\u0438\u0437\u043E\u043D\u0442\u043E\u043C";
        private const string MoonBelow =
            "\u041B\u0443\u043D\u0430: \u043F\u043E\u0434 \u0433\u043E\u0440\u0438\u0437\u043E\u043D\u0442\u043E\u043C";
        private const string MoonNew =
            "\u041B\u0443\u043D\u0430: \u043D\u043E\u0432\u0430\u044F (\u043D\u0435 \u0432\u0438\u0434\u043D\u0430)";
        private const string RecommendPrefix =
            "\u2192 \u0412\u0442\u043E\u0440\u043E\u0435 \u0441\u0432\u0435\u0442\u0438\u043B\u043E: ";

        private const float IntervalSeconds = 1f;
        // The whole eligible pool is listed (author request 2026-11-04): the
        // triple/pair forecast can pick any of the nav stars above the floor,
        // so the navigator must see all of them with their altitude/azimuth/
        // magnitude — not a top-10 that may omit a combination member. The
        // pool itself is bounded by the 57 curated nav stars.
        private const int MaxStars = 48;
        // Keep in step with SextantStarId: a new moon is not sightable.
        private const double MoonMinIllumination = 0.05;

        private readonly IPlayerPositionProvider positionProvider;

        private UnityEngine.UI.Text bodiesText;
        private string lastBodiesLine;
        // Auto-fit state (2026-11-04): the uncapped star list shrinks its font
        // when it would run into the game's bottom UI. The fit measures at the
        // BASE font only (the card's lesson: never feed the live height back).
        private float lastAvailableUnits;
        private int baseFontSize;
        private float timer;

        public SextantBodiesReadout(IPlayerPositionProvider positionProvider)
        {
            this.positionProvider = positionProvider;
        }

        /// <summary>Binds the bodies label built by the overlay constructor.</summary>
        public void BindLabels(UnityEngine.UI.Text bodies)
        {
            bodiesText = bodies;
            baseFontSize = bodies != null ? bodies.fontSize : 0;
        }

        /// <summary>Forces the block to repaint on the next update (open),
        /// including the cached forecast: a stale line or an old triple
        /// membership must not survive a reopen.</summary>
        public void Invalidate()
        {
            lastBodiesLine = null;
            timer = 0f;
            forecastLine = null;
            forecastTimer = 0f;
            forecastHasTriple = false;
            forecastTripleA = null;
            forecastTripleB = null;
            forecastTripleC = null;
        }

        /// <summary>
        /// Bodies block (P5): eye height, the Sun and the Moon, then every
        /// navigation star the recommender may pick — ONE filtered pool
        /// (alt ≥ 10°) feeds both the list and the recommendation, so a
        /// recommended body is always on screen. Rows carry magnitude (or the
        /// Moon's illuminated %) and steering arrows from the reticle.
        /// <paramref name="availableUnits"/> is the vertical room (canvas
        /// units) the block may use; the uncapped row list shrinks its font
        /// to fit it (auto-fit, 2026-11-04).
        /// </summary>
        public void UpdateBodies(float dt, float eyeHeightM,
            double firstSightAzDeg, string firstSightName,
            Vector3 pipFwd, bool canSight, float availableUnits)
        {
            if (bodiesText == null)
                return;
            timer -= dt;
            if (timer > 0f)
                return;
            timer = IntervalSeconds;

            float lat, lon;
            if (!positionProvider.TryGetPosition(out lat, out lon))
            {
                SetBodiesLine(null, availableUnits);
                return;
            }

            DateTime utc = GameTimeResolver.GetUtcNow();
            double jd = AstroMath.ToJulianDay(utc);
            double lstDeg = AstroMath.LstDeg(jd, lon);
            // The Moon shares the game sky time shift (ADR-033 v2.4).
            double moonJd = CelestialEphemeris.JdFor(CelestialBody.Moon(), utc);
            double moonLst = AstroMath.LstDeg(moonJd, lon);

            double pipAzDeg = PipAzimuthDeg(pipFwd);
            double pipAltDeg = PipAltitudeDeg(pipFwd);

            var candidates = new List<BodyRecommendation.Candidate>();
            var rows = new List<KeyValuePair<string, string>>();
            var status = new StringBuilder();

            // Eye height above sea level first (HE, meters — the value a
            // navigator needs for the dip correction the game does not model).
            // "—" when the camera height is not a sea-level height (map camera).
            status.Append(EyeHeightLabel + " " + (float.IsNaN(eyeHeightM)
                ? "\u2014"
                : eyeHeightM.ToString("F1", CultureInfo.InvariantCulture) + " \u043C"));

            AppendSun(rows, status, candidates, utc, lon, lat, pipAzDeg, pipAltDeg, canSight);
            AppendMoon(rows, status, candidates, moonJd, moonLst, lat, pipAzDeg, pipAltDeg, canSight);
            AppendStars(rows, candidates, jd, lstDeg, lat, pipAzDeg, pipAltDeg, canSight);
            AppendForecast(status, utc, lat, lon);
            if (canSight)
            {
                AppendNearReticle(status, candidates, pipAltDeg, pipAzDeg);
                AppendWaitAdvice(status, utc, lat, lon, firstSightName);
            }

            BodyRecommendation.Result rec = new BodyRecommendation.Result();
            if (!double.IsNaN(firstSightAzDeg))
                rec = BodyRecommendation.Recommend(firstSightAzDeg, candidates, firstSightName);

            var sb = new StringBuilder();
            sb.Append(status);
            for (int i = 0; i < rows.Count; i++)
            {
                sb.Append('\n');
                string rowName = rows[i].Key;
                if ((rec.Found && rowName == rec.Name) || IsTripleMember(rowName))
                    sb.Append("\u2605 ");
                sb.Append(rows[i].Value);
            }
            if (rec.Found)
            {
                sb.Append('\n').Append(RecommendPrefix).Append(rec.Name)
                  .Append(" (\u0394Az ")
                  .Append(rec.SeparationDeg.ToString("F0", CultureInfo.InvariantCulture))
                  .Append("\u00B0)");
            }

            SetBodiesLine(sb.Length > 0 ? sb.ToString() : null, availableUnits);
        }

        // P5 forecast: the twilight boundary and the best pair — or, when a
        // triple exists, the triple instead (it fixes better) — cached (the
        // star scan is heavy for a 1 Hz readout, and the recommendation
        // changes slowly). The line text itself is assembled by
        // ForecastLineText (offline-tested, no formatting here).
        private const float ForecastIntervalSeconds = 60f;

        private string forecastLine;
        private float forecastTimer;
        private float forecastLat = float.NaN;
        private float forecastLon = float.NaN;
        // Triple membership of the cached line: rows with these names get the
        // star marker (plain string compares at 1 Hz, no set allocation).
        private bool forecastHasTriple;
        private string forecastTripleA;
        private string forecastTripleB;
        private string forecastTripleC;

        private const double NearReticleDeg = 5.0;
        private const string NearReticleLabel = "\u0411\u043B\u0438\u0437\u043A\u043E \u043A \u0446\u0435\u043D\u0442\u0440\u0443";
        private const string WaitLabel = "\u0416\u0434\u0438\u0442\u0435";
        private const string WaitMinutesLabel = "\u043C\u0438\u043D";

        /// <summary>Bodies within NearReticleDeg of the aim (P5, max 3), so the
        /// navigator can pick what is actually in the ocular.</summary>
        private static void AppendNearReticle(StringBuilder status,
            List<BodyRecommendation.Candidate> candidates, double pipAltDeg, double pipAzDeg)
        {
            var near = new List<KeyValuePair<double, string>>();
            for (int i = 0; i < candidates.Count; i++)
            {
                BodyRecommendation.Candidate c = candidates[i];
                double sep = BodyRowFormatter.AngularSeparationDeg(
                    c.AltDeg, c.AzDeg, pipAltDeg, pipAzDeg);
                if (sep <= NearReticleDeg)
                    near.Add(new KeyValuePair<double, string>(sep,
                        c.Name + " (" + sep.ToString("F1", CultureInfo.InvariantCulture) + "\u00B0)"));
            }
            if (near.Count == 0)
                return;

            near.Sort((a, b) => a.Key.CompareTo(b.Key));
            status.Append('\n').Append(NearReticleLabel);
            int shown = 0;
            for (int i = 0; i < near.Count && shown < 3; i++)
            {
                status.Append(shown == 0 ? " " : ", ").Append(near[i].Value);
                shown++;
            }
        }

        /// <summary>
        /// Same-body running fix: how long until the crossing angle is usable
        /// (P5). Wired for the Sun, the canonical two-sight series; the frozen
        /// card only says "space the sights out in time".
        /// </summary>
        private static void AppendWaitAdvice(StringBuilder status, DateTime utc,
            float lat, float lon, string firstSightName)
        {
            if (string.IsNullOrEmpty(firstSightName)
                || firstSightName != CelestialBody.Sun().Name)
                return;
            if (!CelestialForecast.TryWaitMinutesForCrossing(CelestialBody.Sun(),
                    lat, lon, utc, 30.0, 6.0, out double minutes, out _))
                return;
            status.Append('\n').Append(WaitLabel).Append(' ')
                .Append(minutes.ToString("F0", CultureInfo.InvariantCulture))
                .Append(' ').Append(WaitMinutesLabel);
        }

        /// <summary>Live planning line (P5): when the nautical twilight opens
        /// or closes, and the best star pair inside the next 6 h.</summary>
        private void AppendForecast(StringBuilder status, DateTime utc, float lat, float lon)
        {
            forecastTimer -= IntervalSeconds; // called at the 1 Hz cadence
            bool moved = float.IsNaN(forecastLat)
                || Math.Abs(lat - forecastLat) > 0.5f
                || Math.Abs(lon - forecastLon) > 0.5f;
            if (forecastTimer <= 0f || moved)
            {
                forecastTimer = ForecastIntervalSeconds;
                forecastLat = lat;
                forecastLon = lon;
                forecastLine = BuildForecast(utc, lat, lon);
            }
            if (!string.IsNullOrEmpty(forecastLine))
                status.Append('\n').Append(forecastLine);
        }

        /// <summary>Live planning line (P5 + triple): when the nautical twilight
        /// opens or closes, and the best star series inside the next 6 h. The
        /// triple, when found, replaces the pair and its membership is kept
        /// for the row markers. Line assembly belongs to ForecastLineText.</summary>
        private string BuildForecast(DateTime utc, double lat, double lon)
        {
            double zenith = CelestialForecast.SunZenithDeg(lat, lon, utc);
            bool night = !double.IsNaN(zenith)
                && zenith >= CelestialForecast.NauticalZenithDeg;
            DateTime boundary;
            bool found = night
                ? CelestialForecast.TryFindSunCrossing(lat, lon, utc,
                    CelestialForecast.NauticalZenithDeg, true, 24.0, out boundary)
                : CelestialForecast.TryFindSunCrossing(lat, lon, utc,
                    CelestialForecast.NauticalZenithDeg, false, 24.0, out boundary);
            string twilight = ForecastLineText.TwilightSegment(night, found, boundary);

            forecastHasTriple = false;
            forecastTripleA = null;
            forecastTripleB = null;
            forecastTripleC = null;
            if (CelestialForecast.TryFindBestTriple(lat, lon, utc, 6.0, 10.0,
                    out CelestialForecast.TripleResult triple))
            {
                forecastHasTriple = true;
                forecastTripleA = triple.NameA;
                forecastTripleB = triple.NameB;
                forecastTripleC = triple.NameC;
                string tripleSegment = ForecastLineText.TripleSegment(
                    triple.NameA, triple.NameB, triple.NameC,
                    triple.MinSeparationDeg, triple.WhenUtc);
                return ForecastLineText.Compose(twilight, tripleSegment, null,
                    triple.NameA, triple.NameB, triple.NameC).Text;
            }

            string pair = "";
            if (CelestialForecast.TryFindBestPair(lat, lon, utc, 6.0, 10.0,
                    out string a, out string b, out double sep, out DateTime when))
                pair = ForecastLineText.PairSegment(a, b, sep, when);
            return ForecastLineText.Compose(twilight, null, pair, null, null, null).Text;
        }

        /// <summary>True when the row belongs to the cached forecast's triple
        /// (same star marker as the second-sight recommendation).</summary>
        private bool IsTripleMember(string name)
        {
            return forecastHasTriple
                && (name == forecastTripleA || name == forecastTripleB
                    || name == forecastTripleC);
        }

        /// <summary>Reticle azimuth in degrees (0 = north, +east).</summary>
        private static double PipAzimuthDeg(Vector3 pipFwd)
        {
            if (pipFwd.sqrMagnitude < 1e-6f)
                return 0.0;
            return Math.Atan2(pipFwd.x, pipFwd.z) * Mathf.Rad2Deg;
        }

        /// <summary>Reticle altitude in degrees.</summary>
        private static double PipAltitudeDeg(Vector3 pipFwd)
        {
            float y = pipFwd.y;
            if (y > 1f) y = 1f;
            if (y < -1f) y = -1f;
            return Math.Asin(y) * Mathf.Rad2Deg;
        }

        /// <summary>Moon line: status when below the horizon or new, else a row
        /// with the illuminated percentage and steering arrows. Only a
        /// sightable Moon enters the candidates.</summary>
        private static void AppendMoon(List<KeyValuePair<string, string>> rows, StringBuilder status,
            List<BodyRecommendation.Candidate> candidates,
            double jd, double lstDeg, double lat, double pipAzDeg, double pipAltDeg,
            bool withArrows)
        {
            bool hasCoords = AstroMath.MoonTopocentricRaDec(jd, lat, lstDeg,
                out double raRad, out double decRad);
            if (!hasCoords)
                return;

            AstroMath.RaDecToEnu(raRad, decRad, lstDeg, lat,
                out double altRad, out double azRad, out _, out _, out _);
            float alt = (float)(altRad * Mathf.Rad2Deg);
            float az = (float)(azRad * Mathf.Rad2Deg);
            double illumination = AstroMath.MoonIllumination(jd);

            if (illumination < MoonMinIllumination)
            {
                status.Append('\n').Append(MoonNew);
                return;
            }
            if (alt <= 0f)
            {
                status.Append('\n').Append(MoonBelow);
                return;
            }

            rows.Add(new KeyValuePair<string, string>(MoonLabel,
                withArrows
                    ? BodyRowFormatter.FormatRow(MoonLabel,
                        BodyRowFormatter.FormatPercent(illumination), alt, az,
                        BodyRowFormatter.Wrap180(az - pipAzDeg), alt - pipAltDeg)
                    : BodyRowFormatter.FormatRow(MoonLabel,
                        BodyRowFormatter.FormatPercent(illumination), alt, az)));
            candidates.Add(new BodyRecommendation.Candidate
            {
                Name = MoonLabel,
                AltDeg = alt,
                AzDeg = az
            });
        }

        /// <summary>Sun row (2026-11-04): a full row with steering arrows while
        /// the Sun is up — it is the canonical alignment reference (the arc
        /// should match its altitude) and a valid second body — and the
        /// below-horizon status otherwise. Only a Sun above the horizon enters
        /// the candidates (it cannot be sighted below).</summary>
        private static void AppendSun(List<KeyValuePair<string, string>> rows, StringBuilder status,
            List<BodyRecommendation.Candidate> candidates,
            DateTime utc, float lonDeg, float lat,
            double pipAzDeg, double pipAltDeg, bool withArrows)
        {
            float alt, az;
            if (!TrySunAltAz(utc, lonDeg, lat, out alt, out az))
                return;

            if (alt <= 0f)
            {
                status.Append('\n').Append(SunBelow);
                return;
            }

            string name = CelestialBody.Sun().Name;
            rows.Add(new KeyValuePair<string, string>(name,
                withArrows
                    ? BodyRowFormatter.FormatRow(name, "", alt, az,
                        BodyRowFormatter.Wrap180(az - pipAzDeg), alt - pipAltDeg)
                    : BodyRowFormatter.FormatRow(name, "", alt, az)));
            candidates.Add(new BodyRecommendation.Candidate
            {
                Name = name,
                AltDeg = alt,
                AzDeg = az
            });
        }

        /// <summary>Sun altitude/azimuth from the calibrated ephemeris. Its LST
        /// must come from the CALIBRATED jd: the caller's plain-jd lstDeg
        /// differs by the SUNDRIFT shift (~1.65°), which would skew the row's
        /// azimuth (live the calibrated value matches the game sky within
        /// ~0.001°).</summary>
        private static bool TrySunAltAz(DateTime utc, float lonDeg, float lat,
            out float alt, out float az)
        {
            alt = 0f;
            az = 0f;
            if (!CelestialEphemeris.Compute(CelestialBody.Sun(), utc,
                    out double raRad, out double decRad))
                return false;
            double sunLstDeg = AstroMath.LstDeg(
                CelestialEphemeris.JdFor(CelestialBody.Sun(), utc), lonDeg);
            AstroMath.RaDecToEnu(raRad, decRad, sunLstDeg, lat,
                out double altRad, out double azRad, out _, out _, out _);
            alt = (float)(altRad * Mathf.Rad2Deg);
            az = (float)(azRad * Mathf.Rad2Deg);
            return true;
        }

        private struct StarRow
        {
            public string Name;
            public float Alt;
            public float Az;
            public float Vmag;
        }

        /// <summary>
        /// Navigation stars above the unified altitude floor, highest first,
        /// capped at MaxStars (48 = effectively the whole pool, author
        /// request 2026-11-04: every combination candidate is shown with its
        /// data). F226: the catalog keeps every catalogue row, so a multiple
        /// system shares one HIP across rows; show it once. Hip=0 is the
        /// no-HIP sentinel and must NOT collapse distinct stars. The SHOWN
        /// rows are exactly the recommendation pool (P5: no "recommended but
        /// invisible" body).
        /// </summary>
        private static void AppendStars(List<KeyValuePair<string, string>> rows,
            List<BodyRecommendation.Candidate> candidates,
            double jd, double lstDeg, float lat, double pipAzDeg, double pipAltDeg,
            bool withArrows)
        {
            var entries = new List<StarRow>();
            var seenHips = new HashSet<int>();

            foreach (StarEntry star in StarCatalog.Stars)
            {
                if (!star.IsNav)
                    continue;
                if (star.Hip != 0 && !seenHips.Add(star.Hip))
                    continue;
                // Precess the catalog row (epoch 1926.0) to the sight date (P1b).
                CelestialEphemeris.PrecessCatalog(star.RaDeg, star.DecDeg, jd,
                    out double raRad, out double decRad);
                AstroMath.RaDecToEnu(raRad, decRad, lstDeg, lat,
                    out double altRad, out double azRad, out _, out _, out _);
                float alt = (float)(altRad * Mathf.Rad2Deg);
                if (alt < BodyRecommendation.MinAltitudeDeg)
                    continue; // unified floor: shown == recommendable (P5)
                entries.Add(new StarRow
                {
                    Name = NavStarNames.TryGetName(star.Hip, out string starName)
                        ? starName
                        : "HIP " + star.Hip.ToString(CultureInfo.InvariantCulture),
                    Alt = alt,
                    Az = (float)(azRad * Mathf.Rad2Deg),
                    Vmag = star.Vmag
                });
            }

            entries.Sort((a, b) => b.Alt.CompareTo(a.Alt));
            int shown = 0;
            for (int i = 0; i < entries.Count && shown < MaxStars; i++)
            {
                StarRow row = entries[i];
                rows.Add(new KeyValuePair<string, string>(row.Name,
                    withArrows
                        ? BodyRowFormatter.FormatRow(row.Name,
                            BodyRowFormatter.FormatMagnitude(row.Vmag), row.Alt, row.Az,
                            BodyRowFormatter.Wrap180(row.Az - pipAzDeg), row.Alt - pipAltDeg)
                        : BodyRowFormatter.FormatRow(row.Name,
                            BodyRowFormatter.FormatMagnitude(row.Vmag), row.Alt, row.Az)));
                candidates.Add(new BodyRecommendation.Candidate
                {
                    Name = row.Name,
                    AltDeg = row.Alt,
                    AzDeg = row.Az
                });
                shown++;
            }
        }

        /// <summary>Writes the block text and fits its font (2026-11-04): the
        /// star list is uncapped, so at high UI scales the rows would run into
        /// the game's bottom UI. The measurement is always taken at the BASE
        /// font and the fitted size is a pure function of (height, available)
        /// — feeding the live height back would make the font oscillate (the
        /// card's lesson). Re-runs only on a text or room change.</summary>
        private void SetBodiesLine(string line, float availableUnits)
        {
            if (line == lastBodiesLine && availableUnits == lastAvailableUnits)
                return;
            lastBodiesLine = line;
            lastAvailableUnits = availableUnits;

            bodiesText.text = line ?? "";
            if (baseFontSize <= 0)
                return;
            bodiesText.fontSize = baseFontSize;
            int fitted = FixCardLayout.FontSize(bodiesText.preferredHeight,
                availableUnits, baseFontSize, FixCardLayout.MinFontSize);
            if (fitted != bodiesText.fontSize)
                bodiesText.fontSize = fitted;
        }
    }
}
