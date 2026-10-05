using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace RadioNavigation.SourceCheck
{
    /// <summary>
    /// Documentation hygiene gates (added 2026-09-26). The cleanup pass that
    /// introduced these gates fixed, in one sweep: 24 mojibake sequences in
    /// AGENTS.md, a duplicate ADR-023 number, ADR-030..034 missing from the SAD/12
    /// status table, test counts frozen at 786 while the real count was 1135, a
    /// claim that graphify-out/wiki/index.md exists (it does not), and a BOM on
    /// seven markdown files. Every one of those is a class of defect that walks
    /// straight back in the next time somebody edits a document, so each is pinned
    /// here instead of relying on someone remembering.
    ///
    /// Scope: maintained project documentation. Excluded are build output, the
    /// graphify cache, vendored node_modules, and documents-readonly/ (primary
    /// sources for the radio-navigation research, never edited).
    ///
    /// Source .cs files are deliberately NOT covered: several of them carry a BOM
    /// and Roslyn/the game compiler both accept it. Rewriting 35 source files to
    /// satisfy a documentation rule would be noise, not hygiene.
    /// </summary>
    public class DocumentationTests
    {
        private static readonly string[] SkipSegments =
        {
            "\\bin\\", "\\obj\\", "\\graphify-out\\", "\\.git\\", "\\node_modules\\", "\\documents-readonly\\",
        };

        /// <summary>
        /// Mojibake signatures produced by decoding UTF-8 bytes as CP1251 and
        /// re-encoding as UTF-8. Each is a Cyrillic 've' (U+0432) followed by the
        /// two bytes CP1251 produced for the original character's second and third
        /// UTF-8 bytes. Spelled by code point so this file never contains the
        /// broken bytes itself and stays clean under its own gate.
        /// </summary>
        private static readonly string[] MojibakeMarkers =
        {
            "04320402" + "201D", // ve + DJE + right-double-quote  -> em dash
            "04322020" + "201D", // ve + dagger + right-double-quote  -> right arrow
            "04322020" + "2019", // ve + dagger + right-single-quote  -> right arrow
            "04322020" + "0027", // ve + dagger + apostrophe          -> right arrow
        };

        private static string Root => ModCompiler.ModRoot;

        private static IEnumerable<string> MaintainedMarkdown()
        {
            string root = Root;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                yield break;

            foreach (string path in Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories))
            {
                string relative = path.Substring(root.Length).Replace('/', '\\');
                if (SkipSegments.Any(s => relative.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0))
                    continue;
                yield return path;
            }
        }

        private static bool HasUtf8Bom(string path)
        {
            byte[] head = new byte[3];
            using (FileStream stream = File.OpenRead(path))
            {
                if (stream.Read(head, 0, 3) < 3)
                    return false;
            }
            return head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;
        }

        /// <summary>
        /// Every maintained document must be valid UTF-8 with no BOM and no
        /// mojibake, and must end with a newline. A BOM is invisible in most
        /// editors but breaks naive byte-level tooling; the mojibake class is the
        /// one that silently corrupted AGENTS.md.
        /// </summary>
        [Fact]
        public void Docs_AreUtf8WithoutMojibake()
        {
            var problems = new List<string>();
            var strictUtf8 = new UTF8Encoding(false, true);

            foreach (string path in MaintainedMarkdown())
            {
                string name = Path.GetFileName(path);
                byte[] bytes = File.ReadAllBytes(path);

                if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                    problems.Add(name + ": has UTF-8 BOM");

                string text;
                try
                {
                    text = strictUtf8.GetString(bytes);
                }
                catch (DecoderFallbackException)
                {
                    problems.Add(name + ": not valid UTF-8");
                    continue;
                }

                foreach (string marker in MojibakeMarkers)
                {
                    if (text.Contains(DecodeHex(marker)))
                        problems.Add(name + ": mojibake sequence " + marker);
                }

                if (text.Length > 0 && text[text.Length - 1] != '\n')
                    problems.Add(name + ": no trailing newline");
            }

            Assert.True(problems.Count == 0,
                "documentation encoding/hygiene violations:" + Environment.NewLine
                + string.Join(Environment.NewLine, problems));
        }

        private static string DecodeHex(string hex)
        {
            var builder = new StringBuilder(hex.Length / 4);
            for (int i = 0; i + 3 < hex.Length; i += 4)
                builder.Append((char)int.Parse(hex.Substring(i, 4), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        /// <summary>
        /// ADR file numbers must be unique. The duplicate ADR-023 (planets written
        /// 2026-08-19, chronometer written 2026-08-23) shipped unnoticed for weeks
        /// and had to be papered over with a "distinguish by filename, not number"
        /// footnote. The later ADR is now ADR-034; this gate stops a repeat.
        /// </summary>
        [Fact]
        public void Adr_NumbersAreUnique()
        {
            string adrDir = Path.Combine(Root, "docs", "adr");
            if (!Directory.Exists(adrDir))
                return;

            var byNumber = new Dictionary<int, List<string>>();
            var pattern = new Regex(@"^ADR-(\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

            foreach (string path in Directory.GetFiles(adrDir, "ADR-*.md"))
            {
                Match match = pattern.Match(Path.GetFileName(path));
                if (!match.Success)
                    continue;
                int number = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                if (!byNumber.TryGetValue(number, out List<string> names))
                {
                    names = new List<string>();
                    byNumber[number] = names;
                }
                names.Add(Path.GetFileName(path));
            }

            var duplicates = byNumber
                .Where(pair => pair.Value.Count > 1)
                .Select(pair => "ADR-" + pair.Key.ToString("D3", CultureInfo.InvariantCulture)
                    + ": " + string.Join(" + ", pair.Value))
                .ToList();

            Assert.True(duplicates.Count == 0,
                "duplicate ADR numbers:" + Environment.NewLine + string.Join(Environment.NewLine, duplicates));
        }

        /// <summary>
        /// Every ADR file must appear in the SAD/12 status table. The table used to
        /// stop at ADR-029 while files 030-033 already existed, so four decisions
        /// were invisible to anyone reading the architecture document.
        /// </summary>
        [Fact]
        public void Adr_StatusTableCoversEveryFile()
        {
            string adrDir = Path.Combine(Root, "docs", "adr");
            string tablePath = Path.Combine(Root, "docs", "SAD", "12_Architecture_Decisions.md");
            if (!Directory.Exists(adrDir) || !File.Exists(tablePath))
                return;

            string table = File.ReadAllText(tablePath);
            var missing = new List<string>();

            foreach (string path in Directory.GetFiles(adrDir, "ADR-*.md").OrderBy(p => p, StringComparer.Ordinal))
            {
                string number = Regex.Match(Path.GetFileName(path), @"^ADR-(\d+)").Groups[1].Value;
                if (number.Length == 0)
                    continue;
                if (!table.Contains("| ADR-" + number))
                    missing.Add("ADR-" + number + " (" + Path.GetFileName(path) + ")");
            }

            Assert.True(missing.Count == 0,
                "ADR files absent from SAD/12 status table:" + Environment.NewLine
                + string.Join(Environment.NewLine, missing));
        }

        /// <summary>
        /// DOCS.md is the project's documentation entry point, so every file path
        /// it names must exist. A map that points at moved files is worse than no
        /// map, and this is the check that keeps the map honest when files move.
        /// Bare filenames (listed under a section heading that already gives the
        /// folder) are resolved against the directories DOCS.md uses.
        /// </summary>
        [Fact]
        public void Docs_MapLinksResolve()
        {
            string mapPath = Path.Combine(Root, "DOCS.md");
            if (!File.Exists(mapPath))
                return;

            string map = File.ReadAllText(mapPath);
            var searchRoots = new List<string>
            {
                Root,
                Path.Combine(Root, "docs", "refactoring-plan"),
                Path.Combine(Root, "docs", "refactoring-plan", "hypothesis"),
                Path.Combine(Root, "docs", "refactoring-plan", "game-source-findings"),
                Path.Combine(Root, "docs", "SAD"),
                Path.Combine(Root, "docs"),
                Path.Combine(Root, "knowledge"),
                Path.Combine(Root, "knowledge", "inventory"),
                Path.Combine(Root, "docs", "archive"),
                Path.Combine(Root, "Tools"),
            };

            var missing = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var reference = new Regex(@"`([A-Za-z0-9_./\-]+\.(?:md|cs|ps1|json))`", RegexOptions.Compiled);

            foreach (Match match in reference.Matches(map))
            {
                string name = match.Groups[1].Value;
                if (name.Contains(".."))
                    continue; // relative hops are prose, not map entries
                string relative = name.Replace('/', Path.DirectorySeparatorChar);
                if (!seen.Add(relative))
                    continue;

                bool found = searchRoots.Any(dir =>
                    File.Exists(Path.Combine(dir, relative))
                    || Directory.Exists(Path.Combine(dir, relative)));
                if (!found)
                    missing.Add(name);
            }

            Assert.True(missing.Count == 0,
                "DOCS.md references paths that do not exist:" + Environment.NewLine
                + string.Join(Environment.NewLine, missing));
        }

        /// <summary>
        /// Every `Name.cs` reference in the SAD chapters must resolve to a real file
        /// in Source\, Tests\, or the decompiled game source. Measured on 2026-09-26:
        /// 216 references (103 distinct names) over 10 of the 11 chapters, 195 of them
        /// resolve, and all 20 names that do not are annotated in place as deliberately
        /// not implemented - chapter 08 proposes 19 types (5 interfaces in 2.1, 14
        /// implementation classes in 2.2, every one marked "NOT implemented") and
        /// chapter 11 lists AudioPipelineTests, rejected as F47. So the reference
        /// graph is sound; this gate exists to keep it that way after future dead-code
        /// sweeps, when a SAD line can outlive its class.
        ///
        /// Deliberately NOT implemented: a completeness check ("does SAD describe the
        /// sextant / the map markers"). Absence is not a dangling reference, and the
        /// judgement about what SAD ought to cover is recorded in DOCS.md section 8
        /// rather than enforced here.
        /// </summary>
        [Fact]
        public void Sad_ClassReferencesResolve()
        {
            string sadDir = Path.Combine(Root, "docs", "SAD");
            if (!Directory.Exists(sadDir))
                return;

            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string dir in new[]
            {
                Path.Combine(Root, "Source"),
                Path.Combine(Root, "Dev", "Tests"),
            })
            {
                if (!Directory.Exists(dir))
                    continue;
                foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    // Generated under obj\ and bin\ by the test build; no chapter may
                    // reference them, and counting them would inflate the known set.
                    if (SkipSegments.Any(s => file.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0))
                        continue;
                    known.Add(Path.GetFileNameWithoutExtension(file));
                }
            }

            // Game types (ShipControlUI, Periscope, SavesManager, ...) live in the
            // decompiled source tree, not in the mod. Resolve the tree through
            // GameSourceCompiler so the gate also runs where UBOAT_GAME_SOURCE is not
            // exported (it falls back to the default root) - reading the variable
            // directly made this test skip silently on a default dev box. Without the
            // tree those names cannot be resolved at all, so skip rather than report
            // false damage.
            if (!GameSourceCompiler.HasGameSource)
                return;

            foreach (string file in GameSourceCompiler.EnumerateSources())
                known.Add(Path.GetFileNameWithoutExtension(file));

            var broken = new List<string>();
            var reference = new Regex(@"([A-Z][A-Za-z0-9_]{4,})\.cs", RegexOptions.Compiled);

            foreach (string chapter in Directory.GetFiles(sadDir, "*.md").OrderBy(p => p, StringComparer.Ordinal))
            {
                string text = File.ReadAllText(chapter);
                foreach (Match match in reference.Matches(text))
                {
                    string name = match.Groups[1].Value;
                    if (!known.Contains(name))
                        broken.Add(Path.GetFileName(chapter) + " -> " + name + ".cs");
                }
            }

            // Chapter 08 proposes 19 types that were never implemented - 5 interfaces
            // in 2.1 and 14 implementation classes in 2.2, each marked "NOT
            // implemented" in place - and chapter 11 lists one rejected test. Both are
            // documented as such, so allow exactly those 20 names and nothing else.
            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // SAD/08 section 2.1, proposed service interfaces
                "IRadioState", "IStationDataSource", "IAudioOutput", "IGameServices",
                "IMorseDecoder",
                // SAD/08 section 2.2, proposed implementation classes
                "RadioContext", "HardcodedStationRepository", "JsonStationRepository",
                "AudioPipeline", "GameReflectionCache", "RadioController",
                "DiagnosticsCollector", "StationInfoDisplay", "StationFinder",
                "CarrierBank", "Atmosphere", "ReceiverDsp", "TickScheduler",
                "GameStrings",
                // SAD/11, rejected under F47
                "AudioPipelineTests",
            };

            var unexpected = broken
                .Where(entry => !expected.Contains(entry.Substring(entry.IndexOf(" -> ") + 4)
                    .Replace(".cs", string.Empty)))
                .Distinct()
                .ToList();

            Assert.True(unexpected.Count == 0,
                "SAD references classes that exist nowhere (Source, Tests, or the game "
                + "source). Fix the reference or annotate the entry as intentionally "
                + "not implemented:" + Environment.NewLine
                + string.Join(Environment.NewLine, unexpected));
        }

        /// <summary>
        /// The counts quoted in DOCS.md and README.md must be the counts actually on
        /// disk. Both files used to say 207 (the size of the first 2026-08-01 audit)
        /// while the table had grown to 241 rows, and nothing compared the prose
        /// against the data.
        ///
        /// Deliberately NOT implemented: "flag any mention of 207". A 207 reference
        /// is legitimate as history ("the 2026-08-01 audit found 207 problems"), as a
        /// range ("F01-F207"), and as the correction itself ("207 -> 241") - an
        /// earlier version of this gate flagged all three and had to be weakened
        /// twice. Instead each number a doc claims is compared to the real count,
        /// which is unambiguous.
        /// </summary>
        [Fact]
        public void Docs_RegistryCountsMatchDisk()
        {
            string indexPath = Path.Combine(Root, "docs", "refactoring-plan", "hypothesis", "INDEX.md");
            if (!File.Exists(indexPath))
                return;

            string index = File.ReadAllText(indexPath);
            int rows = Regex.Matches(index, @"^\|\s*\*\*F\d+\*\*\s*\|",
                RegexOptions.Multiline | RegexOptions.Compiled).Count;

            int confirmed = Directory.Exists(Path.Combine(Root, "docs", "refactoring-plan", "confirmed"))
                ? Directory.GetFiles(Path.Combine(Root, "docs", "refactoring-plan", "confirmed"), "*.md").Length
                : -1;
            int rejected = Directory.Exists(Path.Combine(Root, "docs", "refactoring-plan", "rejected"))
                ? Directory.GetFiles(Path.Combine(Root, "docs", "refactoring-plan", "rejected"), "*.md").Length
                : -1;
            int adrFiles = Directory.Exists(Path.Combine(Root, "docs", "adr"))
                ? Directory.GetFiles(Path.Combine(Root, "docs", "adr"), "ADR-*.md").Length
                : -1;

            Assert.True(rows > 0,
                "registry row count came out as zero - the INDEX.md row pattern changed");
            Assert.True(confirmed >= 0 && rejected >= 0,
                "refactoring-plan/confirmed or /rejected is missing from the repository");

            foreach (string doc in new[] { "DOCS.md", "README.md" })
            {
                string path = Path.Combine(Root, doc);
                if (!File.Exists(path))
                    continue;
                string text = File.ReadAllText(path);

                Assert.True(text.Contains(rows.ToString(CultureInfo.InvariantCulture)),
                    doc + " never states the actual registry size (" + rows + " rows in hypothesis/INDEX.md). "
                    + "It still quotes the superseded 207-finding audit.");

                foreach (var claim in new[]
                {
                    new { Label = "confirmed/ file count", Actual = confirmed },
                    new { Label = "rejected/ file count", Actual = rejected },
                    new { Label = "ADR file count", Actual = adrFiles },
                })
                {
                    if (claim.Actual < 0)
                        continue;
                    Assert.True(text.Contains(claim.Actual.ToString(CultureInfo.InvariantCulture)),
                        doc + " does not state the current " + claim.Label + " ("
                        + claim.Actual + "); the number it quotes is stale.");
                }
            }
        }

        /// <summary>
        /// AGENTS.md is injected into every session, so a stale count or a broken
        /// claim in it propagates into all future work. Pin the facts that were
        /// wrong: the test count (frozen at 786 for weeks) and the non-existent
        /// graphify-out/wiki/ tree that AGENTS.md told agents to read.
        /// </summary>
        [Fact]
        public void Agents_DocStatesCurrentFacts()
        {
            string path = Path.Combine(Root, "AGENTS.md");
            if (!File.Exists(path))
                return;

            string text = File.ReadAllText(path);

            Assert.True(text.Contains("1135"),
                "AGENTS.md no longer states the current test count (1135 PASS).");
            Assert.True(text.Contains("241"),
                "AGENTS.md no longer states the current registry size (241 findings).");
            Assert.True(!text.Contains("graphify-out/wiki/index.md exists"),
                "AGENTS.md still claims graphify-out/wiki/index.md exists; that tree is not generated.");
            Assert.True(!text.Contains("786 PASS"),
                "AGENTS.md still states the superseded 786-PASS test count as current.");
        }
    }
}
