using System.Collections.Generic;
using System.Text;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Live signal transcript: entries, morse decode buffer and the decoded
    /// callsign behind SonneController's element/sequence handlers.
    /// Extracted (SRP — transcript state is not controller logic) so element
    /// feed, callsign decode, counters and clear run offline in
    /// SignalTranscriptTests. Pure C#, no Unity dependency.
    /// </summary>
    public sealed class SignalTranscript
    {
        // F120: pre-sized list avoids capacity-doubling allocations.
        private readonly List<SignalEntry> entries = new List<SignalEntry>(SignalRichTextFormatter.MaxSignalEntries);
        private int morseCharCount;
        private readonly StringBuilder morseBuffer = new StringBuilder();
        private readonly StringBuilder decoded = new StringBuilder();

        // Probe diagnostics (see DecodeBuffer): decode attempt counters.
        public static int DecodeAttempts;
        public static int DecodeSuccess;
        public static int LastBufferLen = -1;
        public static int LastExpectedLen = -1;

        public string RichText
        {
            get { return SignalRichTextFormatter.FormatRichText(entries); }
        }

        public string DecodedText
        {
            get { return decoded.ToString(); }
        }

        public void AddElement(NavigationElementType elemType, bool isCallsign, BeaconStation station)
        {
            SignalTranscriber.MapElement(elemType, isCallsign, out SignalCharType type, out char c);
            entries.Add(new SignalEntry(c, type));
            morseCharCount++;
            SignalRichTextFormatter.TrimEntries(entries, ref morseCharCount);

            if (isCallsign)
            {
                if (elemType == NavigationElementType.Dot)
                    morseBuffer.Append('.');
                else if (elemType == NavigationElementType.Dash)
                    morseBuffer.Append('-');
            }
            else if (morseBuffer.Length.IsPositive())
            {
                DecodeBuffer(station);
            }
        }

        public void FinishSequence(BeaconStation station)
        {
            if (morseBuffer.Length.IsPositive())
                DecodeBuffer(station);

            entries.Add(new SignalEntry(' ', SignalCharType.Separator));
            SignalRichTextFormatter.TrimEntries(entries, ref morseCharCount);
        }

        public void Clear()
        {
            entries.Clear();
            morseCharCount = 0;
            morseBuffer.Clear();
            decoded.Clear();
        }

        public int CountDots()
        {
            return CountByType(SignalCharType.Dot);
        }

        public int CountDashes()
        {
            return CountByType(SignalCharType.Dash);
        }

        public int CountCallsignChars()
        {
            return CountByType(SignalCharType.Callsign);
        }

        private int CountByType(SignalCharType type)
        {
            int count = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Type == type)
                    count++;
            }
            return count;
        }

        private void DecodeBuffer(BeaconStation station)
        {
            // Probe diagnostics: every decode attempt with buffer vs expected
            // lengths — a persistent mismatch means the callsign window is
            // being truncated/split before the full callsign arrives.
            DecodeAttempts++;
            LastBufferLen = morseBuffer.Length;

            if (morseBuffer.Length.IsZero())
                return;

            string callsign = null;
            StationCallsignResolver.TryGet(station, out callsign);

            if (string.IsNullOrEmpty(callsign))
            {
                morseBuffer.Clear();
                return;
            }

            LastExpectedLen = MorseDecoder.ExpectedLength(callsign);
            if (morseBuffer.Length != LastExpectedLen)
            {
                morseBuffer.Clear();
                return;
            }

            // Keep only the latest completed callsign. Appending every cycle
            // made the panel grow until trimming.
            DecodeSuccess++;
            decoded.Clear();
            decoded.Append(MorseDecoder.Decode(callsign, morseBuffer));

            SignalRichTextFormatter.TrimDecodedText(decoded);
            morseBuffer.Clear();
        }
    }
}
