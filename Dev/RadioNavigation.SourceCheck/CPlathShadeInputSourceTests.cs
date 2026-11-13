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
    /// <summary>Executes the real shade polling method, edge implementation
    /// and key declarations; doubles only physical input and model backends.</summary>
    public sealed class CPlathShadeInputSourceTests
    {
        private static readonly Lazy<Type> Probe = new Lazy<Type>(Compile);

        [Fact] public void CPlathShades_RoutesBothBanksToAvailableBackend() { Run("Routing"); }
        [Fact] public void CPlathShades_InactiveOrMissingBackendDoesNotClaimInput() { Run("Inactive"); }
        [Fact] public void CPlathShades_ModifiersAndReservedFourthLeftShadeKeepTheirMeaning() { Run("Modifiers"); }
        [Fact] public void CPlathShades_HeldKeysToggleOnlyOnceAndChangedTracksActualToggle() { Run("Edges"); }

        private static void Run(string name) { Probe.Value.GetMethod(name).Invoke(null, null); }

        private static Type Compile()
        {
            string source = File.ReadAllText(Path.Combine(ModCompiler.ModRoot, "Source/UI/CPlathHeldModel.cs"));
            var type = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes()
                .OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.Text == "CPlathHeldModel");
            string[] fields = { "rig", "animated", "shadeEdges", "shadeKeys" };
            var members = type.Members.Where(m => m is FieldDeclarationSyntax field && field.Declaration.Variables.Any(v => fields.Contains(v.Identifier.Text))
                || m is MethodDeclarationSyntax method && method.Identifier.Text == "PollIndependentShades"
                || m is PropertyDeclarationSyntax property && property.Identifier.Text == "IsHeld");
            string slice = "using UnityEngine; namespace UBOAT.Mods.RadioNavigation { partial class CPlathHeldModel {"
                + string.Join("\n", members.Select(m => m.ToFullString())) + "}}";
            string edges = File.ReadAllText(Path.Combine(ModCompiler.ModRoot, "Source/Logic/HeldKeyEdge.cs"));
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path));
            var compilation = CSharpCompilation.Create("CPlathShadeInput_" + Guid.NewGuid().ToString("N"),
                new[] { slice, edges, Host }.Select(text => CSharpSyntaxTree.ParseText(text, ModCompiler.GameParityParseOptions)),
                references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using (var stream = new MemoryStream())
            {
                var emit = compilation.Emit(stream);
                Assert.True(emit.Success, string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
                return Assembly.Load(stream.ToArray()).GetType("UBOAT.Mods.RadioNavigation.ShadeInputProbe", true);
            }
        }

        private const string Host = @"
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace UnityEngine {
    public enum KeyCode { LeftAlt, RightAlt, LeftControl, RightControl, Alpha1, Alpha2, Alpha3, Alpha4 }
    public static class Input {
        public static readonly HashSet<KeyCode> Held = new HashSet<KeyCode>();
        public static bool GetKey(KeyCode key) { return Held.Contains(key); }
    }
}
namespace UBOAT.Mods.RadioNavigation {
    class CPlathModelRig {
        internal readonly List<int> Calls = new List<int>();
        internal void ToggleShade(int index) { Calls.Add(index); }
    }
    class CPlathAnimatedActor {
        internal readonly List<int> Calls = new List<int>();
        internal void ToggleShade(int index) { Calls.Add(index); }
    }
    partial class CPlathHeldModel {
        internal static CPlathHeldModel Make(bool held, bool useRig, bool useAnimated) {
            Input.Held.Clear();
            return new CPlathHeldModel { IsHeld = held, rig = useRig ? new CPlathModelRig() : null,
                animated = useAnimated ? new CPlathAnimatedActor() : null };
        }
        internal int[] RigCalls { get { return rig == null ? new int[0] : rig.Calls.ToArray(); } }
        internal int[] AnimatedCalls { get { return animated == null ? new int[0] : animated.Calls.ToArray(); } }
    }
    public static class ShadeInputProbe {
        static void Check(bool condition) { if (!condition) throw new Exception(""Shade input contract violated""); }
        public static void Routing() {
            foreach (int mode in new[] { 1, 2, 3 }) {
                var model = CPlathHeldModel.Make(true, mode != 2, mode != 1);
                Input.Held.UnionWith(new[] { KeyCode.LeftAlt, KeyCode.Alpha1 });
                bool changed; Check(model.PollIndependentShades(out changed) && changed);
                Input.Held.Remove(KeyCode.Alpha1); model.PollIndependentShades(out changed);
                Input.Held.UnionWith(new[] { KeyCode.LeftControl, KeyCode.Alpha2 });
                Check(model.PollIndependentShades(out changed) && changed);
                Check((mode == 1 ? model.RigCalls : model.AnimatedCalls).SequenceEqual(new[] { 0, 5 }));
                Check((mode == 1 ? model.AnimatedCalls : model.RigCalls).Length == 0);
            }
        }
        public static void Inactive() {
            foreach (bool held in new[] { false, true }) foreach (int mode in new[] { 0, 1, 2, 3 }) {
                if (held && mode != 0) continue;
                var model = CPlathHeldModel.Make(held, (mode & 1) != 0, (mode & 2) != 0);
                Input.Held.UnionWith(new[] { KeyCode.LeftAlt, KeyCode.Alpha1 });
                bool changed; Check(!model.PollIndependentShades(out changed) && !changed);
                Check(model.RigCalls.Length == 0 && model.AnimatedCalls.Length == 0);
            }
        }
        public static void Modifiers() {
            var model = CPlathHeldModel.Make(true, true, false);
            Input.Held.UnionWith(new[] { KeyCode.RightAlt, KeyCode.RightControl, KeyCode.Alpha4 });
            bool changed; Check(model.PollIndependentShades(out changed) && !changed);
            Input.Held.Remove(KeyCode.RightControl);
            Check(model.PollIndependentShades(out changed) && !changed);
            Input.Held.Remove(KeyCode.Alpha4); model.PollIndependentShades(out changed);
            Input.Held.Add(KeyCode.Alpha4);
            Check(model.PollIndependentShades(out changed) && changed);
            Check(model.RigCalls.SequenceEqual(new[] { 3 }));
        }
        public static void Edges() {
            var model = CPlathHeldModel.Make(true, false, true);
            Input.Held.Add(KeyCode.Alpha3);
            bool changed; Check(!model.PollIndependentShades(out changed) && !changed);
            Input.Held.Add(KeyCode.LeftAlt);
            Check(model.PollIndependentShades(out changed) && !changed);
            Input.Held.Remove(KeyCode.Alpha3); model.PollIndependentShades(out changed);
            Input.Held.Add(KeyCode.Alpha3);
            Check(model.PollIndependentShades(out changed) && changed);
            Check(model.PollIndependentShades(out changed) && !changed);
            Input.Held.Remove(KeyCode.Alpha3); model.PollIndependentShades(out changed);
            Input.Held.Add(KeyCode.Alpha3); Check(model.PollIndependentShades(out changed) && changed);
            Check(model.AnimatedCalls.SequenceEqual(new[] { 2, 2 }));
        }
    }
}";
    }
}
