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

        public static float FilterDimAlpha(int count)
        {
            return 0.30f * count;
        }
    }
}
