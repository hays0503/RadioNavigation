using System;

namespace UBOAT.Mods.TestUI
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
            ElementsPerGroup = elementsPerGroup;
            TransitionIndex = transitionIndex;
        }

        public bool GetElement(int index)
        {
            if (Groups.Length == 0) return false;
            int g = index / ElementsPerGroup;
            int e = index % ElementsPerGroup;
            if (g >= Groups.Length) return false;
            if (g == TransitionIndex) return false;
            return Groups[g].GetElement(e);
        }

        public static string FormatPattern(NavigationPattern pattern)
        {
            if (pattern.Groups == null || pattern.Groups.Length == 0)
                return "---";

            string result = "";
            for (int g = 0; g < pattern.Groups.Length; g++)
            {
                if (g > 0) result += " ";
                result += "[";
                for (int e = 0; e < pattern.ElementsPerGroup; e++)
                {
                    if (g == pattern.TransitionIndex)
                        result += "=";
                    else if (pattern.Groups[g].GetElement(e))
                        result += "-";
                    else
                        result += ".";
                }
                result += "]";
            }
            return result;
        }

        public static string FormatCompact(NavigationPattern pattern)
        {
            if (pattern.Groups == null || pattern.Groups.Length == 0)
                return "---";

            string result = "";
            bool inDashes = false;

            for (int g = 0; g < pattern.Groups.Length; g++)
            {
                if (g == pattern.TransitionIndex)
                {
                    for (int e = 0; e < pattern.ElementsPerGroup; e++)
                        result += ".";
                    result += " = ";
                    inDashes = true;
                }
                else if (!inDashes)
                {
                    for (int e = 0; e < pattern.ElementsPerGroup; e++)
                        result += ".";
                }
                else
                {
                    for (int e = 0; e < pattern.ElementsPerGroup; e++)
                        result += "-";
                }
            }

            return result;
        }
    }

    public struct CallsignPattern : IKeySequence
    {
        private readonly NavigationPattern _inner;

        public int ElementCount => _inner.ElementCount;
        public string Description => "CALL";

        public CallsignPattern(string callsign)
        {
            _inner = MorseAlphabet.TextToPattern(callsign);
        }

        public bool GetElement(int index)
        {
            return _inner.GetElement(index);
        }
    }
}
