using System;
using System.Collections.Generic;

namespace UBOAT.Mods.RadioNavigation
{
    public static class SonneSignalGenerator
    {
        public const float SectorWidthDeg = 5f;
        public const int TotalSignals = 60;
        public const float DegPerSign = SectorWidthDeg / TotalSignals;

        public struct SectorResult
        {
            public int DotCount;
            public bool IsDashSector;
            public float LocalAngle;
            public float SectorBase;
        }

        public static SectorResult CalculateSector(float bearing, float baseAngle)
        {
            float delta = ((bearing - baseAngle) % 360f + 360f) % 360f;
            float localAngle = delta % SectorWidthDeg;
            int sectorNumber = (int)(delta / SectorWidthDeg);
            bool isDashSector = (sectorNumber % 2 == 1);

            int dotCount;
            if (isDashSector)
                dotCount = (int)(((SectorWidthDeg - localAngle) / SectorWidthDeg) * TotalSignals);
            else
                dotCount = (int)((localAngle / SectorWidthDeg) * TotalSignals);

            dotCount = Math.Max(0, Math.Min(TotalSignals, dotCount));

            return new SectorResult
            {
                DotCount = dotCount,
                IsDashSector = isDashSector,
                LocalAngle = localAngle,
                SectorBase = bearing - localAngle
            };
        }

        public static float RestoreBearingFromDotCount(int dotCount, bool isDashSector, float sectorBase)
        {
            int correctedDots;
            if (isDashSector)
                correctedDots = TotalSignals - dotCount;
            else
                correctedDots = dotCount;

            return sectorBase + correctedDots * DegPerSign;
        }

        public static int[] GenerateBearingTable(BeaconStation station)
        {
            var config = ConsolCycleConfig.Cycle120s;
            int maxCount = config.MaxNavigationCount;
            var table = new int[maxCount + 1];

            for (int i = 0; i <= maxCount; i++)
            {
                float ratio = maxCount.IsPositive() ? (float)i / maxCount : 0f;
                float localAngle = ratio * SectorWidthDeg;
                float bearing = station.BaseAngle + localAngle;
                table[i] = (int)((bearing + 360f) % 360f);
            }

            return table;
        }

        public static int LookupBearing(int dotCount, int[] bearingTable)
        {
            if (bearingTable.IsNull() || bearingTable.Length.IsZero())
                return 0;

            dotCount = Math.Max(0, Math.Min(bearingTable.Length - 1, dotCount));
            return bearingTable[dotCount];
        }

        public static NavigationElementType[] GenerateConsolPattern(
            string callsign,
            int dotCount,
            ConsolCycleConfig config)
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
            if (pattern.IsNull())
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

        // F79: true when the bearing is inside the station's navigation sector
        // (|angular difference to BaseAngle| <= half of the sector width, wrapped).
        public static bool CalculateInBeam(float bearing, float baseAngle, float sectorWidthDeg)
        {
            float angleDiff = Math.Abs(((bearing + 360f) % 360f) - baseAngle);
            if (angleDiff > 180f)
                angleDiff = 360f - angleDiff;
            return angleDiff <= sectorWidthDeg / 2f;
        }

        // F77: pack a generated consol element array into a NavigationPattern
        // for diagnostics. The group containing the equisignal becomes the
        // transition group.
        public static NavigationPattern ToNavigationPattern(NavigationElementType[] elements, int groupSize = 8)
        {
            if (elements.IsNull() || elements.Length.IsZero())
                return new NavigationPattern(Array.Empty<Group>(), (byte)groupSize, 255);

            int groupCount = (elements.Length + groupSize - 1) / groupSize;
            var groups = new Group[groupCount];
            int transitionIndex = -1;

            for (int g = 0; g < groupCount; g++)
            {
                ulong bits = 0;
                bool hasEquisignal = false;
                for (int e = 0; e < groupSize; e++)
                {
                    int idx = g * groupSize + e;
                    if (idx >= elements.Length)
                        break;
                    if (elements[idx] == NavigationElementType.Dash)
                        bits |= 1UL << e;
                    else if (elements[idx] == NavigationElementType.Equisignal)
                        hasEquisignal = true;
                }
                groups[g] = new Group(bits);
                if (hasEquisignal && transitionIndex < 0)
                    transitionIndex = g;
            }

            return new NavigationPattern(groups, (byte)groupSize,
                transitionIndex >= 0 ? (byte)transitionIndex : (byte)255);
        }
    }
}
