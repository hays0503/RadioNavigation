using System;
using System.Globalization;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Star/Moon/Sun identification for the sextant (SEXTANT_PLAN v2.2): a
    /// 0.5° cone around the PIP forward direction, a 2.5 s hold to confirm,
    /// and the identified body the worksheet records. The Sun aims at the
    /// GAME sun direction (SUNDRIFT sep would never fit the cone against our
    /// own ephemeris); the Moon and stars resolve through the mod's own
    /// ephemeris by the player's sandbox position. Extracted from
    /// SextantController (SRP: the identification cycle is not camera or
    /// overlay logic). Owns the tick timer, the hold timer and the names.
    /// </summary>
    internal sealed class SextantStarId
    {
        private const string LogPrefix = "[RadioNav][Sextant]";

        private const float ConeDeg = 0.5f;
        // v1.12: 2.5 s (was 5 s) — author request, faster sight workflow.
        private const float HoldSeconds = 2.5f;
        private const float TickIntervalSeconds = 0.2f;
        // New moon is invisible; do not let the player "sight" a black disc.
        private const double MoonMinIllumination = 0.05;

        private float tickTimer;
        private float holdTimer;
        private CelestialBody lastFoundBody;
        private CelestialBody identifiedBody;
        private float identifiedAltDeg;

        /// <summary>The currently identified body (Kind=None while none).</summary>
        public CelestialBody IdentifiedBody
        {
            get { return identifiedBody; }
        }

        /// <summary>Altitude of the identified body at identification time
        /// (degrees; 0 while none) — the MCP probe reports it.</summary>
        public float IdentifiedAltDeg
        {
            get { return identifiedAltDeg; }
        }

        /// <summary>The currently identified body name ("" while none).</summary>
        public string IdentifiedName
        {
            get { return identifiedBody.IsNone ? "" : identifiedBody.DisplayName; }
        }

        /// <summary>Resets the cycle (sextant open, pain onset).</summary>
        public void Reset()
        {
            holdTimer = 0f;
            identifiedAltDeg = 0f;
            lastFoundBody = CelestialBody.None();
            identifiedBody = CelestialBody.None();
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
                identifiedAltDeg = 0f;
                identifiedBody = CelestialBody.None();
                if (nameText != null)
                    nameText.text = "";
                return;
            }

            if (tick)
            {
                float bodyAlt;
                lastFoundBody = FindNearestBody(out bodyAlt, pipFwd,
                    ownSunOk, ownSunDir, ownSunAlt, gameSunOk, gameSunDir,
                    positionOk, lat, lon);
                if (!lastFoundBody.IsNone)
                {
                    holdTimer += TickIntervalSeconds;
                    if (holdTimer >= HoldSeconds)
                    {
                        bool justIdentified = identifiedBody.Kind != lastFoundBody.Kind
                            || identifiedBody.Hip != lastFoundBody.Hip
                            || identifiedBody.Name != lastFoundBody.Name;
                        identifiedBody = lastFoundBody;
                        identifiedAltDeg = bodyAlt;
                        if (justIdentified)
                            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                                "{0} IDHIT name={1} kind={2} alt={3:F2} arc={4:F2} |d|={5:F2}",
                                LogPrefix, identifiedBody.DisplayName, identifiedBody.Kind,
                                bodyAlt, arcAngle, Math.Abs(bodyAlt - (float)arcAngle)));
                    }
                }
                else
                {
                    holdTimer = 0f;
                    identifiedAltDeg = 0f;
                    identifiedBody = CelestialBody.None();
                }
            }

            if (nameText != null && nameText.text != IdentifiedName)
                nameText.text = IdentifiedName;
        }

        /// <summary>
        /// Nearest sightable body in the cone: Sun (game direction), then the
        /// Moon and the catalog stars by own ephemeris, closest wins.
        /// </summary>
        private CelestialBody FindNearestBody(out float bodyAltDeg, Vector3 pipFwd,
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
                return CelestialBody.None();

            float sunAlt;
            CelestialBody sun = FindSunInCone(pipFwd, ownSunOk, ownSunDir, ownSunAlt,
                gameSunOk, gameSunDir, out sunAlt);
            if (!sun.IsNone)
            {
                bodyAltDeg = sunAlt;
                return sun;
            }

            if (!positionOk)
                return CelestialBody.None();

            DateTime utc = GameTimeResolver.GetUtcNow();
            double jd = AstroMath.ToJulianDay(utc);
            double lstDeg = AstroMath.LstDeg(jd, lon);
            // The Moon shares the game sky time shift (ADR-033 v2.4): aim at
            // the game's moon, not the almanac moon ~1.6° away.
            double moonJd = CelestialEphemeris.JdFor(CelestialBody.Moon(), utc);
            double moonLst = AstroMath.LstDeg(moonJd, lon);

            double bestCos = Math.Cos(ConeDeg * Mathf.Deg2Rad);
            int bestHip = -1;
            bool moonBest = false;
            float bestAlt = -1f;

            float moonAlt;
            double moonCos;
            if (TryFindMoonCos(pipFwd, moonJd, moonLst, lat, out moonAlt, out moonCos)
                && moonCos > bestCos)
            {
                bestCos = moonCos;
                bestAlt = moonAlt;
                moonBest = true;
            }

            int starHip;
            float starAlt;
            double starCos;
            if (TryFindStar(pipFwd, lstDeg, lat, bestCos, out starHip, out starAlt, out starCos)
                && starCos > bestCos)
            {
                bestHip = starHip;
                bestAlt = starAlt;
                moonBest = false;
            }

            if (moonBest)
            {
                bodyAltDeg = bestAlt;
                return CelestialBody.Moon();
            }
            if (bestHip >= 0)
            {
                bodyAltDeg = bestAlt;
                string name;
                if (!NavStarNames.TryGetName(bestHip, out name))
                    name = "HIP " + bestHip;
                return CelestialBody.Star(bestHip, name);
            }
            return CelestialBody.None();
        }

        private static CelestialBody FindSunInCone(Vector3 pipFwd,
            bool ownSunOk, Vector3 ownSunDir, float ownSunAlt,
            bool gameSunOk, Vector3 gameSunDir, out float altDeg)
        {
            altDeg = -1f;
            if (!ownSunOk)
                return CelestialBody.None();
            Vector3 idTarget = gameSunOk ? gameSunDir : ownSunDir;
            if (Vector3.Angle(pipFwd, idTarget) > ConeDeg)
                return CelestialBody.None();
            altDeg = ownSunAlt;
            return CelestialBody.Sun();
        }

        /// <summary>Moon cosine to the cone axis; false when the Moon is
        /// below the horizon or a new moon (invisible).</summary>
        private static bool TryFindMoonCos(Vector3 pipFwd, double jd, double lstDeg, float lat,
            out float altDeg, out double cos)
        {
            altDeg = -1f;
            cos = -1.0;
            if (!AstroMath.MoonRaDec(jd, out double moonRa, out double moonDec))
                return false;
            if (AstroMath.MoonIllumination(jd) < MoonMinIllumination)
                return false;
            AstroMath.RaDecToEnu(moonRa, moonDec, lstDeg, lat,
                out double moonAlt, out _, out double me, out double mn, out double mu);
            if (moonAlt <= 0)
                return false;
            double len = Math.Sqrt(me * me + mn * mn + mu * mu);
            if (len < 1e-9)
                return false;
            cos = (me * pipFwd.x + mu * pipFwd.y + mn * pipFwd.z) / len;
            altDeg = (float)(moonAlt * Mathf.Rad2Deg);
            return true;
        }

        /// <summary>Closest catalog star above the horizon within the cone
        /// (better than <paramref name="startCos"/>); false when none.</summary>
        private static bool TryFindStar(Vector3 pipFwd, double lstDeg, float lat,
            double startCos, out int hip, out float altDeg, out double cos)
        {
            hip = -1;
            altDeg = -1f;
            cos = startCos;
            StarEntry[] stars = StarCatalog.Stars;
            for (int i = 0; i < stars.Length; i++)
            {
                if (stars[i].DecDeg < -89.5f && stars[i].Vmag > 6f)
                    continue; // cheap skip near-pole dim entries costs nothing
                if (!CelestialEphemeris.IsSightableStar(stars[i].Hip))
                    continue; // Hip=0 sentinel: no identity, HIP lookup would lie
                AstroMath.RaDecToEnu(stars[i].RaDeg * Mathf.Deg2Rad, stars[i].DecDeg * Mathf.Deg2Rad,
                    lstDeg, lat, out double altRad, out _, out double e, out double n, out double u);
                if (altRad <= 0)
                    continue;
                double len = Math.Sqrt(e * e + n * n + u * u);
                if (len < 1e-9)
                    continue;
                double c = (e * pipFwd.x + u * pipFwd.y + n * pipFwd.z) / len;
                if (c > cos)
                {
                    cos = c;
                    hip = stars[i].Hip;
                    altDeg = (float)(altRad * Mathf.Rad2Deg);
                }
            }
            return hip >= 0;
        }
    }
}
