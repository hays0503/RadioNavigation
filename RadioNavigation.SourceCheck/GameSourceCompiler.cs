using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace RadioNavigation.SourceCheck
{
    /// <summary>
    /// Compiles the CLEAN (non-obfuscated) UBOAT game source tree into an in-memory
    /// reference assembly using net40 semantics (mirrors com.uboat.game.csproj:
    /// LangVersion 15 / CSharp12+, AllowUnsafeBlocks, no overflow checks).
    ///
    /// References are the real Unity/third-party DLLs from UBOAT_Data\Managed,
    /// EXCLUDING com.uboat.game.dll (whose source lives in the tree - referencing it
    /// would duplicate every type). com.uboat.dependencies.dll, com.uboat.modapi.dll and
    /// com.uboat.rendering.dll ARE referenced: they supply the plugin types (TOD_Sky,
    /// LipSync, Blake2Sharp, EzySlice) and DI infra (InjectAttribute, ISingleton) that the
    /// decompiled tree references but does not define.
    /// </summary>
    internal static class GameSourceCompiler
    {
        public const string GameRoot = @"C:\code\projects\uboat";
        public const string ManagedDir =
            @"C:\Program Files (x86)\Steam\steamapps\common\UBOAT\UBOAT_Data\Managed";

        private static readonly string[] ExcludedOwnAssemblies =
        {
            "com.uboat.game.dll",
        };

        public static bool IsOwnAssembly(string fileName) =>
            ExcludedOwnAssemblies.Contains(fileName, StringComparer.OrdinalIgnoreCase);

        public static bool HasGameSource => Directory.Exists(GameRoot);

        /// <summary>All .cs files in the game tree, excluding build artifacts.</summary>
        public static ImmutableArray<string> EnumerateSources()
        {
            var excludedDirs = new HashSet<string>(
                new[] { "bin", "obj", "graphify-out", "Mods" },
                StringComparer.OrdinalIgnoreCase);

            var files = Directory.EnumerateFiles(GameRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path =>
                {
                    var parts = path.Substring(GameRoot.Length).Split(
                        new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                        StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Any(p => excludedDirs.Contains(p)))
                        return false;
                    // Properties/AssemblyInfo.cs is a decompiler artifact: it references
                    // Burst-generated types (UBOAT_002E..._0024BurstDirectCall) at a
                    // top-level namespace path that does not exist (the real types are
                    // nested classes). Exclude it; it carries no API surface the mod needs.
                    if (path.EndsWith("Properties\\AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase))
                        return false;
                    return true;
                })
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray();
            return files;
        }

        /// <summary>
        /// Reference every DLL in Managed except the game's own assemblies (their
        /// source is compiled in). Returns null when Managed is missing.
        /// </summary>
        public static ImmutableArray<MetadataReference> BuildReferences()
        {
            if (!Directory.Exists(ManagedDir))
                return ImmutableArray<MetadataReference>.Empty;

            var refs = Directory.EnumerateFiles(ManagedDir, "*.dll")
                .Where(f => !ExcludedOwnAssemblies.Contains(
                    Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                .Select(f => (MetadataReference)MetadataReference.CreateFromFile(f))
                .ToImmutableArray();
            return refs;
        }

        /// <summary>All managed DLLs (including the game's own), for diagnostics.</summary>
        public static ImmutableArray<string> AllManagedDlls()
        {
            if (!Directory.Exists(ManagedDir))
                return ImmutableArray<string>.Empty;
            return Directory.EnumerateFiles(ManagedDir, "*.dll")
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray();
        }

        /// <summary>
        /// Finds which managed DLL defines a top-level type by simple name.
        /// Uses Roslyn metadata reading (handles obfuscated assemblies).
        /// </summary>
        public static string FindAssemblyForType(string simpleTypeName)
        {
            foreach (var dll in AllManagedDlls())
            {
                try
                {
                    var comp = CSharpCompilation.Create(
                        "probe",
                        references: new[] { MetadataReference.CreateFromFile(dll) });
                    var asm = comp.GetAssemblyOrModuleSymbol(
                        comp.References.First()) as IAssemblySymbol;
                    if (asm == null) continue;
                    if (asm.GlobalNamespace.GetTypeMembers().Any(t => t.Name == simpleTypeName))
                        return Path.GetFileName(dll);
                    foreach (var ns in asm.GlobalNamespace.GetNamespaceMembers())
                    {
                        if (FindInNamespace(ns, simpleTypeName))
                            return Path.GetFileName(dll);
                    }
                }
                catch
                {
                    // skip unreadable assemblies
                }
            }
            return null;
        }

        private static bool FindInNamespace(INamespaceSymbol ns, string name)
        {
            foreach (var t in ns.GetTypeMembers())
                if (t.Name == name)
                    return true;
            foreach (var child in ns.GetNamespaceMembers())
                if (FindInNamespace(child, name))
                    return true;
            return false;
        }

        public static CSharpCompilation CreateCompilation(
            IEnumerable<SyntaxTree> trees,
            IEnumerable<MetadataReference> references)
        {
            var options = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: true,
                checkOverflow: false,
                optimizationLevel: OptimizationLevel.Debug,
                platform: Platform.AnyCpu);
            // Body-stripping leaves unused fields/events and obsolete-usage warnings that
            // Emit() promotes to errors. Suppress the common ones so the signature-only
            // reference can be emitted. CS1701: the game's Unity DLLs reference mscorlib
            // 2.0 while we compile against 4.0 (net40 semantics).
            options = options.WithSpecificDiagnosticOptions(
                new Dictionary<string, ReportDiagnostic>
                {
                    ["CS1701"] = ReportDiagnostic.Suppress,
                    ["CS0169"] = ReportDiagnostic.Suppress, // field never used
                    ["CS0649"] = ReportDiagnostic.Suppress, // field never assigned
                    ["CS0414"] = ReportDiagnostic.Suppress, // assigned but never used
                    ["CS0067"] = ReportDiagnostic.Suppress, // event never used
                    ["CS0618"] = ReportDiagnostic.Suppress, // obsolete member usage
                    ["CS8632"] = ReportDiagnostic.Suppress, // nullable annotation outside #nullable
                    ["CS0108"] = ReportDiagnostic.Suppress, // hides inherited member
                    ["CS0114"] = ReportDiagnostic.Suppress, // hides inherited member (no override)
                    ["CS0109"] = ReportDiagnostic.Suppress, // redundant modifier
                    ["CS1591"] = ReportDiagnostic.Suppress, // missing XML comment
                    ["CS9258"] = ReportDiagnostic.Suppress, // duplicate/conflicting members
                    ["CS9113"] = ReportDiagnostic.Suppress, // parameter not read (body-stripped)
                    ["CS1998"] = ReportDiagnostic.Suppress, // async without await (body-stripped)
                });
            return CSharpCompilation.Create(
                "com.uboat.game.clean",
                trees,
                references,
                options);
        }
    }
}
