namespace UBOAT.Mods.RadioNavigation
{
    public enum SignalCharType : byte
    {
        Callsign,
        Dot,
        Dash,
        Equisignal,
        Separator
    }

    public struct SignalEntry
    {
        public char Ch;
        public SignalCharType Type;

        public SignalEntry(char ch, SignalCharType type)
        {
            Ch = ch;
            Type = type;
        }
    }
}
