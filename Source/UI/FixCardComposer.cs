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
            "\u041B\u0443\u043D\u0430 \u00B10.3\u00B0";
        // Game assumptions footer (author request): the game does not model
        // refraction / lunar parallax / horizon dip, so the worksheet cannot
        // either. "P: справка" opens the on-screen F-Tafel legend.
        private const string FixCardAssumptions =
            "\u0411\u0435\u0437 \u0440\u0435\u0444\u0440\u0430\u043A\u0446\u0438\u0438/" +
            "\u043F\u0430\u0440\u0430\u043B\u043B\u0430\u043A\u0441\u0430/dip \u00B7 " +
            "\u041B\u0443\u043D\u0430 \u00B10.3\u00B0 \u00B7 P: \u0441\u043F\u0440\u0430\u0432\u043A\u0430";

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

        /// <summary>Message for an unusable near-parallel crossing.</summary>
        public static string CoincidentMessage(bool sameBody)
        {
            return sameBody ? FixCardCoincidentTime : FixCardCoincidentAzimuth;
        }

        public static string Compose(SextantObservation obs1, SextantObservation obs2,
            string sectionText, string statusLine, double calMinutes)
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
            AppendObservation(sb, obs1, 1);
            sb.Append('\n');
            if (obs2 == null)
            {
                sb.Append('\n').Append(FixCardAwaitSecond).Append('\n');
                sb.Append(FixCardAssumptions);
                return sb.ToString();
            }
            sb.Append('\n');
            AppendObservation(sb, obs2, 2);
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
