namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// The (element, callsign) -&gt; (glyph, entry type) transcript alphabet
    /// behind SonneController.OnStationElementCompleted. Extracted (SRP — the
    /// mapping is not buffer management) so it is pinned offline in
    /// SignalTranscriberTests. Pure C#, no Unity dependency.
    /// </summary>
    public static class SignalTranscriber
    {
        public static void MapElement(NavigationElementType elemType, bool isCallsign,
            out SignalCharType type, out char glyph)
        {
            switch (elemType)
            {
                case NavigationElementType.Equisignal:
                    type = SignalCharType.Equisignal;
                    glyph = '=';
                    break;
                case NavigationElementType.Dash:
                    type = isCallsign ? SignalCharType.Callsign : SignalCharType.Dash;
                    glyph = '-';
                    break;
                default:
                    type = isCallsign ? SignalCharType.Callsign : SignalCharType.Dot;
                    glyph = '.';
                    break;
            }
        }
    }
}
