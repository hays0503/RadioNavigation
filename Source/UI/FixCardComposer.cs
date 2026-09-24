using System.Globalization;
using System.Text;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// F-Tafel worksheet card assembly behind SextantWorksheet.BuildCard.
    /// Extracted (SRP — card text is not worksheet state logic) so the
    /// title/status/calibration/sight/section composition runs offline in
    /// FixCardComposerTests. Pure C#, no Unity dependency.
    /// v2.2: body-aware sights (Sun/Moon/star), a per-sight accuracy flag
    /// for the low-precision Moon, a low-altitude caution and a game-
    /// assumptions footer (refraction/parallax/dip are not modelled).
    /// </summary>
    internal static class FixCardComposer
    {
        // F-Tafel worksheet card strings (v1.9).
        private const string FixCardTitle =
            "\u0410\u0421\u0422\u0420\u041E\u041D\u0410\u0412\u0418\u0413\u0410\u0426\u0418\u042F";
        private const string FixCardSight =
            "\u041D\u0410\u0411\u041B\u042E\u0414\u0415\u041D\u0418\u0415";
        private const string FixCardAwaitSecond =
            "\u0417\u0430\u043F\u0438\u0448\u0438\u0442\u0435 \u0432\u0442\u043E\u0440\u043E\u0435 " +
            "\u043D\u0430\u0431\u043B\u044E\u0434\u0435\u043D\u0438\u0435 (I)";
        // v2.2: generalized from "Солнце в центр..." to any body.
        private const string FixCardFirstHint =
            "\u041D\u0430\u0432\u0435\u0434\u0438\u0442\u0435 \u0441\u0432\u0435\u0442\u0438\u043B\u043E " +
            "\u0432 \u0446\u0435\u043D\u0442\u0440, 2.5 \u0441, \u0437\u0430\u0442\u0435\u043C I";
        internal const string FixCardMinutes = "\u043C\u0438\u043D";
        private const string FixCardQuadrant = "\u043A\u0432.";
        private const string FixCardCalibration =
            "\u041A\u0430\u043B\u0438\u0431\u0440\u043E\u0432\u043A\u0430";
        // v2.2 caution flags.
        private const string FixCardLowAltitude =
            "\u043D\u0438\u0437\u043A\u043E \u2014 \u0433\u0440\u0443\u0431\u043E";
        private const string FixCardMoonAccuracy =
            "\u041B\u0443\u043D\u0430 \u00B10.1\u00B0";
        // Game assumptions footer (author request): the game does not model
        // refraction / horizon dip, so the worksheet cannot either. The lunar
        // parallax IS modelled (P2/R5: topocentric reduction), and the Moon's
        // ephemeris is now the Meeus-47 series (±0.1° instead of ±0.3°).
        // "P: справка" opens the on-screen F-Tafel legend.
        private const string FixCardAssumptions =
            "\u0411\u0435\u0437 \u0440\u0435\u0444\u0440\u0430\u043A\u0446\u0438\u0438/dip \u00B7 " +
            "\u041B\u0443\u043D\u0430 \u00B10.1\u00B0 " +
            "(\u043F\u0430\u0440\u0430\u043B\u043B\u0430\u043A\u0441 \u0443\u0447\u0442\u0451\u043D) \u00B7 " +
            "P: \u0441\u043F\u0440\u0430\u0432\u043A\u0430";

        // v2.2 geometry guidance: waiting for a wider crossing angle only
        // helps when both sights are the SAME body; with two different bodies
        // the navigator must pick another azimuth.
        private const string FixCardAdviceTime =
            "<15\u00B0: \u0440\u0430\u0437\u043D\u0435\u0441\u0438\u0442\u0435 " +
            "\u0437\u0430\u043C\u0435\u0440\u044B \u043F\u043E \u0432\u0440\u0435\u043C\u0435\u043D\u0438";
        private const string FixCardAdviceAzimuth =
            "<15\u00B0: \u0440\u0430\u0437\u043D\u0435\u0441\u0438\u0442\u0435 " +
            "\u0441\u0432\u0435\u0442\u0438\u043B\u0430 \u043F\u043E \u0430\u0437\u0438\u043C\u0443\u0442\u0443";
        private const string FixCardCoincidentTime =
            "\u041B\u0438\u043D\u0438\u0438 \u0441\u043E\u0432\u043F\u0430\u0434\u0430\u044E\u0442 \u2014 " +
            "\u0440\u0430\u0437\u043D\u0435\u0441\u0438\u0442\u0435 \u0437\u0430\u043C\u0435\u0440\u044B " +
            "\u043F\u043E \u0432\u0440\u0435\u043C\u0435\u043D\u0438";
        private const string FixCardCoincidentAzimuth =
            "\u041B\u0438\u043D\u0438\u0438 \u0441\u043E\u0432\u043F\u0430\u0434\u0430\u044E\u0442 \u2014 " +
            "\u0440\u0430\u0437\u043D\u0435\u0441\u0438\u0442\u0435 \u0441\u0432\u0435\u0442\u0438\u043B\u0430 " +
            "\u043F\u043E \u0430\u0437\u0438\u043C\u0443\u0442\u0443";

        /// <summary>Advice under a shallow crossing angle, or null when the
        /// geometry is not shallow. Context-aware (same body vs two bodies).</summary>
        public static string GeometryAdvice(double crossDeg, bool sameBody)
        {
            if (crossDeg >= 15.0)
                return null;
            return sameBody ? FixCardAdviceTime : FixCardAdviceAzimuth;
        }

        /// <summary>Third-sight suggestion (change D): the generic advice, plus
        /// the recommended body and the smallest crossing angle to the two
        /// recorded sights when a name is available. The worksheet passes its
        /// frozen snapshot values, so this never reads the clock.</summary>
        public static string ComposeThirdSuggestion(string name, double minSeparationDeg)
        {
            if (string.IsNullOrEmpty(name))
                return FixCardRecommendThird;
            return FixCardRecommendThird + " \u2014 " + name + " (\u043C\u0438\u043D "
                + System.Math.Round(minSeparationDeg).ToString("F0", CultureInfo.InvariantCulture)
                + "\u00B0)";
        }

        // P6 cocked hat (three sights): author thresholds on the centre radius.
        private const string FixCardTriangle =
            "\u0422\u0420\u0415\u0423\u0413\u041E\u041B\u042C\u041D\u0418\u041A";
        internal const string FixCardRecommendThird =
            "\u0420\u0435\u043A\u043E\u043C\u0435\u043D\u0434\u0443\u0435\u043C 3-\u0439 \u0437\u0430\u043C\u0435\u0440 \u2014 \u0444\u0438\u043A\u0441 \u0443\u0442\u043E\u0447\u043D\u0438\u0442\u0441\u044F";
        private const string FixCardGradeGood = "\u0445\u043E\u0440\u043E\u0448\u043E";
        private const string FixCardGradeFair = "\u043F\u043E\u0441\u0440\u0435\u0434\u0441\u0442\u0432\u0435\u043D\u043D\u043E";
        private const string FixCardGradeRough = "\u0433\u0440\u0443\u0431\u043E";
        private const string FixCardSides = "\u0441\u0442\u043E\u0440\u043E\u043D\u044B";
        private const string FixCardArea = "\u043F\u043B\u043E\u0449\u0430\u0434\u044C";

        /// <summary>P6 cocked-hat block: the centre radius with the author's
        /// grade (≤3 NM good, 3–6 fair, &gt;6 rough), the triangle sides and
        /// its area. The radius is the visible "how good is this fix" number.</summary>
        public static string ComposeTriangle(double sideANm, double sideBNm, double sideCNm,
            double areaNm2, double radiusNm)        {
            string grade = radiusNm <= 3.0
                ? FixCardGradeGood
                : (radiusNm <= 6.0 ? FixCardGradeFair : FixCardGradeRough);

            var sb = new StringBuilder(96);
            sb.Append(FixCardTriangle).Append(" R ")
              .Append(Format1(radiusNm)).Append(" NM \u2014 ").Append(grade);
            sb.Append('\n').Append(FixCardSides).Append(' ')
              .Append(Format1(sideANm)).Append('/')
              .Append(Format1(sideBNm)).Append('/')
              .Append(Format1(sideCNm)).Append(" NM");
            sb.Append('\n').Append(FixCardArea).Append(' ')
              .Append(Format1(areaNm2)).Append(" NM\u00B2");
            return sb.ToString();
        }

        /// <summary>Instrument index error line (P6): "ИП +0.4′".</summary>
        public static string ComposeIndexError(double indexErrorArcMin)
        {
            string sign = indexErrorArcMin >= 0.0 ? "+" : "\u2212";
            return FixCardIndexError + " " + sign
                + Format1(System.Math.Abs(indexErrorArcMin)) + "\u2032";
        }

        // P6 sight-gate refusal (the reason is shown, not silently dropped).
        private const string FixCardRejected = "\u041E\u0422\u041A\u041B\u041E\u041D\u0415\u041D\u041E";
        private const string FixCardBlockLow = "\u043D\u0438\u0437\u043A\u043E (<10\u00B0)";
        private const string FixCardBlockDaylight = "\u0434\u043D\u0451\u043C";
        private const string FixCardBlockMoonNew = "\u043D\u043E\u0432\u0430\u044F \u041B\u0443\u043D\u0430";
        private const string FixCardBlockMoonSet =
            "\u041B\u0443\u043D\u0430 \u043F\u043E\u0434 \u0433\u043E\u0440\u0438\u0437\u043E\u043D\u0442\u043E\u043C";

        /// <summary>Refusal line for a blocked sight (P6): "ОТКЛОНЕНО: низко
        /// (&lt;10°)" etc. Empty for SightBlock.None.</summary>
        public static string ComposeBlockReason(SightBlock block)
        {
            switch (block)
            {
                case SightBlock.Low: return FixCardRejected + ": " + FixCardBlockLow;
                case SightBlock.Daylight: return FixCardRejected + ": " + FixCardBlockDaylight;
                case SightBlock.MoonNew: return FixCardRejected + ": " + FixCardBlockMoonNew;
                case SightBlock.MoonSet: return FixCardRejected + ": " + FixCardBlockMoonSet;
                default: return "";
            }
        }

        /// <summary>Note when a rough cocked hat published the best pair
        /// instead of its centre (P6 follow-up, live 2026-09-22).</summary>
        public const string BestPairNote =
            "\u0424\u0418\u041A\u0421 \u043F\u043E \u043B\u0443\u0447\u0448\u0435\u0439 \u043F\u0430\u0440\u0435 " +
            "(\u0442\u0440\u0435\u0443\u0433\u043E\u043B\u044C\u043D\u0438\u043A \u0433\u0440\u0443\u0431\u044B\u0439)";

        /// <summary>
        /// One-line form of an observation (2026-09-22): a full F-Tafel block
        /// for every sight made the card ~41 lines, and in the stacked column
        /// (only ~410 units of usable height) it ran off the bottom edge. The
        /// third sight only has to show which line is off, so it uses this
        /// form — the full chain stays on №1/№2. UT is optional because
        /// simultaneous star sights share the second sight's time.
        /// </summary>
        public static string ComposeObservationCompact(SextantObservation obs, int index,
            bool includeUt)
        {
            var sb = new StringBuilder(96);
            sb.Append('\u2116').Append(index.ToString(CultureInfo.InvariantCulture))
              .Append(' ').Append(obs.BodyName);
            if (includeUt)
                sb.Append(" UT ").Append(obs.Utc.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
            sb.Append(" hb ").Append(SextantDmFormat.FormatDm(obs.HoDeg));
            sb.Append(" h1 ").Append(SextantDmFormat.FormatDm(obs.HcDeg));
            sb.Append(" Az ").Append(Format1(obs.AzDeg));
            sb.Append(" a ").Append(obs.InterceptNm >= 0 ? "+" : "\u2212")
              .Append(Format1(System.Math.Abs(obs.InterceptNm))).Append(" NM");
            return sb.ToString();
        }

        private static string Format1(double value)
        {
            return value.ToString("F1", CultureInfo.InvariantCulture);
        }

        // P6 burst annotation: the committed h_b is the mean of the taps.
        private const string FixCardBurstRejected = "\u043E\u0442\u0431\u0440";
        private const string FixCardSightRough = "\u043A\u0430\u0447\u043A\u0430 \u2014 \u0433\u0440\u0443\u0431\u043E";
        // P6 instrument index error ("ИП").
        private const string FixCardIndexError = "\u0418\u041F";

        /// <summary>Burst annotation (P6): "± 0.4′ (n=5, 1 отбр)" next to h_b —
        /// the spread of the taps taken at the recorded drum setting (a
        /// quality meter since 2026-09-22, when the recorded Ho became the
        /// drum reading itself), with the rough flag when the spread exceeds
        /// 1.0′ (swell).</summary>
        public static string ComposeBurst(double sigmaArcMin, int samples, int rejected)
        {
            var sb = new StringBuilder(32);
            sb.Append("\u00B1 ").Append(Format1(sigmaArcMin)).Append("\u2032 (n=")
              .Append(samples.ToString(CultureInfo.InvariantCulture));
            if (rejected > 0)
                sb.Append(", ").Append(rejected.ToString(CultureInfo.InvariantCulture))
                  .Append(' ').Append(FixCardBurstRejected);
            sb.Append(')');
            if (sigmaArcMin > SextantBurstSampler.MaxSigmaArcMin)
                sb.Append(" \u2014 ").Append(FixCardSightRough);
            return sb.ToString();
        }

        /// <summary>Message for an unusable near-parallel crossing.</summary>
        public static string CoincidentMessage(bool sameBody)
        {
            return sameBody ? FixCardCoincidentTime : FixCardCoincidentAzimuth;
        }

        /// <summary>Full-card entry point (the SourceCheck contract pins this
        /// 5-parameter shape): both observations print as full F-Tafel blocks.</summary>
        public static string Compose(SextantObservation obs1, SextantObservation obs2,
            string sectionText, string statusLine, double calMinutes)
        {
            return Compose(obs1, obs2, sectionText, statusLine, calMinutes, false);
        }

        /// <summary>Card assembly with the compact-observation switch
        /// (2026-09-22): the worksheet picks it when the column cannot fit the
        /// full blocks (FixCardLayout.DecideMode).</summary>
        public static string Compose(SextantObservation obs1, SextantObservation obs2,
            string sectionText, string statusLine, double calMinutes,
            bool compactObservations)
        {
            StringBuilder sb = new StringBuilder(512);
            sb.Append(FixCardTitle).Append('\n');
            if (!string.IsNullOrEmpty(statusLine))
                sb.Append(statusLine).Append('\n');
            if (System.Math.Abs(calMinutes) > 0.01)
            {
                sb.Append(FixCardCalibration).Append(" \u0394t ")
                  .Append(calMinutes >= 0 ? "+" : "\u2212")
                  .Append(System.Math.Abs(calMinutes).ToString("F1", CultureInfo.InvariantCulture))
                  .Append(' ').Append(FixCardMinutes).Append('\n');
            }

            if (obs1 == null)
            {
                sb.Append(FixCardFirstHint).Append('\n');
                sb.Append(FixCardAssumptions);
                return sb.ToString();
            }
            AppendSight(sb, obs1, 1, compactObservations);
            sb.Append('\n');
            if (obs2 == null)
            {
                sb.Append('\n').Append(FixCardAwaitSecond).Append('\n');
                sb.Append(FixCardAssumptions);
                return sb.ToString();
            }
            sb.Append('\n');
            AppendSight(sb, obs2, 2, compactObservations);
            sb.Append('\n').Append('\n');

            // v1.13: the Versetzung/geometry/FIX tail is a SNAPSHOT computed
            // once when observation 2 was recorded (I key) — reopening the
            // sextant or the passage of time never recomputes it. The math
            // belongs to the two recorded sights, not to the live view.
            if (sectionText != null)
                sb.Append(sectionText);
            sb.Append('\n').Append(FixCardAssumptions);
            return sb.ToString();
        }

        /// <summary>Appends one observation in the requested form: the full
        /// F-Tafel block, or the compact one-liner when the card must fit a
        /// small unit space (2026-09-22, FixCardLayout.DecideMode).</summary>
        private static void AppendSight(StringBuilder sb, SextantObservation obs, int index,
            bool compact)
        {
            if (compact)
                sb.Append(ComposeObservationCompact(obs, index, true));
            else
                AppendObservation(sb, obs, index);
        }

        private static void AppendObservation(StringBuilder sb, SextantObservation obs, int index)
        {
            // F-Tafel block order: UT -> gamma/SHA/GHA -> lambda_g/LHA ->
            // delta -> h_b/h1 -> Az/intercept.
            sb.Append(FixCardSight).Append(" \u2116").Append(index);
            if (!string.IsNullOrEmpty(obs.BodyName))
                sb.Append(" \u2014 ").Append(obs.BodyName);
            sb.Append('\n');
            sb.Append("UT ").Append(obs.Utc.ToString("HH:mm:ss", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("\u03B3 ").Append(SextantDmFormat.FormatDm(obs.GhaAriesDeg));
            // Same D°M.m′ format as gamma/GHA: a 0.1° decimal SHA read as
            // 198°18.0′ made gamma+SHA look 2° off GHA on the card (in-game
            // verification 2026-08-30) — the chain is exact, only the old
            // display rounded SHA to ±3′.
            sb.Append("  SHA ").Append(SextantDmFormat.FormatDm(obs.ShaBodyDeg)).Append('\n');
            sb.Append("GHA ").Append(SextantDmFormat.FormatDm(obs.GhaBodyDeg)).Append('\n');
            sb.Append("\u03BBg ").Append(SextantDmFormat.FormatLatLon(obs.DrLonDeg, false));
            sb.Append("  LHA ").Append(SextantDmFormat.FormatDm(obs.LhaDeg));
            sb.Append(' ').Append(FixCardQuadrant).Append(obs.Quadrant)
              .Append(obs.QuadrantWest ? " W" : " O").Append('\n');
            sb.Append("\u03B4 ").Append(SextantDmFormat.FormatSignedDm(obs.DecDeg)).Append('\n');
            sb.Append("hb ").Append(SextantDmFormat.FormatDm(obs.HoDeg));
            if (obs.HoSampleCount > 1)
                sb.Append(' ').Append(ComposeBurst(obs.HoSigmaArcMin,
                    obs.HoSampleCount, obs.HoRejectedCount));
            if (obs.IndexErrorArcMin != 0.0)
                sb.Append('\n').Append(ComposeIndexError(obs.IndexErrorArcMin));
            sb.Append("  h1 ").Append(SextantDmFormat.FormatDm(obs.HcDeg));
            if (obs.BodyKind == CelestialBodyKind.Moon)
                sb.Append("  ").Append(FixCardMoonAccuracy);
            sb.Append('\n');
            sb.Append("Az ").Append(obs.AzDeg.ToString("F1", CultureInfo.InvariantCulture));
            sb.Append("  a ").Append(obs.InterceptNm >= 0 ? "+" : "\u2212")
              .Append(System.Math.Abs(obs.InterceptNm).ToString("F1", CultureInfo.InvariantCulture))
              .Append(" NM");
            if (obs.HoDeg < BodyRecommendation.MinAltitudeDeg)
                sb.Append('\n').Append(FixCardLowAltitude);
        }
    }
}
