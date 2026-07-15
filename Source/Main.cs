using DWS.Common.InjectionFramework;
using DWS.Common.Resources;
using HarmonyLib;
using System;
using System.Reflection;
using UBOAT.Game;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Core.Serialization;
using UBOAT.Game.Scene.Stories;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using UnityEngine.UI;

namespace UBOAT.Mods.TestUI
{
    [NonSerializedInGameState]
    public class Main : IUserMod
    {
        [Inject] private static ResourceManager resourceManager;
        [NonSerializedInGameState] private Harmony _harmony;
        private static bool _loaded;

        public void OnLoaded()
        {
            Debug.Log("[TestUI][Main] TestUI v" + Assembly.GetExecutingAssembly().GetName().Version + ", UBOAT " + Application.version);

            if (_loaded)
            {
                _harmony?.UnpatchAll();
                RadioStoryIntegration.ResetRuntimeState();
                Debug.Log("[TestUI][Main] Previous patches removed, runtime state reset (reload)");
            }

            _harmony = new Harmony("UBOAT.Mods.TestUI");
            var patchType = typeof(RadioStoryIntegration);
            var anyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            var continueStory = typeof(RadioStory).GetMethod("ContinueStory", BindingFlags.Public | BindingFlags.Instance);
            var retrieveString1 = typeof(Locale).GetMethod("RetrieveString", new[] { typeof(string) });
            var retrieveString2 = typeof(Locale).GetMethod("RetrieveString", new[] { typeof(string), typeof(string) });
            var onDecision = typeof(RadioStory).GetMethod("OnDecisionSelected", BindingFlags.NonPublic | BindingFlags.Instance);
            var onStopped = typeof(RadioStory).GetMethod("OnStopped", BindingFlags.NonPublic | BindingFlags.Instance);

            var targets = new[]
            {
                new { method = continueStory, prefix = (HarmonyMethod)null, postfix = new HarmonyMethod(patchType.GetMethod("InjectT3PLL38Decision", anyStatic)), name = "RadioStory.ContinueStory" },
                new { method = retrieveString1, prefix = new HarmonyMethod(patchType.GetMethod("OverrideLocaleKey", anyStatic)), postfix = (HarmonyMethod)null, name = "Locale.RetrieveString" },
                new { method = retrieveString2, prefix = new HarmonyMethod(patchType.GetMethod("OverrideLocaleKey", anyStatic)), postfix = (HarmonyMethod)null, name = "Locale.RetrieveString(string,string)" },
                new { method = onDecision, prefix = new HarmonyMethod(patchType.GetMethod("HandleCustomDecisions", anyStatic)), postfix = (HarmonyMethod)null, name = "RadioStory.OnDecisionSelected" },
                new { method = onStopped, prefix = (HarmonyMethod)null, postfix = new HarmonyMethod(patchType.GetMethod("HideRadioOnStop", anyStatic)), name = "RadioStory.OnStopped" },
            };

            string missing = null;
            foreach (var t in targets)
            {
                if (t.method == null)
                    missing = (missing ?? "") + t.name + ", ";
            }
            if (missing != null)
            {
                Debug.LogError("[TestUI] Pre-flight failed — target methods not found: " + missing.TrimEnd(',', ' '));
            }
            else
            {
                int applied = 0;
                foreach (var t in targets)
                {
                    try
                    {
                        _harmony.Patch(t.method, prefix: t.prefix, postfix: t.postfix);
                        Debug.Log("[TestUI] Patched " + t.name);
                        applied++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError("[TestUI] Patch failed for " + t.name + ": " + ex.Message);
                        _harmony.UnpatchAll();
                        Debug.Log("[TestUI] All patches rolled back due to partial failure");
                        break;
                    }
                }
                Debug.Log("[TestUI] Patches applied: " + applied + "/" + targets.Length);
            }

            SceneEventsListener.OnSceneStart -= SceneEventsListener_OnSceneStart;
            SceneEventsListener.OnSceneAwake -= SceneEventsListener_OnSceneAwake;
            SceneEventsListener.OnSceneStart += SceneEventsListener_OnSceneStart;
            SceneEventsListener.OnSceneAwake += SceneEventsListener_OnSceneAwake;

            _loaded = true;

            Debug.Log("[TestUI][DIAG] version=" + Assembly.GetExecutingAssembly().GetName().Version +
                " game=" + Application.version +
                " di=" + (InjectionFramework.Instance != null ? "ok" : "null") +
                " resourceManager=" + (resourceManager != null ? "ok" : "null"));
        }

#if DEBUG
        private static void DumpHierarchy(Transform t, string indent)
        {
            Debug.Log(indent + t.name);
            foreach (Transform child in t)
                DumpHierarchy(child, indent + "  ");
        }
#endif

