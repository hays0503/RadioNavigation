using System;
using System.Globalization;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Star/Moon/Sun identification for the sextant (SEXTANT_PLAN v2.2): a
    /// fixed 1.0° cone (SightabilityGate.FixedConeDeg) around the PIP forward
    /// direction, a 2.5 s hold to confirm, and the identified body the
    /// worksheet records. The Sun aims at the GAME sun direction (SUNDRIFT sep
    /// would never fit the cone against our own ephemeris); the Moon and stars
    /// resolve through the mod's own ephemeris by the player's sandbox
    /// position. Extracted from
    /// SextantController (SRP: the identification cycle is not camera or
    /// overlay logic). Owns the tick timer, the hold timer and the names.
    /// </summary>
    internal sealed class SextantStarId
    {
        private const string LogPrefix = "[RadioNav][Sextant]";

        // v1.12: 2.5 s hold. P5: the hold belongs to ONE body key and lives in
        // HoldTracker (grace + decay); the cone comes from SightabilityGate
        // (world-fixed 1.0°, author 2026-11-04).
        private const float TickIntervalSeconds = 0.2f;

        /// <summary>Above this time compression identification freezes (P7).
        /// Deliberate cap (author 2026-11-04), NOT a cone constraint any more:
        /// at ×4 the sky drifts 0.042° over the 2.5 s hold (4% of the fixed
        /// 1.0° cone) — the guard stands because high TC also shortens the
        /// roll period in real seconds, and ×32 is pinned frozen by the
        /// acceptance scenario. Raising it needs a live test at ×5…×10.</summary>
        public const float MaxIdentificationTimeScale = 4f;

        private const string TimeCompressionHint = "\u0423\u0441\u043a\u043e\u0440\u0435\u043d\u0438\u0435 >\u00d74";

        private readonly HoldTracker tracker = new HoldTracker();
        // Sticky selection (author decision 2026-11-04): a confirmed body
        // stays selected after it leaves the cone, so the sight can be
        // recorded once the drum is set; O / the sextant reset drops it.
        private readonly BodySelection selection = new BodySelection();
        private bool selectedInCone;
        private float selectionLostSeconds;
        private float tickTimer;
        private CelestialBody lastFoundBody;
        private float identifiedAltDeg;
        private SightBlock lastBlock;
        // Debug aim readout (RadioNav.DebugAim): line-of-sight offsets in
        // arcminutes; NaN when the gate is off or no body is in the cone.
        private double aimTotalArcMin = double.NaN;
        private double aimVerticalArcMin = double.NaN;
        private double aimHorizontalArcMin = double.NaN;

        /// <summary>Last gate verdict (None while the body is sightable) —
        /// feeds the hint and the MCP probe.</summary>
        public SightBlock LastBlock
        {
            get { return lastBlock; }
        }

        /// <summary>Hold progress of the pending body in [0,1] (P5 UI bar).</summary>
        public float HoldProgress
        {
            get { return tracker.Progress; }
        }

        /// <summary>True once the current body's hold has completed.</summary>
        public bool Confirmed
        {
            get { return tracker.Confirmed; }
        }

        /// <summary>The selected (sticky) body: confirmed by a hold and kept
        /// after it leaves the cone until O / the sextant reset.</summary>
        public CelestialBody IdentifiedBody
        {
            get { return selection.Selected; }
        }

        /// <summary>True once a body has been confirmed — the record gate
        /// (author 2026-11-04: record even out of the cone).</summary>
        public bool HasSelection
        {
            get { return selection.HasSelection; }
        }

        /// <summary>True while the SELECTED body is inside the aiming cone.
        /// The burst quality meter and the index-error calibration require a
        /// body actually being sighted; the record does not.</summary>
        public bool InCone
        {
            get { return selectedInCone; }
        }

        /// <summary>Current altitude of the selected body (degrees; NaN while
        /// unknown — the MCP probe and the C calibration read it, and a NaN
        /// must never be mistaken for a valid 0°).</summary>
        public float IdentifiedAltDeg
        {
            get { return identifiedAltDeg; }
        }

        /// <summary>The identification line text: the plain name in the cone,
        /// with the "вне прицела" suffix while the sticky selection is outside
        /// it ("" while nothing is selected).</summary>
        public string IdentifiedName
        {
            get
            {
                return selection.HasSelection
                    ? BodySelection.DisplayName(selection.Selected.DisplayName, selectedInCone)
                    : "";
            }
        }

        /// <summary>Debug aim offsets in arcminutes (NaN when the
        /// RadioNav.DebugAim gate is off or no body is in the cone). The
        /// controller renders them into its own line under the aiming window
        /// (2026-09-22): the identification line stays clean, and the top-left
        /// F-Tafel card cannot collide with the readout.</summary>
        public double AimTotalArcMin
        {
            get { return aimTotalArcMin; }
        }

        public double AimVerticalArcMin
        {
            get { return aimVerticalArcMin; }
        }

        public double AimHorizontalArcMin
        {
            get { return aimHorizontalArcMin; }
        }

        /// <summary>Resets the cycle (sextant open, pain onset): the sticky
        /// selection is dropped too.</summary>
        public void Reset()
        {
            tracker.Reset();
            tickTimer = 0f;
            identifiedAltDeg = 0f;
            lastFoundBody = CelestialBody.None();
            selection.Clear();
            selectedInCone = false;
            selectionLostSeconds = 0f;
            lastBlock = SightBlock.None;
        }

        /// <summary>O key: the new worksheet series starts with no selected
        /// body (author decision 2026-11-04). The hold resets too, so the
        /// body must be re-confirmed with a fresh 2.5 s hold.</summary>
        public void ClearSelection()
        {
            selection.Clear();
            tracker.Reset();
            selectedInCone = false;
            selectionLostSeconds = 0f;
        }

        /// <summary>
        /// Advances the identification cycle. The controller supplies the
        /// PIP forward vector, the pain state, the hull gate and the sun
        /// reads; the name Text is updated here.
        /// <paramref name="canSight"/> is false inside the boat: the hull
        /// blocks the sky, so no body may be identified there (R5).
        /// </summary>
        public void Update(float dt, bool pain, bool canSight, Vector3 pipFwd,
            bool ownSunOk, Vector3 ownSunDir, float ownSunAlt,
            bool gameSunOk, Vector3 gameSunDir, float gameSunAlt,
            bool positionOk, float lat, float lon,
            UnityEngine.UI.Text nameText, double arcAngle)
        {
            tickTimer -= dt;
            bool tick = tickTimer <= 0f;
            if (tick)
                tickTimer = TickIntervalSeconds;

            // Time compression guard (P7): above ×4 identification freezes
            // (a deliberate cap, not a cone constraint — see the constant).
            // The sticky selection survives it (the drum reading stays valid).
            if (Time.timeScale > MaxIdentificationTimeScale)
            {
                ClearForBlock(TimeCompressionHint, nameText, false);
                return;
            }

            if (pain || !canSight)
            {
                // Inside the hull nothing can be sighted, so the selection is
                // dropped there; pain already resets the cycle on onset.
                ClearForBlock("", nameText, !canSight);
                return;
            }

            if (tick)
            {
                TickIdentification(pipFwd, ownSunOk, ownSunDir, ownSunAlt,
                    gameSunOk, gameSunDir, gameSunAlt, positionOk, lat, lon, arcAngle);
            }

            if (nameText != null)
            {
                // Sticky selection: the plain name in the cone, the suffixed
                // one outside it, the hold hint while a hold builds.
                string text;
                if (tracker.Confirmed)
                    text = selection.Selected.DisplayName;
                else if (IdentifiedName.Length > 0)
                    text = IdentifiedName;
                else
                    text = PendingHint();
                if (nameText.text != text)
                    nameText.text = text;
            }
        }

        /// <summary>Drops the hold state and shows a hint ("" clears).
        /// <paramref name="dropSelection"/> also clears the sticky selection
        /// (the hull gate: nothing can be sighted inside the boat).</summary>
        private void ClearForBlock(string hint, UnityEngine.UI.Text nameText, bool dropSelection)
        {
            tracker.Reset();
            identifiedAltDeg = float.NaN;
            lastFoundBody = CelestialBody.None();
            selectedInCone = false;
            selectionLostSeconds = 0f;
            // The readout must not freeze on the last computed offsets while
            // the block lasts (×4 / pain / hull): NaN hides the line.
            aimTotalArcMin = double.NaN;
            aimVerticalArcMin = double.NaN;
            aimHorizontalArcMin = double.NaN;
            if (dropSelection)
                selection.Clear();
            lastBlock = SightBlock.None;
            if (nameText != null && nameText.text != hint)
                nameText.text = hint;
        }

        /// <summary>One identification tick: find the body, apply the gates,
        /// advance the hold and log the completed identification.</summary>
        private void TickIdentification(Vector3 pipFwd,
            bool ownSunOk, Vector3 ownSunDir, float ownSunAlt,
            bool gameSunOk, Vector3 gameSunDir, float gameSunAlt,
            bool positionOk, float lat, float lon, double arcAngle)
        {
            double coneDeg = SightabilityGate.EffectiveConeDeg();
            float bodyAlt;
            Vector3 bodyDir;
            lastFoundBody = FindNearestBody(out bodyAlt, out bodyDir, pipFwd, coneDeg,
                ownSunOk, ownSunDir, ownSunAlt, gameSunOk, gameSunDir, gameSunAlt,
                positionOk, lat, lon);

            SightBlock block = SightBlock.None;
            string key = "";
            if (!lastFoundBody.IsNone)
            {
                double illumination = lastFoundBody.Kind == CelestialBodyKind.Moon
                    ? AstroMath.MoonIllumination(CelestialEphemeris.JdFor(
                        CelestialBody.Moon(), GameTimeResolver.GetUtcNow()))
                    : 0.0;
                block = SightabilityGate.Evaluate(lastFoundBody.Kind, bodyAlt,
                    ownSunAlt, illumination);
                if (block == SightBlock.None)
                    key = BodyKey(lastFoundBody);
            }
            lastBlock = block;

            if (tracker.Tick(key, TickIntervalSeconds))
            {
                selection.Select(lastFoundBody);
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "{0} IDHIT name={1} kind={2} alt={3:F2} arc={4:F2} |d|={5:F2} cone={6:F2}",
                    LogPrefix, lastFoundBody.DisplayName, lastFoundBody.Kind,
                    bodyAlt, arcAngle, Math.Abs(bodyAlt - (float)arcAngle), coneDeg));
            }
            selectedInCone = tracker.Confirmed;

            // Sticky-selection expiry (author request 2026-11-04): a body
            // that stays out of the cone for LostTimeoutSeconds is dropped, so
            // the name and the aim readout cannot show a long-gone body.
            selectionLostSeconds = BodySelection.AdvanceLost(selectionLostSeconds,
                selectedInCone, TickIntervalSeconds);
            if (selection.HasSelection && BodySelection.TimedOut(selectionLostSeconds))
            {
                selection.Clear();
                selectionLostSeconds = 0f;
            }

            // Sticky aim reference (2026-11-04): the selected body's CURRENT
            // direction/altitude drives the debug readout even after it leaves
            // the cone, so the navigator can steer back or set the drum.
            UpdateSelectedState(pipFwd, bodyDir, bodyAlt, arcAngle,
                ownSunOk, ownSunDir, ownSunAlt, gameSunOk, gameSunDir, gameSunAlt,
                positionOk, lat, lon);
        }

        /// <summary>Keeps the sticky selection's live state: its current
        /// direction and altitude (the aim readout and the C calibration use
        /// them even while the body is out of the cone). Falls back to the
        /// found body when nothing is selected or the position is unknown.</summary>
        private void UpdateSelectedState(Vector3 aimFwd, Vector3 foundDir, float foundAlt,
            double arcAngle, bool ownSunOk, Vector3 ownSunDir, float ownSunAlt,
            bool gameSunOk, Vector3 gameSunDir, float gameSunAlt,
            bool positionOk, float lat, float lon)
        {
            if (selection.HasSelection && positionOk)
            {
                Vector3 dir;
                float alt;
                if (TryBodyDirection(selection.Selected, lat, lon,
                        ownSunOk, ownSunDir, ownSunAlt, gameSunOk, gameSunDir, gameSunAlt,
                        out dir, out alt))
                {
                    identifiedAltDeg = alt;
                    UpdateAimDebug(aimFwd, dir, alt, arcAngle);
                    return;
                }
            }

            identifiedAltDeg = float.NaN;
            UpdateAimDebug(aimFwd, foundDir, foundAlt, arcAngle);
        }

        /// <summary>Current direction/altitude of a body by its identity
        /// (sticky-selection support): the Sun uses the GAME direction (the
        /// disc the player actually sees), the Moon and the stars the mod's
        /// own ephemeris at the given position.</summary>
        private static bool TryBodyDirection(CelestialBody body, float lat, float lon,
            bool ownSunOk, Vector3 ownSunDir, float ownSunAlt,
            bool gameSunOk, Vector3 gameSunDir, float gameSunAlt,
            out Vector3 dir, out float altDeg)
        {
            dir = Vector3.zero;
            altDeg = 0f;
            if (body.IsNone)
                return false;

            if (body.Kind == CelestialBodyKind.Sun)
            {
                if (gameSunOk)
                {
                    dir = gameSunDir;
                    altDeg = gameSunAlt;
                    return true;
                }
                if (!ownSunOk)
                    return false;
                dir = ownSunDir;
                altDeg = ownSunAlt;
                return true;
            }

            DateTime utc = GameTimeResolver.GetUtcNow();
            if (body.Kind == CelestialBodyKind.Moon)
            {
                double jd = CelestialEphemeris.JdFor(CelestialBody.Moon(), utc);
                double lst = AstroMath.LstDeg(jd, lon);
                if (!AstroMath.MoonTopocentricRaDec(jd, lat, lst,
                        out double raRad, out double decRad))
                    return false;
                return EnuDirection(raRad, decRad, lst, lat, out dir, out altDeg);
            }

            // Star: the selection carries the HIP; find its catalog row.
            StarEntry[] stars = StarCatalog.Stars;
            double starJd = AstroMath.ToJulianDay(utc);
            double starLst = AstroMath.LstDeg(starJd, lon);
            for (int i = 0; i < stars.Length; i++)
            {
                if (stars[i].Hip != body.Hip)
                    continue;
                CelestialEphemeris.PrecessCatalog(stars[i].RaDeg, stars[i].DecDeg, starJd,
                    out double raRad, out double decRad);
                return EnuDirection(raRad, decRad, starLst, lat, out dir, out altDeg);
            }
            return false;
        }

        /// <summary>Ra/Dec to the world direction (ENU: x=east, y=up,
        /// z=north) and altitude in degrees.</summary>
        private static bool EnuDirection(double raRad, double decRad, double lstDeg,
            float lat, out Vector3 dir, out float altDeg)
        {
            dir = Vector3.zero;
            altDeg = 0f;
            AstroMath.RaDecToEnu(raRad, decRad, lstDeg, lat,
                out double altRad, out _, out double e, out double n, out double u);
            double len = Math.Sqrt(e * e + n * n + u * u);
            if (len < 1e-9)
                return false;
            dir = new Vector3((float)e, (float)u, (float)n);
            altDeg = (float)(altRad * Mathf.Rad2Deg);
            return true;
        }

        /// <summary>
        /// Debug aim readout (RadioNav.DebugAim): how far the line of sight is
        /// from the body, in arcminutes. The vertical offset is the future fix
        /// intercept (body altitude minus the arc with the instrument index
        /// error applied at record time), so nulling it makes the recorded Ho
        /// exact; the horizontal one keeps the body inside the cone while the
        /// drum is set. Gate off = the fields stay NaN and nothing is computed.
        /// </summary>
        private void UpdateAimDebug(Vector3 aimFwd, Vector3 bodyDir, float bodyAlt,
            double arcAngle)
        {
            aimTotalArcMin = double.NaN;
            aimVerticalArcMin = double.NaN;
            aimHorizontalArcMin = double.NaN;
            // Zero direction = nothing to aim at (the found body may be none
            // while the sticky selection still drives this readout).
            if (bodyDir.sqrMagnitude < 1e-9f)
                return;
            if (!RadioNavDiagnostics.IsEnabledCached(RadioNavDiagnostics.AimDebugKey, false))
                return;
            aimTotalArcMin = AimOffsetFormat.ToArcMin(AimOffsetFormat.TotalDeg(
                aimFwd.x, aimFwd.y, aimFwd.z, bodyDir.x, bodyDir.y, bodyDir.z));
            aimVerticalArcMin = AimOffsetFormat.ToArcMin(
                bodyAlt - (arcAngle + IndexErrorCorrection.Load()));
            aimHorizontalArcMin = AimOffsetFormat.ToArcMin(AimOffsetFormat.HorizontalDeg(
                aimFwd.x, aimFwd.y, aimFwd.z, bodyDir.x, bodyDir.y, bodyDir.z));
        }

        /// <summary>Hold key: one body, not just "something in the cone".</summary>
        private static string BodyKey(CelestialBody body)
        {
            return ((int)body.Kind).ToString(CultureInfo.InvariantCulture) + ":"
                + body.Hip.ToString(CultureInfo.InvariantCulture) + ":" + body.Name;
        }

        /// <summary>Live hint while the hold builds: "Name 1.2/2.5s".</summary>
        private string PendingHint()
        {
            if (lastFoundBody.IsNone || tracker.Key.Length == 0 || lastBlock != SightBlock.None)
                return "";
            return string.Format(CultureInfo.InvariantCulture, "{0} {1:F1}/2.5\u0441",
                lastFoundBody.DisplayName, tracker.Progress * HoldTracker.HoldSeconds);
        }

        /// <summary>
        /// Nearest sightable body in the cone: Sun (game direction), then the
        /// Moon and the catalog stars by own ephemeris, closest wins.
        /// </summary>
        private CelestialBody FindNearestBody(out float bodyAltDeg, out Vector3 bodyDir,
            Vector3 pipFwd, double coneDeg,
            bool ownSunOk, Vector3 ownSunDir, float ownSunAlt,
            bool gameSunOk, Vector3 gameSunDir, float gameSunAlt,
            bool positionOk, float lat, float lon)
        {
            // Sun ID aims at the direction the player actually sees: the disc
            // is drawn on the GAME sun (SUNDRIFT sep ~1.6° would never fit
            // the 1.0° cone against our own ephemeris). Own ephemeris stays
            // the fallback when the game sun is unavailable via reflection.
            bodyAltDeg = -1f;
            bodyDir = Vector3.zero;
            if (pipFwd.sqrMagnitude < 0.01f)
                return CelestialBody.None();

            float sunAlt;
            Vector3 sunDir;
            CelestialBody sun = FindSunInCone(pipFwd, coneDeg, ownSunOk, ownSunDir, ownSunAlt,
                gameSunOk, gameSunDir, gameSunAlt, out sunAlt, out sunDir);
            if (!sun.IsNone)
            {
                bodyAltDeg = sunAlt;
                bodyDir = sunDir;
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

            double bestCos = Math.Cos(coneDeg * Mathf.Deg2Rad);
            int bestHip = -1;
            bool moonBest = false;
            float bestAlt = -1f;
            Vector3 bestDir = Vector3.zero;

            float moonAlt;
            double moonCos;
            Vector3 moonDir;
            if (TryFindMoonCos(pipFwd, moonJd, moonLst, lat, out moonAlt, out moonCos, out moonDir)
                && moonCos > bestCos)
            {
                bestCos = moonCos;
                bestAlt = moonAlt;
                bestDir = moonDir;
                moonBest = true;
            }

            int starHip;
            float starAlt;
            double starCos;
            Vector3 starDir;
            if (TryFindStar(pipFwd, jd, lstDeg, lat, bestCos, out starHip, out starAlt, out starCos,
                    out starDir)
                && starCos > bestCos)
            {
                bestHip = starHip;
                bestAlt = starAlt;
                bestDir = starDir;
                moonBest = false;
            }

            if (moonBest)
            {
                bodyAltDeg = bestAlt;
                bodyDir = bestDir;
                return CelestialBody.Moon();
            }
            if (bestHip >= 0)
            {
                bodyAltDeg = bestAlt;
                bodyDir = bestDir;
                string name;
                if (!NavStarNames.TryGetName(bestHip, out name))
                    name = "HIP " + bestHip;
                return CelestialBody.Star(bestHip, name);
            }
            return CelestialBody.None();
        }

        private static CelestialBody FindSunInCone(Vector3 pipFwd, double coneDeg,
            bool ownSunOk, Vector3 ownSunDir, float ownSunAlt,
            bool gameSunOk, Vector3 gameSunDir, float gameSunAlt, out float altDeg,
            out Vector3 dir)
        {
            altDeg = -1f;
            dir = Vector3.zero;
            if (!ownSunOk)
                return CelestialBody.None();
            Vector3 idTarget = gameSunOk ? gameSunDir : ownSunDir;
            if (Vector3.Angle(pipFwd, idTarget) > coneDeg)
                return CelestialBody.None();
            dir = idTarget;
            // The player measures the GAME sun disc (the mod's disc is placed on
            // the game direction, SUNDRIFT sep ~1.6°); report the altitude that
            // matches what is aimed at, not our ephemeris (P1).
            altDeg = gameSunOk ? gameSunAlt : ownSunAlt;
            return CelestialBody.Sun();
        }

        /// <summary>Moon cosine to the cone axis; false when the Moon is
        /// below the horizon or a new moon (invisible).</summary>
        private static bool TryFindMoonCos(Vector3 pipFwd, double jd, double lstDeg, float lat,
            out float altDeg, out double cos, out Vector3 dir)
        {
            altDeg = -1f;
            cos = -1.0;
            dir = Vector3.zero;
            // Topocentric Moon (P2/R5): the player sights the disc the
            // renderer places with the same parallax correction.
            if (!AstroMath.MoonTopocentricRaDec(jd, lat, lstDeg, out double moonRa, out double moonDec))
                return false;
            if (AstroMath.MoonIllumination(jd) < SightabilityGate.MoonMinIllumination)
                return false;
            AstroMath.RaDecToEnu(moonRa, moonDec, lstDeg, lat,
                out double moonAlt, out _, out double me, out double mn, out double mu);
            if (moonAlt <= 0)
                return false;
            double len = Math.Sqrt(me * me + mn * mn + mu * mu);
            if (len < 1e-9)
                return false;
            cos = (me * pipFwd.x + mu * pipFwd.y + mn * pipFwd.z) / len;
            // World mapping of ENU: x = east, y = up, z = north.
            dir = new Vector3((float)me, (float)mu, (float)mn);
            altDeg = (float)(moonAlt * Mathf.Rad2Deg);
            return true;
        }

        /// <summary>Closest catalog star above the horizon within the cone
        /// (better than <paramref name="startCos"/>); false when none.</summary>
        private static bool TryFindStar(Vector3 pipFwd, double jd, double lstDeg, float lat,
            double startCos, out int hip, out float altDeg, out double cos, out Vector3 dir)
        {
            hip = -1;
            altDeg = -1f;
            cos = startCos;
            dir = Vector3.zero;
            StarEntry[] stars = StarCatalog.Stars;
            for (int i = 0; i < stars.Length; i++)
            {
                if (stars[i].DecDeg < -89.5f && stars[i].Vmag > 6f)
                    continue; // cheap skip near-pole dim entries costs nothing
                if (!CelestialEphemeris.IsSightableStar(stars[i].Hip))
                    continue; // Hip=0 sentinel: no identity, HIP lookup would lie
                // Precess the catalog row (epoch 1926.0) to the sight date (P1b).
                CelestialEphemeris.PrecessCatalog(stars[i].RaDeg, stars[i].DecDeg, jd,
                    out double raRad, out double decRad);
                AstroMath.RaDecToEnu(raRad, decRad,
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
                    // World mapping of ENU: x = east, y = up, z = north.
                    dir = new Vector3((float)e, (float)u, (float)n);
                }
            }
            return hip >= 0;
        }
    }
}
