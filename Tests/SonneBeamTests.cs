using System;
using System.IO;
using Xunit;

namespace UBOAT.Mods.RadioNavigation.Tests
{
    public class SonneBeamTests
    {
        [Fact]
        public void BuildPattern_DotSector_UsesSixtyElementsAndFourEquisignals()
        {
            var pattern = SonneBeam.BuildPattern(38, false);

            Assert.Equal(60, pattern.Length);
            AssertRepeated(pattern, 0, 36, NavigationElementType.Dot);
            AssertRepeated(pattern, 36, 4, NavigationElementType.Equisignal);
            AssertRepeated(pattern, 40, 20, NavigationElementType.Dash);
        }

        [Fact]
        public void BuildPattern_DashSector_UsesHistoricalOrder()
        {
            var pattern = SonneBeam.BuildPattern(38, true);

            Assert.Equal(60, pattern.Length);
            AssertRepeated(pattern, 0, 20, NavigationElementType.Dash);
            AssertRepeated(pattern, 20, 4, NavigationElementType.Equisignal);
            AssertRepeated(pattern, 24, 36, NavigationElementType.Dot);
        }

        [Theory]
        [InlineData(0, false, 2)]
        [InlineData(1, false, 3)]
        [InlineData(2, false, 4)]
        [InlineData(60, false, 2)]
        public void BuildPattern_UsesSonneBeamLossCorrection(int dotCount, bool isDashSector, int expectedEquisignals)
        {
            var pattern = SonneBeam.BuildPattern(dotCount, isDashSector);
            int equisignals = 0;

            for (int i = 0; i < pattern.Length; i++)
                if (pattern[i] == NavigationElementType.Equisignal)
                    equisignals++;

            Assert.Equal(60, pattern.Length);
            Assert.Equal(expectedEquisignals, equisignals);
        }

        [Fact]
        public void StationTable_UsesCircularNearestAngle()
        {
            var station = SonneBeamStation.Parse(
                "{\"station\":\"Test\",\"dashes\":{\"359\":60},\"dots\":{\"0\":0}}");

            SonneBeamSignal signal;
            Assert.True(station.TryGetSignal(360.1, out signal));
            Assert.Equal(0, signal.DotCount);
            Assert.Equal(60, signal.DashCount);
            Assert.False(signal.IsDashSector);
        }

        [Fact]
        public void Initialize_LoadsAllStationTables()
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null
                && !Directory.Exists(Path.Combine(directory.FullName, "Source", "Data")))
            {
                directory = directory.Parent;
            }

            Assert.NotNull(directory);
            Assert.True(
                SonneBeam.Initialize(Path.Combine(directory.FullName, "Source", "Data")),
                SonneBeam.LastLoadError);

            NavigationElementType[] pattern;
            SonneBeamSignal signal;
            Assert.True(SonneBeam.TryGetNavigationPattern(
                "Quimper/Ploneis",
                130.2,
                out pattern,
                out signal));
            Assert.Equal(60, pattern.Length);
            Assert.Equal(60, signal.DotCount + signal.DashCount);
        }

        private static string FindDataDirectory()
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null
                && !Directory.Exists(Path.Combine(directory.FullName, "Source", "Data")))
            {
                directory = directory.Parent;
            }

            Assert.NotNull(directory);
            return Path.Combine(directory.FullName, "Source", "Data");
        }

        [Fact]
        public void Initialize_MissingDirectory_ReturnsFalse()
        {
            try
            {
                Assert.False(SonneBeam.Initialize(
                    Path.Combine(Path.GetTempPath(), "rn-no-such-dir")));
                Assert.Contains("not found", SonneBeam.LastLoadError);
            }
            finally
            {
                Assert.True(SonneBeam.Initialize(FindDataDirectory()), SonneBeam.LastLoadError);
            }
        }

        [Fact]
        public void Initialize_DirectoryWithoutTables_AppendsErrors()
        {
            // The test output directory holds assemblies but no station
            // tables — no filesystem writes needed (restricted tokens deny
            // Temp dir creation), yet all five per-file errors trigger.
            try
            {
                Assert.False(SonneBeam.Initialize(AppContext.BaseDirectory));
                Assert.Contains("Bushmills.json", SonneBeam.LastLoadError);
                Assert.Contains(";", SonneBeam.LastLoadError);
            }
            finally
            {
                Assert.True(SonneBeam.Initialize(FindDataDirectory()), SonneBeam.LastLoadError);
            }
        }

        [Fact]
        public void TryGetNavigationPattern_UnknownStation_ReturnsFalse()
        {
            NavigationElementType[] pattern;
            SonneBeamSignal signal;
            Assert.False(SonneBeam.TryGetNavigationPattern(
                "NoSuchStation",
                0.0,
                out pattern,
                out signal));
            Assert.Null(pattern);
        }

        [Fact]
        public void BuildPattern_OutOfRange_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => SonneBeam.BuildPattern(-1, false));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => SonneBeam.BuildPattern(61, true));
        }

        // MiniJson escape handling (ParseString/ParseEscape, refactored
        // 2026-09-02): all 8 standard JSON escapes must decode through the
        // public SonneBeamStation.Parse consumer contract, and malformed
        // input must throw FormatException instead of corrupting state.
        [Theory]
        [InlineData("\\\"", "\"")]           // \" -> quote
        [InlineData("\\\\", "\\")]            // \\ -> backslash
        [InlineData("\\/", "/")]             // \/ -> slash
        [InlineData("\\b", "\b")]             // \b -> backspace
        [InlineData("\\f", "\f")]             // \f -> form feed
        [InlineData("\\n", "\n")]             // \n -> newline
        [InlineData("\\r", "\r")]             // \r -> carriage return
        [InlineData("\\t", "\t")]             // \t -> tab
        public void Parse_StandardEscapes_DecodeInStationName(string escaped, string expected)
        {
            var station = SonneBeamStation.Parse(
                "{\"station\":\"a" + escaped + "b\",\"dashes\":{},\"dots\":{}}");

            Assert.Equal("a" + expected + "b", station.Name);
        }

        [Fact]
        public void Parse_UnknownEscape_ThrowsFormat()
        {
            Assert.Throws<System.FormatException>(() =>
                SonneBeamStation.Parse(
                    "{\"station\":\"a\\qb\",\"dashes\":{},\"dots\":{}}"));
        }

        // Documents the parser boundary: \uXXXX is NOT supported (the data
        // files are ASCII station tables). If this ever changes, update the
        // escape test above instead.
        [Fact]
        public void Parse_UnicodeEscape_NotSupported_ThrowsFormat()
        {
            Assert.Throws<System.FormatException>(() =>
                SonneBeamStation.Parse(
                    "{\"station\":\"a\\u0041b\",\"dashes\":{},\"dots\":{}}"));
        }

        [Fact]
        public void Parse_UnterminatedString_ThrowsFormat()
        {
            Assert.Throws<System.FormatException>(() =>
                SonneBeamStation.Parse(
                    "{\"station\":\"abc,\"dashes\":{},\"dots\":{}}"));
        }

        [Fact]
        public void Parse_TrailingBackslash_ThrowsFormat()
        {
            Assert.Throws<System.FormatException>(() =>
                SonneBeamStation.Parse(
                    "{\"station\":\"abc\\\",\"dashes\":{},\"dots\":{}}"));
        }

        private static void AssertRepeated(
            NavigationElementType[] pattern,
            int start,
            int count,
            NavigationElementType expected)
        {
            for (int i = start; i < start + count; i++)
                Assert.Equal(expected, pattern[i]);
        }
    }
}
