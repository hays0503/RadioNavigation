using System.Collections.Generic;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Gain-gated pattern row selection behind
    /// InformationPanelHandler.UpdatePatterns. Extracted (SRP — row
    /// filtering is not panel rendering) so selection + packing run
    /// offline in PatternRowPackerTests. Pure C#.
    /// </summary>
    public static class PatternRowPacker
    {
        private const float DisplayGainThreshold = 0.001f;

        public static int[] Pack(StationDiagnostics[] diags, int maxRows)
        {
            var indices = new List<int>(diags.Length);
            for (int i = 0; i < diags.Length; i++)
            {
                if (diags[i].Gain.IsNegligible(DisplayGainThreshold))
                    continue;
                indices.Add(i);
                if (indices.Count >= maxRows)
                    break;
            }
            return indices.ToArray();
        }
    }
}
