using System;
using System.Collections.Generic;

namespace UBOAT.Mods.TestUI
{
    public static class SonneSignalGenerator
    {
        public static int CalculateDotCount(float delta, float beamHalfWidthDeg, int maxCount)
        {
            if (beamHalfWidthDeg <= 0f)
                return maxCount / 2;

            float ratio = (delta + beamHalfWidthDeg) / (2f * beamHalfWidthDeg);
            ratio = Math.Max(0f, Math.Min(1f, ratio));
            return (int)Math.Round(ratio * maxCount);
        }

        public static float CalculateDashRatio(float delta, float beamHalfWidthDeg)
        {
            if (beamHalfWidthDeg <= 0f)
                return 0.5f;

            float ratio = (delta + beamHalfWidthDeg) / (2f * beamHalfWidthDeg);
            return Math.Max(0f, Math.Min(1f, ratio));
        }

        public static int[] GenerateBearingTable(BeaconStation station)
        {
            return station.GetDotCountToBearingTable();
        }

        public static int LookupBearing(int dotCount, int[] bearingTable)
        {
            if (bearingTable == null || bearingTable.Length == 0)
                return 0;

            dotCount = Math.Max(0, Math.Min(bearingTable.Length - 1, dotCount));
            return bearingTable[dotCount];
        }

        public static NavigationElementType[] GenerateConsolPattern(
            string callsign,
            int dotCount,
            ConsolCycleConfig config,
            int[] bearingTable)
        {
            var result = new List<NavigationElementType>();

            if (!string.IsNullOrEmpty(callsign))
            {
                foreach (char c in callsign.ToUpperInvariant())
                {
                    string mc = MorseAlphabet.GetMorse(c);
                    for (int b = 0; b < mc.Length; b++)
                    {
                        if (mc[b] == '-')
                            result.Add(NavigationElementType.Dash);
                        else
                            result.Add(NavigationElementType.Dot);
                    }
                }
            }

            for (int i = 0; i < dotCount; i++)
            {
                result.Add(NavigationElementType.Dot);
            }

            result.Add(NavigationElementType.Equisignal);

            for (int i = dotCount; i < config.MaxNavigationCount; i++)
            {
                result.Add(NavigationElementType.Dash);
            }

            return result.ToArray();
        }

        public static int GetTransitionCount(NavigationElementType[] pattern)
        {
            if (pattern == null)
                return 0;

            int count = 0;
            for (int i = 0; i < pattern.Length; i++)
            {
                if (pattern[i] == NavigationElementType.Dot)
                    count++;
            }
            return count;
        }

        public static int GetCallsignElementCount(string callsign)
        {
            if (string.IsNullOrEmpty(callsign))
                return 0;

            int count = 0;
            foreach (char c in callsign.ToUpperInvariant())
            {
                string mc = MorseAlphabet.GetMorse(c);
                count += mc.Length;
            }
            return count;
        }
    }
}
