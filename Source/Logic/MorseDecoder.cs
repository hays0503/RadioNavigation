using System.Text;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Pure morse-callsign decode helpers, extracted from
    /// SonneController.DecodeCallsignBuffer (P5.6 SC-1). No state; operates on
    /// the MorseAlphabet table only, so it is unit-testable without Unity.
    /// </summary>
    public static class MorseDecoder
    {
        /// <summary>
        /// Total morse-element length of a callsign's letters (sum of each
        /// character's morse pattern length).
        /// </summary>
        public static int ExpectedLength(string callsign)
        {
            if (string.IsNullOrEmpty(callsign))
                return 0;
            int len = 0;
            foreach (char ch in callsign.ToUpperInvariant())
                len += MorseAlphabet.GetMorse(ch).Length;
            return len;
        }

        /// <summary>
        /// Decodes a received morse buffer against an expected callsign,
        /// emitting each letter when its segment matches and '?' otherwise.
        /// Stops at the first unknown/oversized character. The buffer must be
        /// consumed by the caller (this method does not clear it).
        /// </summary>
        public static string Decode(string callsign, StringBuilder morseBuffer)
        {
            var decoded = new StringBuilder();
            int pos = 0;
            foreach (char ch in callsign.ToUpperInvariant())
            {
                string mc = MorseAlphabet.GetMorse(ch);
                if (mc.Length.IsZero() || pos + mc.Length > morseBuffer.Length)
                    break;
                string segment = morseBuffer.ToString(pos, mc.Length);
                decoded.Append(segment == mc ? ch : '?');
                pos += mc.Length;
            }
            return decoded.ToString();
        }
    }
}
