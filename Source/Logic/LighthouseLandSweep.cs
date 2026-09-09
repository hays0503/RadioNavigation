using System;
using System.Globalization;
using System.Text;
using DWS.Common.InjectionFramework;
using UBOAT.Game.Scene.Environment.Terrain;
using UBOAT.Game.Sandbox;
using UBOAT.Game.UI.DevConsole;
using UnityEngine;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// One-shot diagnostic for the land/water curation of the lighthouse
    /// fleet. Run from the dev console:
    ///   rn_lighthouse_landprobe
    ///
    /// For every curated profile and every LeuchtfeuerRegistry theater
    /// profile it queries the game's global CoastQuadTree for the SIGNED
    /// distance to the coast (negative = land, positive = water, no
    /// terrain streaming needed) and writes
    ///   Source/Data/LeuchtfeuerLandProbe.csv
    /// next to Leuchtfeuer.json. The CSV feeds Tools/Exclude-WaterBeacons.ps1,
    /// which moves water-side entries into LeuchtfeuerExcluded.json.
    ///
    /// The terrainHeightM column is best-effort: it is filled only for
    /// profiles whose terrain segment is currently streamed in (i.e. the
    /// player is nearby); it cross-checks the coast verdict against the
    /// actual terrain height during MCP validation sessions.
    /// </summary>
    public static class LighthouseLandSweep
    {
        private const string LogPrefix = "[RadioNav][LandProbe]";
        private const string CsvFileName = "LeuchtfeuerLandProbe.csv";
        private const float TerrainNoDataM = -1999f;

        [Inject] private static TerrainController terrainController = null;

        private static bool registered;

        public static void EnsureRegistered()
        {
            if (registered)
            {
                return;
            }

            registered = true;
            DevConsole.AddListener("rn_lighthouse_landprobe", Run);
            Debug.Log(LogPrefix + " rn_lighthouse_landprobe registered");
        }

        private static void Run(string[] args)
        {
            if (!LighthouseCoastProbe.Ready)
            {
                Debug.LogError(LogPrefix +
                    " coast quad tree not loaded yet — enter gameplay, wait a few seconds and retry");
                return;
            }

            string dataDirectory = System.IO.Path.Combine(
                Application.persistentDataPath,
                "Mods",
                "RadioNavigation",
                "Source",
                "Data");

            int water;
            int borderline;
            int land;
            int unknown;
            string csv = BuildCsv(out water, out borderline, out land, out unknown);

            string path = System.IO.Path.Combine(dataDirectory, CsvFileName);
            System.IO.File.WriteAllText(path, csv, new UTF8Encoding(true));

            Debug.Log(LogPrefix + " CSV written: " + path);
            Debug.Log(LogPrefix + " verdicts: " +
                "land=" + land.ToString(CultureInfo.InvariantCulture) +
                " borderline=" + borderline.ToString(CultureInfo.InvariantCulture) +
                " water=" + water.ToString(CultureInfo.InvariantCulture) +
                " unknown=" + unknown.ToString(CultureInfo.InvariantCulture) +
                " (exclusion threshold " + LighthouseLandGate.ShoreBandKm +
                " km on the water side; borderline stays unless reviewed)");
        }

        /// <summary>
        /// Samples every profile and renders the semicolon-separated CSV.
        /// Curated profiles first (stable order), then the registry subset.
        /// </summary>
        private static string BuildCsv(out int water, out int borderline, out int land, out int unknown)
        {
            StringBuilder csv = new StringBuilder(256 * 1024);
            csv.Append("code;name;type;lat;lon;distanceKm;verdict;terrainHeightM;source\r\n");

            water = 0;
            borderline = 0;
            land = 0;
            unknown = 0;

            for (int i = 0; i < LighthouseProfiles.All.Length; i++)
            {
                AppendRow(csv, LighthouseProfiles.All[i], "curated", ref water, ref borderline, ref land, ref unknown);
            }

            if (LeuchtfeuerRegistry.Loaded)
            {
                for (int i = 0; i < LeuchtfeuerRegistry.Profiles.Count; i++)
                {
                    AppendRow(csv, LeuchtfeuerRegistry.Profiles[i], "registry",
                        ref water, ref borderline, ref land, ref unknown);
                }
            }
            else
            {
                Debug.LogWarning(LogPrefix + " registry not loaded — CSV covers curated profiles only");
            }

            return csv.ToString();
        }

        private static void AppendRow(
            StringBuilder csv,
            LighthouseProfile profile,
            string source,
            ref int water,
            ref int borderline,
            ref int land,
            ref int unknown)
        {
            double distanceKm;
            bool probed = LighthouseCoastProbe.TryGetDistanceKm(profile.Latitude, profile.Longitude, out distanceKm);
            string verdict = LighthouseLandGate.VerdictLabel(probed ? distanceKm : double.NaN);

            if (verdict == "water") { water++; }
            else if (verdict == "borderline") { borderline++; }
            else if (verdict == "land") { land++; }
            else { unknown++; }

            csv.Append(CsvCell(profile.Code)).Append(';');
            csv.Append(CsvCell(profile.Name)).Append(';');
            csv.Append(CsvCell(profile.Type)).Append(';');
            csv.Append(profile.Latitude.ToString("F6", CultureInfo.InvariantCulture)).Append(';');
            csv.Append(profile.Longitude.ToString("F6", CultureInfo.InvariantCulture)).Append(';');
            csv.Append(probed ? distanceKm.ToString("F3", CultureInfo.InvariantCulture) : "").Append(';');
            csv.Append(verdict).Append(';');
            csv.Append(TerrainColumn(profile)).Append(';');
            csv.Append(source).Append("\r\n");
        }

        /// <summary>
        /// Best-effort terrain height at the profile's charted position:
        /// only valid where the terrain segment is currently streamed in
        /// (player nearby). ProceduralTerrainGrid.GetInterpolatedHeight
        /// returns its -2000 default for missing segments; real Baltic/North
        /// Sea depths never approach that sentinel. Any failure yields an
        /// empty cell — the column is diagnostic only.
        /// </summary>
        private static string TerrainColumn(LighthouseProfile profile)
        {
            try
            {
                Sandbox sandbox = Sandbox.Instance;
                if (sandbox == null || terrainController == null)
                {
                    return "";
                }

                Vector2 encounterKm = Coordinates.ToWorldPosition(profile.Latitude, profile.Longitude);
                Vector3 scenePosition = sandbox.EncounterToSceneSpace(encounterKm);
                float height = terrainController.MainGrid.GetInterpolatedHeight(scenePosition.x, scenePosition.z);
                if (height <= TerrainNoDataM || float.IsNaN(height))
                {
                    return "";
                }

                return height.ToString("F1", CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>Semicolon-CSV cell: quotes when the value can break the row.</summary>
        private static string CsvCell(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            bool needsQuotes = value.IndexOf(';') >= 0
                || value.IndexOf('"') >= 0
                || value.IndexOf('\n') >= 0
                || value.IndexOf('\r') >= 0;
            if (!needsQuotes)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
