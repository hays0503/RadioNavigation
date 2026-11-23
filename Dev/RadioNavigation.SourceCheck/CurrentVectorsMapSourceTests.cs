using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace RadioNavigation.SourceCheck
{
    /// <summary>
    /// Compiles and executes the real MapCurrentVectorsDriver +
    /// CurrentVectorOverlayMath + CurrentSettings + NavMarkerGate against
    /// compact Unity/map boundary doubles, plus regression mutation checks.
    /// </summary>
    public sealed class CurrentVectorsMapSourceTests
    {
        private static readonly Lazy<Type> Probe = new Lazy<Type>(() => Compile(DriverSource()));

        [Fact] public void CurrentVectors_MapLifecycleAndPoolAreSafeAndNonInteractive() { Run("MapLifecycle_RendersNonInteractiveSiblingPoolAndHidesOutsideMap"); }
        [Fact] public void CurrentVectors_HighElevationExpandsCoverageWithFewerArrows() { Run("ZoomAdaptation_HighElevationReducesCountAndExpandsRadius"); }
        [Fact] public void CurrentVectors_CalmFlowHidesSlotsAndDestroyFreesAssets() { Run("CalmHide_TimeAdvanceResample_AndOnDestroyCleansResources"); }
        [Fact] public void CurrentVectors_ShiftYToggleAndDepthOverrideWork() { Run("HotkeyShiftYAndDepthMode_ToggleAndDriveDepthSampling"); }

        [Fact]
        public void CurrentVectors_DoesNotOverrideHudDepthTestWithAlways()
        {
            Assert.DoesNotContain("CompareFunction.Always", DriverSource(), StringComparison.Ordinal);
        }

        [Fact]
        public void CurrentVectors_MutationGuardCatchesUnshiftedYAndBrokenCleanup()
        {
            Type noShift = Compile(Mutate("if (!shift)", "if (false)"));
            Assert.Throws<TargetInvocationException>(() => Run(noShift, "HotkeyShiftYAndDepthMode_ToggleAndDriveDepthSampling"));

            Type badSibling = Compile(Mutate("rootGo.transform.SetSiblingIndex(0);", "rootGo.transform.SetSiblingIndex(99);"));
            Assert.Throws<TargetInvocationException>(() => Run(badSibling, "MapLifecycle_RendersNonInteractiveSiblingPoolAndHidesOutsideMap"));

            Type leakedTexture = Compile(Mutate("if (shaftTexture != null)\r\n                Destroy(shaftTexture);", ""));
            Assert.Throws<TargetInvocationException>(() => Run(leakedTexture, "CalmHide_TimeAdvanceResample_AndOnDestroyCleansResources"));
        }

        private static string DriverSource()
        {
            return File.ReadAllText(Path.Combine(ModCompiler.ModRoot, "Source/UI/MapCurrentVectorsDriver.cs"));
        }

        private static string Mutate(string target, string replacement)
        {
            string source = DriverSource().Replace("\r\n", "\n");
            string normalizedTarget = target.Replace("\r\n", "\n");
            Assert.Contains(normalizedTarget, source);
            Assert.Equal(source.IndexOf(normalizedTarget, StringComparison.Ordinal),
                source.LastIndexOf(normalizedTarget, StringComparison.Ordinal));
            return source.Replace(normalizedTarget, replacement);
        }

        private static string ReadSource(string relativePath)
        {
            return File.ReadAllText(Path.Combine(ModCompiler.ModRoot, relativePath));
        }

        private static string Fixture(string name)
        {
            using (var stream = typeof(CurrentVectorsMapSourceTests).Assembly.GetManifestResourceStream(name))
            {
                Assert.NotNull(stream);
                using (var reader = new StreamReader(stream))
                    return reader.ReadToEnd();
            }
        }

        private static Type Compile(string driverSource)
        {
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path));

            string[] sources =
            {
                driverSource,
                ReadSource("Source/Logic/CurrentVectorOverlayMath.cs"),
                ReadSource("Source/Logic/CurrentSettings.cs"),
                ReadSource("Source/Logic/HeldKeyEdge.cs"),
                ReadSource("Source/Logic/NavMarkerGate.cs"),
                Fixture("CurrentVectorsMap.Boundaries"),
                Fixture("CurrentVectorsMap.Scenarios")
            };

            var compilation = CSharpCompilation.Create(
                "CurrentVectorsMap_" + Guid.NewGuid().ToString("N"),
                sources.Select(text => CSharpSyntaxTree.ParseText(text, ModCompiler.GameParityParseOptions)),
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using (var stream = new MemoryStream())
            {
                var emit = compilation.Emit(stream);
                Assert.True(emit.Success, string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
                return Assembly.Load(stream.ToArray()).GetType("UBOAT.Mods.RadioNavigation.CurrentVectorsMapProbe", true);
            }
        }

        private static void Run(string scenario) { Run(Probe.Value, scenario); }
        private static void Run(Type probe, string scenario) { probe.GetMethod(scenario).Invoke(null, null); }
    }
}
