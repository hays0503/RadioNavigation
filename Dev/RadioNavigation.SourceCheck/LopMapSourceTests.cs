using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace RadioNavigation.SourceCheck
{
    /// <summary>Runs the complete native LOP adapter with boundary doubles,
    /// including cleanup/material bodies. No Unity rendering is simulated.</summary>
    public sealed class LopMapSourceTests
    {
        private static readonly Lazy<Type> Probe = new Lazy<Type>(() => Compile(Source()));

        [Fact] public void LopMap_CleanupPreservesPlayerLineAndRemovesOnlyOwnedRows() { Run("PlayerLineAndDeferredDestroy"); }
        [Fact] public void LopMap_CleanupPreservesCircleAngleAndBearingReferences() { Run("OtherPlayerShapes"); }
        [Fact] public void LopMap_RetryAfterPartialPointCreationCleansOrphans() { Run("RetryPointCreation"); }
        [Fact] public void LopMap_RetryAfterRegisteredLineFailureDoesNotDuplicate() { Run("RetryLineCreation"); }
        [Fact] public void LopMap_MaterialIsAssignedReusedAndReleasedWithoutChangingSharedDefault() { Run("MaterialLifetime"); }

        [Fact]
        public void LopMap_RegressionRejectsRemovalOfPlayerAnchors()
        {
            Type mutant = Compile(Mutate("referenced.Contains(point)", "false"));
            Assert.Throws<TargetInvocationException>(() => Run(mutant, "PlayerLineAndDeferredDestroy"));
        }

        [Fact]
        public void LopMap_RegressionRejectsBrokenMaterialBindingAndReuse()
        {
            string[][] changes =
            {
                new[] { "owner.Bind(graphic);", "" },
                new[] { "graphic.material = ownedMaterial;", "" },
                new[] { "if (ownedMaterial == null)", "if (true)" }
            };
            foreach (string[] change in changes)
            {
                Type mutant = Compile(Mutate(change[0], change[1]));
                Assert.Throws<TargetInvocationException>(() => Run(mutant, "MaterialLifetime"));
            }
        }

        private static string Source()
        {
            return File.ReadAllText(Path.Combine(ModCompiler.ModRoot, "Source/UI/SextantLopMapLines.cs"));
        }

        private static string Mutate(string marker, string replacement)
        {
            string source = Source();
            Assert.Contains(marker, source);
            Assert.Equal(source.IndexOf(marker, StringComparison.Ordinal), source.LastIndexOf(marker, StringComparison.Ordinal));
            return source.Replace(marker, replacement);
        }

        private static string Ownership()
        {
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(ModCompiler.ModRoot,
                "Source/Logic/SextantLopPlot.cs"))).GetRoot();
            var plot = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.Text == "SextantLopPlot");
            string[] methods = { "EndpointKey", "TryKey", "OwnsEndpoint", "OwnsLine" };
            var members = plot.Members.Where(m => m is FieldDeclarationSyntax field && field.Declaration.Variables.Any(v => v.Identifier.Text == "Prefix")
                || m is MethodDeclarationSyntax method && methods.Contains(method.Identifier.Text));
            // Only pure geometry dependencies are omitted; actual ownership
            // predicates, segment and adapter interface are compiled unchanged.
            return "using System; namespace UBOAT.Mods.RadioNavigation {"
                + root.DescendantNodes().OfType<StructDeclarationSyntax>().Single(s => s.Identifier.Text == "SextantLopSegment")
                + root.DescendantNodes().OfType<InterfaceDeclarationSyntax>().Single(s => s.Identifier.Text == "ISextantLopChart")
                + "public static class SextantLopPlot {" + string.Join("\n", members.Select(m => m.ToFullString())) + "}}";
        }

        private static string Fixture(string name)
        {
            using (var stream = typeof(LopMapSourceTests).Assembly.GetManifestResourceStream(name))
            {
                Assert.NotNull(stream);
                using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
            }
        }

        private static Type Compile(string source)
        {
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path));
            string[] sources = { source, Ownership(), Fixture("LopMap.Boundaries"), Fixture("LopMap.Scenarios") };
            var compilation = CSharpCompilation.Create("LopMap_" + Guid.NewGuid().ToString("N"),
                sources.Select(text => CSharpSyntaxTree.ParseText(text, ModCompiler.GameParityParseOptions)), references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using (var stream = new MemoryStream())
            {
                var emit = compilation.Emit(stream);
                Assert.True(emit.Success, string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
                return Assembly.Load(stream.ToArray()).GetType("UBOAT.Mods.RadioNavigation.LopMapProbe", true);
            }
        }

        private static void Run(string scenario) { Run(Probe.Value, scenario); }
        private static void Run(Type probe, string scenario) { probe.GetMethod(scenario).Invoke(null, null); }
    }
}
