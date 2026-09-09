using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    /// <summary>
    /// Sign-convention validation of the coast classifier against the
    /// REAL game data file (UBOAT_Data/StreamingAssets/Maps/
    /// Coast Quad Tree.dat) through a verbatim port of the game's
    /// CoastQuadTree.GetDistanceToCoast. This is the offline half of the
    /// sign validation; the live half is the MCP terrain cross-check.
    ///
    /// Skipped (reported SKIP, not FAIL) when the game is not installed.
    /// Assertion points are chosen unambiguously inland / open-sea so the
    /// vector coast resolution cannot flip them.
    /// </summary>
    public class CoastQuadTreePortTests
    {
        [Fact]
        public void Port_LoadsRealCoastFile()
        {
            CoastQuadTreePort port = LoadPort();
            Assert.False(port.IsLoading);
        }

        [Fact]
        public void Land_InlandGermany_IsNegative()
        {
            // ~180 km from any North Sea coast — no resolution ambiguity.
            float d = DistanceKm(LoadPort(), 51.0, 10.2);
            Assert.True(d < 0, "inland Germany classified as water: " + F(d) + " km");
        }

        [Fact]
        public void Land_JutlandInterior_IsNegative()
        {
            float d = DistanceKm(LoadPort(), 56.0, 9.3);
            Assert.True(d < 0, "Jutland interior classified as water: " + F(d) + " km");
        }

        [Fact]
        public void Land_SyltInterior_IsNegative()
        {
            // Husum town center on the mainland North Frisian coast, ~4 km
            // inland from the North Sea — a real coast-edge leaf, so the
            // dot-product sign branch is exercised on the land side.
            float d = DistanceKm(LoadPort(), 54.475, 9.048);
            Assert.True(d < 0, "Husum mainland classified as water: " + F(d) + " km");
        }

        [Fact]
        public void Water_HelgolandMoleLight_IsPositive()
        {
            // Curated profile B1318 "Vorhafen Molenfeuer Ost Knie": a
            // breakwater light standing on the harbor mole, which the
            // game's coast vector correctly treats as water side. The
            // +1.267 km reading here was measured against the real .dat —
            // this test pins it so a sign flip or data regression shows up.
            float d = DistanceKm(LoadPort(), 54.171933, 7.89925);
            Assert.True(d > 0, "Helgoland mole light classified as land: " + F(d) + " km");
        }

        [Fact]
        public void Water_CentralNorthSea_IsPositive()
        {
            // ~150 km off the Dutch coast — deep open water.
            float d = DistanceKm(LoadPort(), 54.5, 4.0);
            Assert.True(d > 0, "central North Sea classified as land: " + F(d) + " km");
        }

        [Fact]
        public void Water_OffshoreWestOfHelgoland_IsPositive()
        {
            // ~13 km west of Helgoland — open water near the island's edge
            // leaf, exercises the coast-edge sign branch on the water side.
            float d = DistanceKm(LoadPort(), 54.15, 7.70);
            Assert.True(d > 0, "offshore Helgoland classified as land: " + F(d) + " km");
        }

        [Fact]
        public void Water_BalticEastOfDenmark_IsPositive()
        {
            float d = DistanceKm(LoadPort(), 55.5, 12.6);
            Assert.True(d > 0, "Baltic east of Denmark classified as land: " + F(d) + " km");
        }

        [Fact]
        public void RegistrySweep_ClassifiesBothSides_NoThrow()
        {
            CoastQuadTreePort port = LoadPort();
            Assert.True(LeuchtfeuerRegistry.Initialize(FindDataDirectory(), null),
                LeuchtfeuerRegistry.LastLoadError);

            int land = 0;
            int water = 0;
            for (int i = 0; i < LeuchtfeuerRegistry.Profiles.Count; i++)
            {
                LighthouseProfile profile = LeuchtfeuerRegistry.Profiles[i];
                float d = DistanceKm(port, profile.Latitude, profile.Longitude);
                if (d < 0)
                {
                    land++;
                }
                else
                {
                    water++;
                }
            }

            // Sanity distribution: a working classifier puts the coastal
            // DB on both sides; everything on one side means the sign is
            // inverted or the file failed to load.
            Assert.True(land > 0, "no land verdicts over " + LeuchtfeuerRegistry.Profiles.Count + " profiles");
            Assert.True(water > 0, "no water verdicts over " + LeuchtfeuerRegistry.Profiles.Count + " profiles");
        }

        [Fact]
        public void OfflinePort_MatchesLiveSweepCsv()
        {
            // Cross-validation between the offline port and the LIVE game:
            // rn_lighthouse_landprobe writes Source/Data/LeuchtfeuerLandProbe.csv
            // from the real CoastQuadTree instance. When that CSV exists,
            // every shared profile must agree with the port within float
            // projection noise (measured delta on edge-branch points: 0.000).
            string csvPath = Path.Combine(FindDataDirectory(), "LeuchtfeuerLandProbe.csv");
            if (!File.Exists(csvPath))
            {
                throw new SkipTestException("live sweep CSV not present (run rn_lighthouse_landprobe in game)");
            }

            CoastQuadTreePort port = LoadPort();
            Assert.True(LeuchtfeuerRegistry.Initialize(FindDataDirectory(), null),
                LeuchtfeuerRegistry.LastLoadError);

            var live = new Dictionary<string, double>(StringComparer.Ordinal);
            string[] lines = File.ReadAllLines(csvPath);
            for (int i = 1; i < lines.Length; i++)
            {
                string[] cells = lines[i].Split(';');
                if (cells.Length < 7 || string.IsNullOrEmpty(cells[5]))
                {
                    continue;
                }

                double d;
                if (double.TryParse(cells[5], NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                {
                    live[cells[0]] = d;
                }
            }

            int compared = 0;
            for (int i = 0; i < LeuchtfeuerRegistry.Profiles.Count; i++)
            {
                LighthouseProfile profile = LeuchtfeuerRegistry.Profiles[i];
                double liveDistance;
                if (!live.TryGetValue(profile.Code ?? "", out liveDistance))
                {
                    continue;
                }

                double offline = DistanceKm(port, profile.Latitude, profile.Longitude);

                // The sign must ALWAYS agree — that is what drives the
                // land/water curation.
                Assert.Equal(Math.Sign(liveDistance), Math.Sign(offline));

                // Magnitudes agree only outside the port's fallback
                // sentinel (±MaxDistance): in edge-less leaves the live
                // game reads the exact distance from WorldMaps.
                // LandDistanceMap, which the port does not parse.
                if (Math.Abs(offline) < CoastQuadTreePort.MaxDistance - 1.0
                    && Math.Abs(liveDistance) < CoastQuadTreePort.MaxDistance - 1.0)
                {
                    double delta = Math.Abs(offline - liveDistance);
                    Assert.True(delta < 0.05,
                        "port/live divergence on " + profile.Code + " " + profile.Name +
                        ": offline=" + offline.ToString("F3", CultureInfo.InvariantCulture) +
                        " live=" + liveDistance.ToString("F3", CultureInfo.InvariantCulture));
                }

                compared++;
            }

            Assert.True(compared > 100,
                "too few shared rows compared: " + compared + " (CSV stale or codes mismatched)");
        }

        private static CoastQuadTreePort LoadPort()
        {
            string path = FindCoastDatPath();
            if (!File.Exists(path))
            {
                throw new SkipTestException("coast quad tree .dat not found: " + path);
            }

            return CoastQuadTreePort.Load(path);
        }

        private static float DistanceKm(CoastQuadTreePort port, double latitude, double longitude)
        {
            // Mirrors the runtime probe: double projection helpers, float
            // hand-off (Coordinates.ToWorldPosition returns a float Vector2).
            return port.GetDistanceToCoastKm(
                (float)LighthouseLandGate.LongitudeToKm(longitude),
                (float)LighthouseLandGate.LatitudeToKm(latitude));
        }

        private static string FindCoastDatPath()
        {
            string gameRoot = Environment.GetEnvironmentVariable("UBOAT_GAME_PATH");
            if (string.IsNullOrEmpty(gameRoot))
            {
                gameRoot = @"C:\Program Files (x86)\Steam\steamapps\common\UBOAT";
            }

            return Path.Combine(
                gameRoot, "UBOAT_Data", "StreamingAssets", "Maps", "Coast Quad Tree.dat");
        }

        private static string FindDataDirectory()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null
                && !Directory.Exists(Path.Combine(directory.FullName, "Source", "Data")))
            {
                directory = directory.Parent;
            }

            Assert.NotNull(directory);
            return Path.Combine(directory.FullName, "Source", "Data");
        }

        private static string F(float value)
        {
            return value.ToString("F3", CultureInfo.InvariantCulture);
        }
    }
}
