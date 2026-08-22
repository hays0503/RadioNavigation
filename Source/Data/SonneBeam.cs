using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace UBOAT.Mods.RadioNavigation
{
    public sealed class SonneBeamStation
    {
        public string Name;
        public readonly Dictionary<double, int> Dashes = new Dictionary<double, int>();
        public readonly Dictionary<double, int> Dots = new Dictionary<double, int>();

        public static SonneBeamStation Parse(string json)
        {
            Dictionary<string, object> root = MiniJson.Parse(json);
            object stationValue;
            object dashesValue;
            object dotsValue;

            if (!root.TryGetValue("station", out stationValue)
                || !(stationValue is string)
                || string.IsNullOrEmpty((string)stationValue))
            {
                throw new FormatException("missing 'station'");
            }

            if (!root.TryGetValue("dashes", out dashesValue)
                || !(dashesValue is Dictionary<string, object>))
            {
                throw new FormatException("missing 'dashes'");
            }

            if (!root.TryGetValue("dots", out dotsValue)
                || !(dotsValue is Dictionary<string, object>))
            {
                throw new FormatException("missing 'dots'");
            }

            var station = new SonneBeamStation
            {
                Name = (string)stationValue
            };

            LoadEntries(station.Dashes, (Dictionary<string, object>)dashesValue);
            LoadEntries(station.Dots, (Dictionary<string, object>)dotsValue);
            return station;
        }

        public bool TryGetSignal(double angle, out SonneBeamSignal signal)
        {
            signal = default(SonneBeamSignal);
            if (Dashes.Count == 0 || Dots.Count == 0 || double.IsNaN(angle) || double.IsInfinity(angle))
                return false;

            double normalizedAngle = NormalizeAngle(angle);
            double dashDistance;
            double dotDistance;
            double dashAngle;
            double dotAngle;
            int dashCount;
            int dotCount;

            if (!FindClosest(Dashes, normalizedAngle, out dashAngle, out dashCount, out dashDistance)
                || !FindClosest(Dots, normalizedAngle, out dotAngle, out dotCount, out dotDistance))
            {
                return false;
            }

            // Preserve SonneBeam.cs tie behaviour: an equal-distance tie uses dashes.
            bool matchedDots = dotDistance < dashDistance;
            int matchedCount = matchedDots ? dotCount : dashCount;
            if (matchedCount < 0 || matchedCount > SonneBeam.TotalElements)
                return false;

            int otherCount = SonneBeam.TotalElements - matchedCount;
            int dotsCount = matchedDots ? matchedCount : otherCount;
            int dashesCount = matchedDots ? otherCount : matchedCount;

            int dotLoss = Math.Min(2, dotsCount);
            int dashLoss = Math.Min(2, dashesCount);
            int equisignalCount = dotLoss + dashLoss;
            int dotsOutput = dotsCount - dotLoss;
            int dashesOutput = dashesCount - dashLoss;

            signal = new SonneBeamSignal
            {
                DotCount = dotsCount,
                DashCount = dashesCount,
                IsDashSector = !matchedDots,
                EquisignalCount = equisignalCount,
                LeadingCount = matchedDots ? dotsOutput : dashesOutput,
                TrailingCount = matchedDots ? dashesOutput : dotsOutput,
                MatchedAngle = matchedDots ? dotAngle : dashAngle
            };
            return true;
        }

        private static void LoadEntries(Dictionary<double, int> target, Dictionary<string, object> source)
        {
            foreach (KeyValuePair<string, object> entry in source)
            {
                double angle;
                if (!double.TryParse(entry.Key, NumberStyles.Float, CultureInfo.InvariantCulture, out angle))
                    throw new FormatException("invalid angle '" + entry.Key + "'");

                int count = Convert.ToInt32(entry.Value, CultureInfo.InvariantCulture);
                if (count < 0 || count > SonneBeam.TotalElements)
                    throw new FormatException("signal count out of range at '" + entry.Key + "'");

                target[NormalizeAngle(angle)] = count;
            }
        }

        private static bool FindClosest(
            Dictionary<double, int> entries,
            double angle,
            out double matchedAngle,
            out int matchedCount,
            out double distance)
        {
            matchedAngle = 0.0;
            matchedCount = -1;
            distance = double.MaxValue;

            foreach (KeyValuePair<double, int> entry in entries)
            {
                double currentDistance = AngularDistance(entry.Key, angle);
                if (currentDistance < distance)
                {
                    matchedAngle = entry.Key;
                    matchedCount = entry.Value;
                    distance = currentDistance;
                }
            }

            return matchedCount >= 0;
        }

        private static double NormalizeAngle(double angle)
        {
            double normalized = angle % 360.0;
            if (normalized < 0.0)
                normalized += 360.0;
            return normalized;
        }

        private static double AngularDistance(double first, double second)
        {
            double distance = Math.Abs(first - second) % 360.0;
            return distance > 180.0 ? 360.0 - distance : distance;
        }
    }

    public struct SonneBeamSignal
    {
        public int DotCount;
        public int DashCount;
        public bool IsDashSector;
        public int EquisignalCount;
        public int LeadingCount;
        public int TrailingCount;
        public double MatchedAngle;
    }

    public static class SonneBeam
    {
        public const int TotalElements = 60;

        private static readonly string[] TableFiles =
        {
            "Bushmills.json",
            "Ploneis.json",
            "Stavanger.json",
            "Lugo.json",
            "Seville.json"
        };

        private static readonly Dictionary<string, SonneBeamStation> Stations =
            new Dictionary<string, SonneBeamStation>(StringComparer.OrdinalIgnoreCase);

        public static string LastLoadError { get; private set; }

        public static bool Initialize(string dataDirectory)
        {
            Stations.Clear();
            LastLoadError = null;

            if (string.IsNullOrEmpty(dataDirectory) || !Directory.Exists(dataDirectory))
            {
                LastLoadError = "Sonne table directory not found: " + dataDirectory;
                return false;
            }

            for (int i = 0; i < TableFiles.Length; i++)
            {
                string file = Path.Combine(dataDirectory, TableFiles[i]);
                try
                {
                    if (!File.Exists(file))
                        throw new FileNotFoundException("table file not found", file);

                    SonneBeamStation station = SonneBeamStation.Parse(File.ReadAllText(file, Encoding.UTF8));
                    Stations[station.Name] = station;
                }
                catch (Exception ex)
                {
                    AppendLoadError(TableFiles[i] + ": " + ex.Message);
                }
            }

            return Stations.Count == TableFiles.Length;
        }

        public static bool TryGetNavigationPattern(
            string stationName,
            double angle,
            out NavigationElementType[] pattern,
            out SonneBeamSignal signal)
        {
            pattern = null;
            signal = default(SonneBeamSignal);

            SonneBeamStation station;
            string tableName = GetTableName(stationName);
            if (!Stations.TryGetValue(tableName, out station)
                || !station.TryGetSignal(angle, out signal))
            {
                return false;
            }

            pattern = BuildPattern(signal.DotCount, signal.IsDashSector);
            return true;
        }

        public static NavigationElementType[] BuildPattern(int dotCount, bool isDashSector)
        {
            if (dotCount < 0 || dotCount > TotalElements)
                throw new ArgumentOutOfRangeException("dotCount");

            int dashCount = TotalElements - dotCount;
            int dotLoss = Math.Min(2, dotCount);
            int dashLoss = Math.Min(2, dashCount);
            int equisignalCount = dotLoss + dashLoss;
            int dotsOutput = dotCount - dotLoss;
            int dashesOutput = dashCount - dashLoss;

            var pattern = new NavigationElementType[TotalElements];
            int index = 0;
            if (isDashSector)
            {
                Append(pattern, ref index, NavigationElementType.Dash, dashesOutput);
                Append(pattern, ref index, NavigationElementType.Equisignal, equisignalCount);
                Append(pattern, ref index, NavigationElementType.Dot, dotsOutput);
            }
            else
            {
                Append(pattern, ref index, NavigationElementType.Dot, dotsOutput);
                Append(pattern, ref index, NavigationElementType.Equisignal, equisignalCount);
                Append(pattern, ref index, NavigationElementType.Dash, dashesOutput);
            }

            if (index != TotalElements)
                throw new InvalidOperationException("Sonne navigation pattern must contain 60 elements");
            return pattern;
        }

        private static void Append(
            NavigationElementType[] pattern,
            ref int index,
            NavigationElementType element,
            int count)
        {
            for (int i = 0; i < count; i++)
                pattern[index++] = element;
        }

        private static string GetTableName(string stationName)
        {
            if (string.Equals(stationName, "Quimper/Ploneis", StringComparison.OrdinalIgnoreCase))
                return "Ploneis";
            return stationName;
        }

        private static void AppendLoadError(string message)
        {
            if (string.IsNullOrEmpty(LastLoadError))
                LastLoadError = message;
            else
                LastLoadError += "; " + message;
        }
    }

    public static class MiniJson
    {
        public static Dictionary<string, object> Parse(string json)
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
            return ParseNumber(s, ref pos);
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
                    pos++;
                    if (pos >= s.Length)
                        throw new FormatException("Unterminated escape");

                    char escape = s[pos];
                    switch (escape)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        default: throw new FormatException("Unknown escape \\" + escape);
                    }
                    pos++;
                }
                else
                {
                    sb.Append(c);
                    pos++;
                }
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
