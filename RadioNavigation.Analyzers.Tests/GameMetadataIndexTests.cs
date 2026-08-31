using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace RadioNavigation.Analyzers.Tests
{
    /// <summary>
    /// Step 2 regression tests against the REAL obfuscated com.uboat.game.dll. These pin the
    /// reason GameMetadataIndex exists: Roslyn under-reports members of obfuscated UBOAT types,
    /// so raw metadata is the source of truth for whether a Harmony target really exists.
    ///
    /// Skipped (no assertions) when the game DLL is not present on this machine.
    /// </summary>
    public class GameMetadataIndexTests
    {
        private const string GameDllPath =
            @"C:\Program Files (x86)\Steam\steamapps\common\UBOAT\UBOAT_Data\Managed\com.uboat.game.dll";

        private static readonly bool HasGameDll = File.Exists(GameDllPath);

        private const string HarmonyStub = @"
namespace HarmonyLib
{
    public sealed class HarmonyMethod { public HarmonyMethod(System.Reflection.MethodInfo m) {} }
    public sealed class Harmony
    {
        public void Patch(System.Reflection.MethodBase original,
            HarmonyMethod prefix = null, HarmonyMethod postfix = null) {}
    }
}
";

        private static ImmutableArray<MetadataReference> BuildReferences()
        {
            var tpa = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            var refs = tpa.Split(';')
                .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
                .ToList();
            if (HasGameDll)
                refs.Add(MetadataReference.CreateFromFile(GameDllPath));
            return refs.ToImmutableArray();
        }

        private static readonly ImmutableArray<MetadataReference> References = BuildReferences();

        private static List<Diagnostic> Run(string source)
        {
            var tree = CSharpSyntaxTree.ParseText(HarmonyStub + source);
            var compilation = CSharpCompilation.Create(
                "TestAssembly",
                new[] { tree },
                References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var analyzer = new HarmonyPatchAnalyzer();
            var withAnalyzers = compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
            return withAnalyzers.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult()
                .OrderBy(d => d.Location.SourceSpan.Start)
                .ToList();
        }

        private static void AssertHas(string id, List<Diagnostic> diags)
        {
            Assert.True(diags.Any(d => d.Id == id),
                $"Expected diagnostic {id} but got: [{string.Join(", ", diags.Select(d => d.Id))}]");
        }

        private static void AssertNone(List<Diagnostic> diags)
        {
            Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Warning);
        }

        // ---- Direct index tests -------------------------------------------------------------

        [Fact]
        public void MetadataIndex_FindsShipControlUI()
        {
            if (!HasGameDll) return;
            var index = LoadIndex();
            var typeName = new MetadataTypeName("com.uboat.game", "UBOAT.Game.UI", "ShipControlUI", 0);
            Assert.True(index.HasType(typeName));
        }

        [Fact]
        public void MetadataIndex_ShipControlUI_Has87Methods()
        {
            if (!HasGameDll) return;
            var index = LoadIndex();
            var typeName = new MetadataTypeName("com.uboat.game", "UBOAT.Game.UI", "ShipControlUI", 0);
            Assert.Equal(87, index.GetAllMethods(typeName).Count());
        }

        [Fact]
        public void MetadataIndex_ApplyToolPositionLimits_TwoOverloads()
        {
            if (!HasGameDll) return;
            var index = LoadIndex();
            var typeName = new MetadataTypeName("com.uboat.game", "UBOAT.Game.UI", "ShipControlUI", 0);
            var overloads = index.FindMethods(typeName, "ApplyToolPositionLimits");
            Assert.Equal(2, overloads.Count);
            Assert.Contains(overloads, o => o.IsStatic);
            Assert.Contains(overloads, o => !o.IsStatic);
        }

        [Fact]
        public void MetadataIndex_FindsRetrieveStringString()
        {
            if (!HasGameDll) return;
            var index = LoadIndex();
            var typeName = new MetadataTypeName("com.uboat.game", "UBOAT.Game.Core.Data", "Locale", 0);
            // Obfuscated metadata emits bare simple names ("String"), so match on those.
            Assert.Equal(MetadataLookupResult.Found,
                index.FindMethod(typeName, "RetrieveString", new[] { "String" }, null, out _));
        }

        [Fact]
        public void MetadataIndex_DistinguishesRetrieveStringOverloads()
        {
            if (!HasGameDll) return;
            var index = LoadIndex();
            var typeName = new MetadataTypeName("com.uboat.game", "UBOAT.Game.Core.Data", "Locale", 0);
            Assert.Equal(MetadataLookupResult.Found,
                index.FindMethod(typeName, "RetrieveString", new[] { "String" }, null, out _));
            Assert.Equal(MetadataLookupResult.Found,
                index.FindMethod(typeName, "RetrieveString", new[] { "String", "String" }, null, out _));
            Assert.Equal(MetadataLookupResult.MethodNotFound,
                index.FindMethod(typeName, "RetrieveString", new[] { "Int32" }, null, out _));
        }

        [Fact]
        public void MetadataIndex_MissingMethodIsAbsent()
        {
            if (!HasGameDll) return;
            var index = LoadIndex();
            var typeName = new MetadataTypeName("com.uboat.game", "UBOAT.Game.UI", "ShipControlUI", 0);
            Assert.Equal(MetadataLookupResult.MethodNotFound,
                index.FindMethod(typeName, "DefinitelyNotAMethod", null, null, out _));
        }

        [Fact]
        public void MetadataIndex_AmbiguousMethodIsAmbiguous()
        {
            if (!HasGameDll) return;
            var index = LoadIndex();
            var typeName = new MetadataTypeName("com.uboat.game", "UBOAT.Game.Core.Data", "Locale", 0);
            // Name-only lookup over two RetrieveString overloads -> ambiguous.
            Assert.Equal(MetadataLookupResult.Ambiguous,
                index.FindMethod(typeName, "RetrieveString", null, null, out _));
        }

        [Fact]
        public void MetadataIndex_StaticPinDisambiguatesApplyToolPositionLimits()
        {
            if (!HasGameDll) return;
            var index = LoadIndex();
            var typeName = new MetadataTypeName("com.uboat.game", "UBOAT.Game.UI", "ShipControlUI", 0);
            // BindingFlags.Static pins the single static overload -> Found, not Ambiguous.
            Assert.Equal(MetadataLookupResult.Found,
                index.FindMethod(typeName, "ApplyToolPositionLimits", null, true, out _));
        }

        // ---- End-to-end analyzer tests ------------------------------------------------------

        [Fact]
        public void ApplyToolPositionLimits_RoslynMissing_MetadataFound_NoRN0001()
        {
            // Canary: ShipControlUI.ApplyToolPositionLimits is hidden from Roslyn (obfuscation)
            // but exists in raw metadata. Must NOT report RN0001.
            if (!HasGameDll) return;
            var src = @"
using System.Reflection;
public class Patcher
{
    public void Apply(HarmonyLib.Harmony h)
    {
        var m = typeof(UBOAT.Game.UI.ShipControlUI).GetMethod(""ApplyToolPositionLimits"", BindingFlags.Static | BindingFlags.NonPublic);
        h.Patch(m, postfix: new HarmonyLib.HarmonyMethod(typeof(Patcher).GetMethod(""P"")));
    }
    public static void P(object __instance) {}
}
";
            AssertNone(Run(src));
        }

        [Fact]
        public void ApplyToolPositionLimits_BindingFlagsVariable_StaticPin_NoDiagnostic()
        {
            // Step 3: BindingFlags held in a local variable (like Main.cs 'anyStatic') must be
            // followed to its initializer so the static pin disambiguates the two overloads.
            if (!HasGameDll) return;
            var src = @"
using System.Reflection;
public class Patcher
{
    public void Apply(HarmonyLib.Harmony h)
    {
        var anyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        var m = typeof(UBOAT.Game.UI.ShipControlUI).GetMethod(""ApplyToolPositionLimits"", anyStatic);
        h.Patch(m, postfix: new HarmonyLib.HarmonyMethod(typeof(Patcher).GetMethod(""P"")));
    }
    public static void P(object __instance) {}
}
";
            AssertNone(Run(src));
        }

        [Fact]
        public void RadioStory_Constructor_MetadataFound_NoDiagnostic()
        {
            // Step 3: GetConstructor(Type[]) resolves .ctor through raw metadata when Roslyn
            // cannot see it. RadioStory has a (PlayableCharacter) ctor in raw metadata.
            if (!HasGameDll) return;
            var src = @"
using System.Reflection;
public class Patcher
{
    public void Apply(HarmonyLib.Harmony h)
    {
        var c = typeof(UBOAT.Game.Scene.Stories.RadioStory).GetConstructor(new[] { typeof(UBOAT.Game.Scene.Characters.PlayableCharacter) });
        h.Patch(c, postfix: new HarmonyLib.HarmonyMethod(typeof(Patcher).GetMethod(""P"")));
    }
    public static void P(object __instance) {}
}
";
            AssertNone(Run(src));
        }

        [Fact]
        public void Constructor_WrongSignature_MetadataAbsent_RN0001()
        {
            // Step 3: a constructor signature that does not exist in raw metadata -> RN0001.
            if (!HasGameDll) return;
            var src = @"
using System.Reflection;
public class Patcher
{
    public void Apply(HarmonyLib.Harmony h)
    {
        var c = typeof(UBOAT.Game.Scene.Stories.RadioStory).GetConstructor(new[] { typeof(int) });
        h.Patch(c, postfix: new HarmonyLib.HarmonyMethod(typeof(Patcher).GetMethod(""P"")));
    }
    public static void P(object __instance) {}
}
";
            AssertHas("RN0001", Run(src));
        }

        [Fact]
        public void MetadataIndex_FindsRadioStoryCtor()
        {
            if (!HasGameDll) return;
            var index = LoadIndex();
            var typeName = new MetadataTypeName("com.uboat.game", "UBOAT.Game.Scene.Stories", "RadioStory", 0);
            Assert.Equal(MetadataLookupResult.Found,
                index.FindMethod(typeName, ".ctor", new[] { "PlayableCharacter" }, null, out _));
        }

        [Fact]
        public void DefinitelyNotAMethod_RoslynMissing_MetadataAbsent_RN0001()
        {
            if (!HasGameDll) return;
            var src = @"
using System.Reflection;
public class Patcher
{
    public void Apply(HarmonyLib.Harmony h)
    {
        var m = typeof(UBOAT.Game.UI.ShipControlUI).GetMethod(""DefinitelyNotAMethod"", BindingFlags.Static | BindingFlags.NonPublic);
        h.Patch(m, postfix: new HarmonyLib.HarmonyMethod(typeof(Patcher).GetMethod(""P"")));
    }
    public static void P(object __instance) {}
}
";
            AssertHas("RN0001", Run(src));
        }

        [Fact]
        public void ApplyToolPositionLimits_NameOnly_Ambiguous_RN0002()
        {
            // ShipControlUI.ApplyToolPositionLimits has instance + static overloads, both hidden
            // from Roslyn. A name-only GetMethod cannot disambiguate -> RN0002.
            if (!HasGameDll) return;
            var src = @"
using System.Reflection;
public class Patcher
{
    public void Apply(HarmonyLib.Harmony h)
    {
        var m = typeof(UBOAT.Game.UI.ShipControlUI).GetMethod(""ApplyToolPositionLimits"");
        h.Patch(m, postfix: new HarmonyLib.HarmonyMethod(typeof(Patcher).GetMethod(""P"")));
    }
    public static void P(object __instance) {}
}
";
            AssertHas("RN0002", Run(src));
        }

        [Fact]
        public void RetrieveString_ExplicitSignature_Found_NoDiagnostic()
        {
            if (!HasGameDll) return;
            var src = @"
using System.Reflection;
public class Patcher
{
    public void Apply(HarmonyLib.Harmony h)
    {
        var m = typeof(UBOAT.Game.Core.Data.Locale).GetMethod(""RetrieveString"", new[] { typeof(string), typeof(string) });
        h.Patch(m, postfix: new HarmonyLib.HarmonyMethod(typeof(Patcher).GetMethod(""P"")));
    }
    public static void P(object __instance) {}
}
";
            AssertNone(Run(src));
        }

        // ---- Helpers -----------------------------------------------------------------------

        private static GameMetadataIndex LoadIndex()
        {
            var reference = (PortableExecutableReference)MetadataReference.CreateFromFile(GameDllPath);
            return GameMetadataIndex.TryCreateFromReference(reference);
        }
    }
}
