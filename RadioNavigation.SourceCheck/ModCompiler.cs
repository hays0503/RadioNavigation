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
    /// Compiles the mod's Source\**\*.cs against the CLEAN game reference assembly.
    /// Any CS diagnostic of Error severity = the mod calls a game API that does not
    /// exist or has the wrong signature -> neural slop -> FAIL.
    ///
    /// References mirror RadioNavigation.Analysis.csproj: the clean game assembly +
    /// Unity/third-party DLLs from Managed (0Harmony, TextMeshPro, postprocessing,
    /// water, UnityEngine modules).
    /// </summary>
    internal static class ModCompiler
    {
        public static readonly string ModRoot = ResolveModRoot();

        /// <summary>
        /// F227: derive the mod root from the running assembly instead of a
        /// hardcoded user path, so the neural-slop guard works on any machine
        /// or CI checkout. Walk up from bin/ until a directory contains both
        /// Source/ and Manifest.json.
        /// </summary>
        private static string ResolveModRoot()
        {
            for (DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Source"))
                    && File.Exists(Path.Combine(dir.FullName, "Manifest.json")))
                {
                    return dir.FullName;
                }
            }
            // Fallback: legacy hardcoded location (unusual layouts only).
            return @"C:\Users\Sergey Pimenov\AppData\LocalLow\Deep Water Studio\UBOAT\Mods\RadioNavigation";
        }

        /// <summary>
        /// Exact language/define contract of the game's RoslynCSharp mod
        /// compiler (Mod.Compile): C# 7.0 with the UBOAT/UBOAT_2024_1/
        /// UNITY_STANDALONE_WIN symbols and NO DEBUG. Every mod-source
        /// parse in this tool MUST use these options — a language feature
        /// newer than 7.0 (e.g. the 'default' literal, C# 7.1) compiles
        /// fine under the local latest-Roslyn toolchain but fails in-game
        /// with CS8107 and the game then loads a broken assembly
        /// (BadImageFormatException, in-game 2026-09-03). Pinning the
        /// version here turns the whole class of errors into offline
        /// compile errors in Mod_CompilesAgainstCleanGameApi.
        /// </summary>
        public static readonly CSharpParseOptions GameParityParseOptions =
            CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.CSharp7)
                .WithPreprocessorSymbols(new[]
                {
                    "UBOAT",
                    "UBOAT_2024_1",
                    "UNITY_STANDALONE_WIN",
                });

        internal static readonly string[] ModRefNames =
        {
            "0Harmony.dll",
            "com.uboat.modapi.dll",
            "com.uboat.rendering.dll",
            "com.uboat.dependencies.dll",
            "Unity.TextMeshPro.dll",
            "com.playway.water.Runtime.dll",
            "com.unity.postprocessing.Runtime.dll",
            "UnityEngine.dll",
            "UnityEngine.CoreModule.dll",
            "UnityEngine.UIModule.dll",
            "UnityEngine.UI.dll",
            "UnityEngine.IMGUIModule.dll",
            "UnityEngine.InputLegacyModule.dll",
            "UnityEngine.PhysicsModule.dll",
            "UnityEngine.TextRenderingModule.dll",
            "UnityEngine.AudioModule.dll",
            "UnityEngine.ImageConversionModule.dll",
        };

        public static bool HasModSource => Directory.Exists(Path.Combine(ModRoot, "Source"));

        public static ImmutableArray<string> EnumerateSources()
        {
            var dir = Path.Combine(ModRoot, "Source");
            return Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray();
        }

        public static ImmutableArray<string> EnumerateTests()
        {
            var dir = Path.Combine(ModRoot, "Tests");
            return Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray();
        }

        public static ImmutableArray<MetadataReference> BuildReferences(byte[] cleanGameDll)
        {
            var refs = new List<MetadataReference>();
            refs.Add(MetadataReference.CreateFromImage(cleanGameDll));

            var managed = GameSourceCompiler.ManagedDir;

            // BCL: prefer the game's own Mono BCL from Managed when present.
            // The clean game dll's type refs point at Unity's mscorlib, and
            // some BCL types differ between profiles (e.g. Queue<T> lives in
            // mscorlib on Unity but in System.dll in the desktop framework) —
            // mixing profiles produced CS7069 for those. Fall back to the
            // desktop BCL when the game install is unavailable.
            bool managedBcl = false;
            var managedBclPaths = new List<string>();
            foreach (var name in BclNames)
            {
                var path = Path.Combine(managed, name);
                if (File.Exists(path))
                {
                    managedBclPaths.Add(path);
                    managedBcl = true;
                }
            }

            if (managedBcl)
            {
                foreach (var path in managedBclPaths)
                    refs.Add(MetadataReference.CreateFromFile(path));
            }
            else
            {
                // BCL (net40 semantics): the mod uses System.Enum/Byte/Int32/ValueType etc.
                // In-memory Roslyn does not auto-add mscorlib, so reference the runtime BCL.
                foreach (var bcl in BclPaths)
                {
                    if (File.Exists(bcl))
                        refs.Add(MetadataReference.CreateFromFile(bcl));
                }
            }

            foreach (var name in ModRefNames)
            {
                var path = Path.Combine(managed, name);
                if (File.Exists(path))
                    refs.Add(MetadataReference.CreateFromFile(path));
            }
            return refs.ToImmutableArray();
        }

        private static readonly string[] BclNames =
        {
            "mscorlib.dll",
            "System.dll",
            "System.Core.dll",
            "System.Xml.dll",
        };

        private static readonly string[] BclPaths =
        {
            @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\mscorlib.dll",
            @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.dll",
            @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Core.dll",
            @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Xml.dll",
            @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\mscorlib.dll",
            @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\System.dll",
            @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\System.Core.dll",
            @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\System.Xml.dll",
        };

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
            return CSharpCompilation.Create(
                "radionavigation.sourcecheck",
                trees,
                references,
                options);
        }
    }
}
