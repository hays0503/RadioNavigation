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
    /// <summary>Executes the actual camera-profile adapter with package stubs.
    /// This protects wiring, overrides and ownership, not GPU output.</summary>
    public sealed class SextantShadeSourceTests
    {
        private static readonly Lazy<Assembly> Slice = new Lazy<Assembly>(CompileSlice);

        [Fact]
        public void ShadeProfiles_OnlyTheSelectedHalfIsFiltered() { Run("IndependentHalves"); }

        [Fact]
        public void ShadeProfiles_ZeroAndCloseRestoreNativeProcessing() { Run("ZeroAndClose"); }

        [Fact]
        public void ShadeProfiles_DoNotReplaceTheGamesWholeGrade() { Run("Overrides"); }

        [Fact]
        public void ShadeProfiles_UnsafeLayerMasksAreRefused() { Run("Collision"); }

        [Fact]
        public void ShadeProfiles_SettingsAreOwnedAndDestroyed() { Run("Lifetime"); }

        [Fact]
        public void ShadeProfiles_SolarProbeRefusesOrdinaryClosedAndForeignContexts() { Run("ProbeContext"); }

        [Fact]
        public void ShadeProfiles_SolarProbeRefusesInvalidRadianceWithoutWriting() { Run("ProbeInvalid"); }

        [Fact]
        public void ShadeProfiles_SolarProbeRestoresTheOriginalNotTheLastDiagnosticColour() { Run("ProbeRestore"); }

        [Fact]
        public void ShadeProfiles_SolarProbeCloseAndDisableRestoreTheColour()
        {
            Run("ProbeLifetime");
            var sun = Source("Logic/SunDiscRenderer.cs");
            foreach (var pair in new[] { new[] { "LateUpdate", "ReconcileRadianceProbe" },
                new[] { "OnDisable", "RestoreProbeRadiance" }, new[] { "OnDestroy", "RestoreProbeRadiance" } })
            {
                var method = sun.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.Text == pair[0]);
                Assert.Contains(method.DescendantNodes().OfType<InvocationExpressionSyntax>(),
                    call => Same(call.Expression, pair[1]) && call.ArgumentList.Arguments.Count == 0);
            }
        }

        [Fact]
        public void ShadeProfiles_ControllerDeliversBothCountsAndDimmersAreInert()
        {
            var controller = Source("UI/SextantController.cs");
            foreach (string name in new[] { "AttachAndConfigureCameras", "SetFilters", "SetFiltersForProbe" })
            {
                var method = controller.DescendantNodes().OfType<MethodDeclarationSyntax>()
                    .Single(m => m.Identifier.Text == name);
                Assert.Contains(method.DescendantNodes().OfType<InvocationExpressionSyntax>(), call =>
                    Same(call.Expression, "bloomCut.Update") &&
                    call.ArgumentList.Arguments.Select(a => a.Expression.ToString()).SequenceEqual(
                        new[] { "IsOpen", "leftFilterCount", "rightFilterCount" }));
            }
            var dim = Source("UI/SextantGlareCoordinator.cs").DescendantNodes()
                .OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.Text == "ApplyDim");
            foreach (string side in new[] { "left", "right" })
                Assert.Contains(dim.DescendantNodes().OfType<InvocationExpressionSyntax>(), call =>
                    Same(call.Expression, "ZeroDim") && call.ArgumentList.Arguments.Count == 1 &&
                    Same(call.ArgumentList.Arguments[0].Expression, side));
            Assert.DoesNotContain(dim.DescendantNodes().OfType<IfStatementSyntax>(), _ => true);
            Assert.DoesNotContain(dim.DescendantNodes().OfType<AssignmentExpressionSyntax>(), _ => true);
        }

        private static bool Same(SyntaxNode node, string expression)
        {
            return SyntaxFactory.AreEquivalent(node, SyntaxFactory.ParseExpression(expression));
        }

        private static SyntaxNode Source(string file)
        {
            return CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(ModCompiler.ModRoot,
                "Source", file)), ModCompiler.GameParityParseOptions).GetRoot();
        }

        private static void Run(string method)
        {
            try { Slice.Value.GetType("ShadeHarness", true).GetMethod(method).Invoke(null, null); }
            catch (TargetInvocationException e) { throw e.InnerException ?? e; }
        }

        private static Assembly CompileSlice()
        {
            var trees = new[] { "UI/SextantBloomCut.cs", "Logic/SextantShadeFilter.cs" }
                .Select(file => CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(ModCompiler.ModRoot,
                    "Source", file)), ModCompiler.GameParityParseOptions))
                .Concat(new[] { CSharpSyntaxTree.ParseText(HarnessSource, ModCompiler.GameParityParseOptions),
                    CSharpSyntaxTree.ParseText(SolarProbeSlice(), ModCompiler.GameParityParseOptions) });
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p));
            var compilation = CSharpCompilation.Create("ShadeSlice_" + Guid.NewGuid().ToString("N"), trees,
                references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using (var stream = new MemoryStream())
            {
                var emit = compilation.Emit(stream);
                Assert.True(emit.Success, string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
                return Assembly.Load(stream.ToArray());
            }
        }

        private static string SolarProbeSlice()
        {
            var root = Source("Logic/SunDiscRenderer.cs");
            var names = new[] { "SetRadianceForProbe", "ProbeContextActive", "ReconcileRadianceProbe",
                "RestoreProbeRadiance", "OnDisable" };
            string methods = string.Join("\n", root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Where(m => names.Contains(m.Identifier.Text)).Select(m => m.ToFullString()));
            return @"using UnityEngine; namespace UBOAT.Mods.RadioNavigation {
public sealed class SunDiscRenderer {
    private static SunDiscRenderer instance; public static bool Active;
    private Material material; private Color probeHomeColor; private bool probeRadianceChanged;
    public static SunDiscRenderer Make(float original) {
        var sun = new SunDiscRenderer(); sun.material = new Material { color = new Color(original,original,original,1) };
        instance = sun; Active = true; return sun;
    }
    public Material TestMaterial { get { return material; } }
    public void ReconcileForTests() { ReconcileRadianceProbe(); }
    public void DisableForTests() { OnDisable(); }
" + methods + "}}";
        }

        private const string HarnessSource = @"
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using UBOAT.Mods.RadioNavigation;
namespace UnityEngine {
    public class Object { public string name; public bool destroyed; public static void Destroy(Object o) { o.destroyed = true; } }
    public class ScriptableObject : Object { public static T CreateInstance<T>() where T : new() { return new T(); } }
    public class Component : Object {}
    public class Material : Object { public Color color; public int writes;
        public void SetColor(string name, Color value) { if(name!=""_Color"") throw new Exception(""Wrong material property""); color=value; writes++; } }
    public class Camera : GameObject {}
    public class GameObject : Object {
        public int layer; public bool activeSelf = true;
        private readonly Dictionary<Type, object> parts = new Dictionary<Type, object>();
        public GameObject() {} public GameObject(string name) { this.name = name; }
        public T GetComponent<T>() where T : class { object p; return parts.TryGetValue(typeof(T), out p) ? (T)p : null; }
        public T AddComponent<T>() where T : new() { var p = new T(); parts[typeof(T)] = p; return p; }
        public void SetActive(bool on) { activeSelf = on; }
    }
    public struct Color { public float r,g,b,a; public Color(float r,float g,float b,float a) { this.r=r; this.g=g; this.b=b; this.a=a; }
        public static Color white { get { return new Color(1,1,1,1); } } }
    public struct LayerMask { public int value; public static implicit operator int(LayerMask m) { return m.value; }
        public static implicit operator LayerMask(int v) { return new LayerMask { value=v }; } }
    public static class Debug { public static void Log(string s) {} public static void LogWarning(string s) {} }
}
namespace UnityEngine.Rendering.PostProcessing {
    public abstract class Parameter { public bool overrideState; }
    public class Param<T> : Parameter { public T value; public void Override(T v) { value=v; overrideState=true; } }
    public class PostProcessEffectSettings : ScriptableObject {
        public bool active=true; public Param<bool> enabled = new Param<bool> { value=true };
        public void SetAllOverridesTo(bool on) {
            foreach (var field in GetType().GetFields()) { var p=field.GetValue(this) as Parameter; if(p!=null) p.overrideState=on; }
        }
    }
    public enum GradingMode { LowDefinitionRange, HighDefinitionRange, External }
    public class Bloom : PostProcessEffectSettings {}
    public class AutoExposure : PostProcessEffectSettings { public Param<float> keyValue=new Param<float>(); }
    public class ColorGrading : PostProcessEffectSettings {
        public Param<GradingMode> gradingMode = new Param<GradingMode>();
        public Param<float> postExposure = new Param<float>(); public Param<Color> colorFilter=new Param<Color>();
        public Param<float> contrast = new Param<float>(); public Param<int> tonemapper = new Param<int>();
    }
    public class PostProcessProfile : ScriptableObject {
        public List<PostProcessEffectSettings> settings=new List<PostProcessEffectSettings>();
        public T AddSettings<T>() where T : PostProcessEffectSettings, new() { var s=new T(); s.SetAllOverridesTo(true); settings.Add(s); return s; }
    }
    public class PostProcessVolume : Component { public bool isGlobal; public float priority; public PostProcessProfile sharedProfile; }
    public class PostProcessLayer : Component { public LayerMask volumeLayer; }
}
namespace UBOAT.Mods.RadioNavigation {
    public static class WarnOnce { public static bool ShouldLog(string s) { return true; } }
    public class SextantController { public static SextantController Instance; public bool RenderOnlyForProbe; }
}
public static class ShadeHarness {
    private static void Check(bool ok) { if(!ok) throw new Exception(""Shade adapter contract violated""); }
    private static SextantBloomCut Make(out GameObject l, out GameObject r) {
        l=new GameObject(); r=new GameObject(); l.AddComponent<PostProcessLayer>().volumeLayer=1<<28;
        r.AddComponent<PostProcessLayer>().volumeLayer=1<<28;
        var adapter=new SextantBloomCut(); adapter.EnsureVolume(l,false); adapter.EnsureVolume(r,true); return adapter;
    }
    private static PostProcessProfile Profile(SextantBloomCut a,string side) {
        return (PostProcessProfile)typeof(SextantBloomCut).GetField(side+""Profile"",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(a);
    }
    private static ColorGrading Grade(SextantBloomCut a,string side) { return Profile(a,side).settings.OfType<ColorGrading>().Single(); }
    private static AutoExposure Exposure(SextantBloomCut a,string side) { return Profile(a,side).settings.OfType<AutoExposure>().Single(); }
    public static void IndependentHalves() {
        GameObject l,r; var a=Make(out l,out r); a.Update(true,3,0);
        Check(Grade(a,""left"").active && !Grade(a,""right"").active);
        Check(Exposure(a,""left"").active && !Exposure(a,""left"").enabled.value);
        Check(l.GetComponent<PostProcessLayer>().volumeLayer.value == ((1<<28)|(1<<7)));
        Check(r.GetComponent<PostProcessLayer>().volumeLayer.value == ((1<<28)|(1<<6)));
        Check(Math.Abs(Grade(a,""left"").postExposure.value-SextantShadeFilter.ExposureEv(3))<1e-6);
        Check(Grade(a,""left"").colorFilter.value.g<1 && Grade(a,""left"").colorFilter.value.b<Grade(a,""left"").colorFilter.value.g);
        a.Update(true,0,2); Check(!Grade(a,""left"").active && Grade(a,""right"").active);
    }
    public static void ZeroAndClose() {
        GameObject l,r; var a=Make(out l,out r); a.Update(true,2,3); a.Update(true,0,0);
        foreach(string s in new[]{""left"",""right""}) { Check(!Grade(a,s).active && !Exposure(a,s).active); Check(Grade(a,s).postExposure.value==0); }
        a.Update(true,1,1); a.Hide(); Check(!Grade(a,""left"").active && !Grade(a,""right"").active);
        a.Update(false,3,3); Check(!Grade(a,""left"").active && !Exposure(a,""right"").active);
    }
    public static void Overrides() {
        GameObject l,r; var a=Make(out l,out r);
        foreach(string s in new[]{""left"",""right""}) {
            var g=Grade(a,s); Check(g.enabled.overrideState && g.enabled.value && g.gradingMode.overrideState);
            Check(g.gradingMode.value==GradingMode.HighDefinitionRange && g.postExposure.overrideState && g.colorFilter.overrideState);
            Check(!g.contrast.overrideState && !g.tonemapper.overrideState && !g.active);
            Check(Exposure(a,s).enabled.overrideState && !Exposure(a,s).keyValue.overrideState);
        }
    }
    public static void Collision() {
        foreach(int mask in new[]{-1,(1<<28)|(1<<6),(1<<28)|(1<<7)}) {
            var pip=new GameObject(); pip.AddComponent<PostProcessLayer>().volumeLayer=mask;
            var a=new SextantBloomCut(); a.EnsureVolume(pip,false); a.EnsureVolume(pip,true);
            Check(Profile(a,""left"")==null && Profile(a,""right"")==null);
            Check(pip.GetComponent<PostProcessLayer>().volumeLayer.value==mask);
        }
    }
    public static void Lifetime() {
        GameObject l,r; var a=Make(out l,out r); var lp=Profile(a,""left""); var rp=Profile(a,""right"");
        a.EnsureVolume(l,false); a.EnsureVolume(r,true); Check(lp==Profile(a,""left"") && rp==Profile(a,""right""));
        a.Destroy(); Check(lp.destroyed && rp.destroyed && lp.settings.All(s=>s.destroyed) && rp.settings.All(s=>s.destroyed));
        Check(Profile(a,""left"")==null && Profile(a,""right"")==null); a.Destroy();
    }
    public static void ProbeContext() {
        var s=SunDiscRenderer.Make(23); SextantController.Instance=null;
        Check(!s.SetRadianceForProbe(1) && s.TestMaterial.writes==0);
        SextantController.Instance=new SextantController();
        Check(!s.SetRadianceForProbe(1) && s.TestMaterial.writes==0);
        SextantController.Instance.RenderOnlyForProbe=true; SunDiscRenderer.Active=false;
        Check(!s.SetRadianceForProbe(1) && s.TestMaterial.writes==0);
        SunDiscRenderer.Active=true; var other=SunDiscRenderer.Make(23);
        Check(!s.SetRadianceForProbe(1) && s.TestMaterial.writes==0);
        Check(other.SetRadianceForProbe(1) && other.TestMaterial.color.a==1);
    }
    public static void ProbeInvalid() {
        var s=SunDiscRenderer.Make(23); SextantController.Instance=new SextantController { RenderOnlyForProbe=true };
        foreach(float value in new[]{0f,-1f,.1249f,64.1f,float.NaN,float.PositiveInfinity,float.NegativeInfinity})
            Check(!s.SetRadianceForProbe(value) && s.TestMaterial.writes==0 && s.TestMaterial.color.r==23);
    }
    public static void ProbeRestore() {
        var s=SunDiscRenderer.Make(23); SextantController.Instance=new SextantController { RenderOnlyForProbe=true };
        Check(s.SetRadianceForProbe(1)); Check(s.SetRadianceForProbe(4));
        Check(s.TestMaterial.color.r==4 && s.TestMaterial.color.g==4 && s.TestMaterial.color.b==4 && s.TestMaterial.color.a==1);
        SextantController.Instance.RenderOnlyForProbe=false; s.ReconcileForTests();
        Check(s.TestMaterial.color.r==23 && s.TestMaterial.writes==3);
        s.ReconcileForTests(); Check(s.TestMaterial.writes==3);
    }
    public static void ProbeLifetime() {
        var s=SunDiscRenderer.Make(23); SextantController.Instance=new SextantController { RenderOnlyForProbe=true };
        Check(s.SetRadianceForProbe(.5f)); SunDiscRenderer.Active=false; s.ReconcileForTests(); Check(s.TestMaterial.color.r==23);
        SunDiscRenderer.Active=true; Check(s.SetRadianceForProbe(2)); s.DisableForTests(); Check(s.TestMaterial.color.r==23);
        s.DisableForTests(); Check(s.TestMaterial.writes==4);
    }
}
";
    }
}
