namespace UBOAT.Mods.TestUI
{
    public static class MorseAlphabet
    {
        private static readonly string[] _table = new string[128];

        static MorseAlphabet()
        {
            Define('A', "._");
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
        }

        private static void Define(char c, string morse)
        {
            _table[c] = morse;
            if (c >= 'A' && c <= 'Z')
                _table[c + 32] = morse;
        }

        public static string GetMorse(char c)
        {
            if (c < 0 || c >= _table.Length)
                return "";
            return _table[c] ?? "";
        }

        public static int GetElementCount(string text)
        {
            int total = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (i > 0) total += 3;
                total += GetMorse(text[i]).Length;
            }
            return total;
        }

        public static NavigationPattern TextToPattern(string text, int elementDurationTicks = 1)
        {
            if (string.IsNullOrEmpty(text))
                return new NavigationPattern(new Group[0], 1);

            var elements = new System.Collections.Generic.List<bool>();

            for (int i = 0; i < text.Length; i++)
            {
                if (i > 0)
                {
                    for (int g = 0; g < 3; g++)
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
