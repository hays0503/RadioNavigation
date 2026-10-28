using System;
using System.Collections.Generic;
using System.Reflection;
using DWS.Common.InjectionFramework;
using UBOAT.Game.Core.Serialization;
using UBOAT.Game.Core.Time;
using UBOAT.Game.Scene.Environment;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>Computation-only lifecycle shell. Never binds a camera/TOD writer or save carrier.</summary>
    [NonSerializedInGameState]
    internal sealed class CelestialWorldHost : MonoBehaviour
    {
        private const string RootName = "RadioNavigation.CelestialWorldSource";
        private const string HostTypeName = "UBOAT.Mods.RadioNavigation.CelestialWorldHost";
        private const string OwnershipContract = "RadioNavigation.CelestialWorldHost.Stage1.v2";
        private static string lastEnsureStatus = "NOT_READY_NOT_ENSURED";
        private readonly CelestialWorldOwner owner = new CelestialWorldOwner();
#pragma warning disable RCS1169 // The existing headless lifecycle contract reassigns this source through reflection.
        private ICelestialSkyInputSource source = new GameInputs();
#pragma warning restore RCS1169
        private SavesManager saves;
        private int mainSceneHandle = -1;
        private bool subscribed;
        private bool waitingForLoaded;
        internal CelestialWorldOwner Owner { get { return owner; } }
        internal static string LastEnsureStatus { get { return lastEnsureStatus; } }

        internal static void EnsureRunning()
        {
            List<MonoBehaviour> hosts = FindHosts(null);
            string status = DescribeOwnership(hosts);
            if (status == "NOT_READY_FOREIGN_COLLISION" || status == "NOT_READY_MULTIPLE_ACTIVE")
            {
                if (!SuspendKnownOwners(hosts)) status = "NOT_READY_RELEASE_FAILED";
                ReportOwnership(status);
                return;
            }
            if (hosts.Count > 0) { ReportOwnership(status); return; }
            var root = new GameObject(RootName);
            DontDestroyOnLoad(root);
            root.AddComponent<CelestialWorldHost>();
            ReportOwnership(DescribeOwnership(FindHosts(null)));
        }

        private static List<MonoBehaviour> FindHosts(MonoBehaviour excluded)
        {
            var result = new List<MonoBehaviour>();
            foreach (MonoBehaviour component in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true))
            {
                if (IsHost(component) && !ReferenceEquals(component, excluded)) result.Add(component);
            }
            return result;
        }

        private static bool IsCompatible(MonoBehaviour component)
        {
            try
            {
                Type type = component.GetType();
                FieldInfo contract = type.GetField(nameof(OwnershipContract),
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (contract?.IsLiteral != true || contract.FieldType != typeof(string)) return false;
                if (!string.Equals(contract.GetRawConstantValue() as string, OwnershipContract, StringComparison.Ordinal)) return false;
                MethodInfo release = ReleaseMethod(type);
                return release != null && release.ReturnType == typeof(void);
            }
            catch (Exception) { return false; }
        }

        private static MethodInfo ReleaseMethod(Type type)
        {
            return type.GetMethod(nameof(ReleaseForOwnershipConflict), BindingFlags.Instance | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
        }

        private static string DescribeOwnership(List<MonoBehaviour> hosts)
        {
            int active = 0;
            foreach (MonoBehaviour component in hosts)
            {
                if (!IsCompatible(component)) return "NOT_READY_FOREIGN_COLLISION";
                if (component.isActiveAndEnabled) active++;
            }
            if (active > 1) return "NOT_READY_MULTIPLE_ACTIVE";
            if (active == 1) return "OWNER_ACTIVE";
            return hosts.Count == 0 ? "NOT_READY_NO_OWNER" : "NOT_READY_DISABLED_OR_INACTIVE";
        }

        private static bool SuspendKnownOwners(List<MonoBehaviour> hosts)
        {
            bool success = true;
            foreach (MonoBehaviour component in hosts)
            {
                if (!IsCompatible(component) || !component.isActiveAndEnabled) continue;
                try
                {
                    MethodInfo release = ReleaseMethod(component.GetType());
                    if (release == null) { success = false; continue; }
                    release.Invoke(component, null);
                }
                catch (Exception) { success = false; }
            }
            return success;
        }

        private void ReleaseForOwnershipConflict()
        {
            enabled = false; // Own protocol only; OnDisable clears state and hooks.
        }

        private static void ReportOwnership(string status)
        {
            if (string.Equals(lastEnsureStatus, status, StringComparison.Ordinal)) return;
            lastEnsureStatus = status;
            if (status != "OWNER_ACTIVE") Debug.LogWarning("[RadioNav][SkyOwner] " + status);
        }

        private static bool IsHost(MonoBehaviour component)
        {
#pragma warning disable RCS1146 // Unity fake-null must reject a destroyed component before GetType; ?. would not.
            return component != null && component.GetType().FullName == HostTypeName;
#pragma warning restore RCS1146
        }

#pragma warning disable RCS1213 // Unity message dispatch invokes this callback without an ordinary C# call site.
        private void OnEnable()
        {
            List<MonoBehaviour> others = FindHosts(this);
            string otherStatus = DescribeOwnership(others);
            if (otherStatus == "NOT_READY_FOREIGN_COLLISION" || otherStatus == "NOT_READY_MULTIPLE_ACTIVE")
            {
                enabled = false;
                SuspendKnownOwners(others);
                ReportOwnership(otherStatus);
                return;
            }
            foreach (MonoBehaviour component in others)
            {
                if (component.isActiveAndEnabled)
                {
                    enabled = false;
                    ReportOwnership("OWNER_ACTIVE");
                    return;
                }
            }
            if (subscribed) return;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            subscribed = true;
            BindSaves();
            Scene scene = SceneManager.GetSceneByName("Main Scene");
            if (scene.IsValid() && scene.isLoaded) EnterScene(scene);
            ReportOwnership("OWNER_ACTIVE");
        }
#pragma warning restore RCS1213

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "Main Scene") EnterScene(scene);
        }

        private void EnterScene(Scene scene)
        {
            if (mainSceneHandle == scene.handle) return;
            mainSceneHandle = scene.handle;
            owner.BeginLoad();
#pragma warning disable RCS1146 // A destroyed Unity SavesManager is missing even when its CLR reference is non-null.
            waitingForLoaded = saves == null || saves.IsLoading;
#pragma warning restore RCS1146
            if (!waitingForLoaded) owner.CompleteLoad(owner.WorldRevision);
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (mainSceneHandle != scene.handle) return;
            mainSceneHandle = -1;
            waitingForLoaded = false;
            owner.Unload();
        }

        private void BindSaves()
        {
            InjectionFramework framework = InjectionFramework.Instance;
#pragma warning disable RCS1128 // Preserve explicit Unity fake-null normalization; CLR ?? would keep a destroyed manager.
            SavesManager next = framework != null ? framework.GetInstance<SavesManager>() : null;
            // Normalize Unity's destroyed-object fake null, then compare managed
            // identity. Old managed event accessors must still be unsubscribed.
            if (next == null) next = null;
#pragma warning restore RCS1128
            if (ReferenceEquals(next, saves)) return;
            UnbindSaves();
            saves = next;
            if (mainSceneHandle != -1)
            {
                owner.BeginLoad();
                waitingForLoaded = true;
            }
            if (saves == null) return;
            saves.LoadingInitialization += OnLoading;
            saves.Loaded += OnLoaded;
            if (mainSceneHandle != -1 && !saves.IsLoading)
            {
                waitingForLoaded = false;
                owner.CompleteLoad(owner.WorldRevision);
            }
        }

        private void OnLoading()
        {
            waitingForLoaded = true;
            if (mainSceneHandle != -1) owner.BeginLoad();
        }

        private void OnLoaded(Queue<Action> queue)
        {
            long revision = owner.WorldRevision;
            int sceneHandle = mainSceneHandle;
            Action complete = delegate
            {
                if (!subscribed || !isActiveAndEnabled || sceneHandle == -1
                    || mainSceneHandle != sceneHandle || owner.WorldRevision != revision) return;
                waitingForLoaded = false;
                owner.CompleteLoad(revision);
            };
            if (queue == null) { complete(); return; }
            // Only append ephemeral load callbacks. No serialized ExecutionQueue listener.
            queue.Enqueue(delegate { queue.Enqueue(complete); });
        }

