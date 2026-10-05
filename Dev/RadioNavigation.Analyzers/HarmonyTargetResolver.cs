using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RadioNavigation.Analyzers
{
    /// <summary>
    /// Resolves whether a Harmony target method exists, combining Roslyn's semantic model with
    /// a raw-metadata fallback (<see cref="GameMetadataIndex"/>). The analyzer does not care
    /// where the answer comes from - this is the single place that knows about the fallback.
    ///
    /// Rationale: UBOAT assemblies are obfuscated, so Roslyn under-reports members (ShipControlUI
    /// shows 30 methods to Roslyn vs 87 in raw metadata). When Roslyn cannot resolve a target,
    /// we consult raw metadata before deciding it is genuinely missing or ambiguous.
    /// </summary>
    internal static class HarmonyTargetResolver
    {
        private const string GameAssemblyFileName = "com.uboat.game.dll";

        // Cache keyed by reference file path. A Compilation is bound to a specific set of
        // references, so the path alone is a stable key for the lifetime of an analysis; no
        // FileInfo stat needed (and it would trip RS1035 - no filesystem access in analyzers).
        private static readonly ConcurrentDictionary<string, Lazy<GameMetadataIndex>> _indexCache =
            new ConcurrentDictionary<string, Lazy<GameMetadataIndex>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Locate com.uboat.game.dll among the compilation's metadata references and build (or
        /// reuse) an index over its raw metadata. Returns null when no such reference is present.
        /// </summary>
        public static GameMetadataIndex GetIndex(SyntaxNodeAnalysisContext context)
        {
            foreach (var reference in context.SemanticModel.Compilation.References)
            {
                if (reference is PortableExecutableReference pe &&
                    !string.IsNullOrEmpty(pe.FilePath) &&
                    Path.GetFileName(pe.FilePath).Equals(GameAssemblyFileName, StringComparison.OrdinalIgnoreCase))
                {
                    return _indexCache.GetOrAdd(
                        pe.FilePath,
                        _ => new Lazy<GameMetadataIndex>(() => GameMetadataIndex.TryCreateFromReference(pe))).Value;
                }
            }
            return null;
        }

        /// <summary>
        /// Consult raw metadata for the given declaring type + method name (+ optional explicit
        /// parameter types and a static/instance pin), returning a rich result that distinguishes
        /// Found / Absent / Ambiguous.
        /// </summary>
        public static MetadataLookupResult Lookup(
            GameMetadataIndex index,
            ITypeSymbol declaringType,
            string methodName,
            IReadOnlyList<string> parameterTypes,
            bool? requireStatic)
        {
            if (index == null)
                return MetadataLookupResult.Unavailable;

            var typeName = MetadataTypeName.FromSymbol(declaringType);
            return index.FindMethod(typeName, methodName, parameterTypes, requireStatic, out _);
        }
    }
}
