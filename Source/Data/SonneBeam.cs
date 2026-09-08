using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Sonne station table registry: loads the JSON tables from the data
    /// directory and resolves (station, angle) to a navigation pattern.
    /// Extracted from the former multi-type SonneBeam.cs (SRP: registry/IO
    /// is not the domain model and not the parser).
    /// </summary>
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
}
