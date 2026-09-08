namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Resolved signal-block state behind
    /// InformationPanelHandler.UpdateSignalDisplay. Extracted (SRP — block
    /// selection is not panel logic) so the placeholder/compose decision
    /// runs offline in SignalPanelViewTests. The panel applies Text/color.
    /// Pure C#.
    /// </summary>
    public static class SignalPanelView
    {
        public struct State
        {
            public readonly string SignalText;
            public readonly bool Dimmed;
            public readonly string DecodedText;

            public State(string signalText, bool dimmed, string decodedText)
            {
                SignalText = signalText;
                Dimmed = dimmed;
                DecodedText = decodedText;
            }
        }

        public static State Resolve(string rich, string decodedDisplayText)
        {
            if (string.IsNullOrEmpty(rich))
                return new State("--- Signal: ", true, "Decoded: ");
            return new State("--- Signal: " + rich, false, decodedDisplayText);
        }
    }
}
