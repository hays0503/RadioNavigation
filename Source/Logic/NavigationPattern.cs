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
