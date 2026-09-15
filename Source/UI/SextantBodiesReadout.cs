using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Right-side verification readout of the sextant (SEXTANT_PLAN §13):
    /// the own Meeus sun altitude line and, below it, the eye height, the
    /// Moon altitude and the nav stars currently above the horizon (top N
    /// by altitude). Throttled to 1 s — bodies move far too slowly to
    /// justify per-frame strings. Extracted from SextantController (SRP:
    /// readout formatting is not device logic). Owns the throttle timer
    /// and the change-detection caches; the controller binds the labels.
    /// </summary>
    internal sealed class SextantBodiesReadout
    {
        private const string SunAltPrefix =
            "\u0412\u044B\u0441\u043E\u0442\u0430 \u0441\u043E\u043B\u043D\u0446\u0430 \u043D\u0430\u0434 \u0443\u0440\u043E\u0432\u043D\u0435\u043C \u0433\u043E\u0440\u0438\u0437\u043E\u043D\u0442\u0430: ";
        private const string MoonLabel = "\u041B\u0443\u043D\u0430";
        private const string EyeHeightLabel = "\u0413\u043B\u0430\u0437";

        private const float IntervalSeconds = 1f;
        private const int MaxStars = 10;

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
        /// Bodies block: eye height, Moon, then nav stars top-N by altitude.
        /// </summary>
        public void UpdateBodies(float dt, float eyeHeightM)
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

            float moonAlt;
            bool moonUp = TryGetMoonAlt(jd, lstDeg, lat, out moonAlt);
            var entries = BuildStarEntries(lstDeg, lat);

            // Eye height above sea level first (HE, meters - the value a
            // navigator needs for the dip correction), then the Moon, then
            // stars: top N by altitude.
            var sb = new StringBuilder();
            sb.Append(EyeHeightLabel + " " +
                eyeHeightM.ToString("F1", CultureInfo.InvariantCulture) + " \u043C");
            if (moonUp)
            {
                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append(MoonLabel + " " + FormatAlt(moonAlt));
            }
            int shown = 0;
            for (int i = 0; i < entries.Count && shown < MaxStars; i++)
            {
                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append(entries[i].Value);
                shown++;
            }
            SetBodiesLine(sb.Length > 0 ? sb.ToString() : null);
        }

        /// <summary>
        /// Visible navigation stars as (altitude, "name alt") sorted by
        /// altitude descending. F226: the catalog keeps every catalogue row,
        /// so a multiple system shares one HIP across rows; show it once.
        /// Hip=0 is the no-HIP sentinel and must NOT collapse distinct stars.
        /// </summary>
        private static List<KeyValuePair<float, string>> BuildStarEntries(double lstDeg, float lat)
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
                string name = NavStarNames.TryGetName(star.Hip, out string starName)
                    ? starName
                    : "HIP " + star.Hip.ToString(CultureInfo.InvariantCulture);
                entries.Add(new KeyValuePair<float, string>(alt, name + " " + FormatAlt(alt)));
            }

            entries.Sort((a, b) => b.Key.CompareTo(a.Key));
            return entries;
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

        /// <summary>Own Meeus moon altitude (negative below the horizon).</summary>
        private static bool TryGetMoonAlt(double jd, double lstDeg, double lat, out float altDeg)
        {
            altDeg = 0f;
            if (!AstroMath.MoonRaDec(jd, out double raRad, out double decRad))
                return false;
            AstroMath.RaDecToEnu(raRad, decRad, lstDeg, lat,
                out double altRad, out _, out _, out _, out _);
            altDeg = (float)(altRad * Mathf.Rad2Deg);
            return true;
        }
    }
}
