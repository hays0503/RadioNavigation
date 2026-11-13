// Embedded probe scenarios; production state-machine methods are never copied.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CPlath.AnimationImport;
using UnityEngine;
using Phase = CPlath.AnimationImport.CPlathAdaptiveAnimationPlayer.Phase;

public static class CPlathAnimationProbe
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Fixture
    {
        internal readonly CPlathAdaptiveAnimationPlayer Player = new CPlathAdaptiveAnimationPlayer();
        internal readonly List<bool> Events = new List<bool>();
        internal readonly Renderer Body = new Renderer(), Device = new Renderer();

        internal Fixture()
        {
            Boundary.Evaluations = 0;
            Boundary.Components = new List<UnityEngine.Object>
            {
                new Animator(), new Transform { name = "CPlath_EyeAnchor" }, Body, Device
            };
            Player.transform = new Transform();
            Player.VisibilityChanged += visible =>
            {
                Check(Body.enabled == visible && Device.enabled == visible, "Visibility event precedes renderer update");
                Events.Add(visible);
            };
            // Fixture lengths and scale read from installed cplath_animated on
            // 2026-11-12. This tests the player's time-scale contract, not asset freshness.
            Player.Initialize(Clip("primary", 3029.333251953125f), Clip("enter", 810.6666870117188f),
                Clip("hold", 1536f), Clip("exit", 682.6666870117188f), 1024);
        }

        private static AnimationClip Clip(string name, float length) { return new AnimationClip { name = name, length = length }; }

        internal void Hidden()
        {
            Check(Player.CurrentPhase == Phase.Stowed && !Player.IsVisible, "Player is not Stowed");
            Check(!Body.enabled && !Device.enabled, "Hidden renderers remain visible");
            Check(Player.CurrentClip.name == "primary" && Player.CurrentTime == 0 && Player.SampleTime == 0, "Wrong hidden clip/time");
            Check(Boundary.Mixer.Weights.SequenceEqual(new float[] { 1, 0, 0, 0 }), "Wrong hidden mixer weights");
            Check(Boundary.Mixer.Clips[0].Time == 0, "Graph did not receive raw time zero");
        }

        internal void Aim()
        {
            Player.SetDesired(true, false);
            Player.Advance(0);
            Player.Advance(2);
            Check(Player.CurrentPhase == Phase.Aiming, "Raise did not reach Aiming");
        }

        internal void Stow()
        {
            Player.SetDesired(false, false);
            Player.Advance(0);
            Player.Advance(5);
            Hidden();
        }
    }

    public static void InitializeHidden()
    {
        var f = new Fixture();
        f.Hidden();
        Check(f.Events.Count == 0, "Spurious initial visibility event");
        Check(Boundary.Evaluations == 1, "Initial graph was not evaluated");
    }

    public static void IdleHidden()
    {
        var f = new Fixture();
        for (int i = 0; i < 100; i++) f.Player.Advance(.1f);
        f.Hidden();
        Check(f.Events.Count == 0, "Idle emitted visibility events");
    }

    public static void FirstEquip()
    {
        var f = new Fixture();
        f.Player.SetDesired(true, false);
        f.Player.Advance(0);
        Check(f.Player.CurrentPhase == Phase.Raising && f.Player.CurrentTime == 0, "Wrong initial Raising pose");
        Check(f.Body.enabled && f.Events.SequenceEqual(new[] { true }), "Wrong Raising visibility");
    }

    public static void CancelRaise()
    {
        var f = new Fixture();
        f.Player.SetDesired(true, false);
        f.Player.Advance(0);
        f.Player.Advance(.3f);
        f.Player.SetDesired(false, false);
        f.Player.Advance(1);
        f.Hidden();
        Check(f.Events.SequenceEqual(new[] { true, false }), "Wrong cancelled-Raising events");
    }

    public static void LowerToStowed()
    {
        var f = new Fixture();
        f.Aim();
        f.Player.SetDesired(false, false);
        f.Player.Advance(0);
        Check(f.Player.CurrentPhase == Phase.Lowering && f.Player.IsVisible, "Lowering hidden too early");
        f.Player.Advance(.1f);
        Check(f.Player.CurrentPhase == Phase.Lowering && f.Body.enabled, "Lowering renderer lost");
        f.Player.Advance(5);
        f.Hidden();
        Check(f.Events.SequenceEqual(new[] { true, false }), "Missing hidden event after Lowering");
    }

    public static void Reequip()
    {
        var f = new Fixture();
        f.Aim();
        f.Stow();
        f.Player.SetDesired(true, false);
        f.Player.Advance(0);
        Check(f.Player.CurrentPhase == Phase.Raising && f.Player.CurrentTime == 0, "Reequip retained the end pose");
        Check(f.Events.SequenceEqual(new[] { true, false, true }), "Wrong reequip visibility edges");
    }

    public static void ReadThenStow()
    {
        var f = new Fixture();
        f.Aim();
        f.Player.SetDesired(true, true);
        f.Player.Advance(0);
        Check(f.Player.CurrentPhase == Phase.EnteringRead, "Did not enter read");
        f.Player.Advance(2);
        Check(f.Player.CurrentPhase == Phase.Reading, "Did not reach Reading");
        f.Player.SetDesired(false, false);
        f.Player.Advance(0);
        Check(f.Player.CurrentPhase == Phase.ExitingRead, "Did not exit read");
        f.Player.Advance(2);
        Check(f.Player.CurrentPhase == Phase.Aiming, "Wrong read-exit destination");
        f.Player.Advance(0);
        Check(f.Player.CurrentPhase == Phase.Lowering, "Did not lower after read exit");
        f.Player.Advance(5);
        f.Hidden();
    }

    public static void ReverseLowering()
    {
        var f = new Fixture();
        f.Aim();
        f.Player.SetDesired(false, false);
        f.Player.Advance(0);
        f.Player.Advance(.3f);
        f.Player.SetDesired(true, false);
        f.Player.Advance(1);
        Check(f.Player.CurrentPhase == Phase.Aiming && f.Body.enabled, "Failed to reverse Lowering");
        Check(f.Events.SequenceEqual(new[] { true }), "Reversal emitted an unexpected hidden edge");
    }

    public static void DisableAndReinitialize()
    {
        var f = new Fixture();
        f.Aim();
        var clips = Boundary.Mixer.Clips.Select(clip => clip.Clip).ToArray();
        typeof(CPlathAdaptiveAnimationPlayer).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(f.Player, null);
        Check(f.Player.CurrentPhase == Phase.Stowed && !f.Body.enabled, "OnDisable did not hide");
        Check(f.Events.SequenceEqual(new[] { true, false }), "Wrong OnDisable event");
        bool rejected = false;
        try { f.Player.Advance(0); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Advance accepted a disabled player");
        // OnDisable need not reset clip/time: its graph is no longer valid.
        f.Player.Initialize(clips[0], clips[1], clips[2], clips[3], 1024);
        f.Hidden();
        Check(f.Events.SequenceEqual(new[] { true, false }), "Reinitialization emitted a visibility edge");
    }

    public static void RepeatedCycles()
    {
        var f = new Fixture();
        for (int i = 0; i < 100; i++) { f.Aim(); f.Stow(); }
        Check(f.Events.Count == 200, "Lost or duplicated visibility event");
    }
}
