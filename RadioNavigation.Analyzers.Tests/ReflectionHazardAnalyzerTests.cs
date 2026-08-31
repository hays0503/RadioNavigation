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
    public class ReflectionHazardAnalyzerTests
    {
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
            var tree = CSharpSyntaxTree.ParseText(source);
            var compilation = CSharpCompilation.Create(
                "TestAssembly",
                new[] { tree },
                References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var analyzer = new ReflectionHazardAnalyzer();
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
        public void GetFieldChainedSetValue_ReportsRN0010()
        {
            var src = @"
using System.Reflection;
public class Foo { public int f; }
public class M
{
    public void Go(object obj)
    {
        typeof(Foo).GetField(""f"", BindingFlags.Public | BindingFlags.Instance).SetValue(obj, 1);
    }
}
";
            AssertHas("RN0010", Run(src));
        }

        [Fact]
        public void GetPropertyChainedSetValue_ReportsRN0010()
        {
            var src = @"
using System.Reflection;
public class Foo { public int P { get; set; } }
public class M
{
    public void Go(object data)
    {
        typeof(Foo).GetProperty(""P"").SetValue(data, 1);
    }
}
";
            AssertHas("RN0010", Run(src));
        }

        [Fact]
        public void GetMethodChainedInvoke_ReportsRN0010()
        {
            var src = @"
using System.Reflection;
public class Foo { public void Bar() {} }
public class M
{
    public void Go(object o)
    {
        typeof(Foo).GetMethod(""Bar"").Invoke(o, null);
    }
}
";
            AssertHas("RN0010", Run(src));
        }

        [Fact]
        public void NullConditionalGuard_NoRN0010()
        {
            var src = @"
using System.Reflection;
public class Foo { public int f; }
public class M
{
    public void Go(object obj)
    {
        typeof(Foo).GetField(""f"", BindingFlags.Public | BindingFlags.Instance)?.SetValue(obj, 1);
    }
}
";
            AssertNone(Run(src));
        }

        [Fact]
        public void AssignedThenUsedLater_NoRN0010()
        {
            var src = @"
using System.Reflection;
public class Foo { public int f; }
public class M
{
    public void Go(object obj)
    {
        var fi = typeof(Foo).GetField(""f"", BindingFlags.Public | BindingFlags.Instance);
        if (fi != null) fi.SetValue(obj, 1);
    }
}
";
            AssertNone(Run(src));
        }

        [Fact]
        public void NonReflectionCall_NoRN0010()
        {
            var src = @"
public class M
{
    public void Go() { var x = Helper(); }
    private static int Helper() { return 1; }
}
";
            AssertNone(Run(src));
        }
    }
}
