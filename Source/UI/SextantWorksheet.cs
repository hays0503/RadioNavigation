using System;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Running-fix worksheet state (F-Tafel Beobachtungsblatt, SEXTANT_PLAN
    /// v1.9): two sun sights with Versetzung, the course/speed track
    /// sampler, the status line and the 1 Hz card refresh. Extracted from
    /// SextantController (SRP: the nav workflow is not device/camera
    /// logic). The controller feeds it input events, the sextant arc and
    /// the position provider; the card Text is bound at overlay build.
    /// </summary>
    internal sealed class SextantWorksheet
    {
        private const string LogPrefix = "[RadioNav][Sextant]";

        private const float FixPanelIntervalSeconds = 1f;
        private const float FixStatusSeconds = 3f;
        // Intercept above which a sight is almost certainly a mis-aim, not a
        // real position error. Tightened 30 -> 5 NM (2026-09-22): with a
        // centred sight the intercepts run at 1-1.5', so 5' already means a
        // rough observation the navigator should re-take.
        private const double MaxInterceptNm = 5.0;

        // F-Tafel worksheet card strings (v1.9) owned by FixCardComposer;
        // only the snapshot-section and status strings live here.
        private const string FixCardNoSight =
            "\u041D\u0415\u0422 \u041D\u0410\u0411\u041B\u042E\u0414\u0415\u041D\u0418\u042F";
        private const string FixCardNoPosition =
            "\u041D\u0415\u0422 \u041F\u041E\u0417\u0418\u0426\u0418\u0418";
        // Sticky-selection guard (2026-11-04): the selected body may have
        // long set; an impossible sight is refused instead of poisoning the fix.
        private const string FixCardBelowHorizon =
            "\u041E\u0422\u041A\u041B\u041E\u041D\u0415\u041D\u041E: \u0442\u0435\u043B\u043E \u043F\u043E\u0434 \u0433\u043E\u0440\u0438\u0437\u043E\u043D\u0442\u043E\u043C";
        private const string FixCardRecorded1 =
            "\u041D\u0410\u0411\u041B\u042E\u0414\u0415\u041D\u0418\u0415 \u21161 \u0417\u0410\u041F\u0418\u0421\u0410\u041D\u041E";
        private const string FixCardRecorded2 =
            "\u041D\u0410\u0411\u041B\u042E\u0414\u0415\u041D\u0418\u0415 \u21162 \u0417\u0410\u041F\u0418\u0421\u0410\u041D\u041E";
        private const string FixCardRecorded3 =
            "\u041D\u0410\u0411\u041B\u042E\u0414\u0415\u041D\u0418\u0415 \u21163 \u0417\u0410\u041F\u0418\u0421\u0410\u041D\u041E";
        private const string FixCardNewSeries =
            "\u041D\u041E\u0412\u0410\u042F \u0421\u0415\u0420\u0418\u042F";
        private const string FixCardVersetzung =
            "\u0412\u0415\u0420\u0421\u0415\u0422\u0426\u0423\u041D\u0413";
        private const string FixCardCourse = "\u041A\u0443\u0440\u0441";
        private const string FixCardSpeed = "\u0421\u043A\u043E\u0440";
        private const string FixCardTransfer = "\u041F\u0435\u0440\u0435\u043D\u043E\u0441";
        /// <summary>Stale track flag (P7): the transfer speed comes from the
        /// game clock, so pause/skip/compression must be admitted, not hidden.</summary>
        private const string FixCardStaleSpeed = "\u0421\u043A\u043E\u0440 \u0443\u0441\u0442\u0430\u0440";
        // Renamed from "Ошибка от счисления" (v2.2): the reference is the
        // game's true position, not a dead-reckoned one — this is a self-check
        // of the sights, not a navigation result.
        private const string FixCardDeltaFromGame =
            "\u0394 \u043E\u0442 \u043C\u0435\u0441\u0442\u0430 \u0438\u0433\u0440\u044B (\u043F\u0440\u043E\u0432\u0435\u0440\u043A\u0430)";
        private static readonly string FixCardCheckAim =
            "\u041F\u0440\u043E\u0432\u0435\u0440\u044C\u0442\u0435 \u043D\u0430\u0432\u0435\u0434\u0435\u043D\u0438\u0435 " +
            "(a > " + MaxInterceptNm.ToString("F0", CultureInfo.InvariantCulture) + " NM)";
        private const string FixCardGeometry =
            "\u0413\u0435\u043E\u043C\u0435\u0442\u0440\u0438\u044F";
        private const string FixCardGeomGood =
            "\u0445\u043E\u0440\u043E\u0448\u043E";
        private const string FixCardGeomFair =
            "\u043F\u043E\u0441\u0440\u0435\u0434\u0441\u0442\u0432\u0435\u043D\u043D\u043E";
        private const string FixCardGeomPoor =
            "\u0441\u043B\u0430\u0431\u043E";

        private readonly RunningFixCalculator runningFix = new RunningFixCalculator();
        private readonly IPlayerPositionProvider positionProvider;

        private Text cardText;
        // Card fit (2026-09-22): usable column height and the base font size;
        // the card shrinks its font so the whole text fits (FixCardLayout).
        private float cardAvailableUnits;
        private int cardBaseFontSize;
        private string lastCardLine;
        // Compact mode (2026-09-22): the full F-Tafel blocks are dropped for
        // one-line observations when even the full-mode minimum font cannot
        // fit the column (high game UI scale / narrow space).
        private bool cardCompact;
        private string lastCardFull;
        // Height of the stable card measured at cardBaseFontSize (2026-09-22).
        // The font fit must use THIS cached measurement, never the live
        // preferredHeight: that one is measured at whatever font is currently
        // applied, so feeding it back flipped the font between the floor and
        // the base every 1 Hz refresh (live: 9/11, "текст пляшет"). The compact
        // form is measured separately (2026-09-22, swarm review) — the fit
        // consumes the height of the form actually rendered.
        private float cardHeightAtBase;
        private float cardCompactHeightAtBase;
        private string statusLine = "";
        private float statusTimer;
        private float panelTimer;
        private float courseSampleTimer;
        private readonly CourseSampler courseSampler = new CourseSampler();
        private string sectionText;
        private double seriesCalibrationMinutes;

        // Published-fix snapshot for the MCP probe (mirrors the card's
        // Versetzung/FIX section, computed at record time only). ErrorNm is
        // NaN when the sandbox position was unavailable at record time.
        internal bool FixPublished { get; private set; }
        internal double FixLatDeg { get; private set; }
        internal double FixLonDeg { get; private set; }
        internal double FixErrorNm { get; private set; }
        internal double FixCrossingDeg { get; private set; }
        internal bool FixSameBody { get; private set; }

        public SextantWorksheet(IPlayerPositionProvider positionProvider)
        {
            this.positionProvider = positionProvider;
        }

        /// <summary>The underlying calculator (read by the controller for
        /// calibration gating: never re-solve mid-series).</summary>
        public RunningFixCalculator Calculator
        {
            get { return runningFix; }
        }

        /// <summary>True when observation 1 exists (calibration freeze gate).</summary>
        public bool SeriesActive
        {
            get { return runningFix.Observation1 != null; }
        }

        /// <summary>Azimuth of observation 1 (NaN while none) — feeds the
        /// second-sight recommendation in the bodies readout.</summary>
        public double Observation1AzDeg
        {
            get
            {
                return runningFix.Observation1 != null
                    ? runningFix.Observation1.AzDeg
                    : double.NaN;
            }
        }

        /// <summary>Display name of observation 1 (null while none): the
        /// recommendation must not suggest the body already sighted.</summary>
        public string Observation1Name
        {
            get
            {
                return runningFix.Observation1 != null
                    ? runningFix.Observation1.ToBody().DisplayName
                    : null;
            }
        }

        /// <summary>Binds the card Text built by the overlay constructor, with
        /// the usable column height and the base font size for the fit.
        /// Re-binding with different values self-invalidates (2026-09-22): a
        /// changed column width/height/base font must re-measure, otherwise the
        /// cached height and the mode decision go stale.</summary>
        public void BindCard(Text text, float availableUnits, int baseFontSize)
        {
            bool rebind = text != cardText
                || availableUnits != cardAvailableUnits
                || baseFontSize != cardBaseFontSize;
            cardText = text;
            cardAvailableUnits = availableUnits;
            cardBaseFontSize = baseFontSize;
            if (rebind)
                InvalidateCard();
        }

        /// <summary>Forces the next refresh to repaint and re-decide the card
        /// mode (open/reset/UI-scale change).</summary>
        public void InvalidateCard()
        {
            lastCardLine = null;
            lastCardFull = null;
            cardHeightAtBase = 0f;
            cardCompactHeightAtBase = 0f;
            panelTimer = 0f;
        }

        /// <summary>
        /// I-key handler: records a sight of any identified body (Sun, Moon
        /// or star). Returns false when no body is identified or there is no
        /// sandbox position.
        /// </summary>
        public bool CaptureObservation(CelestialBody body, double arcAngle)
        {
            return CaptureObservation(body, arcAngle, 0.0, 1, 0);
        }

        /// <summary>
        /// Burst-aware overload (P6): <paramref name="hoDeg"/> is the committed
        /// value (the mean of the burst when the caller sampled one),
        /// <paramref name="sigmaArcMin"/> its spread and the sample/rejected
        /// counts are carried into the card.
        /// </summary>
        public bool CaptureObservation(CelestialBody body, double hoDeg,
            double sigmaArcMin, int samples, int rejected)
        {
            if (body.IsNone)
            {
                statusLine = FixCardNoSight;
                statusTimer = FixStatusSeconds;
                panelTimer = 0f;
                Debug.Log(LogPrefix + " FIXKEY I ignored - no body identified");
                return false;
            }

            // N1: never record a body whose ephemeris cannot be resolved — the
            // builder would silently fall back to the sun and produce a wrong
            // fix. The ID already filters Hip<=0, so this is belt-and-suspenders
            // with a first-failure warning for regressions.
            DateTime utc = GameTimeResolver.GetUtcNow();
            if (!CelestialEphemeris.Compute(body, utc, out _, out _))
            {
                statusLine = FixCardNoSight;
                statusTimer = FixStatusSeconds;
                panelTimer = 0f;
                if (WarnOnce.ShouldLog("CaptureObservation.unknownBody"))
                    Debug.LogWarning(string.Format(CultureInfo.InvariantCulture,
                        "{0} FIXKEY I rejected - no ephemeris for kind={1} hip={2}",
                        LogPrefix, body.Kind, body.Hip));
                return false;
            }

            float latF, lonF;
            if (!positionProvider.TryGetPosition(out latF, out lonF))
            {
                statusLine = FixCardNoPosition;
                statusTimer = FixStatusSeconds;
                panelTimer = 0f;
                Debug.Log(LogPrefix + " FIXKEY I ignored - no sandbox position");
                return false;
            }

            // Instrument index error (P6): a real sextant reads a constant
            // offset; it is applied to the RAW arc before quantisation so the
            // committed Ho stays on the 1/600° grid.
            double indexErrorArcMin = IndexErrorCorrection.Load();
            double ho = SextantArc.Quantize(IndexErrorCorrection.Apply(hoDeg, indexErrorArcMin));
            SextantObservation obs = RunningFixCalculator.BuildObservation(
                utc, ho, latF, lonF, body);
            // Sticky selection (2026-11-04): the body may have been selected
            // long ago, so a physically impossible sight is rejected here —
            // below -1° neither refraction (<= 0.57°) nor dip lifts the body
            // over the horizon (the margin keeps a legitimate sunset sight).
            if (obs.HcDeg < -1.0)
            {
                statusLine = FixCardBelowHorizon;
                statusTimer = FixStatusSeconds;
                panelTimer = 0f;
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "{0} FIXKEY I rejected - body below the horizon (Hc={1:F2})",
                    LogPrefix, obs.HcDeg));
                return false;
            }
            obs.HoSigmaArcMin = sigmaArcMin;
            obs.HoSampleCount = samples < 1 ? 1 : samples;
            obs.HoRejectedCount = rejected < 0 ? 0 : rejected;
            obs.IndexErrorArcMin = indexErrorArcMin;
            if (!SeriesActive || runningFix.HasThird)
                seriesCalibrationMinutes = SunEphemerisCalibration.DeltaMinutes;
            runningFix.Record(obs);
            FixMapMarker.OnNewObservation();
            // v1.13: the fix is a snapshot computed at record time only.
            if (runningFix.CanSolve)
                RecomputeFixSnapshot();
            else
            {
                sectionText = null;
                FixPublished = false;
                FixMapMarker.Clear();
            }
            bool isThird = runningFix.Observation3 == obs;
            bool isSecond = runningFix.Observation2 == obs;
            statusLine = isThird ? FixCardRecorded3
                : (isSecond ? FixCardRecorded2 : FixCardRecorded1);
            statusTimer = FixStatusSeconds;
            panelTimer = 0f;
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} OBS slot={1} body={2} ho={3:F2} sigma={4:F2}' n={5} rej={6} dr=({7:F4},{8:F4}) hc={9:F2} az={10:F2} a={11:F1}NM",
                LogPrefix, isThird ? 3 : (isSecond ? 2 : 1), obs.BodyName, ho,
                obs.HoSigmaArcMin, obs.HoSampleCount, obs.HoRejectedCount,
                latF, lonF, obs.HcDeg, obs.AzDeg, obs.InterceptNm));
            return true;
        }

        /// <summary>
        /// Status for an I-press that cannot record (P6): shows WHY the sight
        /// was refused instead of a bare "no sight" — low altitude, daylight,
        /// a new/set Moon.
        /// </summary>
        public void ReportBlockedSight(SightBlock block)
        {
            string reason = FixCardComposer.ComposeBlockReason(block);
            statusLine = string.IsNullOrEmpty(reason) ? FixCardNoSight : reason;
            statusTimer = FixStatusSeconds;
            panelTimer = 0f;
        }

        /// <summary>O-key handler: starts a new worksheet series.</summary>
        public void ResetSeries()
        {
            runningFix.Reset();
            FixMapMarker.Clear();
            sectionText = null;
            FixPublished = false;
            // A fresh series starts from the full card (the mode hysteresis
            // would otherwise keep a compact card from the previous series).
            cardCompact = false;
            InvalidateCard();
            statusLine = FixCardNewSeries;
            statusTimer = FixStatusSeconds;
            panelTimer = 0f;
            Debug.Log(LogPrefix + " FIX series reset");
        }

        internal SextantSaveSnapshot CaptureState()
        {
            return new SextantSaveSnapshot
            {
                Observation1 = runningFix.Observation1,
                Observation2 = runningFix.Observation2,
                Observation3 = runningFix.Observation3,
                SectionText = sectionText,
                CalibrationMinutes = seriesCalibrationMinutes,
                FixPublished = FixPublished,
                FixLatDeg = FixLatDeg,
                FixLonDeg = FixLonDeg,
                FixErrorNm = FixErrorNm,
                FixCrossingDeg = FixCrossingDeg,
                FixSameBody = FixSameBody,
                Course = courseSampler.CaptureState(),
                Markers = FixMapMarker.CaptureState()
            };
        }

        /// <summary>Replace, never merge, the previous save's series. Input
        /// has been decoded/validated; there are deliberately no live reads or
        /// calls to RecomputeFixSnapshot/BuildObservation in this path.</summary>
        internal void RestoreState(SextantSaveSnapshot state)
        {
            runningFix.Reset();
            runningFix.Record(state.Observation1);
            runningFix.Record(state.Observation2);
            runningFix.Record(state.Observation3);
            courseSampler.RestoreState(state.Course);
            sectionText = state.SectionText;
            seriesCalibrationMinutes = state.CalibrationMinutes;
            if (SeriesActive)
                SunEphemerisCalibration.SetDelta(seriesCalibrationMinutes);
            FixPublished = state.FixPublished;
            FixLatDeg = state.FixLatDeg;
            FixLonDeg = state.FixLonDeg;
            FixErrorNm = state.FixErrorNm;
            FixCrossingDeg = state.FixCrossingDeg;
            FixSameBody = state.FixSameBody;
            FixMapMarker.RestoreState(state.Markers);
            statusLine = string.Empty;
            statusTimer = 0f;
            courseSampleTimer = 0f;
            cardCompact = false;
            InvalidateCard();
        }

        /// <summary>
        /// Course/speed sampler adapter: the WALL throttle only limits the
        /// reflection read; the sampler itself enforces the GAME-time cadence
        /// (P7), so the 10 s window keeps its samples at any compression.
        /// </summary>
        public void UpdateCourseSampler(float dt)
        {
            courseSampleTimer -= dt;
            if (courseSampleTimer > 0f)
                return;
            courseSampleTimer = 0.05f;

            float latF, lonF;
            if (!positionProvider.TryGetPosition(out latF, out lonF))
                return;
            courseSampler.AddSample(GameTimeResolver.GetUtcNow(), latF, lonF);
        }

        /// <summary>1 Hz card refresh: rebuilds the card text only on change.</summary>
        public void UpdatePanel(float dt)
        {
            if (cardText == null)
                return;
            if (statusTimer > 0f)
                statusTimer -= dt;
            panelTimer -= dt;
            if (panelTimer > 0f)
                return;
            panelTimer = FixPanelIntervalSeconds;

            RefreshCard();
        }

        /// <summary>
        /// Card refresh (2026-09-22): the mode is decided from the STABLE card
        /// (no transient status line) plus a reserved line, via the hysteretic
        /// FixCardLayout.DecideMode. The measurement write updates the surface
        /// cache too, so the card can never show the full blocks while
        /// cardCompact is true (the pre-existing stale-text bug), and caches
        /// the base-size heights — the only measurements the font fit may use.
        /// </summary>
        private void RefreshCard()
        {
            string stable = BuildCard(false, false);
            if (stable != lastCardFull)
            {
                cardText.fontSize = cardBaseFontSize;
                cardText.text = stable;
                lastCardFull = stable;
                lastCardLine = stable;
                cardHeightAtBase = cardText.preferredHeight;

                bool compact = FixCardLayout.DecideMode(cardCompact,
                    cardHeightAtBase, cardAvailableUnits, cardBaseFontSize);
                if (compact != cardCompact)
                {
                    cardCompact = compact;
                    Debug.Log(string.Format(CultureInfo.InvariantCulture,
                        LogPrefix + " CARDMODE {0} h={1:F0} a={2:F0}",
                        compact ? "compact" : "full", cardHeightAtBase,
                        cardAvailableUnits));
                }

                if (cardCompact)
                {
                    // The fit consumes the height of the form actually
                    // rendered; the compact card is ~1.5x shorter, so fitting
                    // it by the full height pinned it to the floor (2026-09-22,
                    // swarm review). lastCardLine = null repaints this
                    // measurement text in the render write below.
                    cardText.text = BuildCard(true, false);
                    cardCompactHeightAtBase = cardText.preferredHeight;
                    lastCardLine = null;
                }
            }

            string render = BuildCard(cardCompact, true);
            if (render != lastCardLine)
            {
                cardText.text = render;
                lastCardLine = render;
            }

            ApplyCardFontSize();
        }

        /// <summary>
        /// Shrinks the card font so the whole text fits the column's usable
        /// height. The height comes from the cached base-size measurement of
        /// the form actually rendered (cardHeightAtBase / the compact one) plus
        /// the reserved status line — never from the live preferredHeight,
        /// which is measured at the current font and would flip the fit every
        /// refresh (the 2026-09-22 dance). The full card keeps at least the
        /// full-mode minimum; the compact one may go to the floor.
        /// </summary>
        private void ApplyCardFontSize()
        {
            if (cardText == null || cardBaseFontSize <= 0)
                return;
            float heightAtBase = FixCardLayout.RenderedHeightAtBase(cardCompact,
                cardHeightAtBase, cardCompactHeightAtBase);
            if (heightAtBase <= 0f)
                return;
            int fitted = FixCardLayout.CardFontSize(heightAtBase,
                cardAvailableUnits, cardBaseFontSize,
                FixCardLayout.MinFontFor(cardCompact));
            if (fitted != cardText.fontSize)
            {
                cardText.fontSize = fitted;
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    LogPrefix + " CARDFONT f={0} h={1:F0} a={2:F0} mode={3}",
                    fitted, heightAtBase, cardAvailableUnits,
                    cardCompact ? "compact" : "full"));
            }
        }

        /// <summary>Card assembly lives in FixCardComposer (tested offline).
        /// The mode decision measures the card WITHOUT the transient status
        /// line (2026-09-22): a 3-second "ЗАПИСАНО" must never flip the mode.</summary>
        private string BuildCard(bool compact, bool includeStatus)
        {
            string status = includeStatus && statusTimer > 0f && !string.IsNullOrEmpty(statusLine)
                ? statusLine : null;
            return FixCardComposer.Compose(
                runningFix.Observation1,
                runningFix.Observation2,
                sectionText,
                status,
                SeriesActive ? seriesCalibrationMinutes : SunEphemerisCalibration.DeltaMinutes,
                compact);
        }

        /// <summary>
        /// Builds the frozen Versetzung/geometry/FIX section from the two
        /// recorded observations with the course/speed current at record
        /// time. Called only from CaptureObservation (and cleared by
        /// ResetSeries) — never on the 1 Hz card refresh.
        /// </summary>
        private void RecomputeFixSnapshot()
        {
            StringBuilder sb = new StringBuilder(256);

            // The card layout prints only observations №1/№2; a third sight
            // (cocked hat) must be visible too, or the navigator cannot see
            // which line is off (P6 follow-up).
            AppendThirdObservation(sb);
            double dtHours = (runningFix.Observation2.Utc - runningFix.Observation1.Utc).TotalHours;
            if (dtHours < 0.0)
                dtHours = 0.0;
            double transferNm = courseSampler.SpeedKn * dtHours;
            sb.Append(FixCardVersetzung).Append('\n');
            sb.Append(FixCardCourse).Append(' ')
              .Append(courseSampler.CourseDeg.ToString("F0", CultureInfo.InvariantCulture)).Append('\u00B0')
              .Append(' ').Append(FixCardSpeed).Append(' ')
              .Append(courseSampler.SpeedKn.ToString("F1", CultureInfo.InvariantCulture)).Append(" kn\n");
            sb.Append("\u0394t ").Append((dtHours * 60.0).ToString("F0", CultureInfo.InvariantCulture))
              .Append(' ').Append(FixCardComposer.FixCardMinutes).Append(' ')
              .Append(FixCardTransfer).Append(' ')
              .Append(transferNm.ToString("F1", CultureInfo.InvariantCulture)).Append(" NM\n");
            if (!courseSampler.IsFresh(runningFix.Observation2.Utc))
                sb.Append(FixCardStaleSpeed).Append('\n');

            // Crossing-angle quality: shallow geometry amplifies small
            // intercept errors (~1/sin θ) — warn the navigator. The advice is
            // context-aware: waiting helps only for two sights of the same body.
            double crossDeg = RunningFixCalculator.CrossingAngleDeg(
                runningFix.Observation1, runningFix.Observation2);
            bool sameBody = runningFix.Observation1.BodyKind == runningFix.Observation2.BodyKind
                && runningFix.Observation1.BodyHip == runningFix.Observation2.BodyHip;
            FixCrossingDeg = crossDeg;
            FixSameBody = sameBody;
            FixPublished = false;
            FixErrorNm = double.NaN;
            sb.Append(FixCardGeometry).Append(' ')
              .Append(crossDeg.ToString("F0", CultureInfo.InvariantCulture))
              .Append('\u00B0').Append(" \u2014 ");
            if (crossDeg > 30.0)
                sb.Append(FixCardGeomGood).Append('\n');
            else if (crossDeg >= 15.0)
                sb.Append(FixCardGeomFair).Append('\n');
            else
                sb.Append(FixCardGeomPoor).Append('\n')
                  .Append(FixCardComposer.GeometryAdvice(crossDeg, sameBody)).Append('\n');

            // A huge intercept is almost always a mis-aim, not a position
            // error: tell the navigator before the fix is published.
            double maxIntercept = Math.Max(
                Math.Abs(runningFix.Observation1.InterceptNm),
                Math.Abs(runningFix.Observation2.InterceptNm));
            if (maxIntercept > MaxInterceptNm)
                sb.Append(FixCardCheckAim).Append('\n');

            // Near-parallel LOPs: the intersection is unusable, do not publish.
            if (!RunningFixCalculator.IsCrossingUsable(crossDeg))
            {
                FixMapMarker.Clear();
                sb.Append("FIX \u2014 ").Append(FixCardComposer.CoincidentMessage(sameBody));
                sectionText = sb.ToString();
                return;
            }

            double fixLat, fixLon;
            CockedHatResult hat = new CockedHatResult();
            bool usedTriangle = runningFix.HasThird
                && runningFix.CalculateTriangleFix(courseSampler.CourseDeg,
                    courseSampler.SpeedKn, 2, out hat)
                && hat.Ok;
            if (usedTriangle)
            {
                fixLat = hat.FixLatDeg;
                fixLon = hat.FixLonDeg;
            }
            else if (!runningFix.CalculateFix(courseSampler.CourseDeg, courseSampler.SpeedKn, out fixLat, out fixLon))
            {
                FixMapMarker.Clear();
                sb.Append("FIX \u2014");
                sectionText = sb.ToString();
                return;
            }
            sb.Append("FIX\n");
            sb.Append("\u03C6 ").Append(SextantDmFormat.FormatLatLon(fixLat, true));
            sb.Append("  \u03BB ").Append(SextantDmFormat.FormatLatLon(fixLon, false)).Append('\n');
            AppendFixTail(sb, usedTriangle, hat);
            FixLatDeg = fixLat;
            FixLonDeg = fixLon;
            FixPublished = true;
            FixMapMarker.Set(fixLat, fixLon,
                "FIX " + runningFix.Observation2.Utc.ToString("HH:mm:ss", CultureInfo.InvariantCulture) +
                "\n" + runningFix.Observation1.ToBody().AsciiName +
                " + " + runningFix.Observation2.ToBody().AsciiName +
                (usedTriangle ? " + " + runningFix.Observation3.ToBody().AsciiName : "") +
                "\nlat " + SextantDmFormat.FormatLatLonAscii(fixLat, true) +
                " lon " + SextantDmFormat.FormatLatLonAscii(fixLon, false));
            float curLat, curLon;
            if (positionProvider.TryGetPosition(out curLat, out curLon))
            {
                double errNm = RunningFixCalculator.FixErrorNm(fixLat, fixLon, curLat, curLon);
                FixErrorNm = errNm;
                sb.Append(FixCardDeltaFromGame).Append(' ')
                  .Append(errNm.ToString("F1", CultureInfo.InvariantCulture)).Append(" NM");
            }
            sectionText = sb.ToString();
        }

        /// <summary>
        /// Fix tail: the cocked-hat metrics, or the third-sight advice (the
        /// recommendation names the best third body, snapshot-pure). Extracted
        /// from RecomputeFixSnapshot to keep its cyclomatic complexity under
        /// the SourceCheck gate (2026-09-22).
        /// </summary>
        private void AppendFixTail(StringBuilder sb, bool usedTriangle, CockedHatResult hat)
        {
            if (usedTriangle)
            {
                AppendTriangleSection(sb, hat);
                return;
            }

            if (!runningFix.HasThird)
            {
                // Two sights give a rough fix; the third tightens it (author).
                AppendThirdAdvice(sb);
                sb.Append('\n');
                return;
            }

            // A third sight exists but the cocked hat was unusable.
            sb.Append(FixCardComposer.FixCardRecommendThird).Append('\n');
        }

        /// <summary>
        /// Names the best third body (change D, 2026-09-22): a star whose
        /// azimuth crosses BOTH recorded sights well. Snapshot-pure — the scan
        /// runs at the second sight's DR and UTC, never the live clock, so the
        /// frozen card cannot mutate on the 1 Hz refresh.
        /// </summary>
        private void AppendThirdAdvice(StringBuilder sb)
        {
            double zenith = CelestialForecast.SunZenithDeg(
                runningFix.Observation2.DrLatDeg, runningFix.Observation2.DrLonDeg,
                runningFix.Observation2.Utc);
            if (!CelestialForecast.IsStarDarkZenith(zenith))
            {
                sb.Append(FixCardComposer.FixCardRecommendThird);
                return;
            }

            var candidates = new System.Collections.Generic.List<BodyRecommendation.Candidate>();
            var seenHips = new System.Collections.Generic.HashSet<int>();
            CelestialForecast.CollectVisibleStars(
                runningFix.Observation2.DrLatDeg, runningFix.Observation2.DrLonDeg,
                runningFix.Observation2.Utc, BodyRecommendation.MinAltitudeDeg,
                candidates, seenHips);
            BodyRecommendation.Result third = BodyRecommendation.RecommendThird(
                runningFix.Observation1.AzDeg, runningFix.Observation2.AzDeg,
                candidates,
                runningFix.Observation1.BodyHip, runningFix.Observation2.BodyHip);
            sb.Append(FixCardComposer.ComposeThirdSuggestion(
                third.Found ? third.Name : null,
                third.Found ? third.SeparationDeg : 0.0));
        }

        /// <summary>Third sight line (2026-09-22): one compact line instead of
        /// the full F-Tafel block — the card has to fit the stacked column, and
        /// the third sight's job is to show which line is off. UT is printed
        /// only when it differs from sight №2 (simultaneous shots share it).</summary>
        private void AppendThirdObservation(StringBuilder sb)
        {
            if (!runningFix.HasThird)
                return;
            bool includeUt = runningFix.Observation2 == null
                || runningFix.Observation3.Utc != runningFix.Observation2.Utc;
            sb.Append(FixCardComposer.ComposeObservationCompact(
                runningFix.Observation3, 3, includeUt)).Append('\n').Append('\n');
        }

        /// <summary>Cocked-hat section (P6): metrics, the best-pair note when
        /// the hat was too rough to publish, and the map vertices.</summary>
        private static void AppendTriangleSection(StringBuilder sb, CockedHatResult hat)
        {
            sb.Append(FixCardComposer.ComposeTriangle(hat.SideANm, hat.SideBNm,
                hat.SideCNm, hat.AreaNm2, hat.RadiusNm)).Append('\n');
            if (hat.UsedBestPairFallback)
                sb.Append(FixCardComposer.BestPairNote).Append('\n');
            FixMapMarker.SetTriangle(
                hat.VertexALatDeg, hat.VertexALonDeg,
                hat.VertexBLatDeg, hat.VertexBLonDeg,
                hat.VertexCLatDeg, hat.VertexCLonDeg);
        }
    }
}
