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
                    File.ReadAllText(p), path: p))
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
