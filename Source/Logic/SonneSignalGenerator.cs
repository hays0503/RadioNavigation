using System;

namespace UBOAT.Mods.RadioNavigation
{
    public static class SonneSignalGenerator
    {
        public const float SectorWidthDeg = 5f;

        public static int CalculateSectorNumber(float bearing, float baseAngle)
        {
            float delta = ((bearing - baseAngle) % 360f + 360f) % 360f;
            return (int)(delta / SectorWidthDeg);
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

        public static float AngularDistance(float first, float second)
        {
            float distance = Math.Abs(((first - second) % 360f + 360f) % 360f);
            return distance > 180f ? 360f - distance : distance;
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
