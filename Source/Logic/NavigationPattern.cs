using System;

namespace UBOAT.Mods.RadioNavigation
{
    public interface IKeySequence
    {
        bool GetElement(int index);
        int ElementCount { get; }
        string Description { get; }
    }

    public struct Group
    {
        public readonly ulong Bits;

        public Group(ulong bits)
        {
            Bits = bits;
        }

        public bool GetElement(int index)
        {
            // F174: negative shifts are undefined in C#.
            if (index < 0) return false;
            return ((Bits >> index) & 1UL) != 0;
        }
    }

    public struct NavigationPattern : IKeySequence
    {
        public readonly Group[] Groups;
        public readonly byte ElementsPerGroup;
        public readonly byte TransitionIndex;

        public int ElementCount => Groups.Length * ElementsPerGroup;
        public string Description => "NAV T=" + TransitionIndex;

        public NavigationPattern(Group[] groups, byte elementsPerGroup, byte transitionIndex = 255)
        {
            Groups = groups ?? Array.Empty<Group>();
            ElementsPerGroup = elementsPerGroup == 0 ? (byte)8 : elementsPerGroup;
            TransitionIndex = transitionIndex;
        }

        public bool GetElement(int index)
        {
            if (Groups.Length.IsZero()) return false;
            // F174: negative index would produce a negative group index and
            // an IndexOutOfRangeException on Groups[g].
            if (index < 0) return false;
            int g = index / ElementsPerGroup;
            int e = index % ElementsPerGroup;
            if (g >= Groups.Length) return false;
            if (g == TransitionIndex) return false;
            return Groups[g].GetElement(e);
        }

        public static string FormatPattern(NavigationPattern pattern)
        {
            if (pattern.Groups.IsNull() || pattern.Groups.Length.IsZero())
                return "---";

            // F19: string concatenation in loops allocates per group and per
            // element; these run ~5x/second from InformationPanelHandler.
            var sb = new System.Text.StringBuilder(pattern.Groups.Length * (pattern.ElementsPerGroup + 3));
            for (int g = 0; g < pattern.Groups.Length; g++)
            {
                if (g > 0) sb.Append(' ');
                sb.Append('[');
                for (int e = 0; e < pattern.ElementsPerGroup; e++)
                {
                    if (g == pattern.TransitionIndex)
                        sb.Append('=');
                    else if (pattern.Groups[g].GetElement(e))
                        sb.Append('-');
                    else
                        sb.Append('.');
                }
                sb.Append(']');
            }
            return sb.ToString();
        }

        public static string FormatCompact(NavigationPattern pattern)
        {
            if (pattern.Groups.IsNull() || pattern.Groups.Length.IsZero())
                return "---";

            var sb = new System.Text.StringBuilder(pattern.Groups.Length * (pattern.ElementsPerGroup + 3));
            bool inDashes = false;

            for (int g = 0; g < pattern.Groups.Length; g++)
            {
                if (g == pattern.TransitionIndex)
                {
                    for (int e = 0; e < pattern.ElementsPerGroup; e++)
                        sb.Append('.');
                    sb.Append(" = ");
                    inDashes = true;
                }
                else if (!inDashes)
                {
                    for (int e = 0; e < pattern.ElementsPerGroup; e++)
                        sb.Append('.');
                }
                else
                {
                    for (int e = 0; e < pattern.ElementsPerGroup; e++)
                        sb.Append('-');
                }
            }

            return sb.ToString();
        }
    }

    public struct CallsignPattern : IKeySequence
    {
        private readonly NavigationPattern inner;

        public int ElementCount => inner.ElementCount;
        public string Description => "CALL";

        public CallsignPattern(string callsign)
        {
            inner = MorseAlphabet.TextToPattern(callsign);
        }

        public bool GetElement(int index)
        {
            return inner.GetElement(index);
        }
    }
}
