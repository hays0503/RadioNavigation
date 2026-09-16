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
        // real position error.
        private const double MaxInterceptNm = 30.0;

        // F-Tafel worksheet card strings (v1.9) owned by FixCardComposer;
        // only the snapshot-section and status strings live here.
        private const string FixCardNoSight =
            "\u041D\u0415\u0422 \u041D\u0410\u0411\u041B\u042E\u0414\u0415\u041D\u0418\u042F";
        private const string FixCardNoPosition =
            "\u041D\u0415\u0422 \u041F\u041E\u0417\u0418\u0426\u0418\u0418";
        private const string FixCardRecorded1 =
            "\u041D\u0410\u0411\u041B\u042E\u0414\u0415\u041D\u0418\u0415 \u21161 \u0417\u0410\u041F\u0418\u0421\u0410\u041D\u041E";
        private const string FixCardRecorded2 =
            "\u041D\u0410\u0411\u041B\u042E\u0414\u0415\u041D\u0418\u0415 \u21162 \u0417\u0410\u041F\u0418\u0421\u0410\u041D\u041E";
        private const string FixCardNewSeries =
            "\u041D\u041E\u0412\u0410\u042F \u0421\u0415\u0420\u0418\u042F";
        private const string FixCardVersetzung =
            "\u0412\u0415\u0420\u0421\u0415\u0422\u0426\u0423\u041D\u0413";
        private const string FixCardCourse = "\u041A\u0443\u0440\u0441";
        private const string FixCardSpeed = "\u0421\u043A\u043E\u0440";
        private const string FixCardTransfer = "\u041F\u0435\u0440\u0435\u043D\u043E\u0441";
        // Renamed from "Ошибка от счисления" (v2.2): the reference is the
        // game's true position, not a dead-reckoned one — this is a self-check
        // of the sights, not a navigation result.
        private const string FixCardDeltaFromGame =
            "\u0394 \u043E\u0442 \u043C\u0435\u0441\u0442\u0430 \u0438\u0433\u0440\u044B (\u043F\u0440\u043E\u0432\u0435\u0440\u043A\u0430)";
        private const string FixCardCheckAim =
            "\u041F\u0440\u043E\u0432\u0435\u0440\u044C\u0442\u0435 \u043D\u0430\u0432\u0435\u0434\u0435\u043D\u0438\u0435 " +
            "(a > 30 NM)";
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
        private string lastCardLine;
        private string statusLine = "";
        private float statusTimer;
        private float panelTimer;
        private float courseSampleTimer;
        private readonly CourseSampler courseSampler = new CourseSampler();
        private string sectionText;

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

        /// <summary>Binds the card Text built by the overlay constructor.</summary>
        public void BindCard(Text text)
        {
            cardText = text;
        }

        /// <summary>Forces the next refresh to repaint (open/reset).</summary>
        public void InvalidateCard()
        {
            lastCardLine = null;
            panelTimer = 0f;
        }

        /// <summary>
        /// I-key handler: records a sight of any identified body (Sun, Moon
        /// or star). Returns false when no body is identified or there is no
        /// sandbox position.
        /// </summary>
        public bool CaptureObservation(CelestialBody body, double arcAngle)
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

            double ho = SextantArc.Quantize(arcAngle);
            SextantObservation obs = RunningFixCalculator.BuildObservation(
                utc, ho, latF, lonF, body);
            runningFix.Record(obs);
            FixMapMarker.OnNewObservation();
            // v1.13: the fix is a snapshot computed at record time only.
            if (runningFix.CanSolve)
                RecomputeFixSnapshot();
            else
                sectionText = null;
            bool isSecond = runningFix.Observation2 == obs;
            statusLine = isSecond ? FixCardRecorded2 : FixCardRecorded1;
            statusTimer = FixStatusSeconds;
            panelTimer = 0f;
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "{0} OBS n={1} body={2} ho={3:F2} dr=({4:F4},{5:F4}) hc={6:F2} az={7:F2} a={8:F1}NM",
                LogPrefix, isSecond ? 2 : 1, obs.BodyName, ho, latF, lonF,
                obs.HcDeg, obs.AzDeg, obs.InterceptNm));
            return true;
        }

        /// <summary>O-key handler: starts a new worksheet series.</summary>
        public void ResetSeries()
        {
            runningFix.Reset();
            FixMapMarker.Clear();
            sectionText = null;
            statusLine = FixCardNewSeries;
            statusTimer = FixStatusSeconds;
            panelTimer = 0f;
            Debug.Log(LogPrefix + " FIX series reset");
        }

        /// <summary>
        /// Course/speed sampler adapter: 2 s throttle (the provider read is
        /// chained reflection), then feeds position + clock into the pure
        /// CourseSampler. The window math lives there, tested offline.
        /// </summary>
        public void UpdateCourseSampler(float dt)
        {
            courseSampleTimer -= dt;
            if (courseSampleTimer > 0f)
                return;
            courseSampleTimer = CourseSampler.SampleIntervalSeconds;

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

            string card = BuildCard();
            if (card != lastCardLine)
            {
                cardText.text = card;
                lastCardLine = card;
            }
        }

        /// <summary>Card assembly lives in FixCardComposer (tested offline).</summary>
        private string BuildCard()
        {
            string status = statusTimer > 0f && !string.IsNullOrEmpty(statusLine) ? statusLine : null;
            return FixCardComposer.Compose(
                runningFix.Observation1,
                runningFix.Observation2,
                sectionText,
                status,
                SunEphemerisCalibration.DeltaMinutes);
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

            // Crossing-angle quality: shallow geometry amplifies small
            // intercept errors (~1/sin θ) — warn the navigator. The advice is
            // context-aware: waiting helps only for two sights of the same body.
            double crossDeg = RunningFixCalculator.CrossingAngleDeg(
                runningFix.Observation1, runningFix.Observation2);
            bool sameBody = runningFix.Observation1.BodyKind == runningFix.Observation2.BodyKind
                && runningFix.Observation1.BodyHip == runningFix.Observation2.BodyHip;
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
            if (!runningFix.CalculateFix(courseSampler.CourseDeg, courseSampler.SpeedKn, out fixLat, out fixLon))
            {
                FixMapMarker.Clear();
                sb.Append("FIX \u2014");
                sectionText = sb.ToString();
                return;
            }
            sb.Append("FIX\n");
            sb.Append("\u03C6 ").Append(SextantDmFormat.FormatLatLon(fixLat, true));
            sb.Append("  \u03BB ").Append(SextantDmFormat.FormatLatLon(fixLon, false)).Append('\n');
            FixMapMarker.Set(fixLat, fixLon,
                "FIX " + runningFix.Observation2.Utc.ToString("HH:mm:ss", CultureInfo.InvariantCulture) +
                "\n" + runningFix.Observation1.ToBody().AsciiName +
                " + " + runningFix.Observation2.ToBody().AsciiName +
                "\nlat " + SextantDmFormat.FormatLatLonAscii(fixLat, true) +
                " lon " + SextantDmFormat.FormatLatLonAscii(fixLon, false));
            float curLat, curLon;
            if (positionProvider.TryGetPosition(out curLat, out curLon))
            {
                double errNm = RunningFixCalculator.FixErrorNm(fixLat, fixLon, curLat, curLon);
                sb.Append(FixCardDeltaFromGame).Append(' ')
                  .Append(errNm.ToString("F1", CultureInfo.InvariantCulture)).Append(" NM");
            }
            sectionText = sb.ToString();
        }
    }
}
