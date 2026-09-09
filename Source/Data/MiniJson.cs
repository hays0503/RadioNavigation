using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Minimal recursive-descent JSON parser for the Sonne station tables.
    /// Supports objects, strings (with the 8 standard escapes), and numbers;
    /// arrays/booleans/null are not needed by the data files and throw.
    /// Extracted from SonneBeam.cs (SRP: parser is not a domain model).
    /// </summary>
    internal static class MiniJson
    {
        internal static Dictionary<string, object> Parse(string json)
        {
            if (json == null)
                throw new ArgumentNullException("json");

            int pos = 0;
            Dictionary<string, object> obj = ParseValue(json, ref pos) as Dictionary<string, object>;
            if (obj == null)
                throw new FormatException("Root of JSON must be an object");

            SkipWs(json, ref pos);
            if (pos != json.Length)
                throw new FormatException("Unexpected data after JSON object");
            return obj;
        }

        private static object ParseValue(string s, ref int pos)
        {
            SkipWs(s, ref pos);
            if (pos >= s.Length)
                throw new FormatException("Unexpected end of JSON");

            char c = s[pos];
            if (c == '{') return ParseObject(s, ref pos);
            if (c == '"') return ParseString(s, ref pos);
            if (c == '[') return ParseArray(s, ref pos);
            return ParseNumber(s, ref pos);
        }

        private static List<object> ParseArray(string s, ref int pos)
        {
            var list = new List<object>();
            pos++;
            SkipWs(s, ref pos);
            if (pos < s.Length && s[pos] == ']')
            {
                pos++;
                return list;
            }

            while (true)
            {
                list.Add(ParseValue(s, ref pos));
                SkipWs(s, ref pos);
                if (pos >= s.Length)
                    throw new FormatException("Unexpected end of JSON array");

                char separator = s[pos];
                if (separator == ',')
                {
                    pos++;
                    continue;
                }
                if (separator == ']')
                {
                    pos++;
                    return list;
                }
                throw new FormatException("Expected ',' or ']'");
            }
        }

        private static Dictionary<string, object> ParseObject(string s, ref int pos)
        {
            var dict = new Dictionary<string, object>();
            pos++;
            SkipWs(s, ref pos);
            if (pos < s.Length && s[pos] == '}')
            {
                pos++;
                return dict;
            }

            while (true)
            {
                SkipWs(s, ref pos);
                string key = ParseString(s, ref pos);
                SkipWs(s, ref pos);
                if (pos >= s.Length || s[pos] != ':')
                    throw new FormatException("Expected ':'");
                pos++;
                dict[key] = ParseValue(s, ref pos);
                SkipWs(s, ref pos);
                if (pos >= s.Length)
                    throw new FormatException("Unexpected end of JSON object");

                char separator = s[pos];
                if (separator == ',')
                {
                    pos++;
                    continue;
                }
                if (separator == '}')
                {
                    pos++;
                    return dict;
                }
                throw new FormatException("Expected ',' or '}'");
            }
        }

        private static string ParseString(string s, ref int pos)
        {
            if (pos >= s.Length || s[pos] != '"')
                throw new FormatException("Expected string");
            pos++;
            var sb = new StringBuilder();

            while (true)
            {
                if (pos >= s.Length)
                    throw new FormatException("Unterminated string");

                char c = s[pos];
                if (c == '"')
                {
                    pos++;
                    return sb.ToString();
                }
                if (c == '\\')
                {
                    sb.Append(ParseEscape(s, ref pos));
                }
                else
                {
                    sb.Append(c);
                    pos++;
                }
            }
        }

        private static char ParseEscape(string s, ref int pos)
        {
            pos++;
            if (pos >= s.Length)
                throw new FormatException("Unterminated escape");

            char escape = s[pos];
            pos++;
            switch (escape)
            {
                case '"': return '"';
                case '\\': return '\\';
                case '/': return '/';
                case 'b': return '\b';
                case 'f': return '\f';
                case 'n': return '\n';
                case 'r': return '\r';
                case 't': return '\t';
                default: throw new FormatException("Unknown escape \\" + escape);
            }
        }

        private static double ParseNumber(string s, ref int pos)
        {
            int start = pos;
            while (pos < s.Length)
            {
                char c = s[pos];
                if (char.IsDigit(c) || c == '.' || c == '-' || c == '+' || c == 'e' || c == 'E')
                    pos++;
                else
                    break;
            }

            if (start == pos)
                throw new FormatException("Expected number");
            string number = s.Substring(start, pos - start);
            return double.Parse(number, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static void SkipWs(string s, ref int pos)
        {
            while (pos < s.Length && char.IsWhiteSpace(s[pos]))
                pos++;
        }
    }
}
