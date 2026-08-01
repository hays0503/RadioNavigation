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

namespace UBOAT.Mods.RadioNavigation
{
    [NonSerializedInGameState]
    public class Main : IUserMod
    {
        [Inject] private static ResourceManager resourceManager;
        [NonSerializedInGameState] private Harmony harmony;
        private static bool loaded;

        public void OnLoaded()
        {
            Debug.Log("[RadioNav][Main] RadioNavigation v" + Assembly.GetExecutingAssembly().GetName().Version + ", UBOAT " + Application.version);

            if (loaded)
            {
                harmony?.UnpatchAll(harmony.Id); // F139: bare UnpatchAll() removes other mods' patches; 0Harmony 2.2.2 has no UnpatchSelf
                RadioStoryIntegration.ResetRuntimeState();
                Debug.Log("[RadioNav][Main] Previous patches removed, runtime state reset (reload)");
            }

            harmony = new Harmony("UBOAT.Mods.RadioNavigation");
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
                if (t.method.IsNull())
                    missing = (missing ?? "") + t.name + ", ";
            }
            if (missing.IsNotNull())
            {
                Debug.LogError("[RadioNav] Pre-flight failed \u2014 target methods not found: " + missing.TrimEnd(',', ' '));
            }
            else
            {
                int applied = 0;
                foreach (var t in targets)
                {
                    try
                    {
                        harmony.Patch(t.method, prefix: t.prefix, postfix: t.postfix);
                        Debug.Log("[RadioNav] Patched " + t.name);
                        applied++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError("[RadioNav] Patch failed for " + t.name + ": " + ex.Message);
                        harmony.UnpatchAll(harmony.Id); // F121: bare UnpatchAll() would roll back other mods' patches too
                        Debug.Log("[RadioNav] RadioNavigation patches rolled back due to partial failure");
                        break;
                    }
                }
                Debug.Log("[RadioNav] Patches applied: " + applied + "/" + targets.Length);
            }

            SceneEventsListener.OnSceneStart -= SceneEventsListener_OnSceneStart;
            SceneEventsListener.OnSceneAwake -= SceneEventsListener_OnSceneAwake;
            SceneEventsListener.OnSceneStart += SceneEventsListener_OnSceneStart;
            SceneEventsListener.OnSceneAwake += SceneEventsListener_OnSceneAwake;

            loaded = true;

            Debug.Log("[RadioNav][DIAG] version=" + Assembly.GetExecutingAssembly().GetName().Version +
                " game=" + Application.version +
                " di=" + (InjectionFramework.Instance.IsNotNull() ? "ok" : "null") +
                " resourceManager=" + (resourceManager.IsNotNull() ? "ok" : "null"));
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
            if (InjectionFramework.Instance.IsNull())
            {
                Debug.LogError("[RadioNav][Main] InjectionFramework.Instance is null \u2014 DI not initialized. Ensure 'Injection' or 'Reflection' permission in Manifest.json");
                return;
            }

            try
            {
                InjectionFramework.Instance.InjectIntoAssembly(Assembly.GetExecutingAssembly());
                Debug.Log("[RadioNav][Main] DI injection completed");
            }
            catch (Exception e)
            {
                Debug.LogError("[RadioNav][Main] Injection failed (Awake): " + e.Message);
                Debug.LogException(e);
            }
        }

        private void SceneEventsListener_OnSceneStart(Scene scene)
        {
            try
            {
                if (scene.name != "Main Scene")
                {
                    Debug.Log("[RadioNav][Main] Scene '" + scene.name + "' is not Main Scene, skipping");
                    return;
                }

                Transform viewport = GameObject.Find("/GUI/Viewport")?.transform;
                if (viewport.IsNull())
                {
                    Debug.LogWarning("[RadioNav][Main] '/GUI/Viewport' not found \u2014 GUI hierarchy may have changed in this UBOAT version");
                    return;
                }

                if (resourceManager.IsNull())
                {
                    Debug.LogError("[RadioNav][Main] ResourceManager not injected \u2014 DI failed");
                    return;
                }

                if (RadioHandler.Instance.IsNotNull() && RadioHandler.Instance.gameObject.IsNotNull())
                {
                    Debug.Log("[RadioNav][Main] Reusing existing Radio panel");
                    return;
                }

                Debug.Log("[RadioNav][Main] InstantiatePrefab(\"Radio\") from resourceManager \u2014 AssetBundle Bundles/testui expected to be loaded by game mod loader");
                GameObject radio = resourceManager.InstantiatePrefab("Radio");
                if (radio.IsNull())
                {
                    Debug.LogError("[RadioNav][Main] Prefab 'Radio' not found in AssetBundle or InstantiatePrefab returned null. Check Bundles/testui");
                    return;
                }

                string[] requiredChildren = { "CursorSpinner", "Toggle", "Ground", "Ground/Background", "Ground/Background/GroundCheckmark", "Expander", "RadioZone", "VolumeSpinner", "FeedbackSpinner" };
                var missing = new System.Collections.Generic.List<string>();
                foreach (var path in requiredChildren)
                {
                    if (radio.transform.Find(path).IsNull())
                        missing.Add(path);
                }
                if (missing.Count.IsPositive())
                {
                    Debug.LogError("[RadioNav][Main] Prefab structure validation FAILED. Missing: " + string.Join(", ", missing));
                    Object.Destroy(radio);
                    return;
                }

                RadioInstaller.Instantiate(radio, viewport);

                int hCount = radio.GetComponents<MonoBehaviour>().Length;
                Debug.Log("[RadioNav][DIAG] scene=" + scene.name +
                    " radio=created handlers=" + hCount +
                    " visible=" + (RadioHandler.Instance.IsNotNull() ? RadioHandler.Instance.IsVisible.ToString() : "no-instance"));

#if DEBUG
                DumpHierarchy(radio.transform, "[RadioNav][Main] Prefab hierarchy: ");
#endif
            }
            catch (Exception e)
            {
                Debug.LogError("[RadioNav][Main] Error: " + e.Message);
            }
        }
    }
}
