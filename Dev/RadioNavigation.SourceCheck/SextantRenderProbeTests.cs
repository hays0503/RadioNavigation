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
    /// <summary>
    /// Executes the actual opening, Update, Close, Record and Reset source slices
    /// against write-failing collaborators. This proves control flow, not Unity
    /// rendering, hardware delivery or native save suppression. Shared rendering
    /// helper bodies are also checked for hidden calibration/worksheet writes.
    /// </summary>
    public sealed class SextantRenderProbeTests
    {
        private static readonly Lazy<Assembly> Slice = new Lazy<Assembly>(CompileSlice);

        [Fact]
        public void RenderProbe_EmptyAndRetainedSeriesNeverEnterAutomaticCalibration()
        {
            foreach (bool series in new[] { false, true })
            {
                var h = new Harness();
                h.Set("SeriesActive", series);
                h.Set("ForbidWrites", true);
                string result = h.Probe("OpenForRender");
                Assert.Contains("\"ok\":true", result);
                Assert.Contains("\"open\":true", result);
                Assert.Contains("\"renderOnly\":true", result);
                Assert.True(h.Mode);
                Assert.True(h.Flag("ModeAtAttach"));
                Assert.Equal(1, h.Count("EnsureCalls"));
                Assert.Equal(1, h.Count("AttachCalls"));
                Assert.Equal(0, h.Count("SolveCalls"));
                Assert.Equal(0, h.Count("Writes"));
            }
        }

        [Fact]
        public void RenderProbe_RepeatedAndGenericProbeOpenCannotDowngradeTheSession()
        {
            var h = new Harness();
            h.Set("ForbidWrites", true);
            Assert.Contains("\"ok\":true", h.Probe("OpenForRender"));
            Assert.Contains("\"renderOnly\":true", h.Probe("OpenForRender"));
            Assert.True((bool)h.Controller("TryOpenForProbe"));
            Assert.True(h.Mode);
            Assert.Equal(1, h.Count("EnsureCalls"));
            Assert.Equal(1, h.Count("AttachCalls"));
            Assert.Equal(0, h.Count("SolveCalls"));
        }

        [Fact]
        public void RenderProbe_OrdinaryOpenStillUsesCalibrationAndWritableActions()
        {
            var h = new Harness();
            Assert.True((bool)h.Controller("TryOpenForProbe"));
            Assert.False(h.Mode);
            Assert.Equal(1, h.Count("SolveCalls"));
            h.Controller("Update");
            Assert.Equal(1, h.Count("FixCalls"));
            Assert.Equal(1, h.Count("IndexCalls"));
            Assert.Equal(1, h.Count("CourseCalls"));
            Assert.Contains("\"recorded\":1", h.Probe("Record"));
            Assert.Contains("\"cleared\":true", h.Probe("Reset"));
        }

        [Fact]
        public void RenderProbe_FailedOpenDoesNotCalibrateOrClaimRendering()
        {
            var h = new Harness();
            h.Set("AllowOpen", false);
            h.Set("ForbidWrites", true);
            Assert.Contains("cannot-open-render-only", h.Probe("OpenForRender"));
            Assert.False(h.Mode);
            Assert.Equal(0, h.Count("EnsureCalls"));
            Assert.Equal(0, h.Count("AttachCalls"));
            Assert.Equal(0, h.Count("SolveCalls"));

            h = new Harness();
            h.Set("AttachSticks", false);
            h.Set("ForbidWrites", true);
            Assert.Contains("cannot-open-render-only", h.Probe("OpenForRender"));
            Assert.Equal(0, h.Count("SolveCalls"));
        }

        [Fact]
        public void RenderProbe_AlreadyOrdinaryOpenIsNotCertifiedAsCalibrationFree()
        {
            var h = new Harness();
            Assert.True((bool)h.Controller("TryOpenForProbe"));
            h.Set("ForbidWrites", true);
            Assert.Contains("cannot-open-render-only", h.Probe("OpenForRender"));
            Assert.False(h.Mode);
            Assert.Equal(1, h.Count("SolveCalls"));
            Assert.Equal(1, h.Count("AttachCalls"));
        }

        [Fact]
        public void RenderProbe_UpdateBlocksWorksheetAndIndexButKeepsRenderControls()
        {
            var h = new Harness();
            h.Set("ForbidWrites", true);
            Assert.Contains("\"ok\":true", h.Probe("OpenForRender"));
            h.Controller("Update");
            h.Controller("Update");
            Assert.Equal(0, h.Count("FixCalls"));
            Assert.Equal(0, h.Count("IndexCalls"));
            Assert.Equal(0, h.Count("CourseCalls"));
            Assert.Equal(0, h.Count("Writes"));
            // Edge sampling continues even when writable actions are blocked:
            // a held I must not turn into a delayed press after this session.
            Assert.Equal(2, h.Count("SampleCalls"));
            Assert.Equal(2, h.Count("ArcCalls"));
            Assert.Equal(2, h.Count("FilterCalls"));
            Assert.Equal(2, h.Count("ZoomCalls"));
            Assert.Equal(2, h.Count("PanelCalls"));
        }

        [Fact]
        public void RenderProbe_RecordAndResetRefuseBeforeReadingOrChangingSights()
        {
            var h = new Harness();
            h.Set("ForbidWrites", true);
            Assert.Contains("\"ok\":true", h.Probe("OpenForRender"));
            Assert.Contains("\"error\":\"render-only\"", h.Probe("Record"));
            Assert.Contains("\"error\":\"render-only\"", h.Probe("Reset"));
            Assert.Equal(0, h.Count("SightReads"));
            Assert.Equal(0, h.Count("Writes"));
        }

        [Fact]
        public void RenderProbe_CloseClearsTheModeAndNormalReopenRemainsWritable()
        {
            var h = new Harness();
            h.Set("ForbidWrites", true);
            Assert.Contains("\"ok\":true", h.Probe("OpenForRender"));
            h.Controller("CloseForProbe");
            Assert.False(h.Mode);
            Assert.False(h.Open);
            Assert.Equal(1, h.Count("ClearAimCalls"));
            Assert.Equal(0, h.Count("Writes"));
            h.Set("ForbidWrites", false);
            Assert.True((bool)h.Controller("TryOpenForProbe"));
            Assert.False(h.Mode);
            Assert.Equal(1, h.Count("SolveCalls"));
            h.Controller("Update");
            Assert.Equal(1, h.Count("FixCalls"));
            Assert.Equal(1, h.Count("IndexCalls"));
            Assert.Equal(1, h.Count("CourseCalls"));
        }

        [Fact]
        public void RenderProbe_SharedOpenHelpersContainNoHiddenCalibrationWrites()
        {
            var controller = Source("SextantController.cs");
            foreach (string name in new[] { "TryOpenForRenderProbe", "TryOpenForSightsProbe", "PrepareOpen", "FinishOpen",
                "ResetReadingState", "AttachAndConfigureCameras" })
            {
                foreach (var call in Method(controller, name).DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    string target = call.Expression.ToString();
                    Assert.DoesNotContain("calibrator.", target);
                    Assert.DoesNotContain("SetDelta", target);
                    Assert.DoesNotContain("PlayerPrefs.Set", target);
                    Assert.DoesNotContain("IndexErrorCorrection.Save", target);
                    Assert.DoesNotContain("IndexErrorCorrection.Clear", target);
                    Assert.DoesNotContain("CaptureObservation", target);
                    Assert.DoesNotContain("ResetSeries", target);
                    Assert.DoesNotContain("UpdateCourseSampler", target);
                }
            }
            var update = Method(controller, "Update");
            Assert.Contains(update.DescendantNodes().OfType<InvocationExpressionSyntax>(),
                c => c.Expression.ToString() == "inputPoller.SampleEdges"
                    && c.ArgumentList.ToString() == "(IsOpen || heldModel.IsHeld)");
            var probe = Source("SextantProbe.cs");
            foreach (string name in new[] { "Snapshot", "BuildSetResult" })
            {
                Assert.Contains(Method(probe, name).DescendantNodes().OfType<MemberAccessExpressionSyntax>(),
                    m => m.ToString() == "controller.RenderOnlyForProbe");
                Assert.Contains(Method(probe, name).DescendantNodes().OfType<MemberAccessExpressionSyntax>(),
                    m => m.ToString() == "controller.SightOnlyForProbe");
            }
        }

        [Fact]
        public void SightProbe_EmptySeriesOpensWithoutCalibrationBeforeAttaching()
        {
            var h = new Harness();
            h.Set("ForbidRestrictedWrites", true);
            Assert.Contains("\"sightOnly\":true", h.Probe("OpenForSights"));
            Assert.True(h.SightMode);
            Assert.False(h.Mode);
            Assert.True(h.Flag("SightModeAtAttach"));
            Assert.Equal(0, h.Count("SolveCalls"));
            Assert.Equal(0, h.Count("Writes"));
            h.Controller("Update");
            Assert.True(h.Flag("PreserveAtPoll"));
            Assert.Equal(1, h.Count("FixCalls"));
            Assert.Equal(1, h.Count("CourseCalls"));
            Assert.Equal(0, h.Count("IndexCalls"));
            Assert.Equal(1, h.Count("SampleCalls"));
        }

        [Fact]
        public void SightProbe_RetainedSeriesIsRejectedWithoutResetOrCalibration()
        {
            var h = new Harness();
            h.Set("SeriesActive", true);
            h.Set("ForbidWrites", true);
            Assert.Contains("cannot-open-sight-only", h.Probe("OpenForSights"));
            Assert.False(h.Open);
            Assert.False(h.SightMode);
            Assert.Equal(0, h.Count("EnsureCalls"));
            Assert.Equal(0, h.Count("SolveCalls"));
            Assert.Equal(0, h.Count("Writes"));
        }

        [Fact]
        public void SightProbe_ModesCannotBeConvertedOrDowngraded()
        {
            var h = new Harness();
            Assert.True((bool)h.Controller("TryOpenForProbe"));
            h.Set("ForbidWrites", true);
            Assert.Contains("cannot-open-sight-only", h.Probe("OpenForSights"));
            Assert.False(h.SightMode);
            Assert.Equal(1, h.Count("SolveCalls"));

            h = new Harness();
            h.Set("ForbidWrites", true);
            Assert.Contains("\"renderOnly\":true", h.Probe("OpenForRender"));
            Assert.Contains("cannot-open-sight-only", h.Probe("OpenForSights"));
            Assert.True(h.Mode);

            h = new Harness();
            h.Set("ForbidRestrictedWrites", true);
            Assert.Contains("\"sightOnly\":true", h.Probe("OpenForSights"));
            Assert.Contains("\"sightOnly\":true", h.Probe("OpenForSights"));
            Assert.True((bool)h.Controller("TryOpenForProbe"));
            Assert.Contains("cannot-open-render-only", h.Probe("OpenForRender"));
            Assert.True(h.SightMode);
            Assert.Equal(1, h.Count("AttachCalls"));
            Assert.Equal(0, h.Count("SolveCalls"));
        }

        [Fact]
        public void SightProbe_RecordWorksButResetRefusesBeforeWrites()
        {
            var h = new Harness();
            h.Set("ForbidRestrictedWrites", true);
            Assert.Contains("\"sightOnly\":true", h.Probe("OpenForSights"));
            Assert.Contains("\"recorded\":1", h.Probe("Record"));
            Assert.Equal(1, h.Count("CaptureCalls"));
            Assert.Contains("\"error\":\"sight-only\"", h.Probe("Reset"));
            Assert.Equal(0, h.Count("ResetCalls"));
            Assert.Equal(0, h.Count("SelectionClearCalls"));
            Assert.Equal(0, h.Count("SolveCalls"));
        }

        [Fact]
        public void SightProbe_FullSeriesRefusesFourthProbeCaptureBeforeSightRead()
        {
            var h = new Harness();
            Assert.Contains("\"sightOnly\":true", h.Probe("OpenForSights"));
            h.Set("HasThird", true);
            h.Set("ForbidWrites", true);
            Assert.Contains("\"error\":\"sight-limit\"", h.Probe("Record"));
            Assert.Equal(0, h.Count("SightReads"));
            Assert.Equal(0, h.Count("CaptureCalls"));
        }

        [Fact]
        public void SightProbe_ActualKeyboardCapturePreservesRawHsAndBlocksOAndFourthI()
        {
            var h = new Harness();
            h.Set("ForbidRestrictedWrites", true);
            h.Set("PressI", true);
            h.Set("PressO", true);
            h.PollFix(13.25, true);
            Assert.Equal(1, h.Count("CaptureCalls"));
            Assert.Equal(13.25, h.Number("CapturedHs"));
            Assert.Equal(0, h.Count("ResetCalls"));
            Assert.Equal(0, h.Count("SelectionClearCalls"));
            h.Set("HasThird", true);
            h.PollFix(18.5, true);
            Assert.Equal(1, h.Count("CaptureCalls"));
            Assert.Equal(13.25, h.Number("CapturedHs"));
            Assert.Equal(0, h.Count("ResetCalls"));
        }

        [Fact]
        public void SightProbe_ActualOrdinaryKeyboardOAndFourthIStayAvailable()
        {
            var h = new Harness();
            h.Set("HasThird", true);
            h.Set("PressI", true);
            h.Set("PressO", true);
            h.PollFix(13.25, false);
            Assert.Equal(1, h.Count("CaptureCalls"));
            Assert.Equal(1, h.Count("ResetCalls"));
            Assert.Equal(1, h.Count("SelectionClearCalls"));
        }

        [Fact]
        public void SightProbe_FailedOpenAndCloseNeverSolveCalibration()
        {
            var h = new Harness();
            h.Set("ForbidWrites", true);
            h.Set("AllowOpen", false);
            Assert.Contains("cannot-open-sight-only", h.Probe("OpenForSights"));
            Assert.Equal(0, h.Count("EnsureCalls"));
            h.Set("AllowOpen", true);
            h.Set("AttachSticks", false);
            Assert.Contains("cannot-open-sight-only", h.Probe("OpenForSights"));
            Assert.Equal(0, h.Count("SolveCalls"));

            h = new Harness();
            h.Set("ForbidWrites", true);
            Assert.Contains("\"sightOnly\":true", h.Probe("OpenForSights"));
            h.Controller("CloseForProbe");
            Assert.False(h.SightMode);
            Assert.False(h.Mode);
            Assert.False(h.Open);
            Assert.Equal(0, h.Count("Writes"));
        }

        private static SyntaxNode Source(string file)
        {
            return CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(ModCompiler.ModRoot,
                "Source", "UI", file)), ModCompiler.GameParityParseOptions).GetRoot();
        }

        private static MethodDeclarationSyntax Method(SyntaxNode root, string name)
        {
            return root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Single(m => m.Identifier.ValueText == name);
        }

        private static string Methods(SyntaxNode root, params string[] names)
        {
            return string.Join("\n", names.Select(n => Method(root, n).ToFullString()));
        }

        private static Assembly CompileSlice()
        {
            var controller = Source("SextantController.cs");
            var probe = Source("SextantProbe.cs");
            string members = string.Join("\n", new[] { "IsOpen", "RenderOnlyForProbe", "SightOnlyForProbe", "ArcAngle",
                "LeftFilterCount", "RightFilterCount" }.Select(name => controller.DescendantNodes()
                    .OfType<PropertyDeclarationSyntax>().Single(p => p.Identifier.ValueText == name).ToFullString()));
            string field = string.Join("\n", controller.DescendantNodes().OfType<FieldDeclarationSyntax>()
                .Where(f => f.Declaration.Variables.Any(v => v.Identifier.ValueText == "renderOnlyProbe"
                    || v.Identifier.ValueText == "sightOnlyProbe")).Select(f => f.ToFullString()));
            string source = HarnessSource.Replace("/* CONTROLLER */", field + members + Methods(controller,
                "TryOpen", "PrepareOpen", "FinishOpen", "TryOpenForProbe", "TryOpenForRenderProbe", "TryOpenForSightsProbe",
                "CloseForProbe", "Close", "ResetReadingState", "Update"))
                .Replace("/* PROBE */", Methods(probe, "OpenForRender", "OpenForSights", "Record", "Reset", "BuildSetResult"))
                .Replace("/* POLLER */", Methods(Source("SextantInputPoller.cs"), "PollFix"));
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p));
            var compilation = CSharpCompilation.Create("RenderProbeSlice_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(source, ModCompiler.GameParityParseOptions) }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using (var stream = new MemoryStream())
            {
                var emit = compilation.Emit(stream);
                Assert.True(emit.Success, string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
                return Assembly.Load(stream.ToArray());
            }
        }

        private sealed class Harness
        {
            private readonly Type controllerType;
            private readonly Type probeType;
            private readonly Type stateType;
            private readonly object controller;
            private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Static | BindingFlags.Instance;

            public Harness()
            {
                var assembly = Slice.Value;
                controllerType = assembly.GetType("SextantController", true);
                probeType = assembly.GetType("SextantProbe", true);
                stateType = assembly.GetType("ProbeState", true);
                stateType.GetMethod("Reset").Invoke(null, null);
                controller = Activator.CreateInstance(controllerType);
                probeType.GetField("Live").SetValue(null, controller);
            }

            public bool Mode => (bool)controllerType.GetProperty("RenderOnlyForProbe", All).GetValue(controller);
            public bool SightMode => (bool)controllerType.GetProperty("SightOnlyForProbe", All).GetValue(controller);
            public bool Open => (bool)controllerType.GetProperty("IsOpen", All).GetValue(null);
            public int Count(string name) => (int)stateType.GetField(name).GetValue(null);
            public double Number(string name) => (double)stateType.GetField(name).GetValue(null);
            public bool Flag(string name) => (bool)stateType.GetField(name).GetValue(null);
            public void Set(string name, bool value) => stateType.GetField(name).SetValue(null, value);
            public object Controller(string method) => controllerType.GetMethod(method, All).Invoke(controller, null);
            public string Probe(string method) => (string)probeType.GetMethod(method, All).Invoke(null, null);
            public void PollFix(double hs, bool preserve)
            {
                var type = Slice.Value.GetType("ActualPoller", true);
                type.GetMethod("PollFix").Invoke(Activator.CreateInstance(type), new object[] { hs, preserve });
            }
        }

        // Rendering and game API collaborators are deliberate stubs. Writable
        // operations throw BEFORE changing their fake state when ForbidWrites is
        // true; this is not a fake production observation or calibration restore.
        private const string HarnessSource = @"
using System;
using System.Globalization;
using System.Text;
using UnityEngine;
public static class ProbeState {
    public static bool AllowOpen, AttachSticks, ForbidWrites, SeriesActive, ModeAtAttach,
        SightModeAtAttach, ForbidRestrictedWrites, PreserveAtPoll, HasThird, PressI, PressO;
    public static double CapturedHs;
    public static int EnsureCalls, AttachCalls, SolveCalls, Writes, FixCalls, IndexCalls,
        CourseCalls, SampleCalls, ArcCalls, FilterCalls, ZoomCalls, PanelCalls, SightReads, ClearAimCalls,
        CaptureCalls, ResetCalls, SelectionClearCalls;
    public static void Reset() {
        AllowOpen = AttachSticks = true; ForbidWrites = SeriesActive = ModeAtAttach = false;
        SightModeAtAttach = ForbidRestrictedWrites = PreserveAtPoll = HasThird = PressI = PressO = false;
        CapturedHs = 0;
        EnsureCalls = AttachCalls = SolveCalls = Writes = FixCalls = IndexCalls = CourseCalls =
            SampleCalls = ArcCalls = FilterCalls = ZoomCalls = PanelCalls = SightReads = ClearAimCalls =
            CaptureCalls = ResetCalls = SelectionClearCalls = 0;
    }
    public static void Write() {
        if (ForbidWrites) throw new InvalidOperationException(""FORBIDDEN WRITE"");
        Writes++;
    }
    public static void RestrictedWrite() {
        if (ForbidRestrictedWrites) throw new InvalidOperationException(""FORBIDDEN CALIBRATION OR RESET"");
        Write();
    }
}
namespace UnityEngine {
    public enum KeyCode { Z, P, O, LeftAlt, RightAlt }
    public static class Input {
        public static bool GetKey(KeyCode key) { return false; }
        public static bool GetKeyDown(KeyCode key) { return key == KeyCode.O && ProbeState.PressO; }
    }
    public static class Time { public static float unscaledDeltaTime = 0.1f; }
    public static class Debug { public static void Log(string text) {} }
}
public sealed class FakeEdge { public bool Advance(bool held) { return false; } }
public sealed class FakeHeldProp { public bool IsHeld; }
public sealed class FakeObject { public bool enabled; public void SetActive(bool on) {} }
public sealed class FakeMirror { public void SetEnabled(bool on) {} }
public sealed class FakePip {
    public object LeftRt, RightRt;
    public FakeObject LeftCamera = new FakeObject(), RightCamera = new FakeObject();
    public FakeMirror InteriorMirror = new FakeMirror();
    public void Ensure(params object[] args) { ProbeState.EnsureCalls++; }
    public void SuspendSternspreizPhase0() {} public void SuspendLens() {} public void Detach() {}
}
public sealed class FakeCalibrator {
    public void Solve(params object[] args) { ProbeState.SolveCalls++; ProbeState.RestrictedWrite(); }
}
public sealed class FakeZoom { public float FovDivisor = 1; }
public sealed class FakePoller {
    public void SampleEdges(bool on) { ProbeState.SampleCalls++; }
    public void PollArc(float dt, ref double arc, bool shift, object log) { ProbeState.ArcCalls++; }
    public void PollYawTrim(float dt, bool shift) {}
    public void PollFilters(object setter) { ProbeState.FilterCalls++; }
    public void PollLens(object toggle) {}
    public void PollZoom(object a, object b, object c) { ProbeState.ZoomCalls++; }
    public void PollFix(double arc, bool preserve = false) {
        ProbeState.FixCalls++; ProbeState.PreserveAtPoll = preserve; ProbeState.Write();
    }
    public void PollIndexError(double arc) { ProbeState.IndexCalls++; ProbeState.RestrictedWrite(); }
}
public struct CelestialBody { public bool IsNone; public string DisplayName; }
public sealed class SextantObservation {}
public sealed class FakeCalculator {
    public SextantObservation Observation1 = new SextantObservation(), Observation2, Observation3;
    public bool HasThird { get { return ProbeState.HasThird; } }
}
public sealed class SextantWorksheet {
    public FakeCalculator Calculator = new FakeCalculator();
    public bool SeriesActive { get { return ProbeState.SeriesActive; } }
    public void InvalidateCard() {}
    public void UpdateCourseSampler(float dt) { ProbeState.CourseCalls++; ProbeState.Write(); }
    public void UpdatePanel(float dt) { ProbeState.PanelCalls++; }
    public bool CaptureObservation(params object[] args) {
        ProbeState.Write(); ProbeState.CaptureCalls++; ProbeState.CapturedHs = (double)args[1]; return true;
    }
    public void ResetSeries() { ProbeState.ResetCalls++; ProbeState.RestrictedWrite(); }
    public void ReportBlockedSight(object block) {}
}
public sealed class FakeStarId {
    public CelestialBody IdentifiedBody { get { ProbeState.SightReads++; return new CelestialBody(); } }
    public bool Confirmed = true, HasSelection = true;
    public object LastBlock;
    public void Reset() {} public void ClearSelection() { ProbeState.SelectionClearCalls++; ProbeState.RestrictedWrite(); }
}
public sealed class FakeBurst {
    public int Count = 1;
    public void Tap(double arc) {} public void Clear() {}
    public bool TryCommit(out double ho, out double sigma, out int rejected) {
        ho = sigma = 0; rejected = 0; return true;
    }
}
public sealed class ActualPoller {
    private const float BurstTapIntervalSeconds = 0.1f;
    private float burstTimer;
    private bool fixPressedThisFrame { get { return ProbeState.PressI; } }
    private readonly FakeStarId starId = new FakeStarId();
    private readonly FakeBurst burst = new FakeBurst();
    private readonly SextantWorksheet worksheet = new SextantWorksheet();
    private object CaptureSunDiscSighting(CelestialBody body) { return null; }
    /* POLLER */
}
public sealed class FakeGlare { public void ResetPain() {} }
public sealed class FakeReadout { public void Reset() {} }
public sealed class FakeBodies { public void Invalidate() {} }
public sealed class FakeRenderCheck { public void ResetForOpen() {} public void Update(params object[] args) {} }
public sealed class FakeGameSun { public void ResetDriftTimer() {} public void ResetMoonDiagTimer() {} }
public sealed class FakeLune { public void Reset() {} }
public sealed class FakeBloom { public void Hide() {} }
public sealed class FakeInput { public void RemoveBlockade(object owner) {} }
public static class SunDiscRenderer { public static bool Active; public static void RandomizeDisc() {} public static void ResetDirection() {} }
public static class StarsMesh { public static bool Shielded; }
public static class CameraResolver { public static void UnfreezeRadius() {} }
public static class SextantPipPose { public static void ClearTestAim() { ProbeState.ClearAimCalls++; } }
public static class RadioNavProbeJson { public static string Escape(string value) { return value; } }
public sealed class SextantController {
    private const string LogPrefix = ""[Sextant]"";
    private readonly FakeEdge zEdge = new FakeEdge(), pEdge = new FakeEdge();
    private readonly FakeHeldProp heldModel = new FakeHeldProp();
    // This fixture tests probe write barriers, not the separate handheld input
    // collaborator. Its actual production methods are executed by CPlathInputSourceTests.
    private bool PollHandheldInput(bool take) { return false; }
    private void PollDeviceFilters() { inputPoller.PollFilters(setFiltersDelegate); }
    // Native map output is a collaborator, not part of the extracted render
    // control slice. Treat it as a write so the existing render-only test also
    // proves the new Update guard instead of silently allowing map mutations.
    private void UpdateLopMap(float dt) { ProbeState.Write(); }
    private readonly FakePip pipFactory = new FakePip();
    private readonly FakeCalibrator calibrator = new FakeCalibrator();
    private readonly FakeZoom zoom = new FakeZoom();
    private readonly FakePoller inputPoller = new FakePoller();
    private readonly SextantWorksheet worksheet = new SextantWorksheet();
    private readonly FakeStarId starId = new FakeStarId();
    private readonly FakeGlare glare = new FakeGlare();
    private readonly FakeReadout readout = new FakeReadout();
    private readonly FakeBodies bodiesReadout = new FakeBodies();
    private readonly FakeRenderCheck renderCheck = new FakeRenderCheck();
    private readonly FakeGameSun gameSun = new FakeGameSun();
    private readonly FakeLune lune = new FakeLune();
    private readonly FakeBloom bloomCut = new FakeBloom();
    private readonly FakeInput gameInput = new FakeInput();
    private readonly FakeObject canvasRoot = new FakeObject(), helpPanel = new FakeObject(), painText = new FakeObject();
    private object viewCamera, leftViewImage, rightViewImage, effectMirror, positionProvider,
        logArcInputDelegate, setFiltersDelegate, toggleLensDelegate, zoomInDelegate, zoomOutDelegate, resetArcDelegate;
    private double arcAngle;
    private int leftFilterCount, rightFilterCount;
    private bool openPoseLogPending;
    public SextantController() { IsOpen = false; }
    public SextantWorksheet WorksheetForProbe { get { return worksheet; } }
    public FakeStarId StarIdForProbe { get { return starId; } }
    private bool CanOpen() { return ProbeState.AllowOpen; }
    private void EnsureOverlay() {}
    private static float UiScale() { return 1; }
    private void AttachAndConfigureCameras() {
        ProbeState.AttachCalls++; ProbeState.ModeAtAttach = renderOnlyProbe;
        ProbeState.SightModeAtAttach = sightOnlyProbe;
        if (ProbeState.AttachSticks) IsOpen = true;
    }
    private static bool ShiftHeld() { return false; }
    private void UpdateFilterLabel() {} private void UpdateFilterDim() {}
    private void UpdateReadout(bool force) {} private void UpdateZoomLabel() {}
    private void UpdateGlare(float dt) {} private void UpdateStarId(float dt) {}
    private void UpdateSightLines() {} private void UpdateOpticsMotes() {}
    private void UpdateBodiesReadout(float dt) {} private void UpdateMoonParity(float dt) {}
    private void ToggleHelp() {} private void PollUiScale() {}
    public bool TryGetPositionForProbe(out float lat, out float lon) { lat = lon = 0; return true; }
    public object CaptureSunDiscSighting(CelestialBody body) { return null; }
    /* CONTROLLER */
}
public static class SextantProbe {
    public static SextantController Live;
    private static SextantController ResolveController() { return Live; }
    private static bool MoonDiagEnabled() { return false; }
    private static string Num(double value) { return value.ToString(CultureInfo.InvariantCulture); }
    private static void AppendObservationBody(StringBuilder sb, SextantObservation sight) { sb.Append(""null""); }
    private static void AppendFixBody(StringBuilder sb, SextantWorksheet worksheet) { sb.Append(""null""); }
    /* PROBE */
}
";
    }
}