        private void SceneEventsListener_OnSceneAwake(Scene scene)
        {
            if (InjectionFramework.Instance == null)
            {
                Debug.LogError("[TestUI][Main] InjectionFramework.Instance is null — DI not initialized. Ensure 'Injection' or 'Reflection' permission in Manifest.json");
                return;
            }

            try
            {
                InjectionFramework.Instance.InjectIntoAssembly(Assembly.GetExecutingAssembly());
                Debug.Log("[TestUI][Main] DI injection completed");
            }
            catch (Exception e)
            {
                Debug.LogError("[TestUI][Main] Injection failed (Awake): " + e.Message);
                Debug.LogException(e);
            }
        }

        private void SceneEventsListener_OnSceneStart(Scene scene)
        {
            try
            {
                if (scene.name != "Main Scene")
                {
                    Debug.Log("[TestUI][Main] Scene '" + scene.name + "' is not Main Scene, skipping");
                    return;
                }

                Transform viewport = GameObject.Find("/GUI/Viewport")?.transform;
                if (viewport == null)
                {
                    Debug.LogWarning("[TestUI][Main] '/GUI/Viewport' not found — GUI hierarchy may have changed in this UBOAT version");
                    return;
                }

                if (resourceManager == null)
                {
                    Debug.LogError("[TestUI][Main] ResourceManager not injected — DI failed");
                    return;
                }

                if (RadioHandler.Instance != null && RadioHandler.Instance.gameObject != null)
                {
                    Debug.Log("[TestUI][Main] Reusing existing Radio panel");
                    return;
                }

                Debug.Log("[TestUI][Main] InstantiatePrefab(\"Radio\") from resourceManager — AssetBundle Bundles/testui expected to be loaded by game mod loader");
                GameObject radio = resourceManager.InstantiatePrefab("Radio");
                if (radio == null)
                {
                    Debug.LogError("[TestUI][Main] Prefab 'Radio' not found in AssetBundle or InstantiatePrefab returned null. Check Bundles/testui");
                    return;
                }

                string[] requiredChildren = { "CursorSpinner", "Toggle", "Ground", "Ground/Background", "Ground/Background/GroundCheckmark", "Expander", "RadioZone", "VolumeSpinner", "FeedbackSpinner" };
                var missing = new System.Collections.Generic.List<string>();
                foreach (var path in requiredChildren)
                {
                    if (radio.transform.Find(path) == null)
                        missing.Add(path);
                }
                if (missing.Count > 0)
                {
                    Debug.LogError("[TestUI][Main] Prefab structure validation FAILED. Missing: " + string.Join(", ", missing));
                    Object.Destroy(radio);
                    return;
                }

                RadioInstaller.Instantiate(radio, viewport);

                int hCount = radio.GetComponents<MonoBehaviour>().Length;
                Debug.Log("[TestUI][DIAG] scene=" + scene.name +
                    " radio=created handlers=" + hCount +
                    " visible=" + (RadioHandler.Instance != null ? RadioHandler.Instance.IsVisible.ToString() : "no-instance"));

#if DEBUG
                DumpHierarchy(radio.transform, "[TestUI][Main] Prefab hierarchy: ");
#endif
            }
            catch (Exception e)
            {
                Debug.LogError("[TestUI][Main] Error: " + e.Message);
            }
        }
    }
}
