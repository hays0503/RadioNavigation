using System;
using System.Text;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Reading-column text behind SextantController (P5.4 slice 4d, SRP):
    /// arc readout with an emit-only-on-change latch, shade-filter label
    /// assembly and filter-dim alpha. The controller applies Text/color;
    /// the decisions live here and run offline in SextantReadoutTests.
    /// Pure C#. Cyrillic is u-escaped like the rest of the mod (game
    /// compiler encoding safety).
    /// </summary>
    public sealed class SextantReadout
    {
        private double lastFormattedArc = double.MinValue;

        public void Reset()
        {
            lastFormattedArc = double.MinValue;
        }

        public bool TryUpdateArc(double arcAngle, bool force, out string text)
        {
            double q = SextantArc.Quantize(arcAngle);
            if (!force && Math.Abs(q - lastFormattedArc) <= 1e-9)
            {
                text = null;
                return false;
            }
            lastFormattedArc = q;
            text = SextantArc.Format(q);
            return true;
        }

        public static string FormatFilters(int leftCount, int rightCount)
        {
            if (leftCount <= 0 && rightCount <= 0)
                return "";
            StringBuilder sb = new StringBuilder("\u0424\u0418\u041B\u042C\u0422\u0420 ");
            if (leftCount > 0)
                sb.Append("\u041B").Append(leftCount).Append(" ");
            if (rightCount > 0)
                sb.Append("\u041F").Append(rightCount);
            return sb.ToString().TrimEnd();
        }

        /// <summary>Lens state: empty while the glass is swung out.</summary>
        public static string FormatLens(bool lensOn, float lengthDeg)
        {
            if (!lensOn)
                return "";
            return "\u041B\u0438\u043D\u0437\u0430 " + lengthDeg.ToString("0.00",
                System.Globalization.CultureInfo.InvariantCulture) + "\u00B0";
        }

        /// <summary>Filters plus the lens on one readout line.</summary>
        public static string FormatFiltersAndLens(int leftCount, int rightCount,
            bool lensOn, float lengthDeg)
        {
            string filters = FormatFilters(leftCount, rightCount);
            string lens = FormatLens(lensOn, lengthDeg);
            if (filters.Length == 0)
                return lens;
            return lens.Length == 0 ? filters : filters + " \u00B7 " + lens;
        }

        public static float FilterDimAlpha(int count)
        {
            return 0.30f * count;
        }
    }
}
