using System.Collections.Generic;

namespace UBOAT.Mods.RadioNavigation
{
    public static class MorseAlphabet
    {
        private static readonly string[] table = new string[128];
        private static readonly Dictionary<string, char> reverse = new Dictionary<string, char>();

        static MorseAlphabet()
        {
            Define('A', ".-");
            Define('B', "-...");
            Define('C', "-.-.");
            Define('D', "-..");
            Define('E', ".");
            Define('F', "..-.");
            Define('G', "--.");
            Define('H', "....");
            Define('I', "..");
            Define('J', ".---");
            Define('K', "-.-");
            Define('L', ".-..");
            Define('M', "--");
            Define('N', "-.");
            Define('O', "---");
            Define('P', ".--.");
            Define('Q', "--.-");
            Define('R', ".-.");
            Define('S', "...");
            Define('T', "-");
            Define('U', "..-");
            Define('V', "...-");
            Define('W', ".--");
            Define('X', "-..-");
            Define('Y', "-.--");
            Define('Z', "--..");
            Define('0', "-----");
            Define('1', ".----");
            Define('2', "..---");
            Define('3', "...--");
            Define('4', "....-");
            Define('5', ".....");
            Define('6', "-....");
            Define('7', "--...");
            Define('8', "---..");
            Define('9', "----.");

            for (int i = 0; i < table.Length; i++)
                if (table[i].IsNotNull() && table[i].Length.IsPositive() && !reverse.ContainsKey(table[i]))
                    reverse[table[i]] = (char)i;
        }

        private static void Define(char c, string morse)
        {
            // F170: defensive bounds check — the table is ASCII-only.
            if (c >= table.Length)
                return;
            table[c] = morse;
            if (c >= 'A' && c <= 'Z' && c + 32 < table.Length)
                table[c + 32] = morse;
        }

        public static string GetMorse(char c)
        {
            if (c < 0 || c >= table.Length)
                return "";
            return table[c] ?? "";
        }

        public static char DecodeMorse(string sequence)
        {
            if (string.IsNullOrEmpty(sequence))
                return '?';
            return reverse.TryGetValue(sequence, out char c) ? c : '?';
        }

        public static int GetElementCount(string text)
        {
            int total = 0;
            for (int i = 0; i < text.Length; i++)
            {
                // F129: inter-word pause (7) instead of inter-character (3)
                // after a space; a space itself encodes as silence, it must
                // not also carry a leading pause.
                if (i.IsPositive() && text[i] != ' ')
                {
                    if (text[i - 1] == ' ')
                        total += 7;
                    else
                        total += 3;
                }
                total += GetMorse(text[i]).Length;
            }
            return total;
        }

        // F166: elementDurationTicks was accepted but never used — element
        // timing lives in MorseTiming, not in the pattern; removed.
        public static NavigationPattern TextToPattern(string text)
        {
            if (string.IsNullOrEmpty(text))
                return new NavigationPattern(new Group[0], 1);

            var elements = new System.Collections.Generic.List<bool>();

            for (int i = 0; i < text.Length; i++)
            {
                // F129: mirror GetElementCount — 7-element inter-word pause
                // after a space, 3-element inter-character pause otherwise;
                // a space itself contributes only silence.
                if (i.IsPositive() && text[i] != ' ')
                {
                    int pause = text[i - 1] == ' ' ? 7 : 3;
                    for (int g = 0; g < pause; g++)
                        elements.Add(false);
                }

                string mc = GetMorse(text[i]);
                for (int b = 0; b < mc.Length; b++)
                    elements.Add(mc[b] == '-');
            }

            int totalElements = elements.Count;
            int groupSize = 8;
            int groupCount = (totalElements + groupSize - 1) / groupSize;

            var groups = new Group[groupCount];
            for (int g = 0; g < groupCount; g++)
            {
                ulong bits = 0;
                for (int e = 0; e < groupSize; e++)
                {
                    int idx = g * groupSize + e;
                    if (idx < totalElements && elements[idx])
                        bits |= 1UL << e;
                }
                groups[g] = new Group(bits);
            }

            return new NavigationPattern(groups, (byte)groupSize, 255);
        }
    }
}
