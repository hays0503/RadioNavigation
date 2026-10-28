using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using DWS.Common.InjectionFramework;
using UBOAT.Game.Core.Serialization;
using UBOAT.Game.Core.Time;
using UBOAT.Game.Scene.Environment;
using UBOAT.Mods.RadioNavigation;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class Program
{
    private sealed class Source : ICelestialSkyInputSource
    {
        internal int Reads;
        internal DateTime Gmt = new DateTime(1942, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        internal double Latitude = 55, Longitude = 4;
        public bool TryRead(long revision, out CelestialSkyInput input, out CelestialSkyNotReady reason)
        {
            Reads++;
            input = new CelestialSkyInput(revision, Gmt,
                Latitude, Longitude, 5, 5, 1010, 10, 0, true, true, true, true, true);
            reason = CelestialSkyNotReady.None; return true;
        }
    }
    private static SavesManager saves;
    private static CelestialWorldHost host;
    private static string copyPath;
    private static string caseFilter;
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reset()
    {
        Teardown();
        UnityEngine.Object.Components.Clear(); SceneManager.Main = default(Scene);
        var framework = new InjectionFramework(); InjectionFramework.Instance = framework;
        saves = new SavesManager(); framework.Values[typeof(SavesManager)] = saves;
        framework.Values[typeof(GameTime)] = new GameTime { GmtDateTime = new DateTime(1942, 6, 15, 12, 0, 0) };
        framework.Values[typeof(Weather)] = new Weather { Sky = new TOD_Sky { World = new TOD_WorldParameters { Latitude = 55, Longitude = 4 } } };
        host = null;
        UnityEngine.Debug.Warnings.Clear();
    }
    private static void Teardown()
    {
        foreach (MonoBehaviour component in UnityEngine.Object.Components.ToArray()) UnityEngine.Object.Destroy(component);
        Require(SceneManager.Subscriptions == 0, "scene hooks survived all host destruction");
        if (!ReferenceEquals(saves, null)) Require(saves.Subscribers == 0, "save hooks survived all host destruction");
    }
    private static void Start()
    {
        SceneManager.Load(1); CelestialWorldHost.EnsureRunning();
        host = (CelestialWorldHost)UnityEngine.Object.Components[0];
    }
    private static Source UseTrustedFakeSource()
    {
        var source = new Source();
        typeof(CelestialWorldHost).GetField("source", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(host, source);
        return source;
    }
    private static void Tick() { host.Invoke("LateUpdate"); }
    private static void Drain(Queue<Action> queue) { while (queue.Count > 0) queue.Dequeue()(); }
    private static string Diagnostic(Assembly assembly = null)
    {
        Type type = (assembly ?? typeof(CelestialWorldHost).Assembly).GetType("UBOAT.Mods.RadioNavigation.CelestialWorldHost");
        PropertyInfo property = type.GetProperty("LastEnsureStatus", BindingFlags.NonPublic | BindingFlags.Static);
        return property == null ? "UNREPORTED" : (string)property.GetValue(null);
    }
    private static void ExpectDiagnostic(string expected) { Require(Diagnostic() == expected, "ownership diagnostic=" + Diagnostic()); }
    private static Type CopyType()
    {
        Require(copyPath != null, "two-assembly fixture missing");
        return Assembly.LoadFrom(copyPath).GetType("UBOAT.Mods.RadioNavigation.CelestialWorldHost", true);
    }
    private static object CopyOwner(MonoBehaviour component)
    { return component.GetType().GetProperty("Owner", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(component); }
    private static object CopySnapshot(MonoBehaviour component)
    {
        object owner = CopyOwner(component);
        return owner.GetType().GetProperty("Current", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(owner);
    }
    private static object CopySource(MonoBehaviour component)
    {
        Type source = component.GetType().Assembly.GetType("Program+Source", true);
        object value = Activator.CreateInstance(source, true);
        component.GetType().GetField("source", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(component, value);
        return value;
    }
    private static MonoBehaviour Foreign(bool inactive)
    {
        var name = new AssemblyName("UnownedHost_" + Guid.NewGuid().ToString("N"));
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
        TypeBuilder type = assembly.DefineDynamicModule(name.Name).DefineType("UBOAT.Mods.RadioNavigation.CelestialWorldHost",
            TypeAttributes.Public | TypeAttributes.Class, typeof(MonoBehaviour));
        type.DefineDefaultConstructor(MethodAttributes.Public);
        FieldBuilder counter = type.DefineField("ForeignCalls", typeof(int), FieldAttributes.Public | FieldAttributes.Static);
        MethodBuilder release = type.DefineMethod("ReleaseForOwnershipConflict", MethodAttributes.Private, typeof(void), Type.EmptyTypes);
        ILGenerator il = release.GetILGenerator();
        il.Emit(OpCodes.Ldsfld, counter); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stsfld, counter); il.Emit(OpCodes.Ret);
        var root = new GameObject("foreign-name-collision");
        if (inactive) root.SetActive(false);
        return root.AddComponent(type.CreateTypeInfo().AsType());
    }
    private static int ForeignCalls(MonoBehaviour component)
    { return (int)component.GetType().GetField("ForeignCalls", BindingFlags.Public | BindingFlags.Static).GetValue(null); }
    private static void Check(string name, Action test, ref int fail)
    {
        try { Reset(); }
        catch (Exception e) { fail++; Console.WriteLine("HARNESS_ERROR\t" + name + "\t" + e); return; }
        Console.WriteLine("BEGIN\t" + name);
        try { test(); Teardown(); Console.WriteLine("PASS\t" + name); }
        catch (Exception e)
        {
            fail++; Console.WriteLine("FAIL\t" + name + "\t" + e);
            try { Teardown(); } catch (Exception cleanup) { Console.WriteLine("CLEANUP_AFTER_FAIL\t" + cleanup.Message); }
        }
    }

    private static int Main(string[] args)
    {
        int fail = 0, count = 0;
        foreach (string arg in args)
        {
            if (arg.StartsWith("--case=", StringComparison.Ordinal)) caseFilter = arg.Substring(7);
            else copyPath = arg;
        }
        Action<string, Action> check = (name, test) => {
            if (caseFilter != null && caseFilter != name) return;
            count++; Check(name, test, ref fail);
        };
        check("Ensure_twice_one_owner_two_hooks", delegate {
            Start(); CelestialWorldHost.EnsureRunning(); Require(UnityEngine.Object.Components.Count == 1, "duplicate root");
            Require(SceneManager.Subscriptions == 2 && saves.Subscribers == 2, "duplicate hooks"); });
        check("Disabled_owner_not_duplicated", delegate {
            Start(); host.enabled = false; CelestialWorldHost.EnsureRunning();
            Require(UnityEngine.Object.Components.Count == 1, "inactive owner missed");
            Require(host.Owner.Current == null && SceneManager.Subscriptions == 0 && saves.Subscribers == 0, "cleanup"); });
        check("Explicit_duplicate_disables_itself", delegate {
            Start(); var other = new GameObject("duplicate").AddComponent<CelestialWorldHost>();
            Require(!other.enabled && host.enabled, "two active owners"); Require(saves.Subscribers == 2, "duplicate subscriptions"); });
        check("Missing_game_clock_is_NOT_READY", delegate {
            Start(); InjectionFramework.Instance.Values.Remove(typeof(GameTime)); Tick();
            Require(host.Owner.Current == null && host.Owner.NotReady == CelestialSkyNotReady.GameTime, "clock fallback"); });
        check("Default_game_source_refuses_untrusted_height", delegate {
            Start(); Tick(); Require(host.Owner.Current == null && host.Owner.NotReady == CelestialSkyNotReady.HeightDatum, "invented eye/atmosphere"); });
        check("Trusted_fake_source_computes_without_Z", delegate {
            Start(); var source = UseTrustedFakeSource(); Tick(); Require(source.Reads == 1 && host.Owner.Current != null, "missing compute"); });
        check("Loading_invalidates_until_queue_tail", delegate {
            Start(); UseTrustedFakeSource(); Tick(); long previousRevision = host.Owner.WorldRevision;
            saves.Begin(); Require(host.Owner.Current == null, "stale on begin");
            Require(host.Owner.WorldRevision > previousRevision, "load invalidated value but reused old world token");
            Queue<Action> q = saves.Finish(); Tick(); Require(host.Owner.Current == null, "published before Loaded tail");
            Drain(q); Tick(); Require(host.Owner.Current != null, "no recovery after Loaded"); });
        check("Old_Loaded_queue_cannot_finish_new_load", delegate {
            Start(); UseTrustedFakeSource(); saves.Begin(); Queue<Action> old = saves.Finish(); saves.Begin();
            Drain(old); Tick(); Require(host.Owner.Current == null, "old load callback accepted");
            Queue<Action> latest = saves.Finish(); Tick(); Require(host.Owner.Current == null, "old token skipped newest restore tail");
            Drain(latest); Tick(); Require(host.Owner.Current != null, "latest load never ready"); });
        check("Unload_invalidates_and_blocks_delayed_queue", delegate {
            Start(); UseTrustedFakeSource(); Tick(); saves.Begin(); Queue<Action> q = saves.Finish();
            SceneManager.Unload(); Drain(q); Tick(); Require(host.Owner.Current == null, "stale after scene unload"); });
        check("Disable_unhooks_and_late_queue_is_inert", delegate {
            Start(); UseTrustedFakeSource(); saves.Begin(); Queue<Action> q = saves.Finish(); host.enabled = false; Drain(q);
            Require(saves.Subscribers == 0 && SceneManager.Subscriptions == 0 && host.Owner.Current == null, "hook/queue leak"); });
        check("Reenable_reuses_single_owner", delegate {
            Start(); host.enabled = false; host.enabled = true; UseTrustedFakeSource(); Tick();
            Require(host.Owner.Current != null && saves.Subscribers == 2 && SceneManager.Subscriptions == 2, "reenable stale"); });
        check("Manager_rebind_detaches_old_instance", delegate {
            Start(); UseTrustedFakeSource(); Tick(); SavesManager previous = saves;
            saves = new SavesManager(); InjectionFramework.Instance.Values[typeof(SavesManager)] = saves; Tick();
            Require(previous.Subscribers == 0 && saves.Subscribers == 2 && host.Owner.Current != null, "rebind"); });
        check("Game_values_untouched", delegate {
            Start(); var weather = InjectionFramework.Instance.GetInstance<Weather>();
            var time = InjectionFramework.Instance.GetInstance<GameTime>(); DateTime gmt = time.GmtDateTime;
            Tick(); Tick(); host.enabled = false;
            Require(time.GmtDateTime == gmt && weather.Sky.World.Latitude == 55 && weather.Sky.World.Longitude == 4 && weather.Sky.World.UTC == 0, "native write"); });
        check("Destroyed_manager_fake_null_unsubscribes_managed_events", delegate {
            Start(); UseTrustedFakeSource(); Tick(); SavesManager previous = saves; previous.Destroyed = true;
            Tick(); Require(previous.Subscribers == 0 && host.Owner.Current == null, "destroyed manager retained");
            saves = new SavesManager(); InjectionFramework.Instance.Values[typeof(SavesManager)] = saves; Tick();
            Require(saves.Subscribers == 2 && host.Owner.Current != null, "no recovery after manager destruction"); });
        if (copyPath != null) check("Second_actual_assembly_copy_no_duplicate", delegate {
            Start(); Assembly copy = Assembly.LoadFrom(copyPath);
            Type type = copy.GetType("UBOAT.Mods.RadioNavigation.CelestialWorldHost", true);
            type.GetMethod("EnsureRunning", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            Require(UnityEngine.Object.Components.Count == 1, "Type-only dedupe");
            MonoBehaviour other = new GameObject("other-copy").AddComponent(type);
            Require(!other.enabled && saves.Subscribers == 2, "second assembly owner active"); });
        check("Disabled_Ensure_reports_blocked_without_implicit_restart", delegate {
            Start(); Source source = UseTrustedFakeSource(); Tick(); var old = host.Owner.Current;
            int reads = source.Reads; host.enabled = false; CelestialWorldHost.EnsureRunning(); Tick();
            ExpectDiagnostic("NOT_READY_DISABLED_OR_INACTIVE");
            Require(!host.enabled && host.Owner.Current == null && source.Reads == reads, "disabled host resumed or retained state");
            Require(saves.Subscribers == 0 && SceneManager.Subscriptions == 0 && UnityEngine.Object.Components.Count == 1, "hidden restart/hooks");
            Require(!host.Owner.CanUse(old, old.Input) && UnityEngine.Debug.Warnings.Count > 0, "silent stale owner"); });
        check("Inactive_root_is_not_healthy_and_never_auto_activated", delegate {
            Start(); UseTrustedFakeSource(); Tick(); host.gameObject.SetActive(false); CelestialWorldHost.EnsureRunning(); Tick();
            ExpectDiagnostic("NOT_READY_DISABLED_OR_INACTIVE");
            Require(host.enabled && !host.gameObject.activeInHierarchy && host.Owner.Current == null, "inactive GO activated or stale");
            Require(SceneManager.Subscriptions == 0 && saves.Subscribers == 0, "inactive owner hooks"); });
        check("Explicit_root_reactivation_restores_computation_not_old_snapshot", delegate {
            Start(); Source source = UseTrustedFakeSource(); Tick(); var old = host.Owner.Current;
            host.gameObject.SetActive(false); SceneManager.Unload(); SceneManager.Load(2);
            CelestialWorldHost.EnsureRunning(); ExpectDiagnostic("NOT_READY_DISABLED_OR_INACTIVE");
            host.gameObject.SetActive(true); source.Gmt = source.Gmt.AddMinutes(2); Tick();
            Require(host.Owner.Current != null && !ReferenceEquals(old, host.Owner.Current), "explicit reactivation did not compute");
            Require(!host.Owner.CanUse(old, old.Input) && saves.Subscribers == 2 && SceneManager.Subscriptions == 2, "stale reactivation/hooks"); });
        check("Destroyed_only_owner_fresh_Ensure_recovers_computation", delegate {
            Start(); UseTrustedFakeSource(); Tick(); var oldOwner = host; var oldState = host.Owner.Current;
            UnityEngine.Object.Destroy(host); Require(oldOwner == null && oldOwner.Owner.Current == null, "Unity fake null/cleanup");
            Require(saves.Subscribers == 0 && SceneManager.Subscriptions == 0, "destroy hook leak");
            CelestialWorldHost.EnsureRunning(); Require(UnityEngine.Object.Components.Count == 1, "fresh recovery did not create one owner");
            host = (CelestialWorldHost)UnityEngine.Object.Components[0]; UseTrustedFakeSource(); Tick();
            ExpectDiagnostic("OWNER_ACTIVE"); Require(!ReferenceEquals(host, oldOwner) && host.Owner.Current != null, "fresh recovery absent");
            Require(!host.Owner.CanUse(oldState, oldState.Input), "old owner snapshot reused"); });
        check("Destroyed_fake_null_ghost_cannot_block_new_owner", delegate {
            Start(); var dead = host; UnityEngine.Object.Destroy(dead);
            UnityEngine.Object.Components.Add(dead); CelestialWorldHost.EnsureRunning();
            host = (CelestialWorldHost)UnityEngine.Object.Components.Find(c => c != null); UseTrustedFakeSource(); Tick();
            ExpectDiagnostic("OWNER_ACTIVE"); Require(host.Owner.Current != null && !ReferenceEquals(host, dead), "fake-null ghost adopted");
            UnityEngine.Object.Components.Remove(dead); });
        check("Destroy_active_with_disabled_duplicate_reports_no_owner", delegate {
            Start(); UseTrustedFakeSource(); Tick(); var duplicate = new GameObject("duplicate").AddComponent<CelestialWorldHost>();
            UnityEngine.Object.Destroy(host); CelestialWorldHost.EnsureRunning();
            ExpectDiagnostic("NOT_READY_DISABLED_OR_INACTIVE"); Require(!duplicate.enabled && duplicate.Owner.Current == null, "standby auto-reactivated");
            Require(saves.Subscribers == 0 && SceneManager.Subscriptions == 0 && UnityEngine.Object.Components.Count == 1, "hidden failover"); });
        check("Explicit_enable_of_remaining_duplicate_restores_liveness", delegate {
            Start(); UseTrustedFakeSource(); Tick(); var old = host.Owner.Current;
            var duplicate = new GameObject("duplicate").AddComponent<CelestialWorldHost>(); UnityEngine.Object.Destroy(host);
            CelestialWorldHost.EnsureRunning(); ExpectDiagnostic("NOT_READY_DISABLED_OR_INACTIVE");
            host = duplicate; host.enabled = true; Source source = UseTrustedFakeSource(); Tick(); var first = host.Owner.Current;
            Require(first != null && !host.Owner.CanUse(old, old.Input), "no recovery/stale state");
            source.Gmt = source.Gmt.AddSeconds(30); source.Longitude += 1; Tick();
            Require(host.Owner.Current.SnapshotVersion > first.SnapshotVersion && !ReferenceEquals(first, host.Owner.Current), "recovered owner never advances");
            Require(saves.Subscribers == 2 && SceneManager.Subscriptions == 2, "recovery hooks not unique"); });
        check("Disabled_unload_load_then_explicit_enable_rejects_old_queue", delegate {
            Start(); Source source = UseTrustedFakeSource(); Tick(); saves.Begin(); Queue<Action> oldQueue = saves.Finish();
            host.enabled = false; SceneManager.Unload(); SceneManager.Load(7); CelestialWorldHost.EnsureRunning();
            ExpectDiagnostic("NOT_READY_DISABLED_OR_INACTIVE"); host.enabled = true; source.Gmt = source.Gmt.AddHours(1); Tick();
            var fresh = host.Owner.Current; Drain(oldQueue); Tick();
            Require(fresh != null && ReferenceEquals(fresh, host.Owner.Current), "old queue reset/reused reenabled owner");
            Require(saves.Subscribers == 2 && SceneManager.Subscriptions == 2, "duplicate subscriptions after reload"); });
        check("Destroyed_owner_old_queue_cannot_target_fresh_replacement", delegate {
            Start(); UseTrustedFakeSource(); saves.Begin(); Queue<Action> oldQueue = saves.Finish(); var oldOwner = host;
            UnityEngine.Object.Destroy(host); CelestialWorldHost.EnsureRunning();
            host = (CelestialWorldHost)UnityEngine.Object.Components[0]; UseTrustedFakeSource(); Tick(); var fresh = host.Owner.Current;
            Drain(oldQueue); Tick(); Require(fresh != null && ReferenceEquals(fresh, host.Owner.Current) && oldOwner.Owner.Current == null, "destroyed callback resurrected state"); });
        check("Ensure_active_owner_preserves_and_advances_snapshot", delegate {
            Start(); Source source = UseTrustedFakeSource(); Tick(); var first = host.Owner.Current;
            CelestialWorldHost.EnsureRunning(); Tick(); Require(ReferenceEquals(first, host.Owner.Current), "Ensure reset healthy owner");
            source.Gmt = source.Gmt.AddMinutes(1); source.Latitude += .1; Tick();
            Require(host.Owner.Current.SnapshotVersion > first.SnapshotVersion, "active owner liveness missing"); ExpectDiagnostic("OWNER_ACTIVE"); });
        check("Foreign_active_FullName_never_adopted_or_invoked", delegate {
            SceneManager.Load(1); MonoBehaviour foreign = Foreign(false); CelestialWorldHost.EnsureRunning();
            ExpectDiagnostic("NOT_READY_FOREIGN_COLLISION"); Require(foreign.enabled && foreign.isActiveAndEnabled && ForeignCalls(foreign) == 0, "foreign host captured");
            Require(UnityEngine.Object.Components.Count == 1 && saves.Subscribers == 0, "owner created around foreign collision"); });
        check("Foreign_inactive_FullName_never_activated_or_invoked", delegate {
            SceneManager.Load(1); MonoBehaviour foreign = Foreign(true); CelestialWorldHost.EnsureRunning();
            ExpectDiagnostic("NOT_READY_FOREIGN_COLLISION"); Require(foreign.enabled && !foreign.gameObject.activeInHierarchy && ForeignCalls(foreign) == 0, "foreign inactive host changed"); });
        check("Foreign_collision_revokes_only_owned_snapshot_and_hooks", delegate {
            Start(); UseTrustedFakeSource(); Tick(); Require(host.Owner.Current != null, "baseline missing");
            MonoBehaviour foreign = Foreign(false); CelestialWorldHost.EnsureRunning(); ExpectDiagnostic("NOT_READY_FOREIGN_COLLISION");
            Require(!host.enabled && host.Owner.Current == null && saves.Subscribers == 0 && SceneManager.Subscriptions == 0, "own state remained qualified");
            Require(foreign.enabled && ForeignCalls(foreign) == 0, "foreign component was touched"); });
        check("Detected_multiple_owned_active_hosts_release_both_no_auto_election", delegate {
            Start(); UseTrustedFakeSource(); Tick(); var duplicate = new GameObject("duplicate").AddComponent<CelestialWorldHost>();
            duplicate.ForceEnabledWithoutCallback(true); CelestialWorldHost.EnsureRunning(); ExpectDiagnostic("NOT_READY_MULTIPLE_ACTIVE");
            Require(!host.enabled && !duplicate.enabled && host.Owner.Current == null && duplicate.Owner.Current == null, "two active canonical hosts");
            Require(saves.Subscribers == 0 && SceneManager.Subscriptions == 0, "conflict hook leak"); });
        check("Unchanged_blocked_Ensure_diagnostics_are_not_spammed", delegate {
            Start(); host.enabled = false; CelestialWorldHost.EnsureRunning(); int warnings = UnityEngine.Debug.Warnings.Count;
            CelestialWorldHost.EnsureRunning(); CelestialWorldHost.EnsureRunning();
            ExpectDiagnostic("NOT_READY_DISABLED_OR_INACTIVE"); Require(warnings > 0 && UnityEngine.Debug.Warnings.Count == warnings, "missing/spammed blocked diagnostic"); });
        check("Disable_drops_snapshot_immediately_without_another_tick", delegate {
            Start(); UseTrustedFakeSource(); Tick(); var old = host.Owner.Current; Require(old != null, "baseline missing");
            host.enabled = false;
            Require(host.Owner.Current == null && !host.Owner.CanUse(old, old.Input), "disabled host still exports stale snapshot");
            Require(saves.Subscribers == 0 && SceneManager.Subscriptions == 0, "disable cleanup not immediate"); });
        if (copyPath != null) check("Compatible_other_copy_is_live_and_survives_explicit_reenable", delegate {
            SceneManager.Load(1); MonoBehaviour other = new GameObject("other-copy-first").AddComponent(CopyType());
            object source = CopySource(other); other.Invoke("LateUpdate"); object first = CopySnapshot(other);
            CelestialWorldHost.EnsureRunning(); ExpectDiagnostic("OWNER_ACTIVE"); Require(first != null && UnityEngine.Object.Components.Count == 1, "other copy not a live owner");
            other.enabled = false; CelestialWorldHost.EnsureRunning(); ExpectDiagnostic("NOT_READY_DISABLED_OR_INACTIVE");
            Require(CopySnapshot(other) == null && saves.Subscribers == 0, "other copy retained stale state/hooks");
            SceneManager.Unload(); SceneManager.Load(9); other.enabled = true;
            source.GetType().GetField("Gmt", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(source, new DateTime(1943, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            other.Invoke("LateUpdate"); CelestialWorldHost.EnsureRunning(); ExpectDiagnostic("OWNER_ACTIVE");
            Require(CopySnapshot(other) != null && !ReferenceEquals(first, CopySnapshot(other)), "other copy not recovered"); });
        if (copyPath != null) check("Cross_copy_disabled_duplicate_after_destroy_needs_explicit_enable", delegate {
            Start(); UseTrustedFakeSource(); Tick(); MonoBehaviour other = new GameObject("other-copy-duplicate").AddComponent(CopyType());
            Require(!other.enabled, "second copy not parked"); UnityEngine.Object.Destroy(host);
            CelestialWorldHost.EnsureRunning(); ExpectDiagnostic("NOT_READY_DISABLED_OR_INACTIVE");
            Require(!other.enabled && saves.Subscribers == 0 && CopySnapshot(other) == null, "implicit cross-copy takeover");
            other.enabled = true; CopySource(other); other.Invoke("LateUpdate"); CelestialWorldHost.EnsureRunning();
            ExpectDiagnostic("OWNER_ACTIVE"); Require(CopySnapshot(other) != null && saves.Subscribers == 2 && SceneManager.Subscriptions == 2, "explicit copy recovery failed"); });
        Console.WriteLine("RESULT\t" + (count - fail) + "\t" + fail);
        return count > 0 && fail == 0 ? 0 : 1;
    }
}
