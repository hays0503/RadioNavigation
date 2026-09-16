using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Right-side verification readout of the sextant (SEXTANT_PLAN §13,
    /// v2.2): the own Meeus sun altitude line and, below it, the eye height,
    /// the Moon (with its sightability status), the nav stars currently
    /// above the horizon (top N by altitude) with their azimuths, and — once
    /// a first sight is recorded — the recommended second body for a good
    /// crossing angle. Throttled to 1 s. Extracted from SextantController
    /// (SRP: readout formatting is not device logic).
    /// </summary>
    internal sealed class SextantBodiesReadout
    {
        private const string SunAltPrefix =
            "\u0412\u044B\u0441\u043E\u0442\u0430 \u0441\u043E\u043B\u043D\u0446\u0430 \u043D\u0430\u0434 \u0443\u0440\u043E\u0432\u043D\u0435\u043C \u0433\u043E\u0440\u0438\u0437\u043E\u043D\u0442\u0430: ";
        private const string MoonLabel = "\u041B\u0443\u043D\u0430";
        private const string EyeHeightLabel = "\u0413\u043B\u0430\u0437";
        private const string MoonBelow =
            "\u041B\u0443\u043D\u0430: \u043F\u043E\u0434 \u0433\u043E\u0440\u0438\u0437\u043E\u043D\u0442\u043E\u043C";
        private const string MoonNew =
            "\u041B\u0443\u043D\u0430: \u043D\u043E\u0432\u0430\u044F (\u043D\u0435 \u0432\u0438\u0434\u043D\u0430)";
        private const string RecommendPrefix =
            "\u2192 \u0412\u0442\u043E\u0440\u043E\u0435 \u0441\u0432\u0435\u0442\u0438\u043B\u043E: ";

        private const float IntervalSeconds = 1f;
        private const int MaxStars = 10;
        // Keep in step with SextantStarId: a new moon is not sightable.
        private const double MoonMinIllumination = 0.05;

        private readonly IPlayerPositionProvider positionProvider;

        private UnityEngine.UI.Text sunAltText;
        private UnityEngine.UI.Text bodiesText;
        private string lastSunAltLine;
        private string lastBodiesLine;
        private float timer;

        public SextantBodiesReadout(IPlayerPositionProvider positionProvider)
        {
            this.positionProvider = positionProvider;
        }

        /// <summary>Binds the labels built by the overlay constructor.</summary>
        public void BindLabels(UnityEngine.UI.Text sunAlt, UnityEngine.UI.Text bodies)
        {
            sunAltText = sunAlt;
            bodiesText = bodies;
        }

        /// <summary>Forces both lines to repaint on the next update (open).</summary>
        public void Invalidate()
        {
            lastSunAltLine = null;
            lastBodiesLine = null;
            timer = 0f;
        }

        /// <summary>
        /// Sun altitude line: own Meeus altitude as the reference the arc
        /// should match at alignment.
        /// </summary>
        public void UpdateSunLine(bool available, float sunAltDeg)
        {
            if (sunAltText == null)
                return;
            string line = available
                ? SunAltPrefix + sunAltDeg.ToString("F1", CultureInfo.InvariantCulture) + "\u00B0"
                : SunAltPrefix + "\u2014";
            if (line != lastSunAltLine)
            {
                sunAltText.text = line;
                lastSunAltLine = line;
            }
        }

        /// <summary>
        /// Bodies block: eye height, Moon status, nav stars with azimuths,
        /// then the recommended second body once a first sight exists.
        /// </summary>
        public void UpdateBodies(float dt, float eyeHeightM,
            double firstSightAzDeg, string firstSightName)
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
                SetBodiesLine(null);
                return;
            }

            DateTime utc = GameTimeResolver.GetUtcNow();
            double jd = AstroMath.ToJulianDay(utc);
            double lstDeg = AstroMath.LstDeg(jd, lon);
            // The Moon shares the game sky time shift (ADR-033 v2.4).
            double moonJd = CelestialEphemeris.JdFor(CelestialBody.Moon(), utc);
            double moonLst = AstroMath.LstDeg(moonJd, lon);

            var candidates = new List<BodyRecommendation.Candidate>();
            var sb = new StringBuilder();

            // Eye height above sea level first (HE, meters - the value a
            // navigator needs for the dip correction the game does not model),
            // then the Moon, then stars: top N by altitude.
            sb.Append(EyeHeightLabel + " " +
                eyeHeightM.ToString("F1", CultureInfo.InvariantCulture) + " \u043C");

            AddSunCandidate(candidates, utc, lstDeg, lat);
            AppendMoon(sb, candidates, moonJd, moonLst, lat);
            AppendStars(sb, candidates, lstDeg, lat);

            if (!double.IsNaN(firstSightAzDeg))
            {
                BodyRecommendation.Result rec =
                    BodyRecommendation.Recommend(firstSightAzDeg, candidates, firstSightName);
                if (rec.Found)
                {
                    sb.Append('\n').Append(RecommendPrefix).Append(rec.Name)
                      .Append(" (\u0394Az ")
                      .Append(rec.SeparationDeg.ToString("F0", CultureInfo.InvariantCulture))
                      .Append("\u00B0)");
                }
            }

            SetBodiesLine(sb.Length > 0 ? sb.ToString() : null);
        }

        /// <summary>Moon line: status when below the horizon or new, else
        /// altitude + azimuth. Only a sightable Moon enters the candidates.</summary>
        private static void AppendMoon(StringBuilder sb, List<BodyRecommendation.Candidate> candidates,
            double jd, double lstDeg, double lat)
        {
            bool hasCoords = AstroMath.MoonRaDec(jd, out double raRad, out double decRad);
            if (!hasCoords)
                return;

            AstroMath.RaDecToEnu(raRad, decRad, lstDeg, lat,
                out double altRad, out double azRad, out _, out _, out _);
            float alt = (float)(altRad * Mathf.Rad2Deg);
            float az = (float)(azRad * Mathf.Rad2Deg);

            if (AstroMath.MoonIllumination(jd) < MoonMinIllumination)
            {
                sb.Append('\n').Append(MoonNew);
                return;
            }
            if (alt <= 0f)
            {
                sb.Append('\n').Append(MoonBelow);
                return;
            }

            sb.Append('\n').Append(MoonLabel + " " + FormatAlt(alt) + " Az " + FormatAz(az));
            candidates.Add(new BodyRecommendation.Candidate
            {
                Name = MoonLabel,
                AltDeg = alt,
                AzDeg = az
            });
        }

        /// <summary>The Sun is a valid second body in daytime; it is not
        /// printed here (its own altitude line sits above).</summary>
        private static void AddSunCandidate(List<BodyRecommendation.Candidate> candidates,
            DateTime utc, double lstDeg, double lat)
        {
            if (!CelestialEphemeris.Compute(CelestialBody.Sun(), utc,
                    out double raRad, out double decRad))
                return;
            AstroMath.RaDecToEnu(raRad, decRad, lstDeg, lat,
                out double altRad, out double azRad, out _, out _, out _);
            float alt = (float)(altRad * Mathf.Rad2Deg);
            if (alt <= 0f)
                return;
            candidates.Add(new BodyRecommendation.Candidate
            {
                Name = CelestialBody.Sun().Name,
                AltDeg = alt,
                AzDeg = (float)(azRad * Mathf.Rad2Deg)
            });
        }

        /// <summary>
        /// Visible navigation stars as "name alt Az az" sorted by altitude
        /// descending. F226: the catalog keeps every catalogue row, so a
        /// multiple system shares one HIP across rows; show it once. Hip=0 is
        /// the no-HIP sentinel and must NOT collapse distinct stars.
        /// </summary>
        private static void AppendStars(StringBuilder sb, List<BodyRecommendation.Candidate> candidates,
            double lstDeg, float lat)
        {
            var entries = new List<KeyValuePair<float, string>>();
            var seenHips = new HashSet<int>();

            foreach (StarEntry star in StarCatalog.Stars)
            {
                if (!star.IsNav)
                    continue;
                if (star.Hip != 0 && !seenHips.Add(star.Hip))
                    continue;
                double altRad, azRad, e, n, u;
                AstroMath.RaDecToEnu(star.RaDeg * Mathf.Deg2Rad, star.DecDeg * Mathf.Deg2Rad,
                    lstDeg, lat, out altRad, out azRad, out e, out n, out u);
                float alt = (float)(altRad * Mathf.Rad2Deg);
                if (alt <= 0f)
                    continue;
                float az = (float)(azRad * Mathf.Rad2Deg);
                string name = NavStarNames.TryGetName(star.Hip, out string starName)
                    ? starName
                    : "HIP " + star.Hip.ToString(CultureInfo.InvariantCulture);
                entries.Add(new KeyValuePair<float, string>(alt,
                    name + " " + FormatAlt(alt) + " Az " + FormatAz(az)));
                candidates.Add(new BodyRecommendation.Candidate
                {
                    Name = name,
                    AltDeg = alt,
                    AzDeg = az
                });
            }

            entries.Sort((a, b) => b.Key.CompareTo(a.Key));
            int shown = 0;
            for (int i = 0; i < entries.Count && shown < MaxStars; i++)
            {
                sb.Append('\n').Append(entries[i].Value);
                shown++;
            }
        }

        private void SetBodiesLine(string line)
        {
            if (line == lastBodiesLine)
                return;
            bodiesText.text = line ?? "";
            lastBodiesLine = line;
        }

        private static string FormatAlt(float altDeg)
        {
            return altDeg.ToString("F1", CultureInfo.InvariantCulture) + "\u00B0";
        }

        private static string FormatAz(float azDeg)
        {
            return azDeg.ToString("F0", CultureInfo.InvariantCulture) + "\u00B0";
        }
    }
}
