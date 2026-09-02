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
        public const string ModRoot =
            @"C:\Users\Sergey Pimenov\AppData\LocalLow\Deep Water Studio\UBOAT\Mods\RadioNavigation";

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

        public static ImmutableArray<MetadataReference> BuildReferences(byte[] cleanGameDll)
        {
            var refs = new List<MetadataReference>();
            refs.Add(MetadataReference.CreateFromImage(cleanGameDll));

            // BCL (net40 semantics): the mod uses System.Enum/Byte/Int32/ValueType etc.
            // In-memory Roslyn does not auto-add mscorlib, so reference the runtime BCL.
            foreach (var bcl in BclPaths)
            {
                if (File.Exists(bcl))
                    refs.Add(MetadataReference.CreateFromFile(bcl));
            }

            var managed = GameSourceCompiler.ManagedDir;
            foreach (var name in ModRefNames)
            {
                var path = Path.Combine(managed, name);
                if (File.Exists(path))
                    refs.Add(MetadataReference.CreateFromFile(path));
            }
            return refs.ToImmutableArray();
        }

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
