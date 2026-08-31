using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace RadioNavigation.Analyzers.Tests
{
    public class HarmonyPatchAnalyzerTests
    {
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

        private static readonly ImmutableArray<MetadataReference> References =
            BuildReferences();

        private static ImmutableArray<MetadataReference> BuildReferences()
        {
            var tpa = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            return tpa.Split(';')
                .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
                .ToImmutableArray();
        }

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

        [Fact]
        public void TargetNotFound_ReportsRN0001()
        {
            var src = @"
using System.Reflection;
public class Foo { public void Bar() {} }
public class Patcher
{
    public void Apply(HarmonyLib.Harmony h)
    {
        var target = typeof(Foo).GetMethod(""Missing"", BindingFlags.Public | BindingFlags.Instance);
        h.Patch(target, postfix: new HarmonyLib.HarmonyMethod(typeof(Patcher).GetMethod(""P"")));
    }
    public static void P(Foo __instance) {}
}
";
            AssertHas("RN0001", Run(src));
        }

        [Fact]
        public void ValidPostfix_NoDiagnostic()
        {
            var src = @"
using System.Reflection;
public class Foo { public void Bar() {} }
public class Patcher
{
    public void Apply(HarmonyLib.Harmony h)
    {
        var target = typeof(Foo).GetMethod(""Bar"", BindingFlags.Public | BindingFlags.Instance);
        h.Patch(target, postfix: new HarmonyLib.HarmonyMethod(typeof(Patcher).GetMethod(""P"")));
    }
    public static void P(Foo __instance) {}
}
";
            AssertNone(Run(src));
        }

        [Fact]
        public void ParameterTypeMismatch_ReportsRN0003()
        {
            var src = @"
using System.Reflection;
public class Foo { public void Bar(int value) {} }
public class Patcher
{
    public void Apply(HarmonyLib.Harmony h)
    {
        var target = typeof(Foo).GetMethod(""Bar"", BindingFlags.Public | BindingFlags.Instance);
        h.Patch(target, prefix: new HarmonyLib.HarmonyMethod(typeof(Patcher).GetMethod(""P"")));
    }
    public static bool P(string value) { return true; }
}
";
            AssertHas("RN0003", Run(src));
        }

        [Fact]
        public void ResultOnVoidTarget_ReportsRN0004()
        {
            var src = @"
using System.Reflection;
public class Foo { public void Bar() {} }
public class Patcher
{
    public void Apply(HarmonyLib.Harmony h)
    {
        var target = typeof(Foo).GetMethod(""Bar"", BindingFlags.Public | BindingFlags.Instance);
        h.Patch(target, postfix: new HarmonyLib.HarmonyMethod(typeof(Patcher).GetMethod(""P"")));
    }
    public static void P(ref string __result) {}
}
";
            AssertHas("RN0004", Run(src));
        }

        [Fact]
        public void RefOutMismatch_ReportsRN0005()
        {
            var src = @"
using System.Reflection;
public class Foo { public void Bar(int value) {} }
public class Patcher
{
    public void Apply(HarmonyLib.Harmony h)
    {
        var target = typeof(Foo).GetMethod(""Bar"", BindingFlags.Public | BindingFlags.Instance);
        h.Patch(target, prefix: new HarmonyLib.HarmonyMethod(typeof(Patcher).GetMethod(""P"")));
    }
    public static bool P(ref int value) { return true; }
}
";
            AssertHas("RN0005", Run(src));
        }

        [Fact]
        public void OverloadedTarget_NoFalseRN0001()
        {
            // Mirrors the real ShipControlUI.ApplyToolPositionLimits: an instance + a static
            // overload share a name. Resolving either must NOT report RN0001.
            var src = @"
using System.Reflection;
public class Foo
{
    private void ApplyToolPositionLimits() {}
    private static int ApplyToolPositionLimits(int a, int b) { return 0; }
}
public class Patcher
{
    public void Apply(HarmonyLib.Harmony h)
    {
        var m = typeof(Foo).GetMethod(""ApplyToolPositionLimits"", BindingFlags.Static | BindingFlags.NonPublic);
        h.Patch(m, postfix: new HarmonyLib.HarmonyMethod(typeof(Patcher).GetMethod(""P"")));
    }
    public static void P(Foo __instance) {}
}
";
            AssertNone(Run(src));
        }

        [Fact]
        public void AnonymousObjectForeach_ObjectParam_ReportsRN0003()
        {
            // Regression: the real Main.cs builds an array of anonymous objects and patches
            // them in a foreach. Handler 'HandleCustomDecisions(object decision)' must be
            // flagged against target 'OnDecisionSelected(Decision decision)' even though
            // object accepts Decision via assignability (strict equality required).
            var src = @"
using System.Reflection;
public class Decision {}
public class RadioStory { public void OnDecisionSelected(Decision decision) {} }
public class Patcher
{
    public void Apply(HarmonyLib.Harmony h)
    {
        var onDecision = typeof(RadioStory).GetMethod(""OnDecisionSelected"", BindingFlags.NonPublic | BindingFlags.Instance);
        var handleDecisions = typeof(Patcher).GetMethod(""HandleCustomDecisions"", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        var targets = new[]
        {
            new { method = onDecision, prefix = new HarmonyLib.HarmonyMethod(handleDecisions), postfix = (HarmonyLib.HarmonyMethod)null },
        };
        foreach (var t in targets)
            h.Patch(t.method, prefix: t.prefix, postfix: t.postfix);
    }
    public static bool HandleCustomDecisions(object decision) { return true; }
}
";
            AssertHas("RN0003", Run(src));
        }

        [Fact]
        public void AnonymousObjectForeach_TypedParam_NoDiagnostic()
        {
            // Positive control: same data flow but the handler declares the exact target
            // parameter type - must produce no diagnostics.
            var src = @"
using System.Reflection;
public class Decision {}
public class RadioStory { public void OnDecisionSelected(Decision decision) {} }
public class Patcher
{
    public void Apply(HarmonyLib.Harmony h)
    {
        var onDecision = typeof(RadioStory).GetMethod(""OnDecisionSelected"", BindingFlags.NonPublic | BindingFlags.Instance);
        var handleDecisions = typeof(Patcher).GetMethod(""HandleCustomDecisions"", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        var targets = new[]
        {
            new { method = onDecision, prefix = new HarmonyLib.HarmonyMethod(handleDecisions), postfix = (HarmonyLib.HarmonyMethod)null },
        };
        foreach (var t in targets)
            h.Patch(t.method, prefix: t.prefix, postfix: t.postfix);
    }
    public static bool HandleCustomDecisions(Decision decision) { return true; }
}
";
            AssertNone(Run(src));
        }

        [Fact]
        public void TypeVariableReceiver_ObjectParam_ReportsRN0003()
        {
            // Mirrors the real Main.cs exactly: handlers are looked up via a Type VARIABLE
            // (patchType = typeof(Patcher)) rather than inline typeof(...). The resolver must
            // follow patchType back to its typeof() initializer and still flag object vs Decision.
            var src = @"
using System.Reflection;
public class Decision {}
public class RadioStory { public void OnDecisionSelected(Decision decision) {} }
public class Patcher
{
    public void Apply(HarmonyLib.Harmony h)
    {
        var patchType = typeof(Patcher);
        var anyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        var onDecision = typeof(RadioStory).GetMethod(""OnDecisionSelected"", BindingFlags.NonPublic | BindingFlags.Instance);
        var handleDecisions = patchType.GetMethod(""HandleCustomDecisions"", anyStatic);
        var targets = new[]
        {
            new { method = onDecision, prefix = new HarmonyLib.HarmonyMethod(handleDecisions), postfix = (HarmonyLib.HarmonyMethod)null },
        };
        foreach (var t in targets)
            h.Patch(t.method, prefix: t.prefix, postfix: t.postfix);
    }
    public static bool HandleCustomDecisions(object decision) { return true; }
}
";
            AssertHas("RN0003", Run(src));
        }

        [Fact]
        public void PrefixReturningInt_ReportsRN0009()
        {
            var src = @"
using System.Reflection;
public class Foo { public void Bar() {} }
public class Patcher
{
    public void Apply(HarmonyLib.Harmony h)
    {
        var target = typeof(Foo).GetMethod(""Bar"", BindingFlags.Public | BindingFlags.Instance);
        h.Patch(target, prefix: new HarmonyLib.HarmonyMethod(typeof(Patcher).GetMethod(""P"")));
    }
    public static int P() { return 1; }
}
";
            AssertHas("RN0009", Run(src));
        }

        [Fact]
        public void PostfixReturningBool_ReportsRN0009()
        {
            var src = @"
using System.Reflection;
public class Foo { public void Bar() {} }
public class Patcher
{
    public void Apply(HarmonyLib.Harmony h)
    {
        var target = typeof(Foo).GetMethod(""Bar"", BindingFlags.Public | BindingFlags.Instance);
        h.Patch(target, postfix: new HarmonyLib.HarmonyMethod(typeof(Patcher).GetMethod(""P"")));
    }
    public static bool P() { return true; }
}
";
            AssertHas("RN0009", Run(src));
        }

        [Fact]
        public void ValidPrefixVoid_NoRN0009()
        {
            var src = @"
using System.Reflection;
public class Foo { public void Bar() {} }
public class Patcher
{
    public void Apply(HarmonyLib.Harmony h)
    {
        var target = typeof(Foo).GetMethod(""Bar"", BindingFlags.Public | BindingFlags.Instance);
        h.Patch(target, prefix: new HarmonyLib.HarmonyMethod(typeof(Patcher).GetMethod(""P"")));
    }
    public static void P() {}
}
";
            AssertNone(Run(src));
        }

        [Fact]
        public void ValidPrefixBool_NoRN0009()
        {
            var src = @"
using System.Reflection;
public class Foo { public void Bar() {} }
public class Patcher
{
    public void Apply(HarmonyLib.Harmony h)
    {
        var target = typeof(Foo).GetMethod(""Bar"", BindingFlags.Public | BindingFlags.Instance);
        h.Patch(target, prefix: new HarmonyLib.HarmonyMethod(typeof(Patcher).GetMethod(""P"")));
    }
    public static bool P() { return true; }
}
";
            AssertNone(Run(src));
        }

        [Fact]
        public void ValidPostfixVoid_NoRN0009()
        {
            var src = @"
using System.Reflection;
public class Foo { public void Bar() {} }
public class Patcher
{
    public void Apply(HarmonyLib.Harmony h)
    {
        var target = typeof(Foo).GetMethod(""Bar"", BindingFlags.Public | BindingFlags.Instance);
        h.Patch(target, postfix: new HarmonyLib.HarmonyMethod(typeof(Patcher).GetMethod(""P"")));
    }
    public static void P() {}
}
";
            AssertNone(Run(src));
        }
    }
}
