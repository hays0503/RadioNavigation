using System.Collections.Generic;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class LeuchtfeuerRegistryTests
    {
        [Theory]
        [InlineData(49.0, -5.0, true)]
        [InlineData(62.0, 20.0, true)]
        [InlineData(53.5, 8.1, true)]
        [InlineData(48.9, 8.1, false)]
        [InlineData(62.1, 8.1, false)]
        [InlineData(53.5, -5.1, false)]
        [InlineData(53.5, 20.1, false)]
        public void InTheater_Bounds(double lat, double lon, bool expected)
        {
            Assert.Equal(expected, LeuchtfeuerRegistry.InTheater(lat, lon));
        }

        [Theory]
        [InlineData("13 m", 13f)]
        [InlineData("63 m", 63f)]
        [InlineData("5.5 m", 5.5f)]
        [InlineData("5,5 m", 5.5f)]
        [InlineData("", 0f)]
        [InlineData(null, 0f)]
        [InlineData("m", 0f)]
        public void ParseFireHeightM(string input, float expected)
        {
            Assert.Equal(expected, LeuchtfeuerRegistry.ParseFireHeightM(input), 3);
        }

        [Theory]
        [InlineData("Fl W 20s", 18f)]          // Wangerooge: 360/20
        [InlineData("Fl(3)W.12s23M", 30f)]     // 360/12, range suffix ignored
        [InlineData("Oc.WG.6s7/4M", 60f)]      // 360/6
        [InlineData("Q(9) 15s", 24f)]          // 360/15
        [InlineData("F.G.9M", 0f)]             // fixed light
        [InlineData("8 F.R. (horiz.)", 0f)]    // fixed
        [InlineData("", 0f)]
        [InlineData(null, 0f)]
        public void RotationFromCharacteristic(string input, float expected)
        {
            Assert.Equal(expected, LeuchtfeuerRegistry.RotationFromCharacteristic(input), 3);
        }

        [Fact]
        public void RotationFromCharacteristic_MultiSegment_TakesFirstPeriod()
        {
            // "Dir.F.W.30M; Dir.Fl.W.5s30M" — first Ns is 5 s -> 72 °/s.
            Assert.Equal(72f, LeuchtfeuerRegistry.RotationFromCharacteristic(
                "Dir.F.W.30M; Dir.Fl.W.5s30M"), 3);
        }

        private static Dictionary<string, object> Entry(
            string name, double lat, double lon,
            string fireHeight = "13 m", string characteristic = "F.W.",
            string code = "B0001")
        {
            return new Dictionary<string, object>
            {
                { "name", name },
                { "country", "Deutschland" },
                { "location", "Test" },
                { "lat", lat },
                { "lon", lon },
                { "fireHeight", fireHeight },
                { "characteristic", characteristic },
                { "type", "Lighthouse" },
                { "code", code }
            };
        }

        [Fact]
        public void BuildProfile_MapsAllFields()
        {
            LighthouseProfile profile = LeuchtfeuerRegistry.BuildProfile(
                Entry("Test Light", 53.5, 8.1, "63 m", "Fl W 20s", "B1054"));

            Assert.NotNull(profile);
            Assert.Equal("Test Light", profile.Name);
            Assert.Equal(53.5f, profile.Latitude, 4);
            Assert.Equal(8.1f, profile.Longitude, 4);
            Assert.Equal(63f, profile.FireHeightM, 3);
            Assert.Equal("Fl W 20s", profile.Characteristic);
            Assert.Equal("B1054", profile.Code);
            Assert.Equal("lighthouse", profile.PrefabPath);
            Assert.Equal("LighthouseRotator", profile.RotatorObjectName);
            Assert.Equal(18f, profile.RotationDegreesPerSecond, 3);
            Assert.True(profile.Enabled);
        }

        [Fact]
        public void BuildProfile_GarbageFireHeight_FallsBackToDefault()
        {
            LighthouseProfile profile = LeuchtfeuerRegistry.BuildProfile(
                Entry("Broken", 53.5, 8.1, "unknown"));

            Assert.NotNull(profile);
            Assert.Equal(20f, profile.FireHeightM, 3);
        }

        [Fact]
        public void BuildProfile_MissingName_ReturnsNull()
        {
            Dictionary<string, object> entry = Entry(null, 53.5, 8.1);
            Assert.Null(LeuchtfeuerRegistry.BuildProfile(entry));
        }

        [Fact]
        public void BuildProfile_NaNCoordinates_ReturnsNull()
        {
            Dictionary<string, object> entry = Entry("X", 53.5, 8.1);
            entry["lat"] = "not-a-number";
            Assert.Null(LeuchtfeuerRegistry.BuildProfile(entry));
        }

        [Fact]
        public void Initialize_RealRegistry_LoadsTheaterSubset()
        {
            Assert.True(LeuchtfeuerRegistry.Initialize(FindDataDirectory(), null),
                LeuchtfeuerRegistry.LastLoadError);

            // The full DB had 1184 entries; the theater window keeps the
            // North Sea + Baltic subset (agent-counted: 841 before the
            // 2026-09-09 land curation moved 287 water-side entries into
            // LeuchtfeuerExcluded.json — 554 remained; the same-day
            // fire-height curation (ADR-031) then moved the 345 theater
            // lights below 15 m into LeuchtfeuerMinor.json — 209 remain).
            Assert.InRange(LeuchtfeuerRegistry.Profiles.Count, 200, 220);

            // Every loaded profile sits inside the window and is enabled.
            foreach (LighthouseProfile profile in LeuchtfeuerRegistry.Profiles)
            {
                Assert.True(LeuchtfeuerRegistry.InTheater(profile.Latitude, profile.Longitude));
                Assert.True(profile.Enabled);
            }
        }

        [Fact]
        public void Initialize_DedupesCuratedProximity()
        {
            var curated = new List<LighthouseProfile>
            {
                new LighthouseProfile(
                    "MOD-WANGEROOGE", "Wangerooge", "Deutschland", "Wangerooge",
                    53.519973f, 8.127701f, 63f, "Fl W 20s", "Lighthouse",
                    "lighthouse", "LighthouseRotator", 18f, true)
            };

            Assert.True(LeuchtfeuerRegistry.Initialize(FindDataDirectory(), curated),
                LeuchtfeuerRegistry.LastLoadError);

            // The DB's own Wangerooge entry must not survive the dedupe.
            foreach (LighthouseProfile profile in LeuchtfeuerRegistry.Profiles)
            {
                bool nearCurated =
                    System.Math.Abs(profile.Latitude - 53.519973f) <= 0.005f
                    && System.Math.Abs(profile.Longitude - 8.127701f) <= 0.005f;
                Assert.False(nearCurated, "duplicate of curated Wangerooge survived: " + profile.Name);
            }
        }

        [Fact]
        public void Initialize_MissingDirectory_ReturnsFalse()
        {
            Assert.False(LeuchtfeuerRegistry.Initialize(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rn-no-such-dir"), null));
            Assert.Contains("not found", LeuchtfeuerRegistry.LastLoadError);
        }

        private static string FindDataDirectory()
        {
            var directory = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            while (directory != null
                && !System.IO.Directory.Exists(
                    System.IO.Path.Combine(directory.FullName, "Source", "Data")))
            {
                directory = directory.Parent;
            }

            Assert.NotNull(directory);
            return System.IO.Path.Combine(directory.FullName, "Source", "Data");
        }
    }
}