#pragma warning disable RCS1213 // Unity message dispatch invokes this callback without an ordinary C# call site.
        private void LateUpdate()
        {
            if (!subscribed || !isActiveAndEnabled) { owner.Invalidate(CelestialSkyNotReady.SourceUnavailable); return; }
            BindSaves();
            if (mainSceneHandle == -1) return;
            if (saves == null) { owner.Invalidate(CelestialSkyNotReady.SourceUnavailable); return; }
            if (saves.IsLoading || waitingForLoaded) { owner.Invalidate(CelestialSkyNotReady.Loading); return; }
            owner.Tick(source);
        }
#pragma warning restore RCS1213

        private void UnbindSaves()
        {
            if (ReferenceEquals(saves, null)) return;
            saves.LoadingInitialization -= OnLoading;
            saves.Loaded -= OnLoaded;
            saves = null;
        }

        private void OnDisable()
        {
            if (subscribed)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                SceneManager.sceneUnloaded -= OnSceneUnloaded;
                subscribed = false;
            }
            UnbindSaves();
            mainSceneHandle = -1;
            waitingForLoaded = false;
            owner.Unload();
            ReportOwnership(DescribeOwnership(FindHosts(null)));
        }

#pragma warning disable RCS1213 // Unity message dispatch invokes this callback without an ordinary C# call site.
        private void OnDestroy()
        {
            OnDisable();
            ReportOwnership(DescribeOwnership(FindHosts(this)));
        }
#pragma warning restore RCS1213

        private sealed class GameInputs : ICelestialSkyInputSource
        {
            public bool TryRead(long revision, out CelestialSkyInput input, out CelestialSkyNotReady reason)
            {
                input = null;
                reason = CelestialSkyNotReady.GameTime;
                InjectionFramework framework = InjectionFramework.Instance;
                if (framework == null) return false;
                GameTime time = framework.GetInstance<GameTime>();
                Weather weather = framework.GetInstance<Weather>();
                if (time == null) return false;
                if (weather == null || weather.Sky == null) { reason = CelestialSkyNotReady.WorldUtc; return false; }
                TOD_Sky sky = weather.Sky;
                if (sky.World == null) { reason = CelestialSkyNotReady.WorldUtc; return false; }
                // Literal game GMT is already CurrentDateTime-1h; NEVER ToUniversalTime/local clock.
                DateTime gmt = DateTime.SpecifyKind(time.GmtDateTime, DateTimeKind.Utc);
                input = new CelestialSkyInput(revision, gmt, sky.World.Latitude, sky.World.Longitude,
                    double.NaN, double.NaN, double.NaN, double.NaN, sky.World.UTC,
                    true, true, false, false, true);
                // Deliberate Stage-1 limit: no trusted ellipsoid/sea-height or
                // accepted atmospheric provider yet. Never invent 5m/1010hPa/10C.
                reason = CelestialSkyMath.Validate(input);
                return reason == CelestialSkyNotReady.None;
            }
        }
    }
}
