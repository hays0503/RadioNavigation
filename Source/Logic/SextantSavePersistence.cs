using System;
using System.Collections.Generic;
using System.Reflection;
using DWS.Common.InjectionFramework;
using UBOAT.Game.Core.Serialization;
using UBOAT.Game.Scene.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>One active worksheet owns the save hooks, even when the mod
    /// is loaded twice. Only primitive payload crosses assembly-copy boundaries.
    /// SceneObject lifetime provides save isolation; no global/sidecar storage.</summary>
    internal static class SextantSavePersistence
    {
        private const string CarrierTypeName = "UBOAT.Mods.RadioNavigation.SextantSaveData";
        private const string LogPrefix = "[RadioNav][AstroSave] ";
        private static SextantWorksheet worksheet;
        private static SavesManager manager;
        private static SextantSaveSnapshot pending;
        private static bool payloadBlocked;
        internal static bool IsLoading { get; private set; }

        internal static void Bind(SextantWorksheet value)
        {
            worksheet = value;
            TryInstall();
            if (pending != null)
            {
                worksheet.RestoreState(pending);
                pending = null;
            }
        }

        internal static void Unbind(SextantWorksheet value)
        {
            if (ReferenceEquals(worksheet, value))
                worksheet = null;
        }

        internal static void TryInstall()
        {
            if (worksheet == null)
                return; // A bootstrap-only assembly copy must not steal the hooks.
            SavesManager resolved = ScriptableObjectSingleton.LoadSingleton<SavesManager>();
            if (resolved == null || resolved == manager)
                return;
            PurgeHandler(resolved, "LoadingInitialization", "OnLoadingInitialization");
            PurgeHandler(resolved, "Loaded", "OnLoaded");
            PurgeHandler(resolved, "SavingStarted", "OnSavingStarted");
            resolved.LoadingInitialization += OnLoadingInitialization;
            resolved.Loaded += OnLoaded;
            resolved.SavingStarted += OnSavingStarted;
            manager = resolved;
            Debug.Log(LogPrefix + "installed (save-local SceneObject, payload v1)");
        }

        private static void PurgeHandler(SavesManager target, string eventName, string method)
        {
            FieldInfo field = typeof(SavesManager).GetField(eventName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                return;
            Delegate handlers = field.GetValue(target) as Delegate;
            field.SetValue(target, SceneEventHandlerPurge.RemoveAll(handlers,
                typeof(SextantSavePersistence).FullName, method));
        }

        private static void OnLoadingInitialization()
        {
            IsLoading = true;
            payloadBlocked = false;
            pending = null;
            // The persistent radio UI can outlive a campaign load. Drop its
            // old series BEFORE destruction, including the course sampler.
            worksheet?.RestoreState(new SextantSaveSnapshot());
        }

        private static void OnLoaded(Queue<Action> queue)
        {
            IsLoading = true;
            if (queue == null)
            {
                RestoreFromSave();
                return;
            }
            // Trampoline to the tail: all Loaded handlers must first enqueue
            // their actions (database restore, MapChartingUI, scene builders).
            // Their subscription order is not a persistence contract.
            queue.Enqueue(delegate() { queue.Enqueue(RestoreFromSave); });
        }

        private static void RestoreFromSave()
        {
            try
            {
                SceneObject carrier = FindCarrier(false);
                SextantSaveSnapshot state;
                if (carrier == null)
                {
                    state = SextantMapSave.RestoreLegacyNamedMarkers();
                    Debug.Log(LogPrefix + "legacy save: named geometry only; anonymous points untouched");
                }
                else
                {
                    string error;
                    if (!SextantSaveCodec.TryDecode(ReadPayload(carrier), out state, out error))
                    {
                        payloadBlocked = true;
                        Debug.LogError(LogPrefix + "payload rejected, original retained: " + error);
                        state = new SextantSaveSnapshot();
                    }
                }
                if (worksheet != null)
                    worksheet.RestoreState(state);
                else
                    pending = state;
                if (carrier != null && !payloadBlocked)
                    SextantMapSave.TrimInactiveMarkers(state.Markers);
                Debug.Log(LogPrefix + "restored sights=" + SightCount(state) +
                    " fix=" + state.FixPublished + " dismissed=" + state.Markers.SuppressedMask);
            }
            catch (Exception e)
            {
                payloadBlocked = true;
                Debug.LogError(LogPrefix + "restore failed; previous series not reused: " + e);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private static void OnSavingStarted()
        {
            if (IsLoading || worksheet == null || payloadBlocked)
            {
                // SavingStarted precedes both isSaving=true and opening the
                // .save file. Abort the coroutine here rather than silently
                // write an old snapshot under a new save name.
                throw new InvalidOperationException(LogPrefix +
                    "cannot save astro state (loading, no worksheet or rejected payload)");
            }
            try
            {
                SextantSaveSnapshot state = worksheet.CaptureState();
                string payload = SextantSaveCodec.Encode(state);
                SceneObject carrier = FindCarrier(true);
                WritePayload(carrier, payload);
                SextantMapSave.TrimInactiveMarkers(state.Markers);
                Debug.Log(LogPrefix + "captured sights=" + SightCount(state) +
                    " fix=" + state.FixPublished + " dismissed=" + state.Markers.SuppressedMask);
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + "snapshot capture failed: " + e);
                throw;
            }
        }

        private static int SightCount(SextantSaveSnapshot state)
        {
            if (state.Observation1 == null) return 0;
            if (state.Observation2 == null) return 1;
            return state.Observation3 == null ? 2 : 3;
        }

        private static SceneObject FindCarrier(bool create)
        {
            SceneObject found = null;
            SceneObject[] roots = UnityEngine.Object.FindObjectsOfType<SceneObject>(true);
            for (int i = 0; i < roots.Length; i++)
            {
                SceneObject root = roots[i];
                if (root == null || root.GetType().FullName != CarrierTypeName)
                    continue;
                if (found != null)
                    throw new InvalidOperationException("Ambiguous astro save carriers");
                found = root;
            }
            if (found != null || !create)
                return found;
            Scene scene = SceneManager.GetSceneByName("Main Scene");
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Campaign scene is not loaded");
            var go = new GameObject("RadioNav AstroNavigation Save State");
            SceneManager.MoveGameObjectToScene(go, scene);
            return go.AddComponent<SextantSaveData>();
        }

        private static FieldInfo PayloadField(SceneObject carrier)
        {
            if (carrier == null)
                throw new InvalidOperationException("Astro save carrier is unavailable");
            FieldInfo field = carrier.GetType().GetField("Payload", BindingFlags.Public | BindingFlags.Instance);
            if (field == null || field.FieldType != typeof(string))
                throw new InvalidOperationException("Astro save carrier has no string Payload");
            return field;
        }

        private static string ReadPayload(SceneObject carrier)
        {
            return PayloadField(carrier).GetValue(carrier) as string;
        }

        private static void WritePayload(SceneObject carrier, string payload)
        {
            PayloadField(carrier).SetValue(carrier, payload);
        }
    }
}
