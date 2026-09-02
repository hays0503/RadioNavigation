using System;
using System.Collections.Generic;
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
        private const string SunName = "\u0421\u043E\u043B\u043D\u0446\u0435";

        private const float FixPanelIntervalSeconds = 1f;
        private const float FixStatusSeconds = 3f;
        private const float CourseSampleIntervalSeconds = 2f;
        private const float CourseSampleWindowSeconds = 10f;
        private const float MinTransferSpeedKn = 0.5f;

        // F-Tafel worksheet card strings (v1.9).
        private const string FixCardTitle =
            "\u0410\u0421\u0422\u0420\u041E\u041D\u0410\u0412\u0418\u0413\u0410\u0426\u0418\u042F";
        private const string FixCardSight =
            "\u041D\u0410\u0411\u041B\u042E\u0414\u0415\u041D\u0418\u0415";
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
        private const string FixCardAwaitSecond =
            "\u0417\u0430\u043F\u0438\u0448\u0438\u0442\u0435 \u0432\u0442\u043E\u0440\u043E\u0435 " +
            "\u043D\u0430\u0431\u043B\u044E\u0434\u0435\u043D\u0438\u0435 (I)";
        private const string FixCardFirstHint =
            "\u0421\u043E\u043B\u043D\u0446\u0435 \u0432 \u0446\u0435\u043D\u0442\u0440, 2.5 \u0441, " +
            "\u0437\u0430\u0442\u0435\u043C I";
        private const string FixCardVersetzung =
            "\u0412\u0415\u0420\u0421\u0415\u0422\u0426\u0423\u041D\u0413";
        private const string FixCardCourse = "\u041A\u0443\u0440\u0441";
        private const string FixCardSpeed = "\u0421\u043A\u043E\u0440";
        private const string FixCardMinutes = "\u043C\u0438\u043D";
        private const string FixCardTransfer = "\u041F\u0435\u0440\u0435\u043D\u043E\u0441";
        private const string FixCardError =
            "\u041E\u0448\u0438\u0431\u043A\u0430 \u043E\u0442 \u0441\u0447\u0438\u0441\u043B\u0435\u043D\u0438\u044F";
        private const string FixCardQuadrant = "\u043A\u0432.";
        private const string FixCardCalibration =
            "\u041A\u0430\u043B\u0438\u0431\u0440\u043E\u0432\u043A\u0430";
        private const string FixCardGeometry =
            "\u0413\u0435\u043E\u043C\u0435\u0442\u0440\u0438\u044F";
        private const string FixCardGeomGood =
            "\u0445\u043E\u0440\u043E\u0448\u043E";
        private const string FixCardGeomFair =
            "\u043F\u043E\u0441\u0440\u0435\u0434\u0441\u0442\u0432\u0435\u043D\u043D\u043E";
        private const string FixCardGeomPoor =
            "\u0441\u043B\u0430\u0431\u043E";
        private const string FixCardGeomAdvice =
            "<15\u00B0: \u0440\u0430\u0437\u043D\u0435\u0441\u0438\u0442\u0435 " +
            "\u0437\u0430\u043C\u0435\u0440\u044B \u043F\u043E \u0432\u0440\u0435\u043C\u0435\u043D\u0438";

        private readonly RunningFixCalculator runningFix = new RunningFixCalculator();
        private readonly IPlayerPositionProvider positionProvider;

        private Text cardText;
        private string lastCardLine;
        private string statusLine = "";
        private float statusTimer;
        private float panelTimer;
        private float courseSampleTimer;
        private float smoothedCourseDeg;
        private float smoothedSpeedKn;
        private string sectionText;

        private struct TrackSample
        {
            public DateTime Utc;
            public double Lat;
            public double Lon;
        }
        private readonly List<TrackSample> trackSamples = new List<TrackSample>(16);

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
        /// I-key handler: records a sight (only with the sun identified).
        /// Returns false when the sight was rejected (no sun / no position).
        /// </summary>
        public bool CaptureObservation(string identifiedName, double arcAngle)
        {
            if (identifiedName != SunName)
            {
                statusLine = FixCardNoSight;
                statusTimer = FixStatusSeconds;
                panelTimer = 0f;
                Debug.Log(LogPrefix + " FIXKEY I ignored - sun not identified");
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
                GameTimeResolver.GetUtcNow(), ho, latF, lonF);
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
                "{0} OBS n={1} ho={2:F2} dr=({3:F4},{4:F4}) hc={5:F2} az={6:F2} a={7:F1}NM",
                LogPrefix, isSecond ? 2 : 1, ho, latF, lonF,
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
        /// Course/speed over ground from the recent position track (10 s
        /// window, 2 s sampling). Below the transfer speed threshold the boat
        /// counts as anchored: speed 0, course keeps its last value.
        /// </summary>
        public void UpdateCourseSampler(float dt)
        {
            courseSampleTimer -= dt;
            if (courseSampleTimer > 0f)
                return;
            courseSampleTimer = CourseSampleIntervalSeconds;

            float latF, lonF;
            if (!positionProvider.TryGetPosition(out latF, out lonF))
                return;
            DateTime utc = GameTimeResolver.GetUtcNow();
            trackSamples.Add(new TrackSample { Utc = utc, Lat = latF, Lon = lonF });
            while (trackSamples.Count > 0 &&
                   (utc - trackSamples[0].Utc).TotalSeconds > CourseSampleWindowSeconds + 2.0)
                trackSamples.RemoveAt(0);
            if (trackSamples.Count < 2)
                return;

            TrackSample first = trackSamples[0];
            TrackSample last = trackSamples[trackSamples.Count - 1];
            double spanHours = (last.Utc - first.Utc).TotalHours;
            if (spanHours < CourseSampleWindowSeconds / 3600.0 * 0.6)
                return; // not enough baseline yet right after opening

            double distKm = GeoMath.HaversineKm(
                (float)first.Lat, (float)first.Lon, (float)last.Lat, (float)last.Lon);
            double kn = distKm / 1.852 / spanHours;
            if (kn >= MinTransferSpeedKn)
            {
                smoothedCourseDeg = GeoMath.InitialBearingDeg(
                    (float)first.Lat, (float)first.Lon, (float)last.Lat, (float)last.Lon);
                smoothedSpeedKn = (float)kn;
            }
            else
            {
                smoothedSpeedKn = 0f;
            }
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

        private void AppendObservation(StringBuilder sb, SextantObservation obs, int index)
        {
            // F-Tafel block order: UT -> gamma/SHA/GHA -> lambda_g/LHA ->
            // delta -> h_b/h1 -> Az/intercept.
            sb.Append(FixCardSight).Append(" \u2116").Append(index).Append('\n');
            sb.Append("UT ").Append(obs.Utc.ToString("HH:mm:ss", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("\u03B3 ").Append(SextantDmFormat.FormatDm(obs.GhaAriesDeg));
            // Same D°M.m′ format as gamma/GHA: a 0.1° decimal SHA read as
            // 198°18.0′ made gamma+SHA look 2° off GHA on the card (in-game
            // verification 2026-08-30) — the chain is exact, only the old
            // display rounded SHA to ±3′.
            sb.Append("  SHA ").Append(SextantDmFormat.FormatDm(obs.ShaSunDeg)).Append('\n');
            sb.Append("GHA ").Append(SextantDmFormat.FormatDm(obs.GhaSunDeg)).Append('\n');
            sb.Append("\u03BBg ").Append(SextantDmFormat.FormatLatLon(obs.DrLonDeg, false));
            sb.Append("  LHA ").Append(SextantDmFormat.FormatDm(obs.LhaDeg));
            sb.Append(' ').Append(FixCardQuadrant).Append(obs.Quadrant)
              .Append(obs.QuadrantWest ? " W" : " O").Append('\n');
            sb.Append("\u03B4 ").Append(SextantDmFormat.FormatSignedDm(obs.DecDeg)).Append('\n');
            sb.Append("hb ").Append(SextantDmFormat.FormatDm(obs.HoDeg));
            sb.Append("  h1 ").Append(SextantDmFormat.FormatDm(obs.HcDeg)).Append('\n');
            sb.Append("Az ").Append(obs.AzDeg.ToString("F1", CultureInfo.InvariantCulture));
            sb.Append("  a ").Append(obs.InterceptNm >= 0 ? "+" : "\u2212")
              .Append(Math.Abs(obs.InterceptNm).ToString("F1", CultureInfo.InvariantCulture))
              .Append(" NM");
        }

        private string BuildCard()
        {
            StringBuilder sb = new StringBuilder(512);
            sb.Append(FixCardTitle).Append('\n');
            if (statusTimer > 0f && !string.IsNullOrEmpty(statusLine))
                sb.Append(statusLine).Append('\n');
            double calMinutes = SunEphemerisCalibration.DeltaMinutes;
            if (Math.Abs(calMinutes) > 0.01)
            {
                sb.Append(FixCardCalibration).Append(" \u0394t ")
                  .Append(calMinutes >= 0 ? "+" : "\u2212")
                  .Append(Math.Abs(calMinutes).ToString("F1", CultureInfo.InvariantCulture))
                  .Append(' ').Append(FixCardMinutes).Append('\n');
            }

            if (runningFix.Observation1 == null)
            {
                sb.Append(FixCardFirstHint);
                return sb.ToString();
            }
            AppendObservation(sb, runningFix.Observation1, 1);
            sb.Append('\n');
            if (runningFix.Observation2 == null)
            {
                sb.Append('\n').Append(FixCardAwaitSecond);
                return sb.ToString();
            }
            sb.Append('\n');
            AppendObservation(sb, runningFix.Observation2, 2);
            sb.Append('\n').Append('\n');

            // v1.13: the Versetzung/geometry/FIX tail is a SNAPSHOT computed
            // once when observation 2 was recorded (I key) — reopening the
            // sextant or the passage of time never recomputes it. The math
            // belongs to the two recorded sights, not to the live view.
            if (sectionText != null)
                sb.Append(sectionText);
            return sb.ToString();
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
            double transferNm = smoothedSpeedKn * dtHours;
            sb.Append(FixCardVersetzung).Append('\n');
            sb.Append(FixCardCourse).Append(' ')
              .Append(smoothedCourseDeg.ToString("F0", CultureInfo.InvariantCulture)).Append('\u00B0')
              .Append(' ').Append(FixCardSpeed).Append(' ')
              .Append(smoothedSpeedKn.ToString("F1", CultureInfo.InvariantCulture)).Append(" kn\n");
            sb.Append("\u0394t ").Append((dtHours * 60.0).ToString("F0", CultureInfo.InvariantCulture))
              .Append(' ').Append(FixCardMinutes).Append(' ')
              .Append(FixCardTransfer).Append(' ')
              .Append(transferNm.ToString("F1", CultureInfo.InvariantCulture)).Append(" NM\n");

            // Crossing-angle quality: shallow geometry amplifies small
            // intercept errors (~1/sin θ) — warn the navigator.
            double crossDeg = RunningFixCalculator.CrossingAngleDeg(
                runningFix.Observation1, runningFix.Observation2);
            sb.Append(FixCardGeometry).Append(' ')
              .Append(crossDeg.ToString("F0", CultureInfo.InvariantCulture))
              .Append('\u00B0').Append(" \u2014 ");
            if (crossDeg > 30.0)
                sb.Append(FixCardGeomGood).Append('\n');
            else if (crossDeg >= 15.0)
                sb.Append(FixCardGeomFair).Append('\n');
            else
                sb.Append(FixCardGeomPoor).Append('\n')
                  .Append(FixCardGeomAdvice).Append('\n');

            double fixLat, fixLon;
            if (!runningFix.CalculateFix(smoothedCourseDeg, smoothedSpeedKn, out fixLat, out fixLon))
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
                "\nlat " + SextantDmFormat.FormatLatLonAscii(fixLat, true) +
                " lon " + SextantDmFormat.FormatLatLonAscii(fixLon, false));
            float curLat, curLon;
            if (positionProvider.TryGetPosition(out curLat, out curLon))
            {
                double errNm = RunningFixCalculator.FixErrorNm(fixLat, fixLon, curLat, curLon);
                sb.Append(FixCardError).Append(' ')
                  .Append(errNm.ToString("F1", CultureInfo.InvariantCulture)).Append(" NM");
            }
            sectionText = sb.ToString();
        }
    }
}
