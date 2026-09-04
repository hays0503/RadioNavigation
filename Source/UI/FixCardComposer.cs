using System.Globalization;
using System.Text;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// F-Tafel worksheet card assembly behind SextantWorksheet.BuildCard.
    /// Extracted (SRP — card text is not worksheet state logic) so the
    /// title/status/calibration/sight/section composition runs offline in
    /// FixCardComposerTests. Pure C#, no Unity dependency.
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
        private const string FixCardFirstHint =
            "\u0421\u043E\u043B\u043D\u0446\u0435 \u0432 \u0446\u0435\u043D\u0442\u0440, 2.5 \u0441, " +
            "\u0437\u0430\u0442\u0435\u043C I";
        internal const string FixCardMinutes = "\u043C\u0438\u043D";
        private const string FixCardQuadrant = "\u043A\u0432.";
        private const string FixCardCalibration =
            "\u041A\u0430\u043B\u0438\u0431\u0440\u043E\u0432\u043A\u0430";

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
                sb.Append(FixCardFirstHint);
                return sb.ToString();
            }
            AppendObservation(sb, obs1, 1);
            sb.Append('\n');
            if (obs2 == null)
            {
                sb.Append('\n').Append(FixCardAwaitSecond);
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
            return sb.ToString();
        }

        private static void AppendObservation(StringBuilder sb, SextantObservation obs, int index)
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
              .Append(System.Math.Abs(obs.InterceptNm).ToString("F1", CultureInfo.InvariantCulture))
              .Append(" NM");
        }
    }
}
