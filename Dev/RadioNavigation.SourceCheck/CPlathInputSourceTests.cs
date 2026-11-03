using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace RadioNavigation.SourceCheck
{
    /// <summary>Execute the actual input partial; mock model loading, camera and PIP collaborators only.</summary>
    public sealed class CPlathInputSourceTests
    {
        [Fact]
        public void TakeNeverOpensTheOcularAndOnlyRmbEntersIt()
        {
            var h = new Harness(); h.Tick(true, false);
            Assert.True(h.Flag("Held")); Assert.False(h.Flag("Open")); Assert.Equal(0, h.Number("Opens"));
            h.Tick(false, true); h.Tick(false, true);
            Assert.True(h.Flag("Open")); Assert.True(h.Flag("Aiming")); Assert.Equal(1, h.Number("Opens"));
            Assert.Equal(2, h.Number("Left")); Assert.Equal(3, h.Number("Right"));
        }

        [Fact]
        public void RmbReleaseReturnsToPropAndZStowsFromEitherState()
        {
            var h = new Harness(); h.Tick(true, false); h.Tick(false, true); h.Tick(false, false);
            Assert.True(h.Flag("Held")); Assert.False(h.Flag("Open")); Assert.False(h.Flag("Aiming"));
            h.Tick(false, true); h.Tick(true, true);
            Assert.False(h.Flag("Held")); Assert.False(h.Flag("Open"));
            h.Tick(false, false); Assert.Equal(2, h.Number("Opens"));
        }

        [Fact]
        public void TakingWithRmbAlreadyHeldNeedsAFreshAimPress()
        {
            var h = new Harness(); h.Tick(true, true); h.Tick(false, true);
            Assert.True(h.Flag("Held")); Assert.False(h.Flag("Open"));
            h.Tick(false, false); h.Tick(false, true); Assert.True(h.Flag("Open"));
        }

        [Fact]
        public void NonFppAndDisableCannotLeaveThePropOrOcularAlive()
        {
            var h = new Harness(); h.Call("LeaveFpp"); h.Tick(true, false); Assert.False(h.Flag("Held"));
            h.Call("EnterFpp"); h.Tick(true, false); h.Tick(false, true); h.Call("LeaveFpp"); h.Tick(false, true);
            Assert.False(h.Flag("Held")); Assert.False(h.Flag("Open"));
            h.Call("EnterFpp"); h.Tick(true, false); h.Tick(false, true); h.Call("Disable");
            Assert.False(h.Flag("Held")); Assert.False(h.Flag("Open"));
        }

        [Fact]
        public void RenderProbesWithoutHeldPropKeepTheirExistingLifecycle()
        {
            var h = new Harness(); h.Call("OpenForExistingProbe"); h.Tick(false, false);
            Assert.True(h.Flag("Open")); Assert.False(h.Flag("Held")); Assert.Equal(0, h.Number("Closes"));
        }

        [Fact]
        public void IndependentShadesAreAppliedOnlyWhenChangedAndNeverReplacedByCounts()
        {
            var h = new Harness(); h.Tick(true, false); h.Call("ChangeIndependentShades"); h.Tick(false, false);
            Assert.Equal(1, h.Number("Left")); Assert.Equal(4, h.Number("Right")); Assert.Equal(0, h.Number("BankResets"));
            int writes = h.Number("FilterWrites"); h.Tick(false, false);
            Assert.Equal(writes, h.Number("FilterWrites"));
        }

        // AdaptiveLoweringAndReadBoundaryV1
        [Fact]
        public void ReadBranchDoesNotOpenOcularAndRmbKeepsPriority()
        {
            var h = new Harness(); h.Tick(true, false); h.Call("ReadOn"); h.Tick(false, false);
            Assert.True(h.Flag("Reading")); Assert.False(h.Flag("Open"));
            h.Tick(false, true); Assert.True(h.Flag("Open")); Assert.False(h.Flag("Reading"));
            h.Tick(false, false); Assert.True(h.Flag("Reading")); h.Call("ReadOff"); h.Tick(false, false);
            Assert.False(h.Flag("Reading")); Assert.True(h.Flag("Held"));
        }

        [Fact]
        public void LoweringContinuesAfterZUntilSettledButDisableStopsIt()
        {
            var h = new Harness(); h.Tick(true, false); h.Tick(true, false);
            Assert.False(h.Flag("Held")); h.Call("LateTick"); Assert.Equal(1, h.Number("Updates"));
            h.Call("LateTick"); h.Call("LateTick"); Assert.Equal(2, h.Number("Updates"));
            h.Tick(true, false); h.Tick(true, false); h.Call("Disable"); h.Call("LateTick");
            Assert.Equal(2, h.Number("Updates")); Assert.False(h.Flag("Held"));
        }

        private sealed class Harness
        {
            readonly Type type;
            readonly object value;
            public Harness()
            {
                string partial = File.ReadAllText(Path.Combine(ModCompiler.ModRoot, "Source/UI/CPlathDeviceInput.cs"));
                var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
                    .Select(p => MetadataReference.CreateFromFile(p));
                var compilation = CSharpCompilation.Create("CPlathInput_" + Guid.NewGuid().ToString("N"),
                    new[] { CSharpSyntaxTree.ParseText(partial, ModCompiler.GameParityParseOptions),
                        CSharpSyntaxTree.ParseText(Host, ModCompiler.GameParityParseOptions) }, references,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                using (var stream = new MemoryStream())
                {
                    var emit = compilation.Emit(stream);
                    Assert.True(emit.Success, string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
                    type = Assembly.Load(stream.ToArray()).GetType("UBOAT.Mods.RadioNavigation.SextantController", true);
                }
                value = Activator.CreateInstance(type);
            }
            public void Call(string name) { type.GetMethod(name).Invoke(value, null); }
            public void Tick(bool take, bool aim) { type.GetMethod("Tick").Invoke(value, new object[] { take, aim }); }
            public bool Flag(string name) { return (bool)type.GetProperty(name).GetValue(value); }
            public int Number(string name) { return (int)type.GetProperty(name).GetValue(value); }
        }

        const string Host = @"
using UnityEngine;
namespace UnityEngine {
    public class Camera {}
    public enum KeyCode { V }
    public static class Input { public static bool Aim, Read; public static bool GetKey(KeyCode key) { return Read; } public static bool GetMouseButton(int button) { return Aim; } }
    public static class Time { public static float unscaledDeltaTime = .02f; }
    public static class Debug { public static void Log(string message) {} }
}
namespace UBOAT.Mods.RadioNavigation {
    internal sealed class CPlathHeldModel {
        public bool IsHeld, Aiming, Alt, Changed, Reading, Lowering; public int Updates; public bool NeedsUpdate { get { return IsHeld || Lowering; } }
        public int LeftCount = 1, RightCount = 4, BankResets;
        public bool Take() { IsHeld = true; Lowering = false; return true; }
        public void Stow() { IsHeld = Aiming = Reading = false; Lowering = true; } public void Suspend() { IsHeld = Aiming = Reading = Lowering = false; } public void SetReading(bool reading) { Reading = reading; }
        public void SetAiming(bool on) { Aiming = on; }
        public void SetBankCount(int n,bool left) { BankResets++; }
        public bool PollIndependentShades(out bool changed) { changed = Changed; Changed = false; return Alt; }
        public void UpdatePose(Camera c,double d,int l,int r,float dt) { Updates++; if (Updates >= 2) Lowering = false; }
    }
    internal sealed class FakePoller {
        public void PollArc(float dt,ref double d,bool shift,object log) {}
        public void PollFilters(object setter) {}
    }
    internal static class CameraResolver { public static bool TryResolve(out Camera c) { c = new Camera(); return true; } }
    public sealed partial class SextantController {
        const string LogPrefix = ""test"";
        bool fpp = true;
        readonly FakePoller inputPoller = new FakePoller();
        double arcAngle;
        int leftFilterCount = 2, rightFilterCount = 3;
        object logArcInputDelegate, setFiltersDelegate;
        public bool IsOpen;
        public bool Held { get { return heldModel.IsHeld; } }
        public bool Open { get { return IsOpen; } }
        public bool Aiming { get { return heldModel.Aiming; } } public bool Reading { get { return heldModel.Reading; } } public int Updates { get { return heldModel.Updates; } }
        public int Opens { get; private set; }
        public int Closes { get; private set; }
        public int FilterWrites { get; private set; }
        public int BankResets { get { return heldModel.BankResets; } }
        public int Left { get { return leftFilterCount; } }
        public int Right { get { return rightFilterCount; } }
        string CurrentModeName() { return fpp ? ""FPP"" : ""Map""; }
        static bool ShiftHeld() { return false; }
        void TryOpen() { IsOpen = true; Opens++; leftFilterCount = rightFilterCount = 0; }
        void Close() { if(IsOpen) Closes++; IsOpen = false; }
        void SetFilters(int n,bool left) {
            if (!applyingHeldShadeCounts) heldModel.SetBankCount(n,left);
            if(left) leftFilterCount=n; else rightFilterCount=n; FilterWrites++;
        }
        public void Tick(bool take,bool aim) { Input.Aim = aim; PollHandheldInput(take); } public void ReadOn() { Input.Read = true; } public void ReadOff() { Input.Read = false; } public void LateTick() { UpdateHeldModelPose(); }
        public void EnterFpp() { fpp = true; } public void LeaveFpp() { fpp = false; }
        public void Disable() { OnDisable(); }
        public void OpenForExistingProbe() { IsOpen = true; }
        public void ChangeIndependentShades() { heldModel.Alt = heldModel.Changed = true; }
    }
}";
    }
}
