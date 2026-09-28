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

            // Incremental: the comparison is deterministic in (game sources,
            // Managed DLLs, mod sources, this tool's sources). Re-run the ~205 s
            // old semantic pass only when those inputs changed since the last
            // verified pass; otherwise the previous 0-diffs verdict still holds.
            if (CleanReferenceCache.IsSeedQualityCurrent("newvsold"))
                return;

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

            CleanReferenceCache.SaveSeedQuality("newvsold");
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

            // Incremental: same input set as NewVsOld (game tree + DLL). Skip the
            // re-index when a previous run already bounded it for these inputs.
            if (CleanReferenceCache.IsSeedQualityCurrent("dllonly"))
                return;

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

            CleanReferenceCache.SaveSeedQuality("dllonly");
        }

        /// <summary>
        /// Pins the seed-quality skip mechanism both ways: a missing stamp is not
        /// current (forces the full comparison), a saved stamp is current (allows
        /// the skip), and a tampered stamp is not current (a stale pass can never
        /// go silently green). Uses a probe name so it never touches the real
        /// newvsold/dllonly stamps.
        /// </summary>
        [Fact]
        public void SeedQualityStamp_RoundTrip()
        {
            if (!HasGame) return;
            if (!ModCompiler.HasModSource) return;

            const string probe = "roundtrip-probe";
            var path = CleanReferenceCache.SeedQualityStampPath(probe);
            try
            {
                if (File.Exists(path)) File.Delete(path);
                Assert.False(CleanReferenceCache.IsSeedQualityCurrent(probe));
                CleanReferenceCache.SaveSeedQuality(probe);
                Assert.True(CleanReferenceCache.IsSeedQualityCurrent(probe));
                File.WriteAllText(path, "tampered");
                Assert.False(CleanReferenceCache.IsSeedQualityCurrent(probe));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        /// <summary>
        /// Cyclomatic-complexity gate: flags "super-methods" — methods whose McCabe
        /// complexity exceeds a threshold (default 15). A method that does too much is
        /// a refactoring candidate; blocking keeps the mod maintainable. Complexity is
        /// computed over the mod's own Source\**\*.cs (not game code), covering methods,
        /// constructors, operators, conversions and property/event accessors — bodies
        /// outside plain methods hide logic the same way.
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
                foreach (var node in root.DescendantNodes())
                {
                    string name = MethodLikeName(node);
                    if (name == null)
                        continue;
                    int c = ComputeCyclomaticComplexity(node);
                    if (c > threshold)
                    {
                        var loc = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        over.Add(($"{Path.GetFileName(path)}:{loc} {name}", c));
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
        /// Names any method-like body for the complexity report: methods and
        /// constructors by identifier, operators by token, accessors by keyword.
        /// Returns null for non-method-like nodes.
        /// </summary>
        private static string MethodLikeName(SyntaxNode node)
        {
            if (node is MethodDeclarationSyntax method)
                return method.Identifier.Text + "()";
            if (node is ConstructorDeclarationSyntax ctor)
                return ctor.Identifier.Text + ".ctor";
            if (node is OperatorDeclarationSyntax op)
                return "operator " + op.OperatorToken.Text;
            if (node is ConversionOperatorDeclarationSyntax conv)
                return "convert to " + conv.Type.ToString();
            if (node is AccessorDeclarationSyntax access)
                return access.Keyword.Text + " accessor";
            return null;
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
        /// Input-blockade lease gate. ChronometerDragProtocol acquires a GameInput
        /// blockade on the component passed to OnMapDragBegin; the SAME reference
        /// must be released when the drag ends. CancelNativeDrag removes a
        /// DIFFERENT component (shipControlUI), so it cannot satisfy this gate.
        /// Without a matching release the mod's own blockade leaks, and
        /// GameInput.HasActiveBlockades() keeps map route drawing and map clicks
        /// disabled for the rest of the session.
        /// </summary>
        [Fact]
        public void Blockade_AcquireReleaseBalanced()
        {
            if (!ModCompiler.HasModSource) return;

            string path = ModCompiler.EnumerateSources()
                .FirstOrDefault(p => Path.GetFileName(p) == "ChronometerDragProtocol.cs");
            Assert.True(path != null, "ChronometerDragProtocol.cs not found");

            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path).GetRoot();
            var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();

            int adds = invocations.Count(inv => inv.Expression is MemberAccessExpressionSyntax ma
                && ma.Name.Identifier.Text == "AddBlockade");
            int removes = invocations.Count(inv => inv.Expression is MemberAccessExpressionSyntax ma
                && ma.Name.Identifier.Text == "RemoveBlockade");

            Assert.True(adds == 1, $"expected exactly 1 AddBlockade call, found {adds}");
            Assert.True(removes >= 1,
                "ChronometerDragProtocol acquires a GameInput blockade but never removes it — "
                + "map input stays blocked after the first expanded-chronometer drag");
        }

        /// <summary>
        /// Sextant owned-resource lifetime. Sprite.Create assets and
        /// ScriptableObject profiles are NOT owned by the canvas/volume
        /// GameObject, so they leak unless destroyed explicitly. Also pins the
        /// eyecup texture handoff: the texture created for the eyecup sprite
        /// must be stored on Result.EyecupTexture or the controller's
        /// Destroy(eyecupTexture) never fires.
        /// </summary>
        [Fact]
        public void SextantOwnedResources_AreDestroyed()
        {
            if (!ModCompiler.HasModSource) return;

            string overlay = SourceText("SextantOverlay.cs");
            Assert.Contains("OwnedSprites", overlay);

            string builder = SourceText("SextantOcularBuilder.cs");
            Assert.Contains("result.EyecupTexture =", builder);
            Assert.Contains("result.OwnedSprites.Add", builder);

            string controller = SourceText("SextantController.cs");
            Assert.Contains("ownedSprites", controller);
            Assert.Contains("Destroy(ownedSprites[i])", controller);

            string bloom = SourceText("SextantBloomCut.cs");
            Assert.Contains("Destroy(leftProfile)", bloom);
            Assert.Contains("Destroy(rightProfile)", bloom);
        }

        /// <summary>
        /// F220 frame-hot gate: the sextant PIP pose sync must not do
        /// per-frame reflection/component lookups (cached in TypeMemberCache +
        /// per-camera dictionaries), and CameraResolver must not use the
        /// allocating Camera.allCameras accessor.
        /// </summary>
        [Fact]
        public void FrameHot_ReflectionAndCameraLookupsCached()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();

            MethodDeclarationSyntax sync = FindMethod("SextantPipPose", "SyncCameraState");
            if (sync == null)
            {
                problems.Add("SextantPipPose.SyncCameraState not found");
            }
            else
            {
                bool reflects = sync.DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Any(inv => CalledName(inv) == "GetProperty"
                        || CalledName(inv) == "GetField");
                if (reflects)
                    problems.Add("SextantPipPose.SyncCameraState still does per-frame GetProperty/GetField");
            }

            bool usesAllCameras = false;
            foreach (var path in ModCompiler.EnumerateSources())
            {
                if (Path.GetFileName(path) != "CameraResolver.cs")
                    continue;
                var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path).GetRoot();
                usesAllCameras = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                    .Any(ma => ma.Name.Identifier.Text == "allCameras");
            }
            if (usesAllCameras)
                problems.Add("CameraResolver still uses Camera.allCameras (array allocated per call)");

            string sky = SourceText("SkyVisibilityResolver.cs");
            if (sky.Contains("GetType().FullName +"))
                problems.Add("SkyVisibilityResolver still builds a per-frame string cache key");

            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        /// <summary>
        /// F221 release-log gate: informational diagnostics must be behind
        /// RadioNavDiagnostics.IsVerboseLogEnabled() (default off) instead of
        /// flooding Player.log in release. The flare over-range log is gated
        /// on frame.verbose (its throttle only advances when verbose is on).
        /// </summary>
        [Fact]
        public void ReleaseLogs_AreGated()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();

            string station = SourceText("StationManager.cs");
            if (!station.Contains("RadioNavDiagnostics.IsVerboseLogEnabled"))
                problems.Add("StationManager bearing report is not gated by RadioNavDiagnostics");

            string story = SourceText("RadioStoryIntegration.cs");
            if (!story.Contains("RadioNavDiagnostics.IsVerboseLogEnabled"))
                problems.Add("RadioStoryIntegration decision log is not gated");

            string gateway = SourceText("StoryDecisionGateway.cs");
            if (!gateway.Contains("RadioNavDiagnostics.IsVerboseLogEnabled"))
                problems.Add("StoryDecisionGateway stage log is not gated");

            string radio = SourceText("RadioHandler.cs");
            if (!radio.Contains("RadioNavDiagnostics.IsVerboseLogEnabled"))
                problems.Add("RadioHandler visibility log is not gated");

            string flare = SourceText("LighthouseFlareDriver.cs");
            if (!flare.Contains("frame.verbose && frame.now"))
                problems.Add("LighthouseFlareDriver over-range log is not verbose-gated");

            // F236/F221 follow-up: the old gate was a whitelist of five
            // files, so a new release log could bypass it. Assert the general
            // default-off and on-change contracts for the known noisy paths.
            string beam = SourceText("LighthouseBeamGlow.cs");
            if (!beam.Contains("LighthouseGlowVerboseKey, false"))
                problems.Add("Lighthouse glow verbose flag must default off");
            string marker = SourceText("LighthouseMapMarker.cs");
            if (!marker.Contains("LighthouseMapMarkerPositionKey, false"))
                problems.Add("Lighthouse map position verbose flag must default off");
            string navMarkers = SourceText("NavMapMarkerBase.cs");
            if (!navMarkers.Contains("RadioNavDiagnostics.Stamp()"))
                problems.Add("throttled marker diagnostics need a time/frame stamp");
            string columns = SourceText("SextantColumnsBuilder.cs");
            if (!columns.Contains("lastFixLayoutLog") ||
                !columns.Contains("RadioNavDiagnostics.Stamp()"))
                problems.Add("FIXLAYOUT diagnostics must be change-gated and stamped");

            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        /// <summary>
        /// F223: a failed UnityEngine.UI.Image resolution must be latched, or
        /// every frame re-scans all loaded assemblies and re-logs the warning.
        /// </summary>
        [Fact]
        public void LighthouseDotUi_ImageTypeFailureLatched()
        {
            if (!ModCompiler.HasModSource) return;

            string text = SourceText("LighthouseDotUi.cs");
            Assert.Contains("imageTypeResolveFailed", text);
            Assert.Contains("imageTypeResolveFailed = true", text);
        }

        /// <summary>
        /// F228: build.ps1 must define the same preprocessor symbols as the
        /// game's RoslynCSharp mod compiler (UBOAT, UBOAT_2024_1,
        /// UNITY_STANDALONE_WIN) and must NOT define RELEASE, which the game
        /// never sets. Otherwise a local build compiles different #if branches
        /// than the in-game compile.
        /// </summary>
        [Fact]
        public void BuildScript_GameDefineParity()
        {
            string build = File.ReadAllText(Path.Combine(ModCompiler.ModRoot, "build.ps1"));
            Assert.Contains("UBOAT;UBOAT_2024_1;UNITY_STANDALONE_WIN", build);
            Assert.DoesNotContain("\"RELEASE\"", build);
        }

        /// <summary>
        /// F227: every pure (non-Unity/game-bound) Source file must be linked
        /// into the unit-test project, or it silently escapes coverage and can
        /// never lower it. Unity/MonoBehaviour files are exercised live and are
        /// skipped by the heuristic (no UnityEngine/UBOAT/DWS using, no [Inject]).
        /// </summary>
        [Fact]
        public void TestProject_LinksAllSourceFiles()
        {
            if (!ModCompiler.HasModSource) return;

            string csproj = File.ReadAllText(
                Path.Combine(ModCompiler.ModRoot, "Tests", "RadioNavigation.Tests.csproj"));
            var problems = new List<string>();

            foreach (var path in ModCompiler.EnumerateSources())
            {
                string text = File.ReadAllText(path);
                if (text.Contains("using UnityEngine")
                    || text.Contains("using UBOAT.")
                    || text.Contains("using DWS.")
                    || text.Contains("[Inject]"))
                    continue;

                string fileName = Path.GetFileName(path);
                if (!csproj.Contains("\\" + fileName + "\""))
                    problems.Add(fileName + " is pure but not linked in Tests/RadioNavigation.Tests.csproj");
            }

            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        private static MethodDeclarationSyntax FindMethod(string typeName, string methodName)
        {
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text != typeName)
                        continue;
                    return type.Members.OfType<MethodDeclarationSyntax>()
                        .FirstOrDefault(m => m.Identifier.Text == methodName);
                }
            }
            return null;
        }

        private static string CalledName(InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax ma)
                return ma.Name.Identifier.Text;
            if (invocation.Expression is IdentifierNameSyntax id)
                return id.Identifier.Text;
            return null;
        }

        private static string SourceText(string fileName)
        {
            string path = ModCompiler.EnumerateSources()
                .FirstOrDefault(p => Path.GetFileName(p) == fileName);
            Assert.True(path != null, fileName + " not found in mod sources");
            return File.ReadAllText(path);
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
        /// GameTypeCacheTests). Pins the type surface and that no raw
        /// Type.GetType lookup remains anywhere else in Source/.
        /// </summary>
        [Fact]
        public void GameTypeCache_OwnsTypeResolution()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax cache = null;
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
        /// StationGainModelTests), with StationManager.UpdateGains as the
        /// array-management caller (SonneController delegates tuning) and
        /// ShieldTests calling production instead of duplicating the
        /// formula. A re-inline (or a new local copy) fails here.
        /// </summary>
        [Fact]
        public void StationGain_OwnedByModel()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax model = null;
            TypeDeclarationSyntax controller = null;
            TypeDeclarationSyntax manager = null;
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
                    if (type.Identifier.Text == "StationManager")
                        manager = type;
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
            if (manager == null)
            {
                problems.Add("type 'StationManager' not found in Source/ — gain math regressed into the controller");
            }
            else
            {
                foreach (var type in new[] { controller, manager })
                {
                    if (type == null)
                        continue;
                    bool inlinesSqrt = type.DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .Any(inv => inv.Expression is MemberAccessExpressionSyntax access
                            && access.Name.Identifier.Text == "Sqrt");
                    if (inlinesSqrt)
                        problems.Add($"{type.Identifier.Text} still contains a Sqrt gain formula — route it through StationGainModel.ComputeGain");
                }

                bool invokesModel = manager.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => inv.Expression is MemberAccessExpressionSyntax qualified
                        && qualified.Expression is IdentifierNameSyntax recv
                        && recv.Identifier.Text == "StationGainModel"
                        && qualified.Name is IdentifierNameSyntax qname
                        && qname.Identifier.Text == "ComputeGain");
                if (!invokesModel)
                    problems.Add("StationManager never invokes StationGainModel.ComputeGain — the tested pure function is bypassed");
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
        /// StationManager.LogBearingDiagnostics as the emit-once-per-
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
                    if (type.Identifier.Text == "StationManager")
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
                problems.Add("StationManager.LogBearingDiagnostics not found");
            }
            else
            {
                bool keepsFormat = log.DescendantNodes()
                    .OfType<LiteralExpressionSyntax>()
                    .Any(lit => lit.Token.ValueText != null
                        && lit.Token.ValueText.Contains("[RadioNav][BEARING]"));
                if (keepsFormat)
                    problems.Add("StationManager.LogBearingDiagnostics still contains [BEARING] format strings — the report belongs to BearingDiagnostics");

                bool invokesDiagnostics = log.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                            && bare.Identifier.Text == "BuildLines")
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == "BuildLines"));
                if (!invokesDiagnostics)
                    problems.Add("StationManager.LogBearingDiagnostics never invokes BuildLines — the tested pure function is bypassed");
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
        /// StationManager.UpdateNavigation as the fleet loop.
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
                    if (type.Identifier.Text == "StationManager")
                    {
                        update = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "UpdateNavigation");
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
                problems.Add("StationManager.UpdateNavigation not found");
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
                    problems.Add("StationManager.UpdateNavigation still calls TryGetNavigationPattern — the lookup belongs to StationNavigator");

                bool invokesNavigator = update.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                            && bare.Identifier.Text == "UpdateStation")
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == "UpdateStation"));
                if (!invokesNavigator)
                    problems.Add("StationManager.UpdateNavigation never invokes StationNavigator.UpdateStation — the tested pure function is bypassed");
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
        /// StationManager.GetStationDiagnostics keeping only validation,
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
            TypeDeclarationSyntax manager = null;
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
                    if (type.Identifier.Text == "StationManager")
                    {
                        manager = type;
                        getter = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "GetStationDiagnostics");
                    }
                }
            }

            if (topLevel == null)
                problems.Add("top-level type 'StationDiagnostics' not found in Source/ — the snapshot is nested in the manager again");
            if (manager != null
                && manager.Members.OfType<TypeDeclarationSyntax>()
                    .Any(t => t.Identifier.Text == "StationDiagnostics"))
                problems.Add("StationManager still nests StationDiagnostics — keep the snapshot top-level");

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
                problems.Add("StationManager.GetStationDiagnostics not found");
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
                    problems.Add("StationManager.GetStationDiagnostics never invokes StationDiagnosticsReport.Build — the tested pure function is bypassed");
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
        /// 140f storage-range clamps via PanelPersistenceLogic (persistence
        /// validation, not mapping). A new inline angle/140f division fails here.
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
                if (file == "SpinnerScale.cs" || file == "PanelStateStore.cs" || file == "PanelPersistenceLogic.cs") continue;
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
        /// BearingDiagnostics), composed by PanelViewBinder (tested in
        /// PanelViewBinderTests), with InformationPanelHandler delegating.
        /// A re-inline of the BFO/ToBoat format fails here.
        /// </summary>
        [Fact]
        public void PatternLine_OwnsLineFormat()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax formatter = null;
            MethodDeclarationSyntax buildRows = null;
            MethodDeclarationSyntax buildStation = null;
            TypeDeclarationSyntax panel = null;
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "PatternLineFormatter")
                        formatter = type;
                    if (type.Identifier.Text == "PanelViewBinder")
                    {
                        buildRows = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "BuildPatternRows");
                        buildStation = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "BuildStationText");
                    }
                    if (type.Identifier.Text == "InformationPanelHandler")
                    {
                        panel = type;
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

            if (buildRows == null)
            {
                problems.Add("PanelViewBinder.BuildPatternRows not found — row assembly regressed into the panel");
            }
            else
            {
                foreach (var lit in buildRows.DescendantNodes().OfType<LiteralExpressionSyntax>())
                {
                    string token = lit.Token.Text;
                    if (token.Contains("BFO:") || token.Contains("ToBoat:"))
                        problems.Add($"PanelViewBinder.BuildPatternRows still contains {token} — the line format belongs to PatternLineFormatter");
                }

                bool invokes = buildRows.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                            && bare.Identifier.Text == "BuildLine")
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == "BuildLine"));
                if (!invokes)
                    problems.Add("PanelViewBinder.BuildPatternRows never invokes BuildLine — the tested pure function is bypassed");
            }

            if (buildStation == null)
            {
                problems.Add("PanelViewBinder.BuildStationText not found — station text regressed into the panel");
            }
            else
            {
                bool invokesSuffix = buildStation.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                            && bare.Identifier.Text == "StationSuffix")
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == "StationSuffix"));
                if (!invokesSuffix)
                    problems.Add("PanelViewBinder.BuildStationText never invokes StationSuffix — the tested pure function is bypassed");
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
                bool invokesBinder = stationText.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                            && bare.Identifier.Text == "BuildStationText")
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == "BuildStationText"));
                if (!invokesBinder)
                    problems.Add("InformationPanelHandler.UpdateStationText never invokes BuildStationText — the tested binder is bypassed");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} pattern-line violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Pattern-rows gate (SRP): the gain-gated row selection must live
        /// in PatternRowPacker as a pure function (tested in
        /// PatternRowPackerTests), composed by PanelViewBinder, with
        /// InformationPanelHandler.UpdatePatterns rendering only. A
        /// re-inline of the gain filter fails here.
        /// </summary>
        [Fact]
        public void PatternRows_OwnsPacking()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax packer = null;
            MethodDeclarationSyntax binderRows = null;
            MethodDeclarationSyntax patterns = null;
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "PatternRowPacker")
                        packer = type;
                    if (type.Identifier.Text == "PanelViewBinder")
                        binderRows = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "BuildPatternRows");
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

            if (binderRows == null)
            {
                problems.Add("PanelViewBinder.BuildPatternRows not found — row assembly regressed into the panel");
            }
            else
            {
                bool invokesPack = binderRows.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                            && bare.Identifier.Text == "Pack")
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == "Pack"));
                if (!invokesPack)
                    problems.Add("PanelViewBinder.BuildPatternRows never invokes Pack — the tested pure function is bypassed");
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

                bool invokesBinder = patterns.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                            && bare.Identifier.Text == "BuildPatternRows")
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == "BuildPatternRows"));
                if (!invokesBinder)
                    problems.Add("InformationPanelHandler.UpdatePatterns never invokes BuildPatternRows — the tested binder is bypassed");
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
        /// SignalPanelViewTests), composed by PanelViewBinder, with
        /// InformationPanelHandler.UpdateSignalDisplay applying Text/color
        /// only. A re-inline of the placeholder fails here.
        /// </summary>
        [Fact]
        public void SignalView_OwnsBlockState()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax view = null;
            MethodDeclarationSyntax binderSignal = null;
            MethodDeclarationSyntax display = null;
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "SignalPanelView")
                        view = type;
                    if (type.Identifier.Text == "PanelViewBinder")
                        binderSignal = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "BuildSignalView");
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

            if (binderSignal == null)
            {
                problems.Add("PanelViewBinder.BuildSignalView not found — block state regressed into the panel");
            }
            else
            {
                bool invokes = binderSignal.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                            && bare.Identifier.Text == "Resolve")
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == "Resolve"));
                if (!invokes)
                    problems.Add("PanelViewBinder.BuildSignalView never invokes Resolve — the tested pure function is bypassed");
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
                            && bare.Identifier.Text == "BuildSignalView")
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == "BuildSignalView"));
                if (!invokes)
                    problems.Add("InformationPanelHandler.UpdateSignalDisplay never invokes BuildSignalView — the tested binder is bypassed");
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
        /// Fix-card gate (SRP): the F-Tafel worksheet card assembly must
        /// live in FixCardComposer as a pure function (tested in
        /// FixCardComposerTests), with SextantWorksheet.BuildCard as a thin
        /// adapter. A re-inline of the sight blocks fails here.
        /// </summary>
        [Fact]
        public void FixCard_OwnsCardAssembly()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax composer = null;
            MethodDeclarationSyntax build = null;
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "FixCardComposer")
                        composer = type;
                    if (type.Identifier.Text == "SextantWorksheet")
                    {
                        build = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "BuildCard");
                    }
                }
            }

            if (composer == null)
            {
                problems.Add("type 'FixCardComposer' not found in Source/ — card assembly regressed into the worksheet");
            }
            else
            {
                bool composes = composer.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "Compose"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword))
                        && m.ParameterList.Parameters.Count == 5);
                if (!composes) problems.Add("FixCardComposer.Compose(obs1, obs2, section, status, calMinutes) public static entry point not found");
            }

            if (build == null)
            {
                problems.Add("SextantWorksheet.BuildCard not found");
            }
            else
            {
                bool keepsBlocks = build.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                            && bare.Identifier.Text == "AppendObservation")
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == "AppendObservation"));
                if (keepsBlocks)
                    problems.Add("SextantWorksheet.BuildCard still calls AppendObservation — sight blocks belong to FixCardComposer");

                bool invokesCompose = build.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                            && bare.Identifier.Text == "Compose")
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == "Compose"));
                if (!invokesCompose)
                    problems.Add("SextantWorksheet.BuildCard never invokes Compose — the tested pure function is bypassed");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} fix-card violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Fix-card snapshot gate (v3, CELESTIAL_REALISM_PLAN.md): the F-Tafel
        /// card is a frozen snapshot assembled at record time
        /// (CaptureObservation -&gt; RecomputeFixSnapshot); the 1 Hz refresh
        /// path — every FixCardComposer member plus
        /// SextantWorksheet.BuildCard — must never read a live clock, or the
        /// card text mutates and the text caches thrash. Record-time clock use
        /// in CaptureObservation/RecomputeFixSnapshot stays legal.
        /// </summary>
        [Fact]
        public void FixCard_SnapshotPurity()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax composer = null;
            MethodDeclarationSyntax build = null;
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "FixCardComposer")
                        composer = type;
                    if (type.Identifier.Text == "SextantWorksheet")
                    {
                        build = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "BuildCard");
                    }
                }
            }

            if (composer == null)
            {
                problems.Add("type 'FixCardComposer' not found in Source/ — card assembly regressed into the worksheet");
            }
            else
            {
                CollectLiveClockReads(composer, problems);
            }

            if (build == null)
            {
                problems.Add("SextantWorksheet.BuildCard not found");
            }
            else
            {
                CollectLiveClockReads(build, problems);
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} snapshot-purity violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Live-clock reads banned from the card assembly: member accesses on
        /// GameTimeResolver (GetUtcNow today, GetUtcAt tomorrow) and any
        /// UtcNow/Now/Today member (DateTime.UtcNow and friends). One problem
        /// line per hit, formatted "live clock in card assembly: Type.member".
        /// </summary>
        private static void CollectLiveClockReads(SyntaxNode scope, List<string> problems)
        {
            foreach (var access in scope.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            {
                bool resolverRead = access.Expression is IdentifierNameSyntax receiver
                    && receiver.Identifier.Text == "GameTimeResolver";
                string member = access.Name.Identifier.Text;
                bool clockMember = member == "UtcNow" || member == "Now" || member == "Today";
                if (resolverRead || clockMember)
                    problems.Add($"live clock in card assembly: {access.Expression}.{member}");
            }
        }

        /// <summary>
        /// Font-fit measurement ownership (2026-09-22, swarm review): the card
        /// font fit may only consume the base-size measurement cached in
        /// SextantWorksheet.RefreshCard (cardHeightAtBase /
        /// cardCompactHeightAtBase), never the live preferredHeight — that one
        /// is measured at whatever font is currently applied, and feeding it
        /// back flipped the font every 1 Hz refresh (live 9/11, "текст
        /// пляшет"). Rules:
        ///  A. no preferredHeight read inside ApplyCardFontSize;
        ///  B. every preferredHeight read in the type lives in RefreshCard as
        ///     the RHS of an assignment to a *HeightAtBase field;
        ///  C. each such read is preceded in RefreshCard by the base-font and
        ///     the text writes of the measurement block;
        ///  D. the fit applies the status-line reserve via
        ///     FixCardLayout.CardFontSize, never the bare FontSize.
        /// Mutation checks: swap CardFontSize for FontSize (D); replace the
        /// cached height with cardText.preferredHeight in ApplyCardFontSize
        /// (A/B); move a measurement into UpdatePanel (B/C); reorder the
        /// measurement before the base-font write (C).
        /// </summary>
        [Fact]
        public void FixCard_FitMeasurementOwnership()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax worksheet = null;
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "SextantWorksheet")
                        worksheet = type;
                }
            }

            if (worksheet == null)
            {
                problems.Add("type 'SextantWorksheet' not found in Source/ — the card fit left its home");
            }
            else
            {
                CollectFontFitViolations(worksheet, problems);
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} font-fit ownership violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>Live-measurement reads: member accesses named
        /// preferredHeight (UnityEngine.UI.Text).</summary>
        private static IEnumerable<MemberAccessExpressionSyntax> LiveMeasurementReads(
            SyntaxNode scope)
        {
            return scope.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                .Where(a => a.Name.Identifier.Text == "preferredHeight");
        }

        /// <summary>True for `cardText.&lt;member&gt;` on the left of an
        /// assignment (the measurement surface writes).</summary>
        private static bool IsCardTextMember(ExpressionSyntax expression, string member)
        {
            var access = expression as MemberAccessExpressionSyntax;
            return access != null
                && access.Name.Identifier.Text == member
                && access.Expression is IdentifierNameSyntax receiver
                && receiver.Identifier.Text == "cardText";
        }

        private static void CollectFontFitViolations(TypeDeclarationSyntax worksheet,
            List<string> problems)
        {
            var fit = worksheet.Members.OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.Text == "ApplyCardFontSize");
            var refresh = worksheet.Members.OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.Text == "RefreshCard");
            if (fit == null)
            {
                problems.Add("SextantWorksheet.ApplyCardFontSize not found");
                return;
            }
            if (refresh == null)
            {
                problems.Add("SextantWorksheet.RefreshCard not found");
                return;
            }

            // A: the fit never reads a live measurement.
            foreach (var read in LiveMeasurementReads(fit))
                problems.Add($"live preferredHeight in ApplyCardFontSize: {read}");

            // B/C: measurements live in RefreshCard, cached into *HeightAtBase
            // after the base-font and text writes of the measurement block.
            foreach (var read in LiveMeasurementReads(worksheet))
            {
                if (fit.Span.Contains(read.Span))
                    continue; // already reported by rule A

                if (!refresh.Span.Contains(read.Span))
                {
                    problems.Add($"preferredHeight read outside RefreshCard: {read}");
                    continue;
                }

                var assignment = read.FirstAncestorOrSelf<AssignmentExpressionSyntax>();
                var left = assignment != null ? assignment.Left as IdentifierNameSyntax : null;
                bool cached = assignment != null && assignment.Right == read
                    && left != null
                    && left.Identifier.Text.EndsWith("HeightAtBase", StringComparison.Ordinal);
                if (!cached)
                {
                    problems.Add($"preferredHeight not cached into a *HeightAtBase field: {read}");
                    continue;
                }

                int position = read.SpanStart;
                bool atBase = refresh.DescendantNodes()
                    .OfType<AssignmentExpressionSyntax>()
                    .Any(a => a.SpanStart < position
                        && IsCardTextMember(a.Left, "fontSize")
                        && a.Right.ToString() == "cardBaseFontSize");
                bool textWritten = refresh.DescendantNodes()
                    .OfType<AssignmentExpressionSyntax>()
                    .Any(a => a.SpanStart < position && IsCardTextMember(a.Left, "text"));
                if (!atBase || !textWritten)
                {
                    problems.Add($"preferredHeight measured before the base-font/text writes: {read}");
                }
            }

            // D: the fit goes through CardFontSize (status-line reserve).
            bool usesCardFontSize = fit.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Any(i => i.Expression.ToString().EndsWith("CardFontSize", StringComparison.Ordinal));
            if (!usesCardFontSize)
                problems.Add("ApplyCardFontSize does not call FixCardLayout.CardFontSize");
            foreach (var call in worksheet.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (call.Expression.ToString() == "FixCardLayout.FontSize")
                    problems.Add($"FixCardLayout.FontSize bypasses the status reserve: {call}");
            }
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
        /// DecodedDisplayTrackerTests), composed by PanelViewBinder, with
        /// InformationPanelHandler.UpdateSignalDisplay delegating text
        /// selection and keeping only the log-once latch calls. A
        /// re-inline of the latch field fails here.
        /// </summary>
        [Fact]
        public void DecodedDisplay_OwnsLatch()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax tracker = null;
            TypeDeclarationSyntax panel = null;
            MethodDeclarationSyntax binderSignal = null;
            MethodDeclarationSyntax display = null;
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "DecodedDisplayTracker")
                        tracker = type;
                    if (type.Identifier.Text == "PanelViewBinder")
                        binderSignal = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "BuildSignalView");
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

            if (binderSignal == null)
            {
                problems.Add("PanelViewBinder.BuildSignalView not found — text selection regressed into the panel");
            }
            else
            {
                bool invokes = binderSignal.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                            && bare.Identifier.Text == "DisplayText")
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == "DisplayText"));
                if (!invokes)
                    problems.Add("PanelViewBinder.BuildSignalView never invokes DisplayText — the tested class is bypassed");
            }

            if (display == null)
            {
                problems.Add("InformationPanelHandler.UpdateSignalDisplay not found");
            }
            else
            {
                foreach (var name in new[] { "BuildSignalView", "ShouldLog" })
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

        /// <summary>
        /// Reload-lifecycle gate (risk register iter2): cross-scene static
        /// state must be reset on mod reload and DDOL roots Find-guarded.
        /// SceneContentBuilder.EnsureSkyRoots needs an Exists guard per root
        /// (backed by GameObject.Find in UnitySceneContentHost) or every
        /// Main Scene entry stacks another DontDestroyOnLoad duplicate with
        /// a zombie LateUpdate; the OnLoaded reload branch must reset the
        /// camera hook, power bus, type cache and fix pin; Reset must drop
        /// the scene-bound lighthouse entity.
        /// </summary>
        [Fact]
        public void ReloadLifecycle_ResetsCrossSceneState()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            MethodDeclarationSyntax ensureSkyRoots = null;
            MethodDeclarationSyntax hostExists = null;
            int skyRootCount = 0;
            MethodDeclarationSyntax onLoaded = null;
            MethodDeclarationSyntax placerReset = null;
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "ModBootstrapper")
                    {
                        onLoaded = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "OnLoaded");
                    }
                    if (type.Identifier.Text == "SceneContentBuilder")
                    {
                        ensureSkyRoots = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "EnsureSkyRoots");
                        foreach (var field in type.Members.OfType<FieldDeclarationSyntax>())
                        {
                            foreach (var variable in field.Declaration.Variables)
                            {
                                if (variable.Identifier.Text == "SkyRoots"
                                    && variable.Initializer != null
                                    && variable.Initializer.Value is InitializerExpressionSyntax init)
                                    skyRootCount = init.Expressions.Count;
                            }
                        }
                        foreach (var nested in type.Members.OfType<TypeDeclarationSyntax>())
                        {
                            if (nested.Identifier.Text == "UnitySceneContentHost")
                                hostExists = nested.Members.OfType<MethodDeclarationSyntax>()
                                    .FirstOrDefault(m => m.Identifier.Text == "Exists");
                        }
                    }
                    if (type.Identifier.Text == "LighthousePlacer")
                    {
                        placerReset = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "Reset"
                                && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword)));
                    }
                }
            }

            if (skyRootCount < 5)
            {
                problems.Add($"SceneContentBuilder.SkyRoots has {skyRootCount} entries, need 5 (StarSkyRoot + ConstellationRoot + PlanetRoot + MoonDiscRoot + SunDiscRoot) — a missing root is never created");
            }

            if (ensureSkyRoots == null)
            {
                problems.Add("SceneContentBuilder.EnsureSkyRoots not found");
            }
            else
            {
                bool guards = ensureSkyRoots.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                            && bare.Identifier.Text == "Exists")
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == "Exists"));
                if (!guards)
                    problems.Add("SceneContentBuilder.EnsureSkyRoots never calls Exists — unguarded DDOL creation stacks duplicates every scene entry");
            }

            if (hostExists == null)
            {
                problems.Add("SceneContentBuilder.UnitySceneContentHost.Exists not found");
            }
            else
            {
                int findGuards = hostExists.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Count(inv => (inv.Expression is IdentifierNameSyntax bare
                            && bare.Identifier.Text == "Find")
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == "Find"));
                if (findGuards < 1)
                    problems.Add("UnitySceneContentHost.Exists has no Find call — the guard never queries the scene, DDOL duplicates stack every scene entry");
            }

            if (onLoaded == null)
            {
                problems.Add("ModBootstrapper.OnLoaded not found");
            }
            else
            {
                var calls = new HashSet<string>();
                foreach (var inv in onLoaded.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (inv.Expression is MemberAccessExpressionSyntax qualified
                        && qualified.Expression is IdentifierNameSyntax recv
                        && qualified.Name is IdentifierNameSyntax qname)
                        calls.Add(recv.Identifier.Text + "." + qname.Identifier.Text);
                }
                foreach (var required in new[] { "CameraResolver.ResetForReload", "RadioPowerBus.Reset", "GameTypeCache.Clear", "FixMapMarker.Clear" })
                {
                    if (!calls.Contains(required))
                        problems.Add($"ModBootstrapper.OnLoaded reload branch never calls {required} — stale cross-scene state survives reload");
                }
            }

            if (placerReset == null)
            {
                problems.Add("LighthousePlacer.Reset not found");
            }
            else
            {
                // The distance-gated placer (Leuchtfeuer fleet) holds no
                // static LighthouseEntity any more — per-group state lives in
                // the placements list, destroyed together with the holder GO.
                // Pin instead that Reset destroys the whole holder GameObject
                // so no per-frame loop survives a reload.
                bool destroysHolder = placerReset.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => inv.Expression is MemberAccessExpressionSyntax member
                        && member.Name is IdentifierNameSyntax name
                        && name.Identifier.Text == "Destroy"
                        && inv.ArgumentList.Arguments.Count == 1
                        && inv.ArgumentList.Arguments[0].Expression.ToString().Contains("gameObject"));
                if (!destroysHolder)
                    problems.Add("LighthousePlacer.Reset never destroys the holder gameObject — stale Update loop survives reload");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} reload-lifecycle violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// No-silent-catch gate (risk register iter3): every catch clause in
        /// Source/ must contain at least one statement. An empty catch is a
        /// silent dead feature — per-frame/setup reflection failures (game
        /// renames) diverge without a trace in Player.log. The policy is
        /// WarnOnce.ShouldLog + Debug.LogWarning (first failure visible, no
        /// spam); behavior (fallback values) is unchanged.
        /// </summary>
        [Fact]
        public void NoSilentCatches()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                string file = Path.GetFileName(path);
                foreach (var clause in root.DescendantNodes().OfType<CatchClauseSyntax>())
                {
                    if (clause.Block == null || clause.Block.Statements.Count == 0)
                    {
                        var line = clause.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        problems.Add($"{file}:{line} empty catch block — log the first failure via WarnOnce");
                    }
                }
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} silent-catch violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Game-boundary null-guard gate (risk register iter4): entry points
        /// that dereference game-provided objects or cached reflection
        /// handles must null-check THAT handle before use — a game update
        /// renaming a member must degrade gracefully, never NRE. Each
        /// listed method must invoke IsNull/IsNotNull on its handle
        /// (receiver-aware: an unrelated IsNull elsewhere does not count).
        /// </summary>
        [Fact]
        public void GameBoundary_NullGuards()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            // Type -> (method -> handle that must be null-checked).
            var targets = new Dictionary<string, Dictionary<string, string>>
            {
                {
                    "StoryDecisionGateway",
                    new Dictionary<string, string>
                    {
                        { "IsStageMainChoices", "stageField" },
                        { "CreateDecision", "decisionCtor" },
                        { "InsertDecision", "insertMethod" },
                    }
                },
                {
                    "ShipControlUIBridge",
                    new Dictionary<string, string>
                    {
                        { "GetDragContainer", "ui" },
                        { "GetButton", "ui" },
                        { "GetDefaultPosition", "ui" },
                        { "SetDefaultPosition", "ui" },
                        { "ResetNativeDragState", "ui" },
                    }
                },
            };
            var seen = new Dictionary<string, HashSet<string>>();
            foreach (var kv in targets)
            {
                seen[kv.Key] = new HashSet<string>();
            }
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (!targets.ContainsKey(type.Identifier.Text))
                        continue;
                    foreach (var method in type.Members.OfType<MethodDeclarationSyntax>())
                    {
                        if (!targets[type.Identifier.Text].TryGetValue(method.Identifier.Text, out string handle))
                            continue;
                        seen[type.Identifier.Text].Add(method.Identifier.Text);
                        bool isGuarded = method.DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Any(inv => inv.Expression is MemberAccessExpressionSyntax access
                                && access.Expression is IdentifierNameSyntax recv
                                && recv.Identifier.Text == handle
                                && access.Name is IdentifierNameSyntax qname
                                && (qname.Identifier.Text == "IsNull" || qname.Identifier.Text == "IsNotNull"));
                        if (!isGuarded)
                            problems.Add($"{type.Identifier.Text}.{method.Identifier.Text} never null-checks '{handle}' — game-boundary deref without a guard on its handle");
                    }
                }
            }
            foreach (var kv in targets)
            {
                foreach (var name in kv.Value.Keys)
                {
                    if (!seen[kv.Key].Contains(name))
                        problems.Add($"{kv.Key}.{name} not found in Source/");
                }
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} game-boundary violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Frame-hot allocation gate (risk register iter5, P5.1): the mesh
        /// rebuild must reuse a scratch list instead of allocating per
        /// polyline, and the scene-wide ship-control fallback must be
        /// frame-throttled instead of running every LateUpdate. A
        /// re-introduced per-frame alloc fails here.
        /// </summary>
        [Fact]
        public void FrameHot_NoPerFrameAllocs()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            TypeDeclarationSyntax throttle = null;
            MethodDeclarationSyntax resolve = null;
            MethodDeclarationSyntax rebuild = null;
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == "FrameThrottle")
                        throttle = type;
                    if (type.Identifier.Text == "ChronometerOverlayHandler")
                    {
                        resolve = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "TryResolveChronometer");
                    }
                    if (type.Identifier.Text == "ConstellationMesh")
                    {
                        rebuild = type.Members.OfType<MethodDeclarationSyntax>()
                            .FirstOrDefault(m => m.Identifier.Text == "RebuildMesh");
                    }
                }
            }

            if (throttle == null)
            {
                problems.Add("type 'FrameThrottle' not found in Source/ — retry math regressed into the handler");
            }
            else
            {
                bool retries = throttle.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "ShouldRetry"
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.PublicKeyword))
                        && m.Modifiers.Any(x => x.IsKind(SyntaxKind.StaticKeyword))
                        && m.ParameterList.Parameters.Count == 3);
                if (!retries) problems.Add("FrameThrottle.ShouldRetry(last, current, interval) public static entry point not found");
            }

            if (resolve == null)
            {
                problems.Add("ChronometerOverlayHandler.TryResolveChronometer not found");
            }
            else
            {
                bool throttled = resolve.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                            && bare.Identifier.Text == "ShouldRetry")
                        || (inv.Expression is MemberAccessExpressionSyntax qualified
                            && qualified.Name is IdentifierNameSyntax qname
                            && qname.Identifier.Text == "ShouldRetry"));
                if (!throttled)
                    problems.Add("ChronometerOverlayHandler.TryResolveChronometer never invokes ShouldRetry — FindObjectsOfTypeAll runs every frame again");
            }

            if (rebuild == null)
            {
                problems.Add("ConstellationMesh.RebuildMesh not found");
            }
            else
            {
                bool allocates = rebuild.DescendantNodes()
                    .OfType<ObjectCreationExpressionSyntax>()
                    .Any(o => o.Type.ToString().Contains("List<Vector3>"));
                if (allocates)
                    problems.Add("ConstellationMesh.RebuildMesh allocates List<Vector3> — use the scratch list");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} frame-hot violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// True when a method body invokes a call named <paramref name="name"/>,
        /// bare or qualified. Shared by the satellite-delegation gates below.
        /// </summary>
        private static bool InvokesName(MethodDeclarationSyntax method, string name)
        {
            return method.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                        && bare.Identifier.Text == name)
                    || (inv.Expression is MemberAccessExpressionSyntax qualified
                        && qualified.Name is IdentifierNameSyntax qname
                        && qname.Identifier.Text == name));
        }

        /// <summary>First position of an assignment to <paramref name="field"/>
        /// inside an `if (Locked)` branch whose right side mentions
        /// <paramref name="source"/>; -1 when absent. Used by the keyboard-trim
        /// gate (the frozen-pose shift must be paired with the trim).</summary>
        private static int LockedBranchAssignmentStart(
            MethodDeclarationSyntax method, string field, string source)
        {
            int best = -1;
            foreach (var ifStatement in method.DescendantNodes().OfType<IfStatementSyntax>())
            {
                if (ifStatement.Condition.ToString() != "Locked")
                    continue;
                foreach (var assignment in ifStatement.Statement.DescendantNodesAndSelf()
                    .OfType<AssignmentExpressionSyntax>())
                {
                    if (!(assignment.Left is IdentifierNameSyntax left)
                        || left.Identifier.Text != field)
                        continue;
                    if (assignment.Right.ToString().IndexOf(source, StringComparison.Ordinal) < 0)
                        continue;
                    if (best < 0 || assignment.SpanStart < best)
                        best = assignment.SpanStart;
                }
            }
            return best;
        }

        /// <summary>True when the method body reads the named member.</summary>
        private static bool ReadsMember(MethodDeclarationSyntax method, string member)
        {
            return method.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                .Any(a => a.Name.Identifier.Text == member);
        }

        /// <summary>True when any method body of the type invokes a call named
        /// <paramref name="name"/> (bare or qualified). Type-level companion
        /// of the method-level overload above.</summary>
        private static bool InvokesName(TypeDeclarationSyntax type, string name)
        {
            return type.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Any(inv => (inv.Expression is IdentifierNameSyntax bare
                        && bare.Identifier.Text == name)
                    || (inv.Expression is MemberAccessExpressionSyntax qualified
                        && qualified.Name is IdentifierNameSyntax qname
                        && qname.Identifier.Text == name));
        }

        private static TypeDeclarationSyntax FindSourceType(
            List<Tuple<string, CompilationUnitSyntax>> roots, string name)
        {
            foreach (var root in roots)
            {
                foreach (var type in root.Item2.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (type.Identifier.Text == name)
                        return type;
                }
            }
            return null;
        }

        private static List<Tuple<string, CompilationUnitSyntax>> ParseAllSources()
        {
            var roots = new List<Tuple<string, CompilationUnitSyntax>>();
            foreach (var path in ModCompiler.EnumerateSources())
            {
                var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path), path: path).GetRoot();
                roots.Add(Tuple.Create(path, root));
            }
            return roots;
        }

        private static MethodDeclarationSyntax FindMethod(TypeDeclarationSyntax type, string name)
        {
            if (type == null)
                return null;
            return type.Members.OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.Text == name);
        }

        /// <summary>
        /// Delegation gate (SRP slices SC-2): tuning flows
        /// SonneController.UpdateStationGains -&gt; StationManager.UpdateGains and the
        /// nav tick flows Update -&gt; StationManager.UpdateNavigation. A re-inline
        /// of gain/nav math into the controller fails here (the pure-function
        /// gates pin the second hop).
        /// </summary>
        [Fact]
        public void StationManager_ReceivesTuning()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            var roots = ParseAllSources();
            var controller = FindSourceType(roots, "SonneController");
            if (controller == null)
            {
                problems.Add("type 'SonneController' not found in Source/");
            }
            else
            {
                var gains = FindMethod(controller, "UpdateStationGains");
                if (gains == null)
                    problems.Add("SonneController.UpdateStationGains not found");
                else if (!InvokesName(gains, "UpdateGains"))
                    problems.Add("SonneController.UpdateStationGains never invokes StationManager.UpdateGains — tuning regressed into the controller");
                var update = FindMethod(controller, "Update");
                if (update == null)
                    problems.Add("SonneController.Update not found");
                else if (!InvokesName(update, "UpdateNavigation"))
                    problems.Add("SonneController.Update never invokes StationManager.UpdateNavigation — nav regressed into the controller");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} station-manager delegation violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Gatekeeper gate (SRP slice SC-1): SignalDecoder.AddElement /
        /// FinishSequence take the dominant gate as an explicit parameter
        /// and feed the transcript only through it. A gate bypass (direct
        /// transcript feed) fails here.
        /// </summary>
        [Fact]
        public void SignalDecoder_OwnsGate()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            var roots = ParseAllSources();
            var decoder = FindSourceType(roots, "SignalDecoder");
            if (decoder == null)
            {
                problems.Add("type 'SignalDecoder' not found in Source/ — gate regressed into the controller");
            }
            else
            {
                foreach (var call in new[] { Tuple.Create("AddElement", "AddElement"), Tuple.Create("FinishSequence", "FinishSequence") })
                {
                    var method = FindMethod(decoder, call.Item1);
                    if (method == null)
                    {
                        problems.Add($"SignalDecoder.{call.Item1} not found");
                        continue;
                    }
                    bool gated = method.ParameterList.Parameters
                        .Any(p => p.Identifier.Text == "processingDominant");
                    if (!gated)
                        problems.Add($"SignalDecoder.{call.Item1} lost the processingDominant gate parameter — non-dominant feed is unfiltered");
                    if (!InvokesName(method, call.Item2))
                        problems.Add($"SignalDecoder.{call.Item1} never invokes transcript.{call.Item2} — the tested class is bypassed");
                }
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} signal-decoder gate violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Factory gate (SRP slice 4a): PIP camera creation flows
        /// SextantController.TryOpen -&gt; SextantPipFactory.Ensure -&gt;
        /// ConfigurePipCamera. A re-inline of creation into the controller
        /// fails here.
        /// </summary>
        [Fact]
        public void SextantPipFactory_BuildsCameras()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            var roots = ParseAllSources();
            var controller = FindSourceType(roots, "SextantController");
            var factory = FindSourceType(roots, "SextantPipFactory");
            if (controller == null)
                problems.Add("type 'SextantController' not found in Source/");
            else
            {
                var open = FindMethod(controller, "TryOpen");
                if (open == null)
                    problems.Add("SextantController.TryOpen not found");
                else if (!InvokesName(open, "Ensure"))
                    problems.Add("SextantController.TryOpen never invokes SextantPipFactory.Ensure — creation regressed into the controller");
            }
            if (factory == null)
            {
                problems.Add("type 'SextantPipFactory' not found in Source/ — creation regressed into the controller");
            }
            else
            {
                var ensure = FindMethod(factory, "Ensure");
                if (ensure == null)
                    problems.Add("SextantPipFactory.Ensure not found");
                else if (!InvokesName(ensure, "ConfigurePipCamera"))
                    problems.Add("SextantPipFactory.Ensure never invokes ConfigurePipCamera — creation entry point is hollow");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} pip-factory violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Sternspreiz probes must remain opt-in. Mode 3 may suppress only the
        /// StarSkyRoot renderer around each PIP render and is pass-through;
        /// mode 4 may additionally redraw that same mesh with a flat
        /// diagnostic tint. Neither mode processes/draws any other celestial
        /// content nor alters Main Camera.
        /// This source gate pins the diagnostic boundary, not live acceptance.
        /// </summary>
        [Fact]
        public void Sternspreiz_Phase0IsProbeOnlyAndPipRouted()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            var roots = ParseAllSources();
            var effect = FindSourceType(roots, "SternspreizPipProbeEffect");
            var factory = FindSourceType(roots, "SextantPipFactory");
            var pose = FindSourceType(roots, "SextantPipPose");
            var probe = FindSourceType(roots, "SextantProbe");
            var renderScope = FindSourceType(roots, "SternspreizPipRenderScopePatch");
            var installer = FindSourceType(roots, "HarmonyPatchInstaller");

            if (effect == null)
            {
                problems.Add("SternspreizPipProbeEffect is missing");
            }
            else
            {
                string bases = effect.BaseList == null ? string.Empty : effect.BaseList.ToString();
                if (bases.IndexOf("IOpaqueImageEffect", StringComparison.Ordinal) < 0)
                    problems.Add("Phase-0 effect no longer implements UBOAT IOpaqueImageEffect");
                if (effect.ToString().IndexOf("GetInt(PrefsKey, 0)", StringComparison.Ordinal) < 0)
                    problems.Add("Probe preference must default to OFF (PlayerPrefs.GetInt(..., 0))");
                if (effect.ToString().IndexOf("StarSourceSuppressionMode", StringComparison.Ordinal) < 0)
                    problems.Add("Actual StarSkyRoot suppression must remain an explicit opt-in mode");
                if (effect.ToString().IndexOf("StarRedrawMode", StringComparison.Ordinal) < 0)
                    problems.Add("Phase-1b star redraw must remain an explicit opt-in mode");
                var configureProbe = FindMethod(effect, "CreateRedrawMaterial");
                if (configureProbe == null
                    || configureProbe.ToString().IndexOf(".mainTexture",
                        StringComparison.Ordinal) < 0)
                    problems.Add("Phase-1b redraw material must sample a texture, or untextured quads add nothing");
                var shaderResolver = FindMethod(effect, "ResolveProbeShader");
                if (shaderResolver == null
                    || shaderResolver.ToString().IndexOf("Hidden/Internal-Colored", StringComparison.Ordinal) < 0
                    || shaderResolver.ToString().IndexOf("Sprites/Default", StringComparison.Ordinal) < 0)
                    problems.Add("Probe fixture must use the live-available built-in color shader with fallback, not stripped Unlit/Color");
                var callback = FindMethod(effect, "RenderImageOpaque");
                if (callback == null)
                {
                    problems.Add("RenderImageOpaque callback is missing");
                }
                else
                {
                    string parameters = callback.ParameterList.ToString();
                    foreach (string required in new[] { "source", "destination", "zBuffer" })
                    {
                        if (parameters.IndexOf(required, StringComparison.Ordinal) < 0)
                            problems.Add("RenderImageOpaque no longer receives " + required);
                    }
                    var execute = FindMethod(effect, "ExecuteProbeDraw");
                    if (!InvokesName(callback, "ExecuteProbeDraw"))
                        problems.Add("RenderImageOpaque no longer owns callback execution");
                    if (execute == null || !InvokesName(execute, "Blit"))
                        problems.Add("Phase-0 callback no longer copies source to destination");
                    if (execute == null || !InvokesName(execute, "QueueSentinelDraws"))
                        problems.Add("Phase-0 callback no longer dispatches its test mesh draws");
                    if (execute == null
                        || execute.ToString().IndexOf("mode == CustomDrawMode", StringComparison.Ordinal) < 0
                        || execute.ToString().IndexOf("StarSourceSuppressionMode", StringComparison.Ordinal) >= 0)
                        problems.Add("Mode 3 must stay pass-through and never dispatch the sentinel draw");
                    if (execute == null
                        || execute.ToString().IndexOf("StarRedrawMode", StringComparison.Ordinal) < 0
                        || !InvokesName(execute, "DrawStarsMesh"))
                        problems.Add("Mode 4 must redraw only the existing star mesh after suppression");
                    var drawStars = FindMethod(effect, "DrawStarsMesh");
                    if (drawStars == null
                        || !drawStars.DescendantNodes().OfType<InvocationExpressionSyntax>()
                            .Any(inv => CalledName(inv) == "DrawMesh"
                                && inv.ArgumentList.Arguments.Count > 0
                                && inv.ArgumentList.Arguments[0].Expression.ToString() == "starsMesh"))
                        problems.Add("Phase-1b redraw must draw the shared star mesh, not a copy or the sentinel");
                    if (drawStars != null)
                    {
                        string drawStarsText = drawStars.ToString();
                        if (drawStarsText.IndexOf("Matrix4x4.identity", StringComparison.Ordinal) < 0
                            || drawStarsText.IndexOf("starRedrawMaterial", StringComparison.Ordinal) < 0
                            || drawStarsText.IndexOf("sentinelMesh", StringComparison.Ordinal) >= 0)
                            problems.Add("Phase-1b redraw must use identity matrix and the tint material, never the sentinel");
                    }
                    string callbackText = callback.ToString();
                    int offGate = callbackText.IndexOf("if (mode == 0)", StringComparison.Ordinal);
                    int executeCall = callbackText.IndexOf("ExecuteProbeDraw", StringComparison.Ordinal);
                    if (offGate < 0 || executeCall < 0 || offGate > executeCall
                        || callbackText.IndexOf("return;", offGate, StringComparison.Ordinal) < 0)
                        problems.Add("OFF mode must return before any callback blit/draw");
                    if (callbackText.IndexOf("IsSourceSuppressedForCurrentRender",
                            StringComparison.Ordinal) < 0
                        || callbackText.IndexOf("registered-source-not-suppressed-for-current-pip-render",
                            StringComparison.Ordinal) < 0
                        || callbackText.IndexOf("StarSourceSuppressionMode", StringComparison.Ordinal) < 0
                        || callbackText.IndexOf("StarRedrawMode", StringComparison.Ordinal) < 0)
                        problems.Add("Modes 2/3/4 must require current-frame suppression of the registered source");
                }
                var drawAtTransform = FindMethod(effect, "DrawAtTransform");
                if (drawAtTransform == null
                    || !drawAtTransform.DescendantNodes().OfType<InvocationExpressionSyntax>()
                        .Any(inv => CalledName(inv) == "DrawMesh"
                            && inv.ArgumentList.Arguments.Count > 0
                            && inv.ArgumentList.Arguments[0].Expression.ToString() == "sentinelMesh"))
                    problems.Add("Visible DrawMesh must use the shared sentinel source mesh");
                var depthDraw = FindMethod(effect, "DrawBehindOccluder");
                if (depthDraw == null
                    || !InvokesName(depthDraw, "WorldToViewportPoint")
                    || !InvokesName(depthDraw, "ViewportToWorldPoint")
                    || depthDraw.ToString().IndexOf("depthOccluderTransform.rotation",
                        StringComparison.Ordinal) < 0
                    || depthDraw.ToString().IndexOf("depthOccluderTransform.lossyScale",
                        StringComparison.Ordinal) < 0
                    || !depthDraw.DescendantNodes().OfType<InvocationExpressionSyntax>()
                        .Any(inv => CalledName(inv) == "DrawMesh"
                            && inv.ArgumentList.Arguments.Count > 0
                            && inv.ArgumentList.Arguments[0].Expression.ToString() == "sentinelMesh"))
                    problems.Add("Depth DrawMesh must share the occluder's projected ray and reuse the sentinel mesh");
                string effectText = effect.ToString();
                foreach (string forbidden in new[] { "SternspreizPsf", "StarStreak", "RebuildMesh" })
                {
                    if (effectText.IndexOf(forbidden, StringComparison.Ordinal) >= 0)
                        problems.Add("Probe effect contains production PSF/celestial mutation token: " + forbidden);
                }
                foreach (string celestial in new[] { "TOD_", "SunDisc", "MoonDisc", "PlanetRenderer", "ConstellationMesh" })
                {
                    if (effectText.IndexOf(celestial, StringComparison.Ordinal) >= 0)
                        problems.Add("Probe effect must not select/process celestial content beyond StarSkyRoot: " + celestial);
                }
            }

            if (factory == null)
            {
                problems.Add("SextantPipFactory is missing");
            }
            else
            {
                var ensureEffect = FindMethod(factory, "EnsurePhase0Effect");
                if (ensureEffect == null
                    || ensureEffect.ToString().IndexOf("ComponentName", StringComparison.Ordinal) < 0
                    || ensureEffect.ToString().IndexOf("AddComponent<SternspreizPipProbeEffect>",
                        StringComparison.Ordinal) < 0)
                    problems.Add("PIP effect attachment must check the component type name before AddComponent");
                var prepare = FindMethod(factory, "PrepareSternspreizPhase0");
                if (prepare == null || !InvokesName(prepare, "TryCreateSternspreizPhase0")
                    || !InvokesName(prepare, "SetPhase0RenderTarget"))
                    problems.Add("Sternspreiz setup and per-mode source registration must be owned by SextantPipFactory");
                if (prepare == null
                    || prepare.ToString().IndexOf("StarRedrawMode", StringComparison.Ordinal) < 0)
                    problems.Add("Mode 4 must hide the disposable fixture and reuse the StarSkyRoot registration");
                var findStarsRenderer = FindMethod(factory, "FindStarsMeshRenderer");
                if (findStarsRenderer == null
                    || !InvokesName(findStarsRenderer, "FindStarsMeshFilter")
                    || findStarsRenderer.ToString().IndexOf("GetComponent<MeshRenderer>", StringComparison.Ordinal) < 0)
                    problems.Add("Mode 3 must target the existing StarSkyRoot MeshRenderer, not a copied star source");
                var setRenderTarget = FindMethod(factory, "SetPhase0RenderTarget");
                if (setRenderTarget == null
                    || setRenderTarget.ToString().IndexOf("StarSourceSuppressionMode", StringComparison.Ordinal) < 0
                    || setRenderTarget.ToString().IndexOf("StarRedrawMode", StringComparison.Ordinal) < 0
                    || setRenderTarget.ToString().IndexOf("FindStarsMeshRenderer", StringComparison.Ordinal) < 0
                    || setRenderTarget.ToString().IndexOf("UnregisterPhase0RenderTarget", StringComparison.Ordinal) < 0
                    || setRenderTarget.ToString().IndexOf("Register(LeftCamera", StringComparison.Ordinal) < 0
                    || setRenderTarget.ToString().IndexOf("Register(RightCamera", StringComparison.Ordinal) < 0)
                    problems.Add("Modes 3/4 must replace the registered renderer with StarSkyRoot on both PIPs");
                var unregisterTarget = FindMethod(factory, "UnregisterPhase0RenderTarget");
                if (unregisterTarget == null
                    || !InvokesName(unregisterTarget, "Unregister")
                    || unregisterTarget.ToString().IndexOf("phase0RegisteredRenderer = null", StringComparison.Ordinal) < 0)
                    problems.Add("The actual-source registration must be released on OFF/close/teardown");
                var fixture = FindMethod(factory, "CreateSternspreizPhase0Fixture");
                if (fixture == null || !InvokesName(fixture, "CreatePhase0SentinelMesh"))
                    problems.Add("Phase-0 fixture must create its one shared, isolated sentinel mesh");
                var route = FindMethod(factory, "CanRoutePhase0Fixture");
                if (route == null || !InvokesName(route, "PassesContainLayer")
                    || route.ToString().IndexOf("viewCamera.cullingMask", StringComparison.Ordinal) < 0
                    || route.ToString().IndexOf("LeftCamera.cullingMask", StringComparison.Ordinal) < 0
                    || route.ToString().IndexOf("RightCamera.cullingMask", StringComparison.Ordinal) < 0)
                    problems.Add("Fixture routing must validate the shared Default bit in Main/PIP camera masks and PIP passes");
                if (factory.ToString().IndexOf("FindUnusedIncludedLayer", StringComparison.Ordinal) >= 0
                    || factory.ToString().IndexOf("LayerToName", StringComparison.Ordinal) >= 0)
                    problems.Add("Fixture routing must not require a custom unnamed layer unavailable in the live game");
                var destroy = FindMethod(factory, "Destroy");
                if (destroy == null || !InvokesName(destroy, "DestroySternspreizPhase0"))
                    problems.Add("PIP factory teardown must own Phase-0 resource cleanup");
                var suspend = FindMethod(factory, "SuspendSternspreizPhase0");
                if (suspend == null
                    || suspend.ToString().IndexOf("UnregisterPhase0RenderTarget", StringComparison.Ordinal) < 0
                    || suspend.ToString().IndexOf("SetPhase0SourcesEnabled(false)", StringComparison.Ordinal) < 0
                    || suspend.ToString().IndexOf("SetPhase0EffectsEnabled(false)", StringComparison.Ordinal) < 0)
                    problems.Add("Closing the sextant must unregister the source and disable both callbacks/fixture renderers");
                var close = FindMethod(FindSourceType(roots, "SextantController"), "Close");
                if (close == null || !InvokesName(close, "SuspendSternspreizPhase0"))
                    problems.Add("Sextant Close must suspend Phase-0 fixture renderers to keep Main Camera clean");
            }

            if (renderScope == null)
            {
                problems.Add("PIP-local source suppression patch is missing");
            }
            else
            {
                var patchCameraMethod = FindMethod(renderScope, "PatchCameraMethod");
                var applyRenderScope = FindMethod(renderScope, "Apply");
                var beginRender = FindMethod(renderScope, "BeginCameraRender");
                var completeRender = FindMethod(renderScope, "CompleteCameraRender");
                var currentSuppression = FindMethod(renderScope, "IsSourceSuppressedForCurrentRender");
                if (patchCameraMethod == null
                    || patchCameraMethod.ToString().IndexOf("ScriptableRenderContext", StringComparison.Ordinal) < 0
                    || patchCameraMethod.ToString().IndexOf("typeof(Camera)", StringComparison.Ordinal) < 0)
                    problems.Add("PIP-local hook must target DeepPipeline camera render methods by signature");
                if (applyRenderScope == null
                    || applyRenderScope.ToString().IndexOf("RenderDeferredCamera", StringComparison.Ordinal) < 0
                    || applyRenderScope.ToString().IndexOf("RenderForwardCamera", StringComparison.Ordinal) < 0)
                    problems.Add("PIP-local hook must install both deferred and forward camera render wrappers");
                if (beginRender == null
                    || beginRender.ToString().IndexOf("CustomDrawMode", StringComparison.Ordinal) < 0
                    || beginRender.ToString().IndexOf("StarSourceSuppressionMode", StringComparison.Ordinal) < 0
                    || beginRender.ToString().IndexOf("StarRedrawMode", StringComparison.Ordinal) < 0
                    || beginRender.ToString().IndexOf("Renderer renderer = target.Renderer", StringComparison.Ordinal) < 0
                    || beginRender.ToString().IndexOf("renderer.enabled = false", StringComparison.Ordinal) < 0)
                    problems.Add("Only Modes 2/3/4 may disable the registered renderer inside the PIP scope");
                if (completeRender == null
                    || completeRender.ToString().IndexOf("target.Renderer.enabled = true", StringComparison.Ordinal) < 0
                    || completeRender.ToString().IndexOf("RestorationCount++", StringComparison.Ordinal) < 0)
                    problems.Add("PIP source renderer must be restored and counted when the render scope exits");
                if (currentSuppression == null
                    || currentSuppression.ToString().IndexOf("LastSuppressionFrame == Time.frameCount",
                        StringComparison.Ordinal) < 0)
                    problems.Add("Source-suppression evidence must belong to the current PIP frame");
                if (renderScope.ToString().IndexOf("rendererName", StringComparison.Ordinal) < 0
                    || renderScope.ToString().IndexOf("rendererId", StringComparison.Ordinal) < 0)
                    problems.Add("Runtime evidence must identify which renderer each PIP suppresses");
                foreach (string celestial in new[] { "TOD_", "SunDisc", "MoonDisc", "PlanetRenderer", "ConstellationMesh" })
                {
                    if (renderScope.ToString().IndexOf(celestial, StringComparison.Ordinal) >= 0)
                        problems.Add("Render scope must not select/process celestial content beyond StarSkyRoot: " + celestial);
                }
                var apply = FindMethod(installer, "Apply");
                if (apply == null
                    || apply.ToString().IndexOf("SternspreizPipRenderScopePatch.Apply", StringComparison.Ordinal) < 0)
                    problems.Add("Startup must install the PIP-local source suppression hooks");
            }

            if (pose == null)
            {
                problems.Add("SextantPipPose is missing");
            }
            else
            {
                var update = FindMethod(pose, "Update");
                var sync = FindMethod(pose, "SyncCameraState");
                if (update == null || sync == null
                    || !InvokesName(update, "PrepareSternspreizPhase0"))
                    problems.Add("Probe setup must precede the PIP camera-mask update");
                if (sync != null)
                {
                    string syncText = sync.ToString();
                    if (syncText.IndexOf("ApplyMaskToPasses", StringComparison.Ordinal) < 0
                        || syncText.IndexOf("cam.cullingMask", StringComparison.Ordinal) < 0
                        || syncText.IndexOf("Sternspreiz", StringComparison.Ordinal) >= 0
                        || syncText.IndexOf("probeLayer", StringComparison.Ordinal) >= 0)
                        problems.Add("Normal PIP masks stay synchronized; Phase-0 suppression must not rewrite either camera mask");
                    if (syncText.IndexOf("MainCamera", StringComparison.Ordinal) >= 0)
                        problems.Add("PIP route must not write Main Camera state");
                }
            }

            var snapshot = probe != null ? FindMethod(probe, "Snapshot") : null;
            if (snapshot == null
                || snapshot.ToString().IndexOf("SternspreizPhase0SnapshotForProbe",
                    StringComparison.Ordinal) < 0)
                problems.Add("Existing sextant snapshot must expose the Phase-0 evidence");

            Assert.True(problems.Count == 0,
                $"{problems.Count} Sternspreiz Phase-0 source-gate violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Phase 1 is only a Unity-free math candidate (kernel + field
        /// convolution). Until the source-mask route is proven and a runtime
        /// stage is explicitly selected, keep that math disconnected from
        /// every mod runtime source.
        /// </summary>
        [Fact]
        public void Sternspreiz_Phase1MathRemainsPureAndOffline()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            var roots = ParseAllSources();
            var math = FindSourceType(roots, "SternspreizPsfMath");
            if (math == null)
            {
                problems.Add("SternspreizPsfMath is missing");
            }
            else
            {
                var method = FindMethod(math, "BuildHorizontalLineSpreadKernel");
                if (method == null || !method.Modifiers.Any(SyntaxKind.StaticKeyword))
                    problems.Add("Phase-1 kernel must remain a static pure-math method");

                var unit = math.SyntaxTree.GetRoot() as CompilationUnitSyntax;
                if (unit != null)
                {
                    foreach (var directive in unit.Usings)
                    {
                        string usedNamespace = directive.Name.ToString();
                        if (usedNamespace.StartsWith("UnityEngine", StringComparison.Ordinal)
                            || usedNamespace.StartsWith("UBOAT.", StringComparison.Ordinal)
                            || usedNamespace.StartsWith("DWS.", StringComparison.Ordinal))
                            problems.Add("Phase-1 math imports runtime API: " + usedNamespace);
                    }
                }
            }

            foreach (var root in roots)
            {
                foreach (var identifier in root.Item2.DescendantNodes()
                    .OfType<IdentifierNameSyntax>())
                {
                    if (identifier.Identifier.Text != "SternspreizPsfMath"
                        && identifier.Identifier.Text != "BuildHorizontalLineSpreadKernel"
                        && identifier.Identifier.Text != "ConvolveHorizontalField")
                        continue;

                    bool insideMathType = identifier.Ancestors()
                        .OfType<TypeDeclarationSyntax>()
                        .Any(type => type.Identifier.Text == "SternspreizPsfMath");
                    if (!insideMathType)
                        problems.Add("Runtime source references the isolated phase-1 math: "
                            + root.Item1);
                }
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} Sternspreiz Phase-1 boundary violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Hiding the game's decorative star sphere must be fail-safe: only
        /// while the mod's own catalog sky actually draws with the bundled
        /// shader, patched onto TOD_Sky.LateUpdate, and revertible by
        /// preference.
        /// </summary>
        [Fact]
        public void StarLayerSuppression_IsFailSafeAndPatchesTodSky()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            var roots = ParseAllSources();
            var policy = FindSourceType(roots, "StarLayerSuppression");
            var suppressor = FindSourceType(roots, "GameStarLayerSuppressor");
            var stars = FindSourceType(roots, "StarsMesh");
            var installer = FindSourceType(roots, "HarmonyPatchInstaller");

            if (policy == null)
                problems.Add("StarLayerSuppression decision is missing");
            else
            {
                string text = policy.ToString();
                if (text.IndexOf("\"TOD_StarVisibility\"", StringComparison.Ordinal) < 0)
                    problems.Add("Suppression must zero the verified TOD global name");
                var shouldHide = FindMethod(policy, "ShouldHide");
                if (shouldHide == null
                    || shouldHide.ToString().IndexOf("&&", StringComparison.Ordinal) < 0)
                    problems.Add("Suppression decision must require BOTH the preference and our active sky");
            }

            if (suppressor == null)
            {
                problems.Add("GameStarLayerSuppressor is missing");
            }
            else
            {
                string text = suppressor.ToString();
                if (text.IndexOf("\"TOD_Sky\"", StringComparison.Ordinal) < 0
                    || text.IndexOf("\"LateUpdate\"", StringComparison.Ordinal) < 0)
                    problems.Add("Suppressor must patch TOD_Sky.LateUpdate (postfix after the game writes the global)");
                if (text.IndexOf("GetInt(PrefsKey, 1)", StringComparison.Ordinal) < 0)
                    problems.Add("Suppressor preference must default to ON while our sky is authoritative");
                var postfix = FindMethod(suppressor, "AfterSkyLateUpdate");
                if (postfix == null
                    || !InvokesName(postfix, "ShouldHide")
                    || postfix.ToString().IndexOf("StarsMesh.UsesBundledStarShader", StringComparison.Ordinal) < 0)
                    problems.Add("Suppressor postfix must gate on our own sky being drawn (fail-safe)");
                if (postfix == null
                    || !InvokesName(postfix, "SetGlobalFloat")
                    || postfix.ToString().IndexOf("PropertyToID", StringComparison.Ordinal) < 0)
                    problems.Add("Suppressor postfix must zero the cached TOD global");
            }

            if (stars == null
                || stars.ToString().IndexOf("UsesBundledStarShader", StringComparison.Ordinal) < 0)
                problems.Add("StarsMesh must report when the bundled star shader is actually in use");

            var apply = installer != null ? FindMethod(installer, "Apply") : null;
            if (apply == null
                || apply.ToString().IndexOf("GameStarLayerSuppressor.Apply", StringComparison.Ordinal) < 0)
                problems.Add("Startup must install the game-star suppression postfix");

            Assert.True(problems.Count == 0,
                $"{problems.Count} game-star suppression violation(s):\n" + string.Join("\n", problems));
        }

        /// <summary>
        /// The Sternspreiz lens must stay an opt-in PIP-only feature: catalog
        /// streaks drawn through the proven IOpaqueImageEffect path, gated by
        /// the sky state, sized in angles (not pixels), loaded through the
        /// bundle ladder with a built-in fallback, and never touching the star
        /// mesh, Main Camera or a per-frame catalog rebuild.
        /// </summary>
        [Fact]
        public void Sternspreiz_LensIsOptInPipOnlyAndGated()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            var roots = ParseAllSources();
            var lens = FindSourceType(roots, "SternspreizLensEffect");
            var field = FindSourceType(roots, "SternspreizLensField");
            var profile = FindSourceType(roots, "SternspreizLineProfile");
            var factory = FindSourceType(roots, "SextantPipFactory");
            var pose = FindSourceType(roots, "SextantPipPose");
            var controller = FindSourceType(roots, "SextantController");

            if (lens == null)
            {
                problems.Add("SternspreizLensEffect is missing");
            }
            else
            {
                string bases = lens.BaseList == null ? string.Empty : lens.BaseList.ToString();
                if (bases.IndexOf("IOpaqueImageEffect", StringComparison.Ordinal) < 0)
                    problems.Add("Lens must draw through UBOAT's IOpaqueImageEffect");
                string lensText = lens.ToString();
                if (lensText.IndexOf("GetInt(PrefsKey, 0)", StringComparison.Ordinal) < 0)
                    problems.Add("Lens preference must default to OFF (PlayerPrefs.GetInt(..., 0))");

                var callback = FindMethod(lens, "RenderImageOpaque");
                if (callback == null)
                {
                    problems.Add("Lens RenderImageOpaque callback is missing");
                }
                else if (!InvokesName(callback, "CanSightSky")
                    || !InvokesName(callback, "DrawStreaks"))
                {
                    problems.Add("Lens callback must gate on CanSightSky and then draw the streaks");
                }

                var canSight = FindMethod(lens, "CanSightSky");
                if (canSight == null
                    || canSight.ToString().IndexOf("SkyVisibilityResolver.IsPlayerOutside", StringComparison.Ordinal) < 0
                    || canSight.ToString().IndexOf("SextantController.IsOpen", StringComparison.Ordinal) < 0)
                    problems.Add("Lens must gate on the shared outside/map resolver and the open sextant");

                var draw = FindMethod(lens, "DrawStreaks");
                if (draw == null
                    || !InvokesName(draw, "DrawMesh")
                    || draw.ToString().IndexOf("Blit", StringComparison.Ordinal) < 0
                    || draw.ToString().IndexOf("SetViewProjectionMatrices", StringComparison.Ordinal) < 0
                    || draw.ToString().IndexOf("worldToCameraMatrix", StringComparison.Ordinal) < 0)
                    problems.Add("Lens must pass the image through then draw with the PIP camera matrices");
                if (draw == null
                    || draw.DescendantNodes().OfType<InvocationExpressionSyntax>()
                        .All(inv => CalledName(inv) != "DrawMesh"
                            || inv.ArgumentList.Arguments.Count < 1
                            || inv.ArgumentList.Arguments[0].Expression.ToString() != "streakMesh"))
                    problems.Add("Lens DrawMesh must draw the lens-owned streak mesh, not a foreign mesh");

                var ensureMaterial = FindMethod(lens, "EnsureMaterial");
                if (ensureMaterial == null
                    || ensureMaterial.ToString().IndexOf("LoadBundleAsset", StringComparison.Ordinal) < 0
                    || ensureMaterial.ToString().IndexOf("Shader.Find(\"Sprites/Default\")", StringComparison.Ordinal) < 0)
                    problems.Add("Lens material must come from the bundle with a built-in fallback");
                if (lensText.IndexOf("HasProperty", StringComparison.Ordinal) < 0)
                    problems.Add("Lens must verify material properties before writing them (silent no-ops)");

                foreach (string forbidden in new[] { "StarSkyRoot", "RebuildMesh", "Camera.main", "MainCamera" })
                {
                    if (lensText.IndexOf(forbidden, StringComparison.Ordinal) >= 0)
                        problems.Add("Lens must not touch the celestial mesh or Main Camera: " + forbidden);
                }
                if (lensText.IndexOf("SizePx", StringComparison.Ordinal) >= 0)
                    problems.Add("Lens must not size streaks by the point-sprite pixel size");
            }

            if (field == null || profile == null)
            {
                problems.Add("Lens field/profile math is missing");
            }
            else
            {
                foreach (var candidate in new[] { field, profile })
                {
                    var unit = candidate.SyntaxTree.GetRoot() as CompilationUnitSyntax;
                    if (unit == null)
                        continue;
                    foreach (var directive in unit.Usings)
                    {
                        string used = directive.Name.ToString();
                        if (used.StartsWith("UnityEngine", StringComparison.Ordinal)
                            || used.StartsWith("UBOAT.", StringComparison.Ordinal)
                            || used.StartsWith("DWS.", StringComparison.Ordinal))
                            problems.Add("Lens math must stay Unity-free: " + candidate.Identifier.Text + " imports " + used);
                    }
                }
                if (field.ToString().IndexOf("AngularWidthMeters", StringComparison.Ordinal) < 0
                    || field.ToString().IndexOf("LineLengthDeg", StringComparison.Ordinal) < 0)
                    problems.Add("Lens field must size streaks by angular length, not by pixels");
            }

            if (factory == null)
            {
                problems.Add("SextantPipFactory is missing");
            }
            else
            {
                var prepare = FindMethod(factory, "PrepareLens");
                if (prepare == null
                    || prepare.ToString().IndexOf("SternspreizLensEffect.Enabled", StringComparison.Ordinal) < 0
                    || !InvokesName(prepare, "EnsureAndConfigureLens"))
                    problems.Add("Lens attachment must be driven by the PIP factory on the preference gate");
                if (prepare == null
                    || prepare.ToString().IndexOf("RightCamera", StringComparison.Ordinal) < 0
                    || prepare.ToString().IndexOf("LeftCamera", StringComparison.Ordinal) >= 0)
                    problems.Add("The glass sits in the index beam: only the alidade (right) PIP may carry the lens");
                var suspend = FindMethod(factory, "SuspendLens");
                if (suspend == null
                    || suspend.ToString().IndexOf("SetLensEffectsEnabled(false)", StringComparison.Ordinal) < 0)
                    problems.Add("Closing the sextant must disable both lens effects");
                var destroy = FindMethod(factory, "Destroy");
                if (destroy == null || !InvokesName(destroy, "DestroyLens"))
                    problems.Add("PIP factory teardown must release the lens effect and its resources");
                var remove = FindMethod(factory, "RemoveLensEffect");
                if (remove == null
                    || remove.ToString().IndexOf("SternspreizLensEffect.ComponentName", StringComparison.Ordinal) < 0)
                    problems.Add("Lens removal must be copy-agnostic (type-name check)");
            }

            if (pose != null)
            {
                var update = FindMethod(pose, "Update");
                if (update == null || !InvokesName(update, "PrepareLens"))
                    problems.Add("The per-frame PIP pose update must drive the lens");
            }

            if (controller != null)
            {
                var close = FindMethod(controller, "Close");
                if (close == null || !InvokesName(close, "SuspendLens"))
                    problems.Add("Sextant Close must suspend the lens effect");

                var toggle = FindMethod(controller, "ToggleLens");
                if (toggle == null
                    || toggle.ToString().IndexOf("SternspreizLensEffect.PrefsKey", StringComparison.Ordinal) < 0)
                    problems.Add("Key 4 must toggle the lens through the SternspreizLensEffect.PrefsKey gate");

                var label = FindMethod(controller, "UpdateFilterLabel");
                if (label == null
                    || label.ToString().IndexOf("FormatFiltersAndLens", StringComparison.Ordinal) < 0
                    || label.ToString().IndexOf("SternspreizLensEffect.Enabled", StringComparison.Ordinal) < 0)
                    problems.Add("The readout label must fold the lens state into FormatFiltersAndLens");
            }

            var poller = FindSourceType(roots, "SextantInputPoller");
            if (poller == null)
            {
                problems.Add("SextantInputPoller is missing");
            }
            else
            {
                var pollLens = FindMethod(poller, "PollLens");
                if (pollLens == null
                    || pollLens.ToString().IndexOf("Alpha4", StringComparison.Ordinal) < 0
                    || pollLens.ToString().IndexOf("Keypad4", StringComparison.Ordinal) < 0)
                    problems.Add("Key 4 must be polled as Alpha4/Keypad4 in SextantInputPoller.PollLens");
            }

            var help = FindSourceType(roots, "SextantHelpPanel");
            if (help == null
                || help.ToString().IndexOf("RadioNav.SternspreizLengthDeg", StringComparison.Ordinal) < 0)
                problems.Add("The help panel must document key 4 (RadioNav.SternspreizLengthDeg anchor)");

            Assert.True(problems.Count == 0,
                $"{problems.Count} Sternspreiz lens violation(s):\n" + string.Join("\n", problems));
        }

        /// <summary>
        /// The streak sprite must be baked as a 2-D alpha map (columns along the
        /// line, rows across it) so pass 0 - the only pass the lens draws - renders
        /// the width taper. A 1-pixel-tall texture is exactly the bug: pass 0's
        /// fragment is tex2D(_MainTex, uv) * i.color, so the taper only exists if
        /// the baked profile carries it across the line.
        /// </summary>
        [Fact]
        public void Sternspreiz_StreakProfileIsBakedWithWidthTaper()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            var roots = ParseAllSources();
            var lens = FindSourceType(roots, "SternspreizLensEffect");
            var profile = FindSourceType(roots, "SternspreizLineProfile");

            if (profile == null)
            {
                problems.Add("SternspreizLineProfile is missing");
            }
            else
            {
                foreach (string taper in new[]
                {
                    "DefaultStreakRows", "DefaultWidthTaper", "DefaultMinWidth", "DefaultCoreFraction"
                })
                {
                    if (!HasFieldNamed(profile, taper))
                        problems.Add("SternspreizLineProfile must expose the taper parameter: " + taper);
                }
            }

            if (lens == null)
            {
                problems.Add("SternspreizLensEffect is missing");
            }
            else
            {
                var ensure = FindMethod(lens, "EnsureProfile");
                if (ensure == null)
                {
                    problems.Add("SternspreizLensEffect.EnsureProfile is missing");
                }
                else if (!InvokesName(ensure, "BuildStreakAlphaMap"))
                {
                    problems.Add(
                        "EnsureProfile must bake the 2-D alpha map from SternspreizLineProfile.BuildStreakAlphaMap");
                }
                else if (Texture2DHeightIsOne(ensure))
                {
                    problems.Add(
                        "EnsureProfile must bake a multi-row texture (rows >= 2): a 1-pixel-tall profile has no width taper");
                }
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} Sternspreiz streak-profile violation(s):\n" + string.Join("\n", problems));
        }

        private static bool HasFieldNamed(TypeDeclarationSyntax type, string name)
        {
            if (type == null)
                return false;
            foreach (var field in type.Members.OfType<FieldDeclarationSyntax>())
            {
                foreach (var variable in field.Declaration.Variables)
                {
                    if (variable.Identifier.Text == name)
                        return true;
                }
            }
            return false;
        }

        private static bool Texture2DHeightIsOne(MethodDeclarationSyntax method)
        {
            foreach (var creation in method.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            {
                if (creation.Type.ToString() != "Texture2D")
                    continue;
                if (creation.ArgumentList == null || creation.ArgumentList.Arguments.Count < 2)
                    return true;
                var height = creation.ArgumentList.Arguments[1].Expression;
                var literal = height as LiteralExpressionSyntax;
                if (literal != null)
                {
                    object value = literal.Token.Value;
                    if (value is int && (int)value < 2)
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Pain-latch gate (SRP slice 4d): glare application flows
        /// SextantController.UpdateGlare -&gt; SextantGlareCoordinator.Update -&gt;
        /// ApplyGlare. A re-inline of the hysteresis fails here.
        /// </summary>
        [Fact]
        public void GlareCoordinator_HandlesPain()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            var roots = ParseAllSources();
            var controller = FindSourceType(roots, "SextantController");
            var coordinator = FindSourceType(roots, "SextantGlareCoordinator");
            if (controller == null)
                problems.Add("type 'SextantController' not found in Source/");
            else
            {
                var glare = FindMethod(controller, "UpdateGlare");
                if (glare == null)
                    problems.Add("SextantController.UpdateGlare not found");
                else if (!InvokesName(glare, "Update"))
                    problems.Add("SextantController.UpdateGlare never invokes SextantGlareCoordinator.Update — glare regressed into the controller");
            }
            if (coordinator == null)
            {
                problems.Add("type 'SextantGlareCoordinator' not found in Source/ — pain latch regressed into the controller");
            }
            else
            {
                var update = FindMethod(coordinator, "Update");
                if (update == null)
                    problems.Add("SextantGlareCoordinator.Update not found");
                else if (!InvokesName(update, "ApplyGlare"))
                    problems.Add("SextantGlareCoordinator.Update never invokes ApplyGlare — the pain latch is bypassed");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} glare-coordinator violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Label gate (SRP slice RadioLocale): locale override flows
        /// RadioStoryIntegration.OverrideLocaleKey -&gt; RadioLocale.MatchesLocaleKey /
        /// GetLocalizedLabel -&gt; GetLabel. A re-inline of the table fails here.
        /// </summary>
        [Fact]
        public void RadioLocale_OwnsLabels()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            var roots = ParseAllSources();
            var integration = FindSourceType(roots, "RadioStoryIntegration");
            var locale = FindSourceType(roots, "RadioLocale");
            if (integration == null)
                problems.Add("type 'RadioStoryIntegration' not found in Source/");
            else
            {
                var over = FindMethod(integration, "OverrideLocaleKey");
                if (over == null)
                    problems.Add("RadioStoryIntegration.OverrideLocaleKey not found");
                else
                {
                    if (!InvokesName(over, "MatchesLocaleKey"))
                        problems.Add("RadioStoryIntegration.OverrideLocaleKey never invokes RadioLocale.MatchesLocaleKey — key match regressed into the facade");
                    if (!InvokesName(over, "GetLocalizedLabel"))
                        problems.Add("RadioStoryIntegration.OverrideLocaleKey never invokes RadioLocale.GetLocalizedLabel — labels regressed into the facade");
                }
            }
            if (locale == null)
            {
                problems.Add("type 'RadioLocale' not found in Source/ — labels regressed into the facade");
            }
            else
            {
                var label = FindMethod(locale, "GetLocalizedLabel");
                if (label == null)
                    problems.Add("RadioLocale.GetLocalizedLabel not found");
                else if (!InvokesName(label, "GetLabel"))
                    problems.Add("RadioLocale.GetLocalizedLabel never invokes GetLabel — table selection is bypassed");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} radio-locale violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Ephemeris gate (SRP slice 4c): own-sun direction flows
        /// SextantController.TryGetOwnSun -&gt; SunEphemeris.Compute -&gt;
        /// SunRaDec/RaDecToEnu. A re-inline of the Meeus math fails here.
        /// </summary>
        [Fact]
        public void SunEphemeris_ComputesSun()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            var roots = ParseAllSources();
            var controller = FindSourceType(roots, "SextantController");
            var ephemeris = FindSourceType(roots, "SunEphemeris");
            if (controller == null)
                problems.Add("type 'SextantController' not found in Source/");
            else
            {
                var own = FindMethod(controller, "TryGetOwnSun");
                if (own == null)
                    problems.Add("SextantController.TryGetOwnSun not found");
                else if (!InvokesName(own, "Compute"))
                    problems.Add("SextantController.TryGetOwnSun never invokes SunEphemeris.Compute — ephemeris regressed into the controller");
            }
            if (ephemeris == null)
            {
                problems.Add("type 'SunEphemeris' not found in Source/ — ephemeris regressed into the controller");
            }
            else
            {
                var compute = FindMethod(ephemeris, "Compute");
                if (compute == null)
                    problems.Add("SunEphemeris.Compute not found");
                else
                {
                    if (!InvokesName(compute, "SunRaDec"))
                        problems.Add("SunEphemeris.Compute never invokes SunRaDec — the Meeus source is bypassed");
                    if (!InvokesName(compute, "RaDecToEnu"))
                        problems.Add("SunEphemeris.Compute never invokes RaDecToEnu — the axis mapping is bypassed");
                }
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} sun-ephemeris violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Body-routing gate (SEXTANT_PLAN v2.2): the sextant must sight any
        /// body through CelestialEphemeris, not just the Sun. Pins
        /// CelestialEphemeris.Compute -> SunRaDec/MoonRaDec/TryGetStarRaDec,
        /// the calibrated JdForKind, the body-aware BuildObservation routing
        /// and the Moon identification. A re-inline or a Sun-only regression
        /// fails here.
        /// </summary>
        [Fact]
        public void CelestialEphemeris_RoutesBodies()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            var roots = ParseAllSources();

            var ephemeris = FindSourceType(roots, "CelestialEphemeris");
            if (ephemeris == null)
            {
                problems.Add("type 'CelestialEphemeris' not found in Source/");
            }
            else
            {
                var compute = FindMethod(ephemeris, "Compute");
                if (compute == null)
                    problems.Add("CelestialEphemeris.Compute not found");
                else
                {
                    if (!InvokesName(compute, "SunRaDec"))
                        problems.Add("CelestialEphemeris.Compute never invokes SunRaDec");
                    if (!InvokesName(compute, "MoonRaDec"))
                        problems.Add("CelestialEphemeris.Compute never invokes MoonRaDec");
                    if (!InvokesName(compute, "TryGetStarRaDec"))
                        problems.Add("CelestialEphemeris.Compute never invokes TryGetStarRaDec");
                }
                var jdForKind = FindMethod(ephemeris, "JdForKind");
                if (jdForKind == null)
                    problems.Add("CelestialEphemeris.JdForKind not found");
                else if (!InvokesName(jdForKind, "CalibratedJd"))
                    problems.Add("CelestialEphemeris.JdForKind never invokes CalibratedJd");

                // P2/R5: the observer-aware path must carry the Moon's
                // topocentric parallax correction.
                var forObserver = FindMethod(ephemeris, "ComputeForObserver");
                if (forObserver == null)
                    problems.Add("CelestialEphemeris.ComputeForObserver not found");
                else if (!InvokesName(forObserver, "MoonTopocentricRaDec"))
                    problems.Add("CelestialEphemeris.ComputeForObserver never invokes MoonTopocentricRaDec");
            }

            var runningFix = FindSourceType(roots, "RunningFixCalculator");
            if (runningFix == null)
            {
                problems.Add("type 'RunningFixCalculator' not found in Source/");
            }
            else
            {
                bool routes = runningFix.Members.OfType<MethodDeclarationSyntax>()
                    .Where(m => m.Identifier.Text == "BuildObservation"
                        && m.ParameterList.Parameters.Count == 5)
                    .Any(m => (InvokesName(m, "Compute") || InvokesName(m, "ComputeForObserver"))
                        && InvokesName(m, "JdFor"));
                if (!routes)
                    problems.Add("RunningFixCalculator.BuildObservation(5 args) never routes through CelestialEphemeris");
            }

            var starId = FindSourceType(roots, "SextantStarId");
            if (starId == null)
                problems.Add("type 'SextantStarId' not found in Source/");
            else if (!starId.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Any(inv => inv.Expression is MemberAccessExpressionSyntax ma
                    && ma.Name.Identifier.Text == "MoonTopocentricRaDec"))
                problems.Add("SextantStarId never invokes MoonTopocentricRaDec — the Moon cannot be sighted topocentrically");

            Assert.True(problems.Count == 0,
                $"{problems.Count} celestial-body routing violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// INV-ALPHA gate (CELESTIAL_REALISM_PLAN P3/P4): the star layer must
        /// assemble visibility in exactly one place. StarsMesh may read
        /// GetVisibility only for the material tint — a second reference
        /// (e.g. multiplied into the vertex alpha) double-dims the whole
        /// field, a regression this project already fixed once.
        /// </summary>
        [Fact]
        public void StarsMesh_VisibilityReferencedOnce()
        {
            if (!ModCompiler.HasModSource) return;

            var roots = ParseAllSources();
            var type = FindSourceType(roots, "StarsMesh");
            Assert.True(type != null, "type 'StarsMesh' not found in Source/");

            int references = type.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Count(i => i.Expression.ToString().EndsWith("GetVisibility"));

            Assert.True(references == 1,
                "StarsMesh must reference GetVisibility exactly once (INV-ALPHA: material tint only); found "
                + references);
        }

        /// <summary>
        /// Hip=0 sentinel gate (SEXTANT_PLAN v2.2 regression F1): the star
        /// scan must skip no-HIP catalog rows, and the worksheet must reject a
        /// body whose ephemeris cannot be resolved instead of silently
        /// building a wrong fix.
        /// </summary>
        [Fact]
        public void SextantStarId_SkipsNoHipSentinel()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            var roots = ParseAllSources();

            var starId = FindSourceType(roots, "SextantStarId");
            if (starId == null)
            {
                problems.Add("type 'SextantStarId' not found in Source/");
            }
            else
            {
                var findStar = FindMethod(starId, "TryFindStar");
                if (findStar == null)
                    problems.Add("SextantStarId.TryFindStar not found");
                else if (!InvokesName(findStar, "IsSightableStar"))
                    problems.Add("SextantStarId.TryFindStar never invokes IsSightableStar — Hip=0 sentinel can be sighted");
            }

            var worksheet = FindSourceType(roots, "SextantWorksheet");
            if (worksheet == null)
            {
                problems.Add("type 'SextantWorksheet' not found in Source/");
            }
            else
            {
                bool captureFound = worksheet.Members.OfType<MethodDeclarationSyntax>()
                    .Any(m => m.Identifier.Text == "CaptureObservation");
                bool validates = worksheet.Members.OfType<MethodDeclarationSyntax>()
                    .Where(m => m.Identifier.Text == "CaptureObservation")
                    .Any(m => InvokesName(m, "Compute"));
                if (!captureFound)
                    problems.Add("SextantWorksheet.CaptureObservation not found");
                else if (!validates)
                    problems.Add("SextantWorksheet.CaptureObservation never validates CelestialEphemeris.Compute — unknown bodies silently fall back to the sun");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} no-HIP sentinel violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Sticky-body gates (swarm review 2026-11-04): the selection itself
        /// lives in the pure BodySelection, but the two invariants a
        /// regression would break silently are imperative —
        ///  (a) SextantStarId.ClearSelection must reset the hold tracker, or
        ///      the next tick re-confirms an empty selection (InCone=true with
        ///      nothing selected) and the C calibration writes a clamped
        ///      index error into PlayerPrefs;
        ///  (b) SextantInputPoller.PollIndexError must require a selection —
        ///      it is the only persistent write in the poller.
        /// Mutation checks: drop tracker.Reset() -> (a) FAIL; drop HasSelection
        /// -> (b) FAIL.
        /// </summary>
        [Fact]
        public void StickyBody_TrackerAndCalibrationGates()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            var roots = ParseAllSources();

            var starId = FindSourceType(roots, "SextantStarId");
            if (starId == null)
            {
                problems.Add("type 'SextantStarId' not found in Source/");
            }
            else
            {
                var clear = FindMethod(starId, "ClearSelection");
                if (clear == null)
                    problems.Add("SextantStarId.ClearSelection not found");
                else if (!InvokesName(clear, "Reset"))
                    problems.Add("SextantStarId.ClearSelection never resets the hold tracker — an empty selection can re-confirm on the next tick");
            }

            var poller = FindSourceType(roots, "SextantInputPoller");
            if (poller == null)
            {
                problems.Add("type 'SextantInputPoller' not found in Source/");
            }
            else
            {
                var indexError = FindMethod(poller, "PollIndexError");
                if (indexError == null)
                    problems.Add("SextantInputPoller.PollIndexError not found");
                else if (!indexError.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                    .Any(a => a.Name.Identifier.Text == "HasSelection"))
                    problems.Add("SextantInputPoller.PollIndexError does not gate on HasSelection — calibration can write from an empty selection");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} sticky-body violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Stabilization open-edge gate (swarm review 2026-11-04): the filter
        /// state lives in static fields and survives an open/close cycle (the
        /// PIP cameras live for the whole scene session), so the session-open
        /// edge must snap them — otherwise the first frame glides from the
        /// pose frozen at Close. Rules:
        ///  A) SextantPipPose.ResetFilters exists and clears all three flags;
        ///  B) SextantPipPose.Reset delegates to it;
        ///  C) AttachAndConfigureCameras calls ResetFilters BEFORE the first
        ///     UpdatePipPose.
        /// Mutation checks: remove the call -> FAIL; move it after
        /// UpdatePipPose -> FAIL; drop lastInsideBoat = false -> FAIL.
        /// </summary>
        [Fact]
        public void SextantPip_OpenResetsStabilizationFilters()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            var roots = ParseAllSources();

            var pose = FindSourceType(roots, "SextantPipPose");
            if (pose == null)
            {
                problems.Add("type 'SextantPipPose' not found in Source/");
            }
            else
            {
                var resetFilters = FindMethod(pose, "ResetFilters");
                if (resetFilters == null)
                {
                    problems.Add("SextantPipPose.ResetFilters not found");
                }
                else
                {
                    foreach (string field in new[]
                    {
                        "yawOffset", "yawTrimDeg", "idleSeconds", "wasLocked",
                        "lockedYaw", "Locked"
                    })
                    {
                        bool cleared = resetFilters.DescendantNodes()
                            .OfType<AssignmentExpressionSyntax>()
                            .Any(a => a.Left is IdentifierNameSyntax left
                                && left.Identifier.Text == field
                                && (a.Right.ToString() == "false"
                                    || a.Right.ToString() == "0f"));
                        if (!cleared)
                            problems.Add("SextantPipPose.ResetFilters does not clear " + field);
                    }
                }

                // The POSITION must stay raw (bino parity, author decision
                // 2026-11-04): the game's bridge binocular keeps its camera at
                // the character's head undamped, and any position filtering or
                // freeze showed as height jumps in the ocular.
                if (FindMethod(pose, "StabilizePosition") != null)
                {
                    problems.Add("SextantPipPose.StabilizePosition is back — the position must stay raw (bino parity, height jumps)");
                }

                // The yaw must stay RAW (author feedback 2026-11-04: a
                // low-passed yaw made the view slide behind the mouse); only
                // the washout offset may touch it.
                var stabilizeYaw = FindMethod(pose, "StabilizeYaw");
                if (stabilizeYaw == null)
                {
                    problems.Add("SextantPipPose.StabilizeYaw not found");
                }
                else if (InvokesName(stabilizeYaw, "Smooth")
                    || InvokesName(stabilizeYaw, "SmoothAngle"))
                {
                    problems.Add("SextantPipPose.StabilizeYaw low-passes the yaw — the aim would slide behind the mouse (raw + washout offset only)");
                }

                var reset = FindMethod(pose, "Reset");
                if (reset == null)
                    problems.Add("SextantPipPose.Reset not found");
                else if (!InvokesName(reset, "ResetFilters"))
                    problems.Add("SextantPipPose.Reset does not delegate to ResetFilters");
            }

            var controller = FindSourceType(roots, "SextantController");
            if (controller == null)
            {
                problems.Add("type 'SextantController' not found in Source/");
            }
            else
            {
                var attach = FindMethod(controller, "AttachAndConfigureCameras");
                if (attach == null)
                {
                    problems.Add("SextantController.AttachAndConfigureCameras not found");
                }
                else
                {
                    int resetAt = -1;
                    int updateAt = -1;
                    foreach (var call in attach.DescendantNodes().OfType<InvocationExpressionSyntax>())
                    {
                        string target = call.Expression.ToString();
                        if (target == "SextantPipPose.ResetFilters")
                            resetAt = call.SpanStart;
                        else if (target == "UpdatePipPose")
                            updateAt = call.SpanStart;
                    }
                    if (resetAt < 0)
                        problems.Add("AttachAndConfigureCameras never calls SextantPipPose.ResetFilters — the first pose can glide from the pose frozen at Close");
                    else if (updateAt >= 0 && resetAt > updateAt)
                        problems.Add("AttachAndConfigureCameras resets the filters AFTER the first UpdatePipPose");
                }
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} stabilization open-edge violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Keyboard aim trim gate (swarm review 2026-11-04, judge C5): the
        /// left/right trim must MOVE the frozen pose instead of releasing the
        /// aim freeze — the frozen branch of StabilizeYaw absorbs any change
        /// of `target` into the washout offset, so a trim that only touches
        /// yawTrimDeg would be swallowed (R would not centre the view), and a
        /// trim that poked idleSeconds would drop the freeze exactly during
        /// the fine adjustment (the star would dance). Rules:
        ///  A) AddYawTrim shifts `lockedYaw` by `deltaDeg` while locked;
        ///  B) ResetYawTrim shifts `lockedYaw` by `yawTrimDeg` while locked,
        ///     BEFORE zeroing the trim;
        ///  C) NotifyAimInput is gone and PollYawTrim never calls it;
        ///  D) SetTestAim resets the trim (an absolute test aim);
        ///  E) SextantProbe.Snapshot reads YawTrimDeg (the live check);
        ///  F) ResetArc (R) drops the trim.
        /// Mutations: drop A -> FAIL; drop B -> FAIL; swap the order in B ->
        /// FAIL; bring the NotifyAimInput call back -> FAIL; drop D -> FAIL.
        /// </summary>
        [Fact]
        public void SextantPip_TrimShiftsFrozenPose()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            var roots = ParseAllSources();

            var pose = FindSourceType(roots, "SextantPipPose");
            if (pose == null)
            {
                problems.Add("type 'SextantPipPose' not found in Source/");
            }
            else
            {
                var add = FindMethod(pose, "AddYawTrim");
                if (add == null)
                {
                    problems.Add("SextantPipPose.AddYawTrim not found");
                }
                else if (LockedBranchAssignmentStart(add, "lockedYaw", "deltaDeg") < 0)
                {
                    problems.Add("SextantPipPose.AddYawTrim does not shift lockedYaw by deltaDeg while locked — the trim is swallowed by the frozen branch (C1)");
                }

                var reset = FindMethod(pose, "ResetYawTrim");
                if (reset == null)
                {
                    problems.Add("SextantPipPose.ResetYawTrim not found");
                }
                else
                {
                    int shiftAt = LockedBranchAssignmentStart(reset, "lockedYaw", "yawTrimDeg");
                    if (shiftAt < 0)
                    {
                        problems.Add("SextantPipPose.ResetYawTrim does not shift lockedYaw by yawTrimDeg while locked — R is absorbed by the offset (C1)");
                    }
                    else
                    {
                        int zeroAt = reset.DescendantNodes()
                            .OfType<AssignmentExpressionSyntax>()
                            .Where(a => a.Left is IdentifierNameSyntax left
                                && left.Identifier.Text == "yawTrimDeg"
                                && a.Right.ToString() == "0f")
                            .Select(a => a.SpanStart)
                            .DefaultIfEmpty(-1)
                            .Min();
                        if (zeroAt >= 0 && shiftAt > zeroAt)
                            problems.Add("SextantPipPose.ResetYawTrim zeroes the trim BEFORE shifting the frozen pose — R would leave the view off-centre");
                    }
                }

                if (FindMethod(pose, "NotifyAimInput") != null)
                {
                    problems.Add("SextantPipPose.NotifyAimInput is back — the trim must not release the aim freeze (C2)");
                }

                var setTestAim = FindMethod(pose, "SetTestAim");
                if (setTestAim == null)
                {
                    problems.Add("SextantPipPose.SetTestAim not found");
                }
                else if (!InvokesName(setTestAim, "ResetYawTrim"))
                {
                    problems.Add("SextantPipPose.SetTestAim does not reset the trim — an agent's absolute aim would be offset by a leftover trim (C3)");
                }
            }

            var poller = FindSourceType(roots, "SextantInputPoller");
            if (poller == null)
            {
                problems.Add("type 'SextantInputPoller' not found in Source/");
            }
            else
            {
                var pollYawTrim = FindMethod(poller, "PollYawTrim");
                if (pollYawTrim == null)
                    problems.Add("SextantInputPoller.PollYawTrim not found");
                else if (InvokesName(pollYawTrim, "NotifyAimInput"))
                    problems.Add("SextantInputPoller.PollYawTrim still calls NotifyAimInput — the star would dance during the fine adjustment (C2)");
            }

            var probe = FindSourceType(roots, "SextantProbe");
            if (probe == null)
            {
                problems.Add("type 'SextantProbe' not found in Source/");
            }
            else
            {
                var snapshot = FindMethod(probe, "Snapshot");
                if (snapshot == null)
                    problems.Add("SextantProbe.Snapshot not found");
                else if (!ReadsMember(snapshot, "YawTrimDeg"))
                    problems.Add("SextantProbe.Snapshot does not read YawTrimDeg — the live check cannot see the trim");
            }

            var controller = FindSourceType(roots, "SextantController");
            if (controller == null)
            {
                problems.Add("type 'SextantController' not found in Source/");
            }
            else
            {
                var resetArc = FindMethod(controller, "ResetArc");
                if (resetArc == null)
                    problems.Add("SextantController.ResetArc not found");
                else if (!InvokesName(resetArc, "ResetYawTrim"))
                    problems.Add("SextantController.ResetArc does not call ResetYawTrim — R would not centre the view");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} keyboard-trim violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Aiming-reticle gate (author 2026-11-04/05, "из перекрестия вычесть
        /// круг", "линии должны идти дальше по сторонам"): the crosshair gap
        /// must mark the identification cone — the four arms start at the
        /// cone's angular radius and run to the ocular rim, so everything
        /// inside the gap is identifiable and the reticle reads like the
        /// classic split/sight lines. The old square window lied at its
        /// corners and is gone. Rules:
        ///  A) SextantConeBox exposes DiameterUnits/RadiusUnits/ArmLengthUnits;
        ///  B) UpdateSightLines derives the gap from RadiusUnits, passes the
        ///     live ocular to ArmLengthUnits and calls ApplyCrossGap;
        ///  C) ApplyCrossGap places the arms at gap + arm/2 (the gap is real)
        ///     and sizes all four;
        ///  D) the square window (ApplyConeBox / ConeBox* fields) is gone;
        ///  E) the four arm fields exist in SextantOverlay.Result;
        ///  F) ArmLengthUnits derives the arm from the ocular (rim-to-rim),
        ///     not from a fixed multiple of the gap.
        /// Mutations: fixed gap -> FAIL; drop ArmLengthUnits -> FAIL; arms at
        /// arm/2 (no gap) -> FAIL; bring the box back -> FAIL; arm as a gap
        /// multiple -> FAIL; a constant ocular at the call site -> FAIL.
        /// </summary>
        [Fact]
        public void SextantCone_GapMarksTheTolerance()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            var roots = ParseAllSources();

            var coneBox = FindSourceType(roots, "SextantConeBox");
            if (coneBox == null)
            {
                problems.Add("type 'SextantConeBox' not found in Source/");
            }
            else
            {
                foreach (string method in new[] { "DiameterUnits", "RadiusUnits", "ArmLengthUnits" })
                {
                    if (FindMethod(coneBox, method) == null)
                        problems.Add("SextantConeBox." + method + " not found");
                }
                var arm = FindMethod(coneBox, "ArmLengthUnits");
                if (arm != null
                    && !arm.DescendantNodes().OfType<BinaryExpressionSyntax>()
                        .Any(b => b.ToString().Contains("ocular * 0.5f")))
                {
                    problems.Add("SextantConeBox.ArmLengthUnits does not derive the arm from the ocular (rim − gap) — the arms must reach the rim");
                }
            }

            var overlay = FindSourceType(roots, "SextantOverlay");
            if (overlay == null)
            {
                problems.Add("type 'SextantOverlay' not found in Source/");
            }
            else
            {
                foreach (string field in new[]
                {
                    "SplitTopImage", "SplitBottomImage", "SightLeftImage", "SightRightImage"
                })
                {
                    bool present = overlay.DescendantNodes().OfType<FieldDeclarationSyntax>()
                        .Any(f => f.Declaration.Variables.Any(v => v.Identifier.Text == field));
                    if (!present)
                        problems.Add("SextantOverlay.Result." + field + " not found");
                }
                if (overlay.DescendantNodes().OfType<FieldDeclarationSyntax>()
                    .Any(f => f.Declaration.Variables.Any(v => v.Identifier.Text.StartsWith("ConeBox"))))
                    problems.Add("the square aiming window is back (ConeBox* fields) — the gap must mark the cone");
            }

            var builder = FindSourceType(roots, "SextantOcularBuilder");
            if (builder == null)
            {
                problems.Add("type 'SextantOcularBuilder' not found in Source/");
            }
            else
            {
                if (FindMethod(builder, "ApplyConeBox") != null)
                    problems.Add("SextantOcularBuilder.ApplyConeBox is back — the square window must stay gone");
                var apply = FindMethod(builder, "ApplyCrossGap");
                if (apply == null)
                {
                    problems.Add("SextantOcularBuilder.ApplyCrossGap not found");
                }
                else
                {
                    // The arms must sit OUTSIDE the gap: centre = gap + arm/2.
                    bool gapInCentre = apply.DescendantNodes().OfType<BinaryExpressionSyntax>()
                        .Any(b => b.ToString().Contains("gapRadiusUnits + armLengthUnits"));
                    if (!gapInCentre)
                        problems.Add("ApplyCrossGap places the arms without the gap — the gap must mark the cone");
                }
            }

            var controller = FindSourceType(roots, "SextantController");
            if (controller == null)
            {
                problems.Add("type 'SextantController' not found in Source/");
            }
            else
            {
                var lines = FindMethod(controller, "UpdateSightLines");
                if (lines == null)
                {
                    problems.Add("SextantController.UpdateSightLines not found");
                }
                else
                {
                    if (!InvokesName(lines, "RadiusUnits"))
                        problems.Add("UpdateSightLines does not derive the gap from SextantConeBox.RadiusUnits");
                    var armCall = lines.DescendantNodes().OfType<InvocationExpressionSyntax>()
                        .FirstOrDefault(i => i.Expression.ToString() == "SextantConeBox.ArmLengthUnits");
                    if (armCall == null)
                        problems.Add("UpdateSightLines does not size the arms via SextantConeBox.ArmLengthUnits");
                    else if (!armCall.ArgumentList.ToString().Contains("OcularSizeUnits"))
                        problems.Add("UpdateSightLines passes no live ocular to ArmLengthUnits — the arms would not reach the rim");
                    if (!InvokesName(lines, "ApplyCrossGap"))
                        problems.Add("UpdateSightLines does not call SextantOcularBuilder.ApplyCrossGap");
                }
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} aiming-reticle violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Native map points gate (author 2026-11-05): every mod marker must
        /// BE a real MapPointUI created through the public
        /// MapChartingUI.CreatePoint — only then do the game's charting tools,
        /// the eraser and the drag see it (they key off MapPointUI.HoveredPoint).
        /// The mod's look is re-hosted as children by MapMarkerPointBridge,
        /// which also neutralises the game's own visuals and applies the
        /// static/dynamic policy; the old custom canvas/hitbox must stay gone.
        /// Rules:
        ///  A) NavMapMarkerBase reconciles through MapMarkerPoints.Plan and
        ///     creates points via charting.CreatePoint;
        ///  B) MapMarkerPointBridge exists and neutralises the point (image
        ///     colour transparent + label disabled);
        ///  C) the bridge applies the interaction policy (Interactive) and the
        ///     static position lock (Position);
        ///  D) the base no longer builds a custom GraphicRaycaster/hitbox;
        ///  E) FixMapMarker owns the eraser semantics (OnPointDestroyed).
        /// Mutations: drop CreatePoint -> FAIL; re-add the raycaster -> FAIL;
        /// drop the label disable -> FAIL; drop the Interactive policy ->
        /// FAIL; drop Plan -> FAIL.
        /// </summary>
        [Fact]
        public void MapMarkers_AreNativeMapPoints()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            var roots = ParseAllSources();

            var baseType = FindSourceType(roots, "NavMapMarkerBase");
            if (baseType == null)
            {
                problems.Add("type 'NavMapMarkerBase' not found in Source/");
            }
            else
            {
                if (!InvokesName(baseType, "CreatePoint"))
                    problems.Add("NavMapMarkerBase never calls MapChartingUI.CreatePoint — the markers must be native map points");
                if (!InvokesName(baseType, "Plan"))
                    problems.Add("NavMapMarkerBase never calls MapMarkerPoints.Plan — the adoption/reconcile is gone");
                if (!InvokesName(baseType, "Evaluate"))
                    problems.Add("NavMapMarkerBase never calls MapMarkerPoints.Evaluate — the interaction policy is gone");
                if (!InvokesName(baseType, "ClampCreateCount"))
                    problems.Add("NavMapMarkerBase never calls MapMarkerPoints.ClampCreateCount — the runaway creation guard is gone");
                if (!InvokesName(baseType, "OwnsKey"))
                    problems.Add("NavMapMarkerBase never calls OwnsKey — stale cleanup is no longer family-scoped");
                if (!InvokesName(baseType, "ShouldWritePosition"))
                    problems.Add("NavMapMarkerBase never calls MapMarkerPoints.ShouldWritePosition — the dynamic-position rule is gone");
                if (!InvokesName(baseType, "RemovePointDataBeforeDestroy"))
                    problems.Add("NavMapMarkerBase destroys points without MapSaveSanitizer.RemovePointDataBeforeDestroy — a save in the same frame would resurrect them");
                if (!baseType.DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Any(i => i.Expression.ToString()
                            .EndsWith("FindObjectsOfType<MapPointUI>", StringComparison.Ordinal)
                        && i.ArgumentList.Arguments.Count == 1
                        && i.ArgumentList.Arguments[0].ToString() == "true"))
                    problems.Add("NavMapMarkerBase scans points without includeInactive:true — hidden points would be recreated (27k runaway)");
                var bridgeFrame = FindMethod(baseType, "ApplyBridgeFrame");
                if (bridgeFrame == null
                    || !bridgeFrame.DescendantNodes().OfType<InvocationExpressionSyntax>()
                        .Any(i => i.Expression.ToString().EndsWith("ApplyFrame", StringComparison.Ordinal)))
                    problems.Add("NavMapMarkerBase.ApplyBridgeFrame no longer calls MapMarkerPointBridge.ApplyFrame");
                if (baseType.DescendantNodes().OfType<SimpleNameSyntax>()
                    .Any(n => n.Identifier.Text == "GraphicRaycaster"))
                    problems.Add("NavMapMarkerBase creates a GraphicRaycaster again — the custom hitbox must stay gone (the native point is the hitbox)");
                if (baseType.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
                    .Any(c => c.ToString().Contains("\"Hitbox\"")))
                    problems.Add("NavMapMarkerBase builds a custom Hitbox again — the native point is the hitbox");
            }

            var bridge = FindSourceType(roots, "MapMarkerPointBridge");
            if (bridge == null)
            {
                problems.Add("type 'MapMarkerPointBridge' not found in Source/");
            }
            else
            {
                if (FindMethod(bridge, "Attach") == null)
                    problems.Add("MapMarkerPointBridge.Attach not found");
                if (!bridge.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                    .Any(m => m.Name.Identifier.Text == "color"))
                    problems.Add("MapMarkerPointBridge does not touch the point image colour — the game's own look would show through");
                if (!bridge.DescendantNodes().OfType<AssignmentExpressionSyntax>()
                    .Any(a => a.Left.ToString().EndsWith(".enabled", StringComparison.Ordinal)
                        && a.Right.ToString() == "false"))
                    problems.Add("MapMarkerPointBridge never disables the point label — the adoption key would be visible");
                if (!bridge.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                    .Any(m => m.Name.Identifier.Text == "Interactive"))
                    problems.Add("MapMarkerPointBridge never applies the Interactive policy");
                if (!bridge.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                    .Any(m => m.Name.Identifier.Text == "Position"))
                    problems.Add("MapMarkerPointBridge never applies the static position lock");
                var tick = FindMethod(bridge, "LateUpdate");
                if (tick == null)
                {
                    problems.Add("MapMarkerPointBridge has no LateUpdate — ApplyFrame never runs (dead-code regression 2026-09-25)");
                }
                else if (!tick.DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Any(i => i.Expression.ToString().EndsWith("ApplyBridgeFrame", StringComparison.Ordinal)))
                {
                    problems.Add("MapMarkerPointBridge.LateUpdate does not call Owner.ApplyBridgeFrame — the per-frame policy is dead");
                }
            }

            var sanitizer = FindSourceType(roots, "MapSaveSanitizer");
            if (sanitizer == null)
            {
                problems.Add("type 'MapSaveSanitizer' not found in Source/ — polluted saves would keep re-materializing the runaway rows");
            }
            else
            {
                if (FindMethod(sanitizer, "TryInstall") == null)
                    problems.Add("MapSaveSanitizer.TryInstall not found");
                if (!sanitizer.DescendantNodes().OfType<IdentifierNameSyntax>()
                    .Any(n => n.Identifier.Text == "SavingStarted"))
                    problems.Add("MapSaveSanitizer never hooks SavesManager.SavingStarted — autosaves would re-serialize junk rows");
                if (!sanitizer.DescendantNodes().OfType<IdentifierNameSyntax>()
                    .Any(n => n.Identifier.Text == "Loaded"))
                    problems.Add("MapSaveSanitizer never hooks SavesManager.Loaded — a polluted save would materialize before cleanup");
            }

            var probe = FindSourceType(roots, "MapProbe");
            if (probe != null && probe.DescendantNodes().OfType<LiteralExpressionSyntax>()
                .Any(l => l.Token.ValueText == "MCP Point"))
            {
                problems.Add("MapProbe still creates points named 'MCP Point' — outside the owned prefix they are unremovable and saved");
            }

            var fix = FindSourceType(roots, "FixMapMarker");
            if (fix == null)
            {
                problems.Add("type 'FixMapMarker' not found in Source/");
            }
            else if (FindMethod(fix, "OnPointDestroyed") == null)
            {
                problems.Add("FixMapMarker does not override OnPointDestroyed — the eraser semantics are gone");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} native-map-point violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Eraser/Delete ownership gate (author 2026-09-25): a line/angle/
        /// circle/bearing anchored to two protected static markers must stay
        /// removable. MapShapeEraser runs before MapChartingUI.Update, removes
        /// the anchored shapes and clears MapPointUI.hoveredPoint — it must
        /// never destroy the marker itself.
        /// Mutations: drop the execution order -> FAIL; drop a shape type ->
        /// FAIL; destroy the hovered point -> FAIL; stop clearing the hover ->
        /// FAIL (the vanilla eraser would kill the marker).
        /// </summary>
        [Fact]
        public void MapShapeEraser_OwnsShapeCleanup()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            var roots = ParseAllSources();

            var eraser = FindSourceType(roots, "MapShapeEraser");
            if (eraser == null)
            {
                problems.Add("type 'MapShapeEraser' not found in Source/ — a line between two protected static markers is unremovable in game");
            }
            else
            {
                if (!eraser.DescendantNodes().OfType<AttributeSyntax>()
                    .Any(a => a.ToString().Contains("DefaultExecutionOrder")
                        && a.ToString().Contains("-1000")))
                {
                    problems.Add("MapShapeEraser has no [DefaultExecutionOrder(-1000)] — it must run before MapChartingUI.Update, otherwise the vanilla eraser destroys the marker first");
                }

                string[] shapeTypes = { "MapLineUI", "MapAngleUI", "MapCircleUI", "MapBearingUI" };
                for (int i = 0; i < shapeTypes.Length; i++)
                {
                    string shape = shapeTypes[i];
                    if (!eraser.DescendantNodes().OfType<SimpleNameSyntax>()
                        .Any(n => n.Identifier.Text == shape))
                    {
                        problems.Add("MapShapeEraser never touches " + shape + " — that shape type would stay unremovable");
                    }
                }

                if (!eraser.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                    .Any(m => m.Name.Identifier.Text == "HoveredPoint"))
                {
                    problems.Add("MapShapeEraser never reads MapPointUI.HoveredPoint — the eraser target is unknown");
                }

                if (!eraser.DescendantNodes().Any(n =>
                    (n is IdentifierNameSyntax id && id.Identifier.Text == "hoveredPoint")
                    || (n is LiteralExpressionSyntax lit && lit.Token.ValueText == "hoveredPoint")))
                {
                    problems.Add("MapShapeEraser never clears MapPointUI.hoveredPoint — the vanilla eraser would destroy the marker after the shapes are gone");
                }

                if (eraser.DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Any(i => i.Expression.ToString().EndsWith("Destroy", StringComparison.Ordinal)
                        && i.ArgumentList.ToString().Contains("hovered")))
                {
                    problems.Add("MapShapeEraser destroys the hovered MapPointUI — the marker must survive");
                }
            }

            var builder = FindSourceType(roots, "SceneContentBuilder");
            if (builder != null
                && !builder.DescendantNodes().OfType<SimpleNameSyntax>()
                    .Any(n => n.Identifier.Text == "MapShapeEraser"))
            {
                problems.Add("SceneContentBuilder does not register MapShapeEraser — it would never exist in the scene");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} shape-eraser violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Visibility gate (SRP slice BeamGate): flare/dot decisions flow
        /// LighthouseBeamGlow.IsFlareTakingOver -&gt; FlareTakesOver and
        /// GetBeamVisibilityFactor -&gt; BeamFactor. A re-inline of the cone
        /// math fails here.
        /// </summary>
        [Fact]
        public void BeamGate_Decides()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            var roots = ParseAllSources();
            var glow = FindSourceType(roots, "LighthouseBeamGlow");
            var gate = FindSourceType(roots, "LighthouseBeamGate");
            if (glow == null)
                problems.Add("type 'LighthouseBeamGlow' not found in Source/");
            else
            {
                var takeover = FindMethod(glow, "IsFlareTakingOver");
                if (takeover == null)
                    problems.Add("LighthouseBeamGlow.IsFlareTakingOver not found");
                else if (!InvokesName(takeover, "FlareTakesOver"))
                    problems.Add("LighthouseBeamGlow.IsFlareTakingOver never invokes FlareTakesOver — takeover regressed into the glow");
                var factor = FindMethod(glow, "GetBeamVisibilityFactor");
                if (factor == null)
                    problems.Add("LighthouseBeamGlow.GetBeamVisibilityFactor not found");
                else if (!InvokesName(factor, "BeamFactor"))
                    problems.Add("LighthouseBeamGlow.GetBeamVisibilityFactor never invokes BeamFactor — cone math regressed into the glow");
            }
            if (gate == null)
                problems.Add("type 'LighthouseBeamGate' not found in Source/ — gates regressed into the glow");

            Assert.True(problems.Count == 0,
                $"{problems.Count} beam-gate violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// Composition gate (SRP slice 1): scene content flows
        /// ModBootstrapper scene-start -&gt; SceneContentBuilder.Build -&gt;
        /// EnsureSkyRoots/EnsureMapComponents/EnsureRunners. A re-inline of
        /// content construction into the bootstrap fails here.
        /// </summary>
        [Fact]
        public void SceneContentBuilder_ComposesRunners()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            var roots = ParseAllSources();
            var bootstrap = FindSourceType(roots, "ModBootstrapper");
            var builder = FindSourceType(roots, "SceneContentBuilder");
            if (bootstrap == null)
                problems.Add("type 'ModBootstrapper' not found in Source/");
            else
            {
                var start = FindMethod(bootstrap, "SceneEventsListener_OnSceneStart");
                if (start == null)
                    problems.Add("ModBootstrapper.SceneEventsListener_OnSceneStart not found");
                else if (!InvokesName(start, "Build"))
                    problems.Add("ModBootstrapper scene-start never invokes SceneContentBuilder.Build — content regressed into the bootstrap");
            }
            if (builder == null)
            {
                problems.Add("type 'SceneContentBuilder' not found in Source/ — content regressed into the bootstrap");
            }
            else
            {
                var build = FindMethod(builder, "Build");
                if (build == null)
                    problems.Add("SceneContentBuilder.Build not found");
                else
                {
                    foreach (var step in new[] { "EnsureSkyRoots", "EnsureMapComponents", "EnsureRunners" })
                    {
                        if (!InvokesName(build, step))
                            problems.Add($"SceneContentBuilder.Build never invokes {step} — composition is hollow");
                    }
                }
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} scene-content violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// F232/F236 — lifecycle and diagnostics ordering gates, written as
        /// SYNTAX checks (not substring greps) so a comment or a dead string
        /// cannot satisfy them.
        ///
        /// 1. MapMarkerPointBridge.LateUpdate must carry a POSITIVE
        ///    DefaultExecutionOrder: the game moves map points from
        ///    MapChartingUI.LateUpdate (order 0) and Unity does not order
        ///    same-phase callbacks, so the compensation can sample a stale
        ///    lossyScale. Drop the attribute -> FAIL.
        /// 2. MapShapeEraser must do its work in Update (the game's eraser is
        ///    in LateUpdate; Update always precedes LateUpdate). Move the body
        ///    into LateUpdate -> FAIL.
        /// 3. Every diagnostics key used from a per-frame method must appear in
        ///    the IsEnabledCached refresh list, otherwise the lookup misses the
        ///    dictionary and falls back to a native PlayerPrefs read every
        ///    frame (the F233 regression: two sextant keys, two calls/frame).
        /// </summary>
        [Fact]
        public void LifecycleOrder_And_DiagnosticsCache_ArePinned()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            var roots = ParseAllSources();

            // --- 1. bridge LateUpdate must be ordered after the game's ---
            var bridge = FindSourceType(roots, "MapMarkerPointBridge");
            if (bridge == null)
            {
                problems.Add("type 'MapMarkerPointBridge' not found in Source/ — the native-point bridge is gone");
            }
            else
            {
                var attrs = bridge.AttributeLists.SelectMany(a => a.Attributes).ToList();
                var order = attrs.FirstOrDefault(a =>
                    a.Name.ToString().Contains("DefaultExecutionOrder"));
                if (order == null)
                {
                    problems.Add("MapMarkerPointBridge has no [DefaultExecutionOrder] — its LateUpdate can run before the game's MapChartingUI.LateUpdate (same order 0 = undefined) and compute a stale hitbox");
                }
                else if (!HasPositiveIntArgument(order))
                {
                    problems.Add("MapMarkerPointBridge [DefaultExecutionOrder] must be POSITIVE (after the game's LateUpdate at order 0), found: " + order);
                }
            }

            // --- 2. eraser interception must stay in the Update phase ---
            var eraser = FindSourceType(roots, "MapShapeEraser");
            if (eraser == null)
            {
                problems.Add("type 'MapShapeEraser' not found in Source/ — Eraser/Delete would destroy protected markers");
            }
            else
            {
                var names = eraser.DescendantNodes().OfType<MethodDeclarationSyntax>()
                    .Select(m => m.Identifier.ValueText).ToList();
                if (!names.Contains("Update"))
                    problems.Add("MapShapeEraser has no Update — the interception must run in the Update phase, before MapChartingUI.LateUpdate");
                if (names.Contains("LateUpdate") || names.Contains("FixedUpdate"))
                    problems.Add("MapShapeEraser must not move its interception into LateUpdate/FixedUpdate — the game erases from LateUpdate, and a same-phase order is undefined");
            }

            // --- 3. per-frame diagnostics keys must be in the cache list ---
            var diag = FindSourceType(roots, "RadioNavDiagnostics");
            if (diag == null)
            {
                problems.Add("type 'RadioNavDiagnostics' not found in Source/");
            }
            else
            {
                var refresh = diag.DescendantNodes()
                    .OfType<ElementAccessExpressionSyntax>()
                    .Where(e => e.Parent is AssignmentExpressionSyntax assign
                        && assign.IsKind(SyntaxKind.SimpleAssignmentExpression)
                        && e.ToString().StartsWith("cachedValue[", StringComparison.Ordinal))
                    .Select(e => e.ArgumentList != null && e.ArgumentList.Arguments.Count == 1
                        ? KeyLeaf(e.ArgumentList.Arguments[0].Expression.ToString())
                        : string.Empty)
                    .ToHashSet(StringComparer.Ordinal);

                // Per-frame methods are the ones Unity calls every frame, PLUS
                // the helpers they delegate to (ONE HOP). Without the hop this
                // gate repeats the very blind spot it exists to close: F233's
                // reads live in SextantPipPose.UpdateAimFreeze, called from
                // SextantPipPose.Update — a name-only check PASSED that
                // mutation, which is why the hop is here and not optional.
                var perFrame = new HashSet<string>(StringComparer.Ordinal)
                    { "Update", "LateUpdate", "FixedUpdate", "OnGUI" };

                foreach (var file in roots)
                {
                    var methods = file.Item2.DescendantNodes()
                        .OfType<MethodDeclarationSyntax>().ToList();

                    foreach (var method in methods)
                    {
                        // Bodies that can run per frame: this method plus, when
                        // it is a Unity per-frame method, the methods it calls.
                        var bodies = new List<MethodDeclarationSyntax> { method };
                        if (perFrame.Contains(method.Identifier.ValueText))
                        {
                            var invoked = new HashSet<string>(StringComparer.Ordinal);
                            foreach (var call in method.DescendantNodes()
                                .OfType<InvocationExpressionSyntax>())
                            {
                                invoked.Add(call.Expression.ToString());
                            }
                            bodies.AddRange(methods
                                .Where(m => invoked.Contains(m.Identifier.ValueText)));
                        }

                        foreach (var body in bodies)
                        {
                            foreach (var invocation in body.DescendantNodes()
                                .OfType<InvocationExpressionSyntax>())
                            {
                                if (invocation.Expression.ToString() != "RadioNavDiagnostics.IsEnabledCached")
                                    continue;
                                // The key is the first argument; only cached-
                                // dictionary misses matter (a literal key would
                                // always miss, so the refresh list must cover it).
                                if (invocation.ArgumentList.Arguments.Count == 0)
                                    continue;
                                string keyExpr = invocation.ArgumentList.Arguments[0].Expression.ToString();
                                if (!refresh.Contains(KeyLeaf(keyExpr)))
                                {
                                    problems.Add($"{file.Item1}.{body.Identifier.ValueText} reads diagnostics key {keyExpr} per frame but cachedValue[{keyExpr}] is not in the IsEnabledCached refresh list — falls back to a native PlayerPrefs call per frame");
                                }
                            }
                        }
                    }
                }
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} lifecycle/diagnostics-cache violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>Reduces a key expression to its leaf identifier: the refresh
        /// list lives INSIDE <c>RadioNavDiagnostics</c> and therefore writes the
        /// bare constant (<c>cachedValue[AimDebugKey]</c>), while callers in
        /// other files qualify it
        /// (<c>RadioNavDiagnostics.AimDebugKey</c>). Both forms name the same
        /// key, so the gate compares the leaf. Anything that is not a
        /// member access (a literal, a local) is returned verbatim, so a literal
        /// key is still required to appear literally in the list.</summary>
        private static string KeyLeaf(string keyExpression)
        {
            string text = keyExpression.Trim();
            int at = text.LastIndexOf('.');
            return at < 0 ? text : text.Substring(at + 1);
        }

        /// <summary>True when an attribute argument list contains a positive
        /// integer literal (DefaultExecutionOrder(1000) etc.).</summary>
        private static bool HasPositiveIntArgument(AttributeSyntax attr)
        {
            if (attr.ArgumentList == null)
                return false;
            foreach (var arg in attr.ArgumentList.Arguments)
            {
                if (arg.Expression is LiteralExpressionSyntax lit
                    && lit.IsKind(SyntaxKind.NumericLiteralExpression)
                    && int.TryParse(lit.Token.ValueText, out int value)
                    && value > 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// F234 — the 44100 constant is an offline fallback only. The live
        /// engine, station carriers, and Sonne's clip must all resolve the
        /// device rate; otherwise the DSP runs 146.7 cents sharp on 48 kHz.
        /// </summary>
        [Fact]
        public void AudioSynthesisEngine_UsesDeviceSampleRate()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            var roots = ParseAllSources();
            var engine = FindSourceType(roots, "AudioSynthesisEngine");
            if (engine == null)
            {
                problems.Add("type 'AudioSynthesisEngine' not found in Source/");
            }
            else
            {
                var property = engine.Members.OfType<PropertyDeclarationSyntax>()
                    .FirstOrDefault(p => p.Identifier.ValueText == "DeviceSampleRate");
                if (property == null || property.ToString().IndexOf(
                    "AudioSettings.outputSampleRate", StringComparison.Ordinal) < 0)
                    problems.Add("AudioSynthesisEngine.DeviceSampleRate must read AudioSettings.outputSampleRate");
                var process = FindMethod(engine, "ProcessAudio");
                if (process == null)
                    problems.Add("AudioSynthesisEngine.ProcessAudio not found");
                else if (process.ToString().IndexOf("SampleRate", StringComparison.Ordinal) >= 0)
                    problems.Add("AudioSynthesisEngine.ProcessAudio still uses the 44100 constant instead of the resolved device rate");
            }

            var fleet = FindSourceType(roots, "StationFleet");
            if (fleet == null || fleet.ToString().IndexOf(
                "CarrierGenerator(AudioSynthesisEngine.DeviceSampleRate)", StringComparison.Ordinal) < 0)
                problems.Add("StationFleet carriers must use AudioSynthesisEngine.DeviceSampleRate");

            var sonne = FindSourceType(roots, "SonneController");
            if (sonne == null || sonne.ToString().IndexOf(
                "AudioSynthesisEngine.DeviceSampleRate", StringComparison.Ordinal) < 0)
                problems.Add("SonneController silent clip must use AudioSynthesisEngine.DeviceSampleRate");

            Assert.True(problems.Count == 0,
                $"{problems.Count} audio sample-rate violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// F235 — LighthouseDotUi is read every frame by LighthouseBeamGlow.
        /// Its rect properties must return cached fields, and color writes must
        /// suppress an unchanged alpha before reflection boxes a Color.
        /// </summary>
        [Fact]
        public void LighthouseDotUi_CachesRectsAndSuppressesUnchangedColor()
        {
            if (!ModCompiler.HasModSource) return;

            var problems = new List<string>();
            var dotUi = FindSourceType(ParseAllSources(), "LighthouseDotUi");
            if (dotUi == null)
            {
                problems.Add("type 'LighthouseDotUi' not found in Source/");
            }
            else
            {
                foreach (var name in new[] { "CoreRect", "HaloRect", "StreakRect" })
                {
                    var property = dotUi.Members.OfType<PropertyDeclarationSyntax>()
                        .FirstOrDefault(p => p.Identifier.ValueText == name);
                    if (property == null || property.ToString().IndexOf(
                        "GetComponent", StringComparison.Ordinal) >= 0)
                        problems.Add("LighthouseDotUi." + name + " must return a cached field, not call GetComponent");
                }

                var build = FindMethod(dotUi, "EnsureBuilt");
                if (build == null || build.ToString().IndexOf(
                    "coreRect", StringComparison.Ordinal) < 0 ||
                    build.ToString().IndexOf("haloRect", StringComparison.Ordinal) < 0 ||
                    build.ToString().IndexOf("streakRect", StringComparison.Ordinal) < 0)
                    problems.Add("LighthouseDotUi.EnsureBuilt must cache coreRect/haloRect/streakRect");

                var color = FindMethod(dotUi, "SetImageColor");
                if (color == null || color.ToString().IndexOf(
                    "lastCoreAlpha", StringComparison.Ordinal) < 0 ||
                    color.ToString().IndexOf("lastHaloAlpha", StringComparison.Ordinal) < 0 ||
                    color.ToString().IndexOf("lastStreakAlpha", StringComparison.Ordinal) < 0)
                    problems.Add("LighthouseDotUi.SetImageColor must latch per-layer alpha before SetValue");
            }

            Assert.True(problems.Count == 0,
                $"{problems.Count} lighthouse hot-path violation(s):\n"
                + string.Join("\n", problems));
        }

        /// <summary>
        /// F240 — the game compiles the mod itself, so assembly version is
        /// always 0.0.0.0. The bootstrap diagnostic must identify the manifest
        /// build and warn when the verified game version drifts.
        /// </summary>
        [Fact]
        public void BootstrapDiagnostics_IdentifyBuildAndGameVersion()
        {
            if (!ModCompiler.HasModSource) return;

            var bootstrap = FindSourceType(ParseAllSources(), "ModBootstrapper");
            if (bootstrap == null)
            {
                Assert.Fail("type 'ModBootstrapper' not found in Source/");
                return;
            }

            string text = bootstrap.ToString();
            Assert.Contains("manifest=", text);
            Assert.Contains("fingerprint=leuchtfeuer:", text);
            Assert.Contains("VerifiedGameVersion", text);
            Assert.Contains("LogWarning", text);
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
        private static int ComputeCyclomaticComplexity(SyntaxNode method)
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
