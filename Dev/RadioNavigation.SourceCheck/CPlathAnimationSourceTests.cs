using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace RadioNavigation.SourceCheck
{
    /// <summary>Runs the complete production player against Unity boundary
    /// doubles. Checks state, clip/time commands, renderer flags and events;
    /// does not certify rendered poses, camera registration or Unity playback.</summary>
    public sealed class CPlathAnimationSourceTests
    {
        private static readonly Lazy<Type> Probe = new Lazy<Type>(() => Compile(Source()));

        [Fact] public void CPlathAnimation_InitializeSelectsHiddenPrimaryAtZero() { Run("InitializeHidden"); }
        [Fact] public void CPlathAnimation_IdleStowedHasNoRepeatedVisibilityEvents() { Run("IdleHidden"); }
        [Fact] public void CPlathAnimation_FirstEquipStartsAtPrimaryZero() { Run("FirstEquip"); }
        [Fact] public void CPlathAnimation_CancelledRaiseStowsAndNotifies() { Run("CancelRaise"); }
        [Fact] public void CPlathAnimation_LoweringHidesOnlyAfterCompletion() { Run("LowerToStowed"); }
        [Fact] public void CPlathAnimation_ReequipRestartsAtZero() { Run("Reequip"); }
        [Fact] public void CPlathAnimation_ReadExitsThroughLoweringToStowed() { Run("ReadThenStow"); }
        [Fact] public void CPlathAnimation_ReequipDuringLoweringHasNoHiddenFrame() { Run("ReverseLowering"); }
        [Fact] public void CPlathAnimation_DisableInvalidatesGraphAndReinitializeResetsPose() { Run("DisableAndReinitialize"); }
        [Fact] public void CPlathAnimation_RepeatedCyclesPreserveVisibilityEdges() { Run("RepeatedCycles"); }

        [Fact]
        public void CPlathAnimation_RegressionRejectsEarlyReturnFromStowed()
        {
            const string marker = "void Evaluate()\n        {";
            string mutant = Mutate(marker, marker + "\n            if (CurrentPhase == Phase.Stowed) return;");
            // This mutation used to skip the initial graph evaluation, hidden
            // pose and visibility path. A passing source-name check is not enough.
            Assert.Throws<TargetInvocationException>(() => Run(Compile(mutant), "InitializeHidden"));
        }

        [Fact]
        public void CPlathAnimation_RegressionRejectsMissingStowCompletion()
        {
            const string marker = "else if (!equipped && clock >= clips[0].length/ClipTimeScale) CurrentPhase = Phase.Stowed;";
            string mutant = Mutate(marker, marker.Replace("Phase.Stowed", "Phase.Aiming"));
            Assert.Throws<TargetInvocationException>(() => Run(Compile(mutant), "LowerToStowed"));
        }

        private static string Mutate(string marker, string replacement)
        {
            string source = Source();
            Assert.Contains(marker, source);
            Assert.Equal(source.IndexOf(marker, StringComparison.Ordinal), source.LastIndexOf(marker, StringComparison.Ordinal));
            string mutant = source.Replace(marker, replacement);
            Assert.NotEqual(source, mutant);
            return mutant;
        }

        private static string Source()
        {
            return File.ReadAllText(Path.Combine(ModCompiler.ModRoot, "Source/UI/CPlathAdaptiveAnimationPlayer.cs"))
                .Replace("\r\n", "\n");
        }

        private static string Fixture(string name)
        {
            using (var stream = typeof(CPlathAnimationSourceTests).Assembly.GetManifestResourceStream(name))
            {
                Assert.NotNull(stream);
                using (var reader = new StreamReader(stream))
                    return reader.ReadToEnd();
            }
        }

        private static Type Compile(string source)
        {
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path));
            string[] sources = { source, Fixture("CPlathAnimation.Unity"), Fixture("CPlathAnimation.Scenarios") };
            var compilation = CSharpCompilation.Create("CPlathAnimation_" + Guid.NewGuid().ToString("N"),
                sources.Select(text => CSharpSyntaxTree.ParseText(text, ModCompiler.GameParityParseOptions)), references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using (var stream = new MemoryStream())
            {
                var emit = compilation.Emit(stream);
                Assert.True(emit.Success, string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
                return Assembly.Load(stream.ToArray()).GetType("CPlathAnimationProbe", true);
            }
        }

        private static void Run(string scenario) { Run(Probe.Value, scenario); }
        private static void Run(Type probe, string scenario) { probe.GetMethod(scenario).Invoke(null, null); }
    }
}
