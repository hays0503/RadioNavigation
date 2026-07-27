using System.Collections.Generic;
using System.Text;

namespace UBOAT.Mods.RadioNavigation
{
    public static class SignalRichTextFormatter
    {
        private const int MaxSignalEntries = 60;
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
            while ((morseCharCount > MaxMorseChars || entries.Count > MaxSignalEntries)
                && entries.Count.IsPositive())
            {
                if (entries[0].Type != SignalCharType.Separator)
                    morseCharCount--;
                entries.RemoveAt(0);
            }
        }

        public static void TrimDecodedText(StringBuilder decodedText)
        {
            if (decodedText.Length > MaxDecodedChars)
                decodedText.Remove(0, decodedText.Length - MaxDecodedChars);
        }
    }
}
