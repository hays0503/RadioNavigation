using System;
using System.Globalization;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Star/sun identification for the sextant (SEXTANT_PLAN §2): a 0.5°
    /// cone around the PIP forward direction, a 2.5 s hold to confirm, and
    /// the identified body name the worksheet gates its sights on. The
    /// sun aims at the GAME sun direction (SUNDRIFT sep would never fit
    /// the cone against our own ephemeris); stars resolve through the
    /// catalog by the player's sandbox position. Extracted from
    /// SextantController (SRP: the identification cycle is not camera or
    /// overlay logic). Owns the tick timer, the hold timer and the names.
    /// </summary>
    internal sealed class SextantStarId
    {
        private const string LogPrefix = "[RadioNav][Sextant]";
        private const string SunName = "\u0421\u043E\u043B\u043D\u0446\u0435";

        private const float ConeDeg = 0.5f;
        // v1.12: 2.5 s (was 5 s) — author request, faster sight workflow.
        private const float HoldSeconds = 2.5f;
        private const float TickIntervalSeconds = 0.2f;

        private float tickTimer;
        private float holdTimer;
        private string lastFoundName;
        private string identifiedName = "";

        /// <summary>The currently identified body name ("" while none).</summary>
        public string IdentifiedName
        {
            get { return identifiedName; }
        }

        /// <summary>Resets the cycle (sextant open, pain onset).</summary>
        public void Reset()
        {
            holdTimer = 0f;
            lastFoundName = null;
            identifiedName = "";
        }

        /// <summary>
        /// Advances the identification cycle. The controller supplies the
        /// PIP forward vector, the pain state and the sun reads; the name
        /// Text is updated here on change.
        /// </summary>
        public void Update(float dt, bool pain, Vector3 pipFwd,
            bool ownSunOk, Vector3 ownSunDir, float ownSunAlt,
            bool gameSunOk, Vector3 gameSunDir,
            bool positionOk, float lat, float lon,
            UnityEngine.UI.Text nameText, double arcAngle)
        {
            tickTimer -= dt;
            bool tick = tickTimer <= 0f;
            if (tick)
                tickTimer = TickIntervalSeconds;

            if (pain)
            {
                holdTimer = 0f;
                identifiedName = "";
                if (nameText != null)
                    nameText.text = "";
                return;
            }

            if (tick)
            {
                float bodyAlt;
                lastFoundName = FindNearestBodyName(out bodyAlt, pipFwd,
                    ownSunOk, ownSunDir, ownSunAlt, gameSunOk, gameSunDir,
                    positionOk, lat, lon);
                if (lastFoundName != null)
                {
                    holdTimer += TickIntervalSeconds;
                    if (holdTimer >= HoldSeconds)
                    {
                        bool justIdentified = identifiedName != lastFoundName;
                        identifiedName = lastFoundName;
                        if (justIdentified)
                            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                                "{0} IDHIT name={1} alt={2:F2} arc={3:F2} |d|={4:F2}",
                                LogPrefix, lastFoundName, bodyAlt, arcAngle,
                                Math.Abs(bodyAlt - (float)arcAngle)));
                    }
                }
                else
                {
                    holdTimer = 0f;
                    identifiedName = "";
                }
            }

            if (nameText != null && nameText.text != identifiedName)
                nameText.text = identifiedName;
        }

        private string FindNearestBodyName(out float bodyAltDeg, Vector3 pipFwd,
            bool ownSunOk, Vector3 ownSunDir, float ownSunAlt,
            bool gameSunOk, Vector3 gameSunDir,
            bool positionOk, float lat, float lon)
        {
            // Sun ID aims at the direction the player actually sees: the disc
            // is drawn on the GAME sun (SUNDRIFT sep ~1.6° would never fit
            // the 0.5° cone against our own ephemeris). Own ephemeris stays
            // the fallback when the game sun is unavailable via reflection.
            bodyAltDeg = -1f;
            if (pipFwd.sqrMagnitude < 0.01f)
                return null;
            if (ownSunOk)
            {
                Vector3 idTarget = ownSunDir;
                if (gameSunOk)
                    idTarget = gameSunDir;
                if (Vector3.Angle(pipFwd, idTarget) <= ConeDeg)
                {
                    bodyAltDeg = ownSunAlt;
                    return SunName;
                }
            }

            if (!positionOk)
                return null;

            DateTime utc = GameTimeResolver.GetUtcNow();
            double jd = AstroMath.ToJulianDay(utc);
            double lstDeg = AstroMath.LstDeg(jd, lon);

            Vector3 fwd = pipFwd;
            double bestCos = Math.Cos(ConeDeg * Mathf.Deg2Rad);
            int bestHip = -1;

            var stars = StarCatalog.Stars;
            for (int i = 0; i < stars.Length; i++)
            {
                if (stars[i].DecDeg < -89.5f && stars[i].Vmag > 6f)
                    continue; // cheap skip near-pole dim entries costs nothing
                AstroMath.RaDecToEnu(stars[i].RaDeg * Mathf.Deg2Rad, stars[i].DecDeg * Mathf.Deg2Rad,
                    lstDeg, lat, out double altRad, out _, out double e, out double n, out double u);
                if (altRad <= 0)
                    continue;

                double len = Math.Sqrt(e * e + n * n + u * u);
                if (len < 1e-9)
                    continue;
                double cos = (e * fwd.x + u * fwd.y + n * fwd.z) / len;
                if (cos > bestCos)
                {
                    bestCos = cos;
                    bestHip = stars[i].Hip;
                    bodyAltDeg = (float)(altRad * Mathf.Rad2Deg);
                }
            }

            if (bestHip < 0)
                return null;

            string name;
            return NavStarNames.TryGetName(bestHip, out name) ? name : ("HIP " + bestHip);
        }
    }
}
