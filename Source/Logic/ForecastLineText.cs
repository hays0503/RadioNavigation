using System;
using System.Globalization;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Assembled forecast line plus the triple membership (the readout
    /// marks those rows with a star).</summary>
    internal struct ForecastLine
    {
        public string Text;
        public bool HasTriple;
        public string TripleA;
        public string TripleB;
        public string TripleC;
    }

    /// <summary>
    /// Bodies-readout forecast line (2026-09-22): the twilight boundary plus
    /// the best PAIR or, when a triple exists, the best TRIPLE — never both.
    /// The column is 440 units (≈70 characters at font 11) and does not wrap,
    /// so the triple goes on its own line. Pure, no Unity — unit-tested offline.
    /// </summary>
    internal static class ForecastLineText
    {
        public const string TwilightLabel = "\u0421\u0443\u043C\u0435\u0440\u043A\u0438";
        public const string NightLabel = "\u041D\u043E\u0447\u044C";
        public const string UntilLabel = "\u0434\u043E";
        public const string AtLabel = "\u0432";
        public const string PairLabel = "\u041F\u0430\u0440\u0430";
        public const string TripleLabel = "\u0422\u0440\u043E\u0439\u043A\u0430";
        public const string MinLabel = "\u043C\u0438\u043D";

        /// <summary>"Пара: A+B (ΔAz 87°) в 21:10".</summary>
        public static string PairSegment(string a, string b, double sepDeg, DateTime whenUtc)
        {
            return PairLabel + ": " + a + "+" + b + " (\u0394Az "
                + Math.Round(sepDeg).ToString("F0", CultureInfo.InvariantCulture) + "\u00B0) "
                + AtLabel + " " + whenUtc.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>"Тройка: A+B+C (мин 60°) в 21:10".</summary>
        public static string TripleSegment(string a, string b, string c, double minSepDeg,
            DateTime whenUtc)
        {
            return TripleLabel + ": " + a + "+" + b + "+" + c + " (" + MinLabel + " "
                + Math.Round(minSepDeg).ToString("F0", CultureInfo.InvariantCulture) + "\u00B0) "
                + AtLabel + " " + whenUtc.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>Twilight prefix ("Ночь до 18:56"), or "" when the boundary
        /// is unknown (polar night/day).</summary>
        public static string TwilightSegment(bool night, bool found, DateTime boundaryUtc)
        {
            if (!found)
                return "";
            return (night ? NightLabel : TwilightLabel) + " " + UntilLabel + " "
                + boundaryUtc.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Assembles the forecast line. A triple always replaces the pair (the
        /// column cannot hold both) and sits on its own line; otherwise the
        /// twilight and the pair share one line with the " · " glue.
        /// </summary>
        public static ForecastLine Compose(string twilight, string triple, string pair,
            string tripleA, string tripleB, string tripleC)
        {
            var line = new ForecastLine();
            bool hasTriple = !string.IsNullOrEmpty(triple);
            line.HasTriple = hasTriple;
            line.TripleA = tripleA;
            line.TripleB = tripleB;
            line.TripleC = tripleC;

            if (hasTriple)
            {
                line.Text = string.IsNullOrEmpty(twilight) ? triple : twilight + "\n" + triple;
                return line;
            }

            if (string.IsNullOrEmpty(pair))
            {
                line.Text = twilight ?? "";
                return line;
            }

            line.Text = string.IsNullOrEmpty(twilight) ? pair : twilight + " \u00B7 " + pair;
            return line;
        }
    }
}
