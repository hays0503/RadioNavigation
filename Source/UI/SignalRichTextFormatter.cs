using System.Collections.Generic;
using System.Text;

namespace UBOAT.Mods.RadioNavigation
{
    public static class SignalRichTextFormatter
    {
        // F120: public so SonneController can pre-size its entry list.
        public const int MaxSignalEntries = 60;
        private const int MaxMorseChars = 20;
        private const int MaxDecodedChars = 30;

        public static string FormatRichText(List<SignalEntry> entries)
        {
            var sb = new StringBuilder(entries.Count * 20);
            SignalCharType prevType = SignalCharType.Separator;

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];

                if (e.Type != prevType && prevType != SignalCharType.Separator && e.Type != SignalCharType.Separator)
                {
                    sb.Append("<color=#888888>\u2502</color>");
                }

                string color;
                string symbol;
                bool bold = false;
                switch (e.Type)
                {
                    case SignalCharType.Callsign:
                        color = "#00FF00";
                        symbol = e.Ch == '-' ? "\u25AC" : "\u25CF";
                        break;
                    case SignalCharType.Dot:
                        color = "#FF2222";
                        symbol = "\u25CF";
                        break;
                    case SignalCharType.Dash:
                        color = "#2288FF";
                        symbol = "\u25AC";
                        break;
                    case SignalCharType.Equisignal:
                        color = "#FF00FF";
                        symbol = "\u25C6\u25C6";
                        bold = true;
                        break;
                    default:
                        color = "#FFFFFF";
                        symbol = " ";
                        break;
                }

                if (bold)
                    sb.Append("<b>");
                sb.Append("<color=");
                sb.Append(color);
                sb.Append('>');
                sb.Append(symbol);
                sb.Append("</color>");
                if (bold)
                    sb.Append("</b>");

                prevType = e.Type;
            }
            return sb.ToString();
        }

        public static void TrimEntries(List<SignalEntry> entries, ref int morseCharCount)
        {
            // F120: RemoveAt(0) is O(n) per call — collapse the trim into a
            // single RemoveRange when multiple entries must go.
            int removeCount = 0;
            while (removeCount < entries.Count &&
                (morseCharCount > MaxMorseChars || entries.Count - removeCount > MaxSignalEntries))
            {
                if (entries[removeCount].Type != SignalCharType.Separator)
                    morseCharCount--;
                removeCount++;
            }
            if (removeCount > 0)
                entries.RemoveRange(0, removeCount);
        }

        public static void TrimDecodedText(StringBuilder decodedText)
        {
            if (decodedText.Length > MaxDecodedChars)
                decodedText.Remove(0, decodedText.Length - MaxDecodedChars);
        }
    }
}
