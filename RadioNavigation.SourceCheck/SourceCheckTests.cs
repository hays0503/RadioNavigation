using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace RadioNavigation.SourceCheck
{
    /// <summary>
    /// Neural-slop guard. Two stages:
    ///   1. GameSource_CompilesClean - the DEPENDENCY-CLOSURE SUBSET of the decompiled
    ///      game source (only files the mod's game-API surface touches) must compile
    ///      under in-memory Roslyn (net40 semantics). Compiling only the subset avoids
    ///      decompiler artifacts (Burst code, deprecated enums, TeximpNet ambiguity) in
    ///      unrelated subsystems. If this fails, the clean reference is unavailable and
    ///      stage 2 cannot run.
    ///   2. Mod_CompilesAgainstCleanGameApi - the mod's Source\**\*.cs must compile
    ///      against the clean (non-obfuscated) game API. Any CS error = the mod calls
    ///      a game member that does not exist or has the wrong signature.
    ///
    /// Skipped (no assertions) when the game source tree is absent on this machine.
    /// </summary>
    public class SourceCheckTests
    {
        private static readonly bool HasGame = GameSourceCompiler.HasGameSource;

        private static (ImmutableArray<string> Files, ImmutableArray<string> BrokenUsings) ComputeSubset()
        {
            var seeds = DependencyClosure.ComputeSeedTypes();
            return DependencyClosure.BuildSignatureOnlyReference(
                seeds,
                GameSourceCompiler.EnumerateSources(),
                GameSourceCompiler.BuildReferences());
        }

        private static List<SyntaxTree> ToTrees(
            IEnumerable<string> files, ImmutableArray<string> stripNamespaces = default)
        {
            var strip = new HashSet<string>(stripNamespaces, StringComparer.Ordinal);
            var trees = new List<SyntaxTree>();
            foreach (var p in files)
            {
                if (p.StartsWith("stub:", StringComparison.Ordinal))
                {
                    var typeName = DependencyClosure.StubTypeName(p);
                    var ns = DependencyClosure.StubNamespace(p);
                    var generic = DependencyClosure.StubIsGeneric(p);
                    var attribute = DependencyClosure.StubIsAttribute(p);
                    var isEnum = DependencyClosure.StubIsEnum(p);
                    var isInterface = DependencyClosure.StubIsInterface(p);
                    var arity = DependencyClosure.StubArity(p);
                    var members = DependencyClosure.StubMembers(p);
                    var nested = DependencyClosure.StubNested(p);
                    var nestedSet = nested.Length > 0
                        ? new HashSet<string>(nested.Split(','), StringComparer.Ordinal)
                        : null;
                    var props = DependencyClosure.StubMemberProps(p);
                    var propsSet = props.Length > 0
                        ? new HashSet<string>(props.Split(','), StringComparer.Ordinal)
                        : null;
                    trees.Add(CSharpSyntaxTree.ParseText(
                        DependencyClosure.StubSource(typeName, ns, generic, attribute, isEnum, isInterface, arity, members, nestedSet, propsSet), path: p));
                }
                else
                {
                    var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(p), path: p);
                    tree = DependencyClosure.StripBrokenUsings(tree, strip);
                    trees.Add(DependencyClosure.StripBodies(tree));
                }
            }
            return trees;
        }

        [Fact]
        public void GameSource_CompilesClean()
        {
            if (!HasGame) return;

            // Cache hit: the clean reference was already proven to compile cleanly and
            // emit successfully when it was built. Re-verifying would recompile all
            // ~2550 game sources (~8 min) on every run, so a valid cache is sufficient.
            if (CleanReferenceCache.TryLoad(out _))
                return;

            var (files, broken) = ComputeSubset();
            Assert.True(files.Length > 0,
                "Narrow reference is empty - seed types not resolved.");
            var trees = ToTrees(files, broken);
            var compilation = GameSourceCompiler.CreateCompilation(
                trees, GameSourceCompiler.BuildReferences());

            var diags = compilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .OrderBy(d => d.Location.SourceTree?.FilePath ?? "")
                .ThenBy(d => d.Location.SourceSpan.Start)
                .ToList();

            Assert.True(diags.Count == 0,
                "Game source subset did not compile cleanly:\n" + FormatDiags(diags));

            // Emit + cache so Mod_CompilesAgainstCleanGameApi and future runs skip the
            // expensive recompile.
            using var stream = new MemoryStream();
            var emit = compilation.Emit(stream);
            Assert.True(emit.Success,
                "Game source subset emit failed:\n" + FormatDiags(emit.Diagnostics));
            CleanReferenceCache.Save(stream.ToArray());
        }

        [Fact]
        public void Mod_CompilesAgainstCleanGameApi()
        {
            if (!HasGame) return;
            if (!ModCompiler.HasModSource) return;

            byte[] cleanDll;
            if (CleanReferenceCache.TryLoad(out cleanDll))
            {
                // cache hit
            }
            else
            {
                var (files, broken) = ComputeSubset();
                var trees = ToTrees(files, broken);
                var compilation = GameSourceCompiler.CreateCompilation(
                    trees, GameSourceCompiler.BuildReferences());
                using var stream = new MemoryStream();
                var emit = compilation.Emit(stream);
                Assert.True(emit.Success,
                    "Game source subset emit failed:\n" + FormatDiags(emit.Diagnostics));
                cleanDll = stream.ToArray();
                CleanReferenceCache.Save(cleanDll);
            }

            var modTrees = ModCompiler.EnumerateSources()
                .Select(p => CSharpSyntaxTree.ParseText(
                    File.ReadAllText(p), options: ModCompiler.GameParityParseOptions, path: p))
                .ToList();
            var modCompilation = ModCompiler.CreateCompilation(
                modTrees, ModCompiler.BuildReferences(cleanDll));

            var diags = modCompilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .OrderBy(d => d.Location.SourceTree?.FilePath ?? "")
                .ThenBy(d => d.Location.SourceSpan.Start)
                .ToList();

            Assert.True(diags.Count == 0,
                "Mod references a game API that does not exist / has wrong signature "
                + "(neural slop):\n" + FormatDiags(diags));

            // Dead-code gate: CS0169 (never-used field) is a real defect the
            // game compiler also reports — typically a leftover after an
            // extraction moved the field to a subsystem class. CS0649
            // (never-assigned) is the [Inject] DI pattern and is allowed.
            var deadFields = modCompilation.GetDiagnostics()
                .Where(d => d.Id == "CS0169")
                .OrderBy(d => d.Location.SourceTree?.FilePath ?? "")
                .ToList();
            Assert.True(deadFields.Count == 0,
                "Never-used fields (CS0169) — dead code left behind by an extraction:\n"
                + FormatDiags(deadFields));
        }

        private static string FormatDiags(IEnumerable<Diagnostic> diags)
        {
            return string.Join("\n", diags.Select(d =>
            {
                var loc = d.Location.IsInSource
                    ? $"{Path.GetFileName(d.Location.SourceTree.FilePath)}:{d.Location.GetLineSpan().StartLinePosition.Line + 1}"
                    : d.Location.Kind.ToString();
                return $"  {d.Id} [{loc}] {d.GetMessage()}";
            }));
        }

        /// <summary>
        /// Quality regression check for the seed-discovery speedup (2026-09-02).
        /// The semantic pass against the obfuscated DLL was removed from
        /// ComputeSeedTypes. This test re-runs the OLD algorithm (semantic + syntax +
        /// member-chain) and asserts the seed set is identical to the NEW algorithm
        /// (syntax + member-chain). If they match, the removal lost no coverage.
        /// </summary>
        [Fact]
        public void SeedQuality_NewVsOld()
        {
            if (!HasGame) return;
            if (!ModCompiler.HasModSource) return;

            var newSeeds = DependencyClosure.ComputeSeedTypes();
            var newSet = new HashSet<string>(
                newSeeds.Select(s => s.Name), StringComparer.Ordinal);

            var oldSeeds = DependencyClosure.ComputeSeedTypesWithSemanticPass();
            var oldSet = new HashSet<string>(
                oldSeeds.Select(s => s.Name), StringComparer.Ordinal);

            var onlyNew = newSet.Except(oldSet).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var onlyOld = oldSet.Except(newSet).OrderBy(x => x, StringComparer.Ordinal).ToList();

            Assert.True(onlyOld.Count == 0,
                $"OLD algorithm found {onlyOld.Count} seeds the NEW one misses (coverage loss):\n"
                + string.Join("\n", onlyOld));
            Assert.True(onlyNew.Count == 0,
                $"NEW algorithm found {onlyNew.Count} seeds the OLD one misses (unexpected additions):\n"
                + string.Join("\n", onlyNew));

            // Arity must match too (generic arity drives stub generic-arity).
            var newArity = newSeeds.ToDictionary(s => s.Name, s => s.Arity, StringComparer.Ordinal);
            var oldArity = oldSeeds.ToDictionary(s => s.Name, s => s.Arity, StringComparer.Ordinal);
            var arityDiff = newArity.Where(kv =>
                    oldArity.TryGetValue(kv.Key, out var oa) && oa != kv.Value)
                .Select(kv => $"{kv.Key}: new={kv.Value} old={oldArity[kv.Key]}")
                .ToList();
            Assert.True(arityDiff.Count == 0,
                $"Arity differs for {arityDiff.Count} seeds:\n" + string.Join("\n", arityDiff));
        }

        /// <summary>
        /// Coverage-risk probe: how many types exist in the obfuscated com.uboat.game.dll
        /// but NOT in the game source? The syntax pass only seeds types present in the
        /// game source (BuildSourceTypeNameSet), so any DLL-only type the mod uses would
        /// be stubbed instead of real — a coverage hole. This asserts the DLL-only set is
        /// empty (or tiny), bounding the risk of the removed semantic pass.
        /// </summary>
        [Fact]
        public void SeedQuality_DllOnlyTypesAreBounded()
        {
            if (!HasGame) return;

            var dllPath = Path.Combine(GameSourceCompiler.ManagedDir, "com.uboat.game.dll");
            if (!File.Exists(dllPath)) return;

            var dllTypes = DependencyClosure.EnumerateAssemblyTypeNames(dllPath);
            var sourceNames = DependencyClosure.BuildSourceTypeNameSet();
            var dllOnly = dllTypes
                .Where(t => !sourceNames.Contains(t))
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToList();

            // Bounded: if the mod uses a DLL-only type, the syntax pass would miss it.
            // Assert the DLL-only set is small (obfuscation renames, not real API gaps).
            Console.WriteLine($"DLL-only types (in com.uboat.game.dll, not in game source): {dllOnly.Count}");
            Assert.True(dllOnly.Count <= 50,
                $"{dllOnly.Count} types in com.uboat.game.dll are missing from game source "
                + "(syntax pass would stub them instead of real):\n"
                + string.Join("\n", dllOnly.Take(50)));
        }

        /// <summary>
        /// Cyclomatic-complexity gate: flags "super-methods" — methods whose McCabe
        /// complexity exceeds a threshold (default 15). A method that does too much is
        /// a refactoring candidate; blocking keeps the mod maintainable. Complexity is
        /// computed over the mod's own Source\**\*.cs (not game code).
        /// </summary>
        [Fact]
        public void MethodComplexity_NoSuperMethods()
        {
            if (!ModCompiler.HasModSource) return;

            const int threshold = 15;
            var over = new List<(string Method, int Complexity)>();
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path).GetRoot();
                foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    int c = ComputeCyclomaticComplexity(method);
                    if (c > threshold)
                    {
                        var name = method.Identifier.Text;
                        var loc = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        over.Add(($"{Path.GetFileName(path)}:{loc} {name}()", c));
                    }
                }
            }

            Assert.True(over.Count == 0,
                $"{over.Count} method(s) exceed cyclomatic complexity {threshold} "
                + "(super-methods, refactor candidates):\n"
                + string.Join("\n", over.OrderByDescending(o => o.Complexity)
                    .Select(o => $"  {o.Complexity}  {o.Method}")));
        }

        /// <summary>
        /// Event-subscription balance gate (P5.2): every event subscribed with
        /// '+=' inside a type must be unsubscribed with '-=' on the same event
        /// somewhere in the same type. Unbalanced subscriptions leak handlers
        /// across panel open/close cycles and hot reloads (live checklist C).
        /// Heuristics (syntax-only):
        ///   - left side must be a member access ('x.Event', 'arr[i].Event') —
        ///     a bare identifier is a local variable/field compound ('gmst += 360');
        ///   - right side must be a method group or lambda — literals/binary
        ///     expressions are string/arithmetic compounds ('s += "; " + msg');
        ///   - balance is per EVENT NAME, not per handler: loop/array handlers
        ///     ('+= elementHandler' vs '-= handlers[i]') are textually unmatchable
        ///     but still balanced when the event itself is torn down.
        /// </summary>
        [Fact]
        public void EventSubscriptions_AreBalanced()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path).GetRoot();
                foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    var subscribed = new Dictionary<string, int>(StringComparer.Ordinal);
                    var unsubscribed = new HashSet<string>(StringComparer.Ordinal);

                    foreach (var expr in typeDecl.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                    {
                        bool isAdd = expr.IsKind(SyntaxKind.AddAssignmentExpression);
                        bool isRemove = expr.IsKind(SyntaxKind.SubtractAssignmentExpression);
                        if (!isAdd && !isRemove) continue;

                        string eventName = EventNameOf(expr.Left);
                        if (eventName == null) continue;          // not a member access
                        if (!IsHandlerExpression(expr.Right)) continue; // compound, not a subscription

                        if (isAdd)
                        {
                            subscribed.TryGetValue(eventName, out int n);
                            subscribed[eventName] = n + 1;
                        }
                        else
                        {
                            unsubscribed.Add(eventName);
                        }
                    }

                    foreach (var kv in subscribed)
                    {
                        if (unsubscribed.Contains(kv.Key)) continue;
                        problems.Add($"{Path.GetFileName(path)} {typeDecl.Identifier.Text}: " +
                            $"event '{kv.Key}' subscribed {kv.Value}x with '+=' but never unsubscribed '-='");
                    }
                }
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} unbalanced event subscription(s) — handler leak risk on open/close or hot reload:\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Callsign-reset wiring gate (P5.2 live checklist: "frequency change
        /// -> single callsign"). SonneController.OnFrequencyChanged must call
        /// ClearSignalBuffer (SonneController.cs:308-312) — the mechanism that
        /// keeps only the latest completed callsign. The behavioral core
        /// (ClearSignalBuffer clears decodedText/morseDecoderBuffer) cannot be
        /// unit-tested without linking the whole MonoBehaviour surface, so
        /// this syntax gate pins the wiring: handler exists AND invokes the
        /// reset. The handler-existence assert prevents a false green if the
        /// handler is renamed.
        /// </summary>
        [Fact]
        public void CallsignReset_WiredToFrequencyChange()
        {
            if (!ModCompiler.HasModSource) return;

            string handlerLoc = null;
            MethodDeclarationSyntax handler = null;
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path).GetRoot();
                foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    if (method.Identifier.Text != "OnFrequencyChanged") continue;
                    // Only the SonneController handler owns the callsign
                    // reset; InformationPanelHandler.OnFrequencyChanged is a
                    // UI refresh handler with its own lastLoggedDecoded reset.
                    if (method.Parent is TypeDeclarationSyntax type && type.Identifier.Text != "SonneController")
                        continue;
                    handler = method;
                    handlerLoc = $"{Path.GetFileName(path)}:{method.GetLocation().GetLineSpan().StartLinePosition.Line + 1}";
                }
            }

            Assert.True(handler != null,
                "SonneController.OnFrequencyChanged handler not found — the callsign reset wiring is gone");

            // Syntax-level invocation check (not string.Contains): a
            // commented-out 'ClearSignalBuffer();' must NOT satisfy the gate.
            bool invokesReset = handler.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Any(inv => inv.Expression is IdentifierNameSyntax name
                    && name.Identifier.Text == "ClearSignalBuffer");
            Assert.True(invokesReset,
                $"{handlerLoc}: OnFrequencyChanged no longer invokes ClearSignalBuffer — "
                + "stale callsigns will accumulate across frequency changes (live checklist: "
                + "'frequency change -> single callsign')");
        }

        /// <summary>
        /// Harmony patch-slot contract (live 2026-09-03: radio menu missing).
        /// RadioStoryIntegration.InjectT3PLL38Decision MUST be wired as a
        /// postfix on RadioStory.ContinueStory, never a prefix:
        /// (1) the original computes RadioStory.stage from tags at the START
        /// of its own body (RadioStory.cs:264-284), so a prefix always reads
        /// the stale stage (0 on a fresh story) and the MainChoices gate can
        /// never fire on first call;
        /// (2) base.ContinueStory's ClearDecisions (GenericStory.cs:577) runs
        /// inside the original, so a prefix injection would be wiped in the
        /// same call. The full 5-patch wiring matrix is pinned below; any
        /// slot move changes runtime semantics silently (the preflight gate
        /// only checks non-null). Handler fragments catch copy-paste wiring
        /// of the wrong handler into a slot.
        /// </summary>
        [Fact]
        public void HarmonyPatchWiring_MatchesDesignContract()
        {
            if (!ModCompiler.HasModSource) return;

            // (target name, prefixMustBeNull, postfixMustBeNull, handlerFragment)
            var contract = new[]
            {
                Tuple.Create("RadioStory.ContinueStory", true, false, "injectT3PLL38"),
                Tuple.Create("Locale.RetrieveString", false, true, "overrideLocale"),
                Tuple.Create("Locale.RetrieveString(string,string)", false, true, "overrideLocale"),
                Tuple.Create("RadioStory.OnDecisionSelected", false, true, "handleDecisions"),
                Tuple.Create("RadioStory.OnStopped", true, false, "hideOnStop"),
            };

            string installerPath = ModCompiler.EnumerateSources()
                .FirstOrDefault(p => Path.GetFileName(p) == "HarmonyPatchInstaller.cs");
            Assert.True(installerPath != null,
                "HarmonyPatchInstaller.cs not found — the wiring contract is unverifiable");

            var root = CSharpSyntaxTree.ParseText(
                File.ReadAllText(installerPath), path: installerPath).GetRoot();
            var creations = root.DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Where(c => c.Type.ToString() == "PatchTarget")
                .ToList();

            var problems = new List<string>();
            foreach (var row in contract)
            {
                var matches = creations
                    .Where(c => c.ArgumentList != null
                        && c.ArgumentList.Arguments.Count == 4
                        && c.ArgumentList.Arguments[3].Expression is LiteralExpressionSyntax lit
                        && lit.IsKind(SyntaxKind.StringLiteralExpression)
                        && lit.Token.ValueText == row.Item1)
                    .ToList();
                if (matches.Count != 1)
                {
                    problems.Add($"target '{row.Item1}': expected exactly 1 PatchTarget creation, found {matches.Count}");
                    continue;
                }

                var args = matches[0].ArgumentList.Arguments;
                string loc = $"{Path.GetFileName(installerPath)}:{matches[0].GetLocation().GetLineSpan().StartLinePosition.Line + 1}";
                bool prefixNull = IsNullLiteral(args[1].Expression);
                bool postfixNull = IsNullLiteral(args[2].Expression);
                if (prefixNull != row.Item2)
                    problems.Add($"{loc}: '{row.Item1}' prefix slot must be "
                        + (row.Item2 ? "null" : "wired")
                        + " — a ContinueStory prefix reads RadioStory.stage before the original computes it from tags");
                if (postfixNull != row.Item3)
                    problems.Add($"{loc}: '{row.Item1}' postfix slot must be "
                        + (row.Item3 ? "null" : "wired"));
                if (!prefixNull && !args[1].ToString().Contains(row.Item4))
                    problems.Add($"{loc}: '{row.Item1}' prefix slot does not reference '{row.Item4}' — wrong handler wired");
                if (!postfixNull && !args[2].ToString().Contains(row.Item4))
                    problems.Add($"{loc}: '{row.Item1}' postfix slot does not reference '{row.Item4}' — wrong handler wired");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} Harmony wiring violation(s) — patch-slot moves change runtime semantics silently:\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Sextant overlay split gate (SRP): the ~290-line Build method must
        /// stay decomposed into an ocular builder and a columns builder.
        /// Pins the split contract, not bodies: both builder types exist with
        /// public static entry points, and SextantOverlay.Build delegates to
        /// both. A re-merge of the builders back into Build fails here.
        /// </summary>
        [Fact]
        public void SextantOverlaySplit_OcularAndColumns()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            var roots = new List<CompilationUnitSyntax>();
            foreach (var path in ModCompiler.EnumerateSources())
            {
                roots.Add((CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot());
            }

            System.Func<string, TypeDeclarationSyntax> findType = name =>
                roots.SelectMany(r => r.DescendantNodes().OfType<TypeDeclarationSyntax>())
                    .FirstOrDefault(t => t.Identifier.Text == name);

            // Builder types exist with public static entry points.
            foreach (var builder in new[] { "SextantOcularBuilder", "SextantColumnsBuilder" })
            {
                var decl = findType(builder);
                if (decl == null)
                {
                    problems.Add($"type '{builder}' not found in Source/ — overlay split regressed");
                    continue;
                }
                bool hasEntry = decl.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword)));
                if (!hasEntry)
                    problems.Add($"type '{builder}' exposes no public static entry point");
            }

            // SextantOverlay.Build delegates to both builders.
            var overlay = findType("SextantOverlay");
            if (overlay == null)
            {
                problems.Add("type 'SextantOverlay' not found in Source/");
            }
            else
            {
                var build = overlay.Members.OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault(m => m.Identifier.Text == "Build");
                if (build == null)
                {
                    problems.Add("SextantOverlay.Build not found");
                }
                else
                {
                    string body = build.ToString();
                    if (!body.Contains("SextantOcularBuilder."))
                        problems.Add("SextantOverlay.Build does not delegate to SextantOcularBuilder");
                    if (!body.Contains("SextantColumnsBuilder."))
                        problems.Add("SextantOverlay.Build does not delegate to SextantColumnsBuilder");
                }
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} overlay split violation(s) — the builders must stay split out of Build:\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Flare-driver context gate (SRP): LighthouseFlareDriver.Update must
        /// take a single FlareFrame parameter instead of a sprawling parameter
        /// list. A bundled frame object keeps the per-frame call-site contract
        /// stable while the frame contents evolve freely; parameter regrowth
        /// fails here.
        /// </summary>
        [Fact]
        public void FlareDriverUpdate_TakesFlareFrame()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax driver = null;
            TypeDeclarationSyntax frame = null;
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "LighthouseFlareDriver")
                        driver = type;
                    if (type.Identifier.Text == "FlareFrame")
                        frame = type;
                }
            }

            if (driver == null)
            {
                problems.Add("type 'LighthouseFlareDriver' not found in Source/");
            }
            else
            {
                var update = driver.Members.OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault(m => m.Identifier.Text == "Update");
                if (update == null)
                {
                    problems.Add("LighthouseFlareDriver.Update not found");
                }
                else
                {
                    var pars = update.ParameterList.Parameters;
                    bool singleFrame = pars.Count == 1 && pars[0].Type != null
                        && pars[0].Type.ToString() == "FlareFrame";
                    if (!singleFrame)
                        problems.Add($"LighthouseFlareDriver.Update takes {pars.Count} parameter(s) "
                            + "instead of a single FlareFrame — bundle the frame, do not sprawl");
                }
            }

            if (frame == null)
                problems.Add("type 'FlareFrame' not found in Source/");

            Assert.True(problems.Count == 0,
                $"{problems.Count} flare-driver context violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Marker backdrop art gate (SRP): the procedural pixel math behind
        /// NavMapMarkerBase.GetBackdropSprite must live in MarkerArtPixels as
        /// pure Color32[] computation (unit-tested offline in
        /// MarkerArtPixelsTests), with GetBackdropSprite as a thin caching
        /// wrapper that invokes it. Pins the extraction AND the wiring: a
        /// re-inline of the pixel loop, or a wrapper that stops calling the
        /// pure function, fails here. The TMP font fallback chain stays in
        /// the base — it needs game TMP assets and is honestly untestable
        /// offline, so this gate deliberately does not cover it.
        /// </summary>
        [Fact]
        public void MarkerArtPixels_UsedByBackdropSprite()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax pixels = null;
            MethodDeclarationSyntax backdrop = null;
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "MarkerArtPixels")
                        pixels = type;
                    if (type.Identifier.Text == "NavMapMarkerBase")
                    {
                        backdrop = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "GetBackdropSprite");
                    }
                }
            }

            if (pixels == null)
            {
                problems.Add("type 'MarkerArtPixels' not found in Source/ — backdrop pixel math regressed into the base");
            }
            else
            {
                bool rendersPixels = pixels.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "RenderBackdropPixels"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword))
                        && m.ReturnType != null
                        && m.ReturnType.ToString() == "Color32[]");
                if (!rendersPixels)
                    problems.Add("MarkerArtPixels.RenderBackdropPixels public static Color32[] entry point not found");
            }

            if (backdrop == null)
            {
                problems.Add("NavMapMarkerBase.GetBackdropSprite not found");
            }
            else
            {
                bool invokesPure = backdrop.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                            && bare.Identifier.Text == "RenderBackdropPixels")
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == "RenderBackdropPixels"));
                if (!invokesPure)
                    problems.Add("NavMapMarkerBase.GetBackdropSprite no longer invokes RenderBackdropPixels — "
                        + "the tested pure function is bypassed (or the pixel loop was re-inlined)");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} marker-art violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Radio power bus gate (SRP): the process-wide on/off + ground state
        /// (read from the audio thread) must live in RadioPowerBus, not as
        /// statics on the ToggleHandler MonoBehaviour widget. Pins the type
        /// with its full public surface, the absence of the state fields in
        /// ToggleHandler, and that every call site goes through the bus —
        /// a partial migration (or a creep-back) fails here.
        /// </summary>
        [Fact]
        public void RadioPowerBus_OwnsRadioState()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax bus = null;
            TypeDeclarationSyntax toggle = null;
            var roots = new List<Tuple<string, CompilationUnitSyntax>>();
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                roots.Add(Tuple.Create(path, root));
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "RadioPowerBus")
                        bus = type;
                    if (type.Identifier.Text == "ToggleHandler")
                        toggle = type;
                }
            }

            if (bus == null)
            {
                problems.Add("type 'RadioPowerBus' not found in Source/ — radio state regressed into the ToggleHandler widget");
            }
            else
            {
                bool hasOn = bus.Members.OfType<PropertyDeclarationSyntax>()
                    .Any(p => p.Identifier.Text == "IsRadioOn"
                        && p.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && p.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword)));
                bool hasInteractable = bus.Members.OfType<PropertyDeclarationSyntax>()
                    .Any(p => p.Identifier.Text == "IsInteractable"
                        && p.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && p.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword)));
                bool hasGround = bus.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "SetGroundActive"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword)));
                bool hasRadio = bus.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "SetRadioOn"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword)));
                bool hasReset = bus.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "Reset"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword)));
                if (!hasOn) problems.Add("RadioPowerBus.IsRadioOn public static property not found");
                if (!hasInteractable) problems.Add("RadioPowerBus.IsInteractable public static property not found");
                if (!hasGround) problems.Add("RadioPowerBus.SetGroundActive public static method not found");
                if (!hasRadio) problems.Add("RadioPowerBus.SetRadioOn public static method not found");
                if (!hasReset) problems.Add("RadioPowerBus.Reset public static method not found");
            }

            if (toggle == null)
            {
                problems.Add("type 'ToggleHandler' not found in Source/");
            }
            else
            {
                foreach (var field in toggle.Members.OfType<FieldDeclarationSyntax>())
                {
                    if (!field.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword))) continue;
                    foreach (var v in field.Declaration.Variables)
                    {
                        string name = v.Identifier.Text;
                        if (name == "isRadioOn" || name == "isInteractable" || name == "groundActive")
                            problems.Add($"ToggleHandler still declares static '{name}' — radio state crept back into the widget");
                    }
                }
            }

            foreach (var root in roots)
            {
                foreach (var access in root.Item2.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
                {
                    if (!(access.Expression is IdentifierNameSyntax recv)
                        || recv.Identifier.Text != "ToggleHandler") continue;
                    string member = access.Name.Identifier.Text;
                    if (member == "IsRadioOn" || member == "IsInteractable" || member == "SetGroundActive")
                        problems.Add($"{Path.GetFileName(root.Item1)}: call site still uses ToggleHandler.{member} instead of RadioPowerBus.{member}");
                }
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} radio-bus violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Game type-cache gate (SRP/P5.1): assembly-qualified Type resolution
        /// must live in GameTypeCache (cached, probe-list, offline-tested in
        /// GameTypeCacheTests), not duplicated as RadioStoryIntegration.ResolveType
        /// (uncached) and SkyVisibilityResolver.typeCache (single-assembly).
        /// Pins the type surface, the delegation of both call sites, and that
        /// no raw Type.GetType lookup remains anywhere else in Source/.
        /// </summary>
        [Fact]
        public void GameTypeCache_OwnsTypeResolution()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax cache = null;
            MethodDeclarationSyntax resolveType = null;
            TypeDeclarationSyntax sky = null;
            var roots = new List<Tuple<string, CompilationUnitSyntax>>();
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                roots.Add(Tuple.Create(path, root));
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "GameTypeCache")
                        cache = type;
                    if (type.Identifier.Text == "RadioStoryIntegration")
                    {
                        resolveType = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "ResolveType");
                    }
                    if (type.Identifier.Text == "SkyVisibilityResolver")
                        sky = type;
                }
            }

            if (cache == null)
            {
                problems.Add("type 'GameTypeCache' not found in Source/ — type resolution is duplicated again");
            }
            else
            {
                bool hasDefault = cache.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "Resolve"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword))
                        && m.ParameterList.Parameters.Count == 1);
                bool hasProbes = cache.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "Resolve"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword))
                        && m.ParameterList.Parameters.Count == 2);
                bool hasClear = cache.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "Clear"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword)));
                if (!hasDefault) problems.Add("GameTypeCache.Resolve(string) default-probe overload not found");
                if (!hasProbes) problems.Add("GameTypeCache.Resolve(string, probes) overload not found");
                if (!hasClear) problems.Add("GameTypeCache.Clear public static method not found");
            }

            if (resolveType == null)
            {
                problems.Add("RadioStoryIntegration.ResolveType not found");
            }
            else
            {
                bool delegates = resolveType.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => inv.Expression is MemberAccessExpressionSyntax qualified
                        && qualified.Expression is IdentifierNameSyntax recv
                        && recv.Identifier.Text == "GameTypeCache"
                        && qualified.Name is IdentifierNameSyntax qname
                        && qname.Identifier.Text == "Resolve");
                if (!delegates)
                    problems.Add("RadioStoryIntegration.ResolveType no longer delegates to GameTypeCache.Resolve — "
                        + "the uncached duplicate is back");
            }

            if (sky == null)
            {
                problems.Add("type 'SkyVisibilityResolver' not found in Source/");
            }
            else
            {
                foreach (var field in sky.Members.OfType<FieldDeclarationSyntax>())
                {
                    foreach (var v in field.Declaration.Variables)
                    {
                        if (v.Identifier.Text == "typeCache")
                            problems.Add("SkyVisibilityResolver still declares 'typeCache' — the per-site duplicate is back");
                    }
                }
            }

            foreach (var root in roots)
            {
                if (Path.GetFileName(root.Item1) == "GameTypeCache.cs") continue;
                foreach (var inv in root.Item2.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (inv.Expression is MemberAccessExpressionSyntax access
                        && access.Expression is IdentifierNameSyntax recv
                        && recv.Identifier.Text == "Type"
                        && access.Name.Identifier.Text == "GetType")
                    {
                        problems.Add($"{Path.GetFileName(root.Item1)}: raw Type.GetType outside GameTypeCache — "
                            + "route it through the cache");
                    }
                }
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} type-cache violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Course-sampler gate (SRP): the course/speed-over-ground track math
        /// (10 s window, transfer threshold, anchored logic) must live in
        /// CourseSampler as pure computation (unit-tested offline in
        /// CourseSamplerTests), with SextantWorksheet.UpdateCourseSampler as
        /// a thin throttle + provider + clock adapter that feeds it. Pins the
        /// type surface, the absence of the track state in the worksheet, and
        /// the wiring — a re-inline of the window math fails here.
        /// </summary>
        [Fact]
        public void CourseSampler_OwnsTrackMath()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax sampler = null;
            TypeDeclarationSyntax worksheet = null;
            MethodDeclarationSyntax update = null;
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "CourseSampler")
                        sampler = type;
                    if (type.Identifier.Text == "SextantWorksheet")
                    {
                        worksheet = type;
                        update = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "UpdateCourseSampler");
                    }
                }
            }

            if (sampler == null)
            {
                problems.Add("type 'CourseSampler' not found in Source/ — track math regressed into the worksheet");
            }
            else
            {
                bool addsSample = sampler.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "AddSample"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && m.ParameterList.Parameters.Count == 3);
                bool hasCourse = sampler.Members.OfType<PropertyDeclarationSyntax>()
                    .Any(p => p.Identifier.Text == "CourseDeg"
                        && p.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword)));
                bool hasSpeed = sampler.Members.OfType<PropertyDeclarationSyntax>()
                    .Any(p => p.Identifier.Text == "SpeedKn"
                        && p.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword)));
                bool hasReset = sampler.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "Reset"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword)));
                if (!addsSample) problems.Add("CourseSampler.AddSample(utc, lat, lon) public entry point not found");
                if (!hasCourse) problems.Add("CourseSampler.CourseDeg public property not found");
                if (!hasSpeed) problems.Add("CourseSampler.SpeedKn public property not found");
                if (!hasReset) problems.Add("CourseSampler.Reset public method not found");
            }

            if (worksheet == null)
            {
                problems.Add("type 'SextantWorksheet' not found in Source/");
            }
            else
            {
                foreach (var field in worksheet.Members.OfType<FieldDeclarationSyntax>())
                {
                    foreach (var v in field.Declaration.Variables)
                    {
                        string name = v.Identifier.Text;
                        if (name == "trackSamples" || name == "smoothedCourseDeg" || name == "smoothedSpeedKn")
                            problems.Add($"SextantWorksheet still declares '{name}' — track math crept back into the worksheet");
                    }
                }
                bool keepsTrackSample = worksheet.DescendantNodes().OfType<StructDeclarationSyntax>()
                    .Any(s => s.Identifier.Text == "TrackSample");
                if (keepsTrackSample)
                    problems.Add("SextantWorksheet still declares struct TrackSample — the sample type belongs to CourseSampler");
            }

            if (update == null)
            {
                problems.Add("SextantWorksheet.UpdateCourseSampler not found");
            }
            else
            {
                bool feedsSampler = update.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                            && bare.Identifier.Text == "AddSample")
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == "AddSample"));
                if (!feedsSampler)
                    problems.Add("SextantWorksheet.UpdateCourseSampler no longer feeds CourseSampler.AddSample — "
                        + "the tested pure function is bypassed (or the window math was re-inlined)");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} course-sampler violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Station-gain gate (SRP): the selectivity curve and BFO beat clamp
        /// must live in StationGainModel as pure functions (tested in
        /// StationGainModelTests), with SonneController.UpdateStationGains as
        /// the array-management caller and ShieldTests calling production
        /// instead of duplicating the formula. A re-inline (or a new local
        /// copy) fails here.
        /// </summary>
        [Fact]
        public void StationGain_OwnedByModel()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax model = null;
            TypeDeclarationSyntax controller = null;
            TypeDeclarationSyntax shield = null;
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "StationGainModel")
                        model = type;
                    if (type.Identifier.Text == "SonneController")
                        controller = type;
                }
            }
            foreach (var path in ModCompiler.EnumerateTests())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "ShieldGainTests")
                        shield = type;
                }
            }

            if (model == null)
            {
                problems.Add("type 'StationGainModel' not found in Source/ — gain math regressed into the controller");
            }
            else
            {
                bool hasGain = model.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "ComputeGain"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword))
                        && m.ParameterList.Parameters.Count == 3);
                bool hasBeat = model.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "ComputeBeatFrequency"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword))
                        && m.ParameterList.Parameters.Count == 1);
                if (!hasGain) problems.Add("StationGainModel.ComputeGain(f, f0, qf) public static entry point not found");
                if (!hasBeat) problems.Add("StationGainModel.ComputeBeatFrequency(offset) public static entry point not found");
            }

            if (controller == null)
            {
                problems.Add("type 'SonneController' not found in Source/");
            }
            else
            {
                bool inlinesSqrt = controller.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => inv.Expression is MemberAccessExpressionSyntax access
                        && access.Name.Identifier.Text == "Sqrt");
                if (inlinesSqrt)
                    problems.Add("SonneController still contains a Sqrt gain formula — route it through StationGainModel.ComputeGain");

                bool invokesModel = controller.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => inv.Expression is MemberAccessExpressionSyntax qualified
                        && qualified.Expression is IdentifierNameSyntax recv
                        && recv.Identifier.Text == "StationGainModel"
                        && qualified.Name is IdentifierNameSyntax qname
                        && qname.Identifier.Text == "ComputeGain");
                if (!invokesModel)
                    problems.Add("SonneController never invokes StationGainModel.ComputeGain — the tested pure function is bypassed");
            }

            if (shield == null)
            {
                problems.Add("type 'ShieldGainTests' not found in Tests/");
            }
            else
            {
                bool keepsLocalCopy = shield.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "Gain");
                if (keepsLocalCopy)
                    problems.Add("ShieldGainTests still declares a local Gain helper — call StationGainModel instead of duplicating the formula");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} station-gain violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Signal-transcriber gate (SRP): the element -&gt; glyph mapping must
        /// live in SignalTranscriber as a pure function (tested in
        /// SignalTranscriberTests), with SignalTranscript.AddElement as the
        /// caller. A re-inline of the switch fails here.
        /// </summary>
        [Fact]
        public void SignalTranscriber_OwnsElementMapping()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax transcriber = null;
            MethodDeclarationSyntax add = null;
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "SignalTranscriber")
                        transcriber = type;
                    if (type.Identifier.Text == "SignalTranscript")
                    {
                        add = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "AddElement");
                    }
                }
            }

            if (transcriber == null)
            {
                problems.Add("type 'SignalTranscriber' not found in Source/ — element mapping regressed into the transcript");
            }
            else
            {
                bool maps = transcriber.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "MapElement"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword))
                        && m.ParameterList.Parameters.Count == 4);
                if (!maps) problems.Add("SignalTranscriber.MapElement(elem, isCallsign, out type, out glyph) public static entry point not found");
            }

            if (add == null)
            {
                problems.Add("SignalTranscript.AddElement not found");
            }
            else
            {
                bool keepsSwitch = add.DescendantNodes()
                    .OfType<SwitchStatementSyntax>()
                    .Any();
                if (keepsSwitch)
                    problems.Add("SignalTranscript.AddElement still contains a switch — the mapping belongs to SignalTranscriber");

                bool invokesTranscriber = add.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                            && bare.Identifier.Text == "MapElement")
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == "MapElement"));
                if (!invokesTranscriber)
                    problems.Add("SignalTranscript.AddElement never invokes MapElement — the tested pure function is bypassed");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} signal-transcriber violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Bearing-diagnostics gate (SRP): the [RadioNav][BEARING] report
        /// lines must be built by BearingDiagnostics as pure strings
        /// (tested in BearingDiagnosticsTests), with
        /// SonneController.LogBearingDiagnostics as the emit-once-per-
        /// position gate that only logs them. A re-inline of the format
        /// strings fails here.
        /// </summary>
        [Fact]
        public void BearingDiagnostics_OwnsReportFormat()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax diagnostics = null;
            MethodDeclarationSyntax log = null;
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "BearingDiagnostics")
                        diagnostics = type;
                    if (type.Identifier.Text == "SonneController")
                    {
                        log = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "LogBearingDiagnostics");
                    }
                }
            }

            if (diagnostics == null)
            {
                problems.Add("type 'BearingDiagnostics' not found in Source/ — report format regressed into the controller");
            }
            else
            {
                bool builds = diagnostics.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "BuildLines"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword))
                        && m.ParameterList.Parameters.Count == 4);
                if (!builds) problems.Add("BearingDiagnostics.BuildLines(lat, lon, stations, states) public static entry point not found");
            }

            if (log == null)
            {
                problems.Add("SonneController.LogBearingDiagnostics not found");
            }
            else
            {
                bool keepsFormat = log.DescendantNodes()
                    .OfType<LiteralExpressionSyntax>()
                    .Any(lit => lit.Token.ValueText != null
                        && lit.Token.ValueText.Contains("[RadioNav][BEARING]"));
                if (keepsFormat)
                    problems.Add("SonneController.LogBearingDiagnostics still contains [BEARING] format strings — the report belongs to BearingDiagnostics");

                bool invokesDiagnostics = log.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                            && bare.Identifier.Text == "BuildLines")
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == "BuildLines"));
                if (!invokesDiagnostics)
                    problems.Add("SonneController.LogBearingDiagnostics never invokes BuildLines — the tested pure function is bypassed");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} bearing-diagnostics violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Station-navigator gate (SRP): the per-station navigation update
        /// (map bearings, in-beam/sector, bearing-change-gated table
        /// lookup, scheduler pattern feed) must live in StationNavigator
        /// (tested offline in StationNavigatorTests), with
        /// SonneController.UpdateStationNavigation as the fleet loop.
        /// A re-inline of the table lookup fails here.
        /// </summary>
        [Fact]
        public void StationNavigator_OwnsStationUpdate()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax navigator = null;
            MethodDeclarationSyntax update = null;
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "StationNavigator")
                        navigator = type;
                    if (type.Identifier.Text == "SonneController")
                    {
                        update = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "UpdateStationNavigation");
                    }
                }
            }

            if (navigator == null)
            {
                problems.Add("type 'StationNavigator' not found in Source/ — station update regressed into the controller");
            }
            else
            {
                bool updates = navigator.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "UpdateStation"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword))
                        && m.ParameterList.Parameters.Count == 5);
                if (!updates) problems.Add("StationNavigator.UpdateStation(state, station, lat, lon, scheduler) public static entry point not found");
            }

            if (update == null)
            {
                problems.Add("SonneController.UpdateStationNavigation not found");
            }
            else
            {
                bool keepsLookup = update.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                            && bare.Identifier.Text == "TryGetNavigationPattern")
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == "TryGetNavigationPattern"));
                if (keepsLookup)
                    problems.Add("SonneController.UpdateStationNavigation still calls TryGetNavigationPattern — the lookup belongs to StationNavigator");

                bool invokesNavigator = update.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                            && bare.Identifier.Text == "UpdateStation")
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == "UpdateStation"));
                if (!invokesNavigator)
                    problems.Add("SonneController.UpdateStationNavigation never invokes StationNavigator.UpdateStation — the tested pure function is bypassed");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} station-navigator violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Signal-transcript gate (SRP): the live transcript state (entries,
        /// morse buffer, decoded callsign) and its behavior must live in
        /// SignalTranscript (tested offline in SignalTranscriptTests), with
        /// SonneController keeping only the dominant-station gate and
        /// delegating. A re-inline of the buffers fails here.
        /// </summary>
        [Fact]
        public void SignalTranscript_OwnsBuffers()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax transcript = null;
            TypeDeclarationSyntax controller = null;
            MethodDeclarationSyntax completed = null;
            MethodDeclarationSyntax finished = null;
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "SignalTranscript")
                        transcript = type;
                    if (type.Identifier.Text == "SonneController")
                    {
                        controller = type;
                        completed = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "OnStationElementCompleted");
                        finished = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "OnStationSequenceFinished");
                    }
                }
            }

            if (transcript == null)
            {
                problems.Add("type 'SignalTranscript' not found in Source/ — transcript state regressed into the controller");
            }
            else
            {
                bool adds = transcript.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "AddElement"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword)));
                bool finishes = transcript.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "FinishSequence"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword)));
                bool clears = transcript.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "Clear"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword)));
                bool countsDots = transcript.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "CountDots"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword)));
                if (!adds) problems.Add("SignalTranscript.AddElement public entry point not found");
                if (!finishes) problems.Add("SignalTranscript.FinishSequence public entry point not found");
                if (!clears) problems.Add("SignalTranscript.Clear public entry point not found");
                if (!countsDots) problems.Add("SignalTranscript.CountDots public entry point not found");
            }

            if (controller == null)
            {
                problems.Add("type 'SonneController' not found in Source/");
            }
            else
            {
                foreach (var field in controller.Members.OfType<FieldDeclarationSyntax>())
                {
                    foreach (var v in field.Declaration.Variables)
                    {
                        string name = v.Identifier.Text;
                        if (name == "signalEntries" || name == "morseCharCount"
                            || name == "morseDecoderBuffer" || name == "decodedText")
                            problems.Add($"SonneController still declares '{name}' — transcript state crept back into the controller");
                    }
                }
            }

            foreach (var method in new[] { completed, finished })
            {
                if (method == null)
                {
                    problems.Add("SonneController element/sequence handler not found");
                    continue;
                }
                bool delegates = method.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                            && (bare.Identifier.Text == "AddElement" || bare.Identifier.Text == "FinishSequence"))
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && (qname.Identifier.Text == "AddElement" || qname.Identifier.Text == "FinishSequence")));
                if (!delegates)
                    problems.Add($"SonneController.{method.Identifier.Text} never invokes the transcript — the tested class is bypassed");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} signal-transcript violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Station-fleet gate (SRP): station object construction + keyer
        /// event wiring must live in StationFleet (tested offline in
        /// StationFleetTests, including symmetric Unwire), with
        /// SonneController.Awake/OnDestroy delegating. A re-inline of the
        /// build loop or the unsubscribe block fails here.
        /// </summary>
        [Fact]
        public void StationFleet_OwnsStationWiring()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax fleet = null;
            MethodDeclarationSyntax awake = null;
            MethodDeclarationSyntax destroy = null;
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "StationFleet")
                        fleet = type;
                    if (type.Identifier.Text == "SonneController")
                    {
                        awake = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "Awake");
                        destroy = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "OnDestroy");
                    }
                }
            }

            if (fleet == null)
            {
                problems.Add("type 'StationFleet' not found in Source/ — station wiring regressed into the controller");
            }
            else
            {
                bool builds = fleet.Members.OfType<ConstructorDeclarationSyntax>().Any();
                bool unwires = fleet.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "Unwire"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword)));
                bool exposesStates = fleet.Members.OfType<PropertyDeclarationSyntax>()
                    .Any(p => p.Identifier.Text == "States");
                if (!builds) problems.Add("StationFleet constructor not found");
                if (!unwires) problems.Add("StationFleet.Unwire public method not found");
                if (!exposesStates) problems.Add("StationFleet.States property not found");
            }

            if (awake == null)
            {
                problems.Add("SonneController.Awake not found");
            }
            else
            {
                bool keepsBuildLoop = awake.DescendantNodes()
                    .OfType<ForStatementSyntax>()
                    .Any();
                if (keepsBuildLoop)
                    problems.Add("SonneController.Awake still contains a for loop — station construction belongs to StationFleet");
            }

            if (destroy == null)
            {
                problems.Add("SonneController.OnDestroy not found");
            }
            else
            {
                bool keepsUnsubscribeLoop = destroy.DescendantNodes()
                    .OfType<ForStatementSyntax>()
                    .Any();
                if (keepsUnsubscribeLoop)
                    problems.Add("SonneController.OnDestroy still contains a for loop — keyer unwiring belongs to StationFleet.Unwire");

                bool invokesUnwire = destroy.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                            && bare.Identifier.Text == "Unwire")
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == "Unwire"));
                if (!invokesUnwire)
                    problems.Add("SonneController.OnDestroy never invokes Unwire — keyer handlers leak on teardown");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} station-fleet violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Station-diagnostics gate (SRP): the snapshot struct must be a
        /// top-level type (not nested in the controller) and its assembly
        /// must live in StationDiagnosticsReport as a pure function (tested
        /// in StationDiagnosticsReportTests), with
        /// SonneController.GetStationDiagnostics keeping only validation,
        /// callsign resolve and the elapsed clock. A re-nesting or
        /// re-inline fails here.
        /// </summary>
        [Fact]
        public void StationDiagnostics_OwnsAssembly()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax topLevel = null;
            TypeDeclarationSyntax report = null;
            TypeDeclarationSyntax controller = null;
            MethodDeclarationSyntax getter = null;
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "StationDiagnostics"
                        && type.Parent is NamespaceDeclarationSyntax)
                        topLevel = type;
                    if (type.Identifier.Text == "StationDiagnosticsReport")
                        report = type;
                    if (type.Identifier.Text == "SonneController")
                    {
                        controller = type;
                        getter = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "GetStationDiagnostics");
                    }
                }
            }

            if (topLevel == null)
                problems.Add("top-level type 'StationDiagnostics' not found in Source/ — the snapshot is nested in the controller again");
            if (controller != null
                && controller.Members.OfType<TypeDeclarationSyntax>()
                    .Any(t => t.Identifier.Text == "StationDiagnostics"))
                problems.Add("SonneController still nests StationDiagnostics — keep the snapshot top-level");

            if (report == null)
            {
                problems.Add("type 'StationDiagnosticsReport' not found in Source/ — assembly regressed into the controller");
            }
            else
            {
                bool builds = report.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "Build"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword))
                        && m.ParameterList.Parameters.Count == 5);
                if (!builds) problems.Add("StationDiagnosticsReport.Build(station, callsign, state, phase, elapsed) public static entry point not found");
            }

            if (getter == null)
            {
                problems.Add("SonneController.GetStationDiagnostics not found");
            }
            else
            {
                bool invokesReport = getter.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                            && bare.Identifier.Text == "Build")
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == "Build"));
                if (!invokesReport)
                    problems.Add("SonneController.GetStationDiagnostics never invokes StationDiagnosticsReport.Build — the tested pure function is bypassed");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} station-diagnostics violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Spinner-scale gate (SRP/dedup): the 140° spinner range mapping
        /// must live in SpinnerScale as the single source of truth (tested
        /// in SpinnerScaleTests), with SonneController, InformationPanelHandler
        /// and AudioSynthesisEngine calling it. PanelStateStore keeps its own
        /// 140f storage-range clamps (persistence validation, not mapping).
        /// A new inline angle/140f division fails here.
        /// </summary>
        [Fact]
        public void SpinnerScale_OwnsAngleMapping()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax scale = null;
            var roots = new List<Tuple<string, CompilationUnitSyntax>>();
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                roots.Add(Tuple.Create(path, root));
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "SpinnerScale")
                        scale = type;
                }
            }

            if (scale == null)
            {
                problems.Add("type 'SpinnerScale' not found in Source/ — angle mapping is scattered again");
            }
            else
            {
                bool hasMax = scale.Members.OfType<FieldDeclarationSyntax>()
                    .Any(f => f.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && f.Modifiers.Any(x => x.IsKind(SyntaxKind.ConstKeyword))
                        && f.Declaration.Variables.Any(v => v.Identifier.Text == "MaxAngle"));
                bool hasUnit = scale.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "ToUnit"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword)));
                bool hasPercent = scale.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "ToPercent"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword)));
                if (!hasMax) problems.Add("SpinnerScale.MaxAngle public const not found");
                if (!hasUnit) problems.Add("SpinnerScale.ToUnit public static method not found");
                if (!hasPercent) problems.Add("SpinnerScale.ToPercent public static method not found");
            }

            foreach (var root in roots)
            {
                string file = Path.GetFileName(root.Item1);
                if (file == "SpinnerScale.cs" || file == "PanelStateStore.cs") continue;
                foreach (var lit in root.Item2.DescendantNodes().OfType<LiteralExpressionSyntax>())
                {
                    string token = lit.Token.Text;
                    if (token == "140f" || token == "140F" || token == "140.0f" || token == "140.0F")
                        problems.Add($"{file}: inline 140° literal outside SpinnerScale (PanelStateStore storage clamps excepted) — route through SpinnerScale");
                }
            }

            foreach (var root in roots)
            {
                string file = Path.GetFileName(root.Item1);
                bool invokes = root.Item2.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => inv.Expression is MemberAccessExpressionSyntax qualified
                        && qualified.Expression is IdentifierNameSyntax recv
                        && recv.Identifier.Text == "SpinnerScale");
                if ((file == "SonneController.cs" || file == "InformationPanelHandler.cs"
                        || file == "AudioSynthesisEngine.cs") && !invokes)
                    problems.Add($"{file} never invokes SpinnerScale — the tested mapping is bypassed");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} spinner-scale violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Position-feed gate (SRP): the significant-movement threshold and
        /// the 10 s position log line must live in PositionFeed as pure
        /// functions (tested in PositionFeedTests, InvariantCulture like
        /// BearingDiagnostics), with SonneController.Update keeping only
        /// the provider read and gating. A re-inline fails here.
        /// </summary>
        [Fact]
        public void PositionFeed_OwnsPollDecisions()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax feed = null;
            MethodDeclarationSyntax update = null;
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "PositionFeed")
                        feed = type;
                    if (type.Identifier.Text == "SonneController")
                    {
                        update = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "Update");
                    }
                }
            }

            if (feed == null)
            {
                problems.Add("type 'PositionFeed' not found in Source/ — poll decisions regressed into the controller");
            }
            else
            {
                bool moves = feed.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "HasMoved"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword)));
                bool formats = feed.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "FormatLogLine"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword)));
                if (!moves) problems.Add("PositionFeed.HasMoved public static method not found");
                if (!formats) problems.Add("PositionFeed.FormatLogLine public static method not found");
            }

            if (update == null)
            {
                problems.Add("SonneController.Update not found");
            }
            else
            {
                foreach (var lit in update.DescendantNodes().OfType<LiteralExpressionSyntax>())
                {
                    if (lit.Token.Text == "\"F5\"")
                        problems.Add("SonneController.Update still formats F5 coordinates inline — the log line belongs to PositionFeed (InvariantCulture)");
                    if (lit.Token.Text == "0.0001f")
                        problems.Add("SonneController.Update still hardcodes the 0.0001 movement threshold — it belongs to PositionFeed");
                }

                foreach (var name in new[] { "HasMoved", "FormatLogLine" })
                {
                    bool invokes = update.DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                                && bare.Identifier.Text == name)
                            || (inv.Expression is MemberAccessExpressionSyntax qualified
                                && qualified.Name is IdentifierNameSyntax qname
                                && qname.Identifier.Text == name));
                    if (!invokes)
                        problems.Add($"SonneController.Update never invokes {name} — the tested pure function is bypassed");
                }
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} position-feed violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Pattern-line gate (SRP): the per-station card line + mm:ss
        /// duration must live in PatternLineFormatter as pure functions
        /// (tested in PatternLineFormatterTests, InvariantCulture like
        /// BearingDiagnostics), with InformationPanelHandler delegating.
        /// A re-inline of the BFO/ToBoat format fails here.
        /// </summary>
        [Fact]
        public void PatternLine_OwnsLineFormat()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax formatter = null;
            MethodDeclarationSyntax build = null;
            TypeDeclarationSyntax panel = null;
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "PatternLineFormatter")
                        formatter = type;
                    if (type.Identifier.Text == "InformationPanelHandler")
                    {
                        panel = type;
                        build = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "BuildPatternLine");
                    }
                }
            }

            if (formatter == null)
            {
                problems.Add("type 'PatternLineFormatter' not found in Source/ — line format regressed into the panel");
            }
            else
            {
                bool builds = formatter.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "BuildLine"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword))
                        && m.ParameterList.Parameters.Count == 5);
                bool formats = formatter.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "FormatDuration"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword)));
                bool suffixes = formatter.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "StationSuffix"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword)));
                if (!builds) problems.Add("PatternLineFormatter.BuildLine(diag, index, domIdx, recvDots, recvDashes) public static entry point not found");
                if (!formats) problems.Add("PatternLineFormatter.FormatDuration public static method not found");
                if (!suffixes) problems.Add("PatternLineFormatter.StationSuffix public static method not found");
            }

            if (build == null)
            {
                problems.Add("InformationPanelHandler.BuildPatternLine not found");
            }
            else
            {
                foreach (var lit in build.DescendantNodes().OfType<LiteralExpressionSyntax>())
                {
                    string token = lit.Token.Text;
                    if (token.Contains("BFO:") || token.Contains("ToBoat:"))
                        problems.Add($"InformationPanelHandler.BuildPatternLine still contains {token} — the line format belongs to PatternLineFormatter");
                }

                bool invokes = build.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                            && bare.Identifier.Text == "BuildLine")
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == "BuildLine"));
                if (!invokes)
                    problems.Add("InformationPanelHandler.BuildPatternLine never invokes BuildLine — the tested pure function is bypassed");
            }

            if (panel != null
                && panel.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "FormatTime"))
                problems.Add("InformationPanelHandler still declares FormatTime — use PatternLineFormatter.FormatDuration");

            var stationText = panel?.Members.OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.Text == "UpdateStationText");
            if (stationText == null)
            {
                problems.Add("InformationPanelHandler.UpdateStationText not found");
            }
            else
            {
                bool invokesSuffix = stationText.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                            && bare.Identifier.Text == "StationSuffix")
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == "StationSuffix"));
                if (!invokesSuffix)
                    problems.Add("InformationPanelHandler.UpdateStationText never invokes StationSuffix — the tested pure function is bypassed");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} pattern-line violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Pattern-rows gate (SRP): the gain-gated row selection must live
        /// in PatternRowPacker as a pure function (tested in
        /// PatternRowPackerTests), with
        /// InformationPanelHandler.UpdatePatterns rendering only. A
        /// re-inline of the gain filter fails here.
        /// </summary>
        [Fact]
        public void PatternRows_OwnsPacking()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax packer = null;
            MethodDeclarationSyntax patterns = null;
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "PatternRowPacker")
                        packer = type;
                    if (type.Identifier.Text == "InformationPanelHandler")
                    {
                        patterns = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "UpdatePatterns");
                    }
                }
            }

            if (packer == null)
            {
                problems.Add("type 'PatternRowPacker' not found in Source/ — row selection regressed into the panel");
            }
            else
            {
                bool packs = packer.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "Pack"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword)));
                if (!packs) problems.Add("PatternRowPacker.Pack public static entry point not found");
            }

            if (patterns == null)
            {
                problems.Add("InformationPanelHandler.UpdatePatterns not found");
            }
            else
            {
                bool keepsFilter = patterns.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                            && bare.Identifier.Text == "IsNegligible")
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == "IsNegligible"));
                if (keepsFilter)
                    problems.Add("InformationPanelHandler.UpdatePatterns still filters by IsNegligible — row selection belongs to PatternRowPacker");

                bool invokesPack = patterns.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                            && bare.Identifier.Text == "Pack")
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == "Pack"));
                if (!invokesPack)
                    problems.Add("InformationPanelHandler.UpdatePatterns never invokes Pack — the tested pure function is bypassed");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} pattern-rows violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Status-lines gate (SRP): the Toggle/Ground/Zone label assembly
        /// must live in StatusLineText as pure functions (tested in
        /// StatusLineTextTests), with InformationPanelHandler delegating.
        /// A re-inline of the literals fails here. (Start() keeps its own
        /// initial label texts — creation, not update.)
        /// </summary>
        [Fact]
        public void StatusLines_OwnsOneLiners()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax text = null;
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "StatusLineText")
                        text = type;
                }
            }

            if (text == null)
            {
                problems.Add("type 'StatusLineText' not found in Source/ — label assembly regressed into the panel");
            }
            else
            {
                foreach (var name in new[] { "Toggle", "Ground", "Zone" })
                {
                    bool has = text.Members.OfType<MethodDeclarationSyntax>()
                        .Any(m => m.Identifier.Text == name
                            && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                            && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword)));
                    if (!has) problems.Add($"StatusLineText.{name} public static method not found");
                }
            }

            var expectedCall = new Dictionary<string, string>
            {
                { "OnToggleChanged", "Toggle" },
                { "OnGroundChanged", "Ground" },
                { "OnZoneChanged", "Zone" },
            };
            var expectedLiteral = new Dictionary<string, string>
            {
                { "OnToggleChanged", "Toggle: " },
                { "OnGroundChanged", "Ground: " },
                { "OnZoneChanged", "Zone: " },
            };
            foreach (var kv in expectedCall)
            {
                MethodDeclarationSyntax method = null;
                foreach (var path in ModCompiler.EnumerateSources())
                {
                    var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                        File.ReadAllText(path), path: path).GetRoot();
                    foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                    {
                        if (type.Identifier.Text != "InformationPanelHandler") continue;
                        method = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == kv.Key);
                    }
                }
                if (method == null)
                {
                    problems.Add($"InformationPanelHandler.{kv.Key} not found");
                    continue;
                }
                bool invokes = method.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                            && bare.Identifier.Text == kv.Value)
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == kv.Value));
                if (!invokes)
                    problems.Add($"InformationPanelHandler.{kv.Key} never invokes StatusLineText.{kv.Value} — the tested pure function is bypassed");
                string literal = expectedLiteral[kv.Key];
                bool inlines = method.DescendantNodes()
                    .OfType<LiteralExpressionSyntax>()
                    .Any(lit => lit.Token.Text.Contains(literal.Trim()));
                if (inlines)
                    problems.Add($"InformationPanelHandler.{kv.Key} still inlines '{literal.Trim()}' — label assembly belongs to StatusLineText");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} status-lines violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Signal-view gate (SRP): the signal-block state (text + dimmed
        /// flag) must live in SignalPanelView as a pure function (tested in
        /// SignalPanelViewTests), with
        /// InformationPanelHandler.UpdateSignalDisplay applying Text/color
        /// only. A re-inline of the placeholder fails here.
        /// </summary>
        [Fact]
        public void SignalView_OwnsBlockState()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax view = null;
            MethodDeclarationSyntax display = null;
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "SignalPanelView")
                        view = type;
                    if (type.Identifier.Text == "InformationPanelHandler")
                    {
                        display = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "UpdateSignalDisplay");
                    }
                }
            }

            if (view == null)
            {
                problems.Add("type 'SignalPanelView' not found in Source/ — block state regressed into the panel");
            }
            else
            {
                bool resolves = view.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "Resolve"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword)));
                if (!resolves) problems.Add("SignalPanelView.Resolve public static entry point not found");
            }

            if (display == null)
            {
                problems.Add("InformationPanelHandler.UpdateSignalDisplay not found");
            }
            else
            {
                bool keepsPlaceholder = display.DescendantNodes()
                    .OfType<LiteralExpressionSyntax>()
                    .Any(lit => lit.Token.Text.Contains("--- Signal: "));
                if (keepsPlaceholder)
                    problems.Add("InformationPanelHandler.UpdateSignalDisplay still contains the '--- Signal: ' placeholder — block state belongs to SignalPanelView");

                bool invokes = display.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                            && bare.Identifier.Text == "Resolve")
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == "Resolve"));
                if (!invokes)
                    problems.Add("InformationPanelHandler.UpdateSignalDisplay never invokes Resolve — the tested pure function is bypassed");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} signal-view violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Entity-rows gate (SRP): the 18-column Entities/Types layout must
        /// live in EntityTypeRowValues as a pure function (tested in
        /// EntityTypeRowValuesTests), with LighthousePlacer.CreateEntityType
        /// keeping buffer ownership (P5.1) and game row construction. A
        /// re-inline of the values.Add block fails here.
        /// </summary>
        [Fact]
        public void EntityRows_OwnsValues()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax rows = null;
            MethodDeclarationSyntax create = null;
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "EntityTypeRowValues")
                        rows = type;
                    if (type.Identifier.Text == "LighthousePlacer")
                    {
                        create = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "CreateEntityType");
                    }
                }
            }

            if (rows == null)
            {
                problems.Add("type 'EntityTypeRowValues' not found in Source/ — row layout regressed into the placer");
            }
            else
            {
                bool builds = rows.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "BuildValues"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword)));
                if (!builds) problems.Add("EntityTypeRowValues.BuildValues public static entry point not found");
            }

            if (create == null)
            {
                problems.Add("LighthousePlacer.CreateEntityType not found");
            }
            else
            {
                bool keepsAdds = create.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => inv.Expression is MemberAccessExpressionSyntax access
                        && access.Name.Identifier.Text == "Add");
                if (keepsAdds)
                    problems.Add("LighthousePlacer.CreateEntityType still calls values.Add — the layout belongs to EntityTypeRowValues");

                bool invokesBuild = create.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                            && bare.Identifier.Text == "BuildValues")
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == "BuildValues"));
                if (!invokesBuild)
                    problems.Add("LighthousePlacer.CreateEntityType never invokes BuildValues — the tested pure function is bypassed");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} entity-rows violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Frequency-text gate (SRP): the frequency/station header lines
        /// must live in FrequencyPanelText as pure functions (tested in
        /// FrequencyPanelTextTests), with
        /// InformationPanelHandler.OnFrequencyChanged delegating. A
        /// re-inline of the Band lookup fails here.
        /// </summary>
        [Fact]
        public void FrequencyText_OwnsHeaderLines()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax text = null;
            MethodDeclarationSyntax changed = null;
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "FrequencyPanelText")
                        text = type;
                    if (type.Identifier.Text == "InformationPanelHandler")
                    {
                        changed = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "OnFrequencyChanged");
                    }
                }
            }

            if (text == null)
            {
                problems.Add("type 'FrequencyPanelText' not found in Source/ — header lines regressed into the panel");
            }
            else
            {
                bool freqs = text.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "FreqLine"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword)));
                bool names = text.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "StationName"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword)));
                if (!freqs) problems.Add("FrequencyPanelText.FreqLine public static method not found");
                if (!names) problems.Add("FrequencyPanelText.StationName public static method not found");
            }

            if (changed == null)
            {
                problems.Add("InformationPanelHandler.OnFrequencyChanged not found");
            }
            else
            {
                bool keepsBandLookup = changed.DescendantNodes()
                    .OfType<MemberAccessExpressionSyntax>()
                    .Any(access => access.Name.Identifier.Text == "Bands");
                if (keepsBandLookup)
                    problems.Add("InformationPanelHandler.OnFrequencyChanged still reads DialConfig.Bands — the lookup belongs to FrequencyPanelText");

                foreach (var name in new[] { "FreqLine", "StationName" })
                {
                    bool invokes = changed.DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                                && bare.Identifier.Text == name)
                            || (inv.Expression is MemberAccessExpressionSyntax qualified
                                && qualified.Name is IdentifierNameSyntax qname
                                && qname.Identifier.Text == name));
                    if (!invokes)
                        problems.Add($"InformationPanelHandler.OnFrequencyChanged never invokes {name} — the tested pure function is bypassed");
                }
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} frequency-text violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Decoded-display gate (SRP): the decoded text selection + log-once
        /// latch must live in DecodedDisplayTracker (tested in
        /// DecodedDisplayTrackerTests), with
        /// InformationPanelHandler.UpdateSignalDisplay delegating. A
        /// re-inline of the latch field fails here.
        /// </summary>
        [Fact]
        public void DecodedDisplay_OwnsLatch()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax tracker = null;
            TypeDeclarationSyntax panel = null;
            MethodDeclarationSyntax display = null;
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "DecodedDisplayTracker")
                        tracker = type;
                    if (type.Identifier.Text == "InformationPanelHandler")
                    {
                        panel = type;
                        display = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "UpdateSignalDisplay");
                    }
                }
            }

            if (tracker == null)
            {
                problems.Add("type 'DecodedDisplayTracker' not found in Source/ — latch regressed into the panel");
            }
            else
            {
                foreach (var name in new[] { "DisplayText", "ShouldLog", "MarkLogged", "Reset" })
                {
                    bool has = tracker.Members.OfType<MethodDeclarationSyntax>()
                        .Any(m => m.Identifier.Text == name
                            && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword)));
                    if (!has) problems.Add($"DecodedDisplayTracker.{name} public method not found");
                }
            }

            if (panel != null)
            {
                foreach (var field in panel.Members.OfType<FieldDeclarationSyntax>())
                {
                    foreach (var v in field.Declaration.Variables)
                    {
                        if (v.Identifier.Text == "lastLoggedDecoded")
                            problems.Add("InformationPanelHandler still declares 'lastLoggedDecoded' — the latch belongs to DecodedDisplayTracker");
                    }
                }
            }
            else
            {
                problems.Add("type 'InformationPanelHandler' not found in Source/");
            }

            if (display == null)
            {
                problems.Add("InformationPanelHandler.UpdateSignalDisplay not found");
            }
            else
            {
                foreach (var name in new[] { "DisplayText", "ShouldLog" })
                {
                    bool invokes = display.DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                                && bare.Identifier.Text == name)
                            || (inv.Expression is MemberAccessExpressionSyntax qualified
                                && qualified.Name is IdentifierNameSyntax qname
                                && qname.Identifier.Text == name));
                    if (!invokes)
                        problems.Add($"InformationPanelHandler.UpdateSignalDisplay never invokes {name} — the tested class is bypassed");
                }
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} decoded-display violation(s):\n"
                + string.Join("\n", problems));
        }

        private static bool IsNullLiteral(ExpressionSyntax expr)
        {
            return expr is LiteralExpressionSyntax lit
                && lit.IsKind(SyntaxKind.NullLiteralExpression);
        }

        /// <summary>'x.Event' / 'arr[i].Event' -> 'Event'; bare identifier -> null.</summary>
        private static string EventNameOf(ExpressionSyntax left)
        {
            if (left is MemberAccessExpressionSyntax member && member.Name is IdentifierNameSyntax id)
                return id.Identifier.Text;
            return null;
        }

        /// <summary>
        /// True when the right side can be a subscription handler: a method
        /// group (identifier or member access, no invocation), an element
        /// access (handlers[i]), or a lambda. Literals/binary/invocation
        /// expressions are compound arithmetic or string concatenation.
        /// </summary>
        private static bool IsHandlerExpression(ExpressionSyntax right)
        {
            switch (right)
            {
                case IdentifierNameSyntax _:
                case MemberAccessExpressionSyntax m when !(m.Name is GenericNameSyntax):
                case ElementAccessExpressionSyntax _:
                case ParenthesizedLambdaExpressionSyntax _:
                case SimpleLambdaExpressionSyntax _:
                case MemberBindingExpressionSyntax _:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// McCabe cyclomatic complexity: 1 + number of decision points. Decision points:
        /// if/else-if, switch case, for/foreach/while/do, conditional ?:, &&, ||, catch,
        /// and the null-coalescing/conditional-access operators. Nested lambdas/local
        /// functions are counted as separate methods (their bodies are not attributed to
        /// the enclosing method).
        /// </summary>
        private static int ComputeCyclomaticComplexity(MethodDeclarationSyntax method)
        {
            int complexity = 1;
            foreach (var node in method.DescendantNodes())
            {
                if (node is IfStatementSyntax) complexity++;
                else if (node is SwitchSectionSyntax) complexity++;
                else if (node is ForStatementSyntax || node is ForEachStatementSyntax
                         || node is WhileStatementSyntax || node is DoStatementSyntax) complexity++;
                else if (node is ConditionalExpressionSyntax) complexity++;
                else if (node is CatchClauseSyntax) complexity++;
                else if (node is BinaryExpressionSyntax bin &&
                         (bin.IsKind(SyntaxKind.LogicalAndExpression)
                          || bin.IsKind(SyntaxKind.LogicalOrExpression))) complexity++;
                else if (node is BinaryExpressionSyntax bin2 &&
                         (bin2.IsKind(SyntaxKind.CoalesceExpression))) complexity++;
                else if (node is ConditionalAccessExpressionSyntax) complexity++;
            }
            return complexity;
        }
    }
}
