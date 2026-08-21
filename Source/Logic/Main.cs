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
        // F176: single source of truth for the game UI container lookup.
        private const string MainSceneViewportPath = "/GUI/Viewport";
        private const string RadioRootName = "RadioNavigationPanel";

        // DI assigns this field during mod initialization; explicit default
        // keeps standalone Roslyn builds free of CS0649.
        [Inject] private static ResourceManager resourceManager = null;
        [NonSerializedInGameState] private Harmony harmony;
        private static bool loaded;

        public void OnLoaded()
        {
            Debug.Log("[RadioNav][Main] RadioNavigation v" + Assembly.GetExecutingAssembly().GetName().Version + ", UBOAT " + Application.version);

            if (loaded)
            {
                harmony?.UnpatchAll(harmony.Id); // F139: bare UnpatchAll() removes other mods' patches; 0Harmony 2.2.2 has no UnpatchSelf
                RadioStoryIntegration.ResetRuntimeState();
                LighthousePlacer.Reset();
                LighthouseBeamGlow.ResetStatics(); // ADR-017: GPU + hook утечка маяка
                GameTimeResolver.Reset();
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

            var injectT3PLL38 = patchType.GetMethod("InjectT3PLL38Decision", anyStatic);
            var overrideLocale = patchType.GetMethod("OverrideLocaleKey", anyStatic);
            var handleDecisions = patchType.GetMethod("HandleCustomDecisions", anyStatic);
            var hideOnStop = patchType.GetMethod("HideRadioOnStop", anyStatic);

            var targets = new[]
            {
                new { method = continueStory, prefix = (HarmonyMethod)null, postfix = injectT3PLL38.IsNotNull() ? new HarmonyMethod(injectT3PLL38) : null, name = "RadioStory.ContinueStory" },
                new { method = retrieveString1, prefix = overrideLocale.IsNotNull() ? new HarmonyMethod(overrideLocale) : null, postfix = (HarmonyMethod)null, name = "Locale.RetrieveString" },
                new { method = retrieveString2, prefix = overrideLocale.IsNotNull() ? new HarmonyMethod(overrideLocale) : null, postfix = (HarmonyMethod)null, name = "Locale.RetrieveString(string,string)" },
                new { method = onDecision, prefix = handleDecisions.IsNotNull() ? new HarmonyMethod(handleDecisions) : null, postfix = (HarmonyMethod)null, name = "RadioStory.OnDecisionSelected" },
                new { method = onStopped, prefix = (HarmonyMethod)null, postfix = hideOnStop.IsNotNull() ? new HarmonyMethod(hideOnStop) : null, name = "RadioStory.OnStopped" },
            };

            string missing = null;
            string missingPatches = null;
            foreach (var t in targets)
            {
                if (t.method.IsNull())
                    missing = (missing ?? "") + t.name + ", ";
                if (t.prefix.IsNotNull() && t.prefix.method.IsNull())
                    missingPatches = (missingPatches ?? "") + t.name + " (prefix), ";
                if (t.postfix.IsNotNull() && t.postfix.method.IsNull())
                    missingPatches = (missingPatches ?? "") + t.name + " (postfix), ";
            }
            if (missing.IsNotNull())
            {
                Debug.LogError("[RadioNav] Pre-flight failed \u2014 target methods not found: " + missing.TrimEnd(',', ' '));
            }
            else if (missingPatches.IsNotNull())
            {
                Debug.LogError("[RadioNav] Pre-flight failed \u2014 patch methods not found: " + missingPatches.TrimEnd(',', ' '));
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

                // F176: the GUI hierarchy path is a single source of truth
                // here; keep it in one constant and fail loudly if the game
                // renames the container.
                Transform viewport = GameObject.Find(MainSceneViewportPath)?.transform;
                if (viewport.IsNull())
                {
                    Debug.LogWarning("[RadioNav][Main] '" + MainSceneViewportPath + "' not found \u2014 GUI hierarchy may have changed in this UBOAT version");
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

                if (viewport.Find(RadioRootName).IsNotNull())
                {
                    Debug.Log("[RadioNav][Main] Existing stable RadioNavigation panel found");
                    return;
                }

                Debug.Log("[RadioNav][Main] InstantiatePrefab(\"Radio\") from resourceManager \u2014 AssetBundle Bundles/testui expected to be loaded by game mod loader");
                GameObject radio = resourceManager.InstantiatePrefab("Radio");
                if (radio.IsNull())
                {
                    Debug.LogError("[RadioNav][Main] Prefab 'Radio' not found in AssetBundle or InstantiatePrefab returned null. Check Bundles/testui");
                    return;
                }

                radio.name = RadioRootName;

                // ADR-019: централизовано через PrefabNames (best practice, не строковые литералы) + degraded open
                string[] requiredChildren = {
                    PrefabNames.Cursor,
                    PrefabNames.Toggle,
                    PrefabNames.Ground,
                    PrefabNames.Ground + "/" + PrefabNames.Background,
                    PrefabNames.Ground + "/" + PrefabNames.Background + "/" + PrefabNames.GroundCheckmark,
                    PrefabNames.Expander,
                    PrefabNames.RadioZone,
                    PrefabNames.VolumeSpinner,
                    PrefabNames.FeedbackSpinner
                };
                var missing = new System.Collections.Generic.List<string>();
                foreach (var path in requiredChildren)
                {
                    if (radio.transform.Find(path).IsNull())
                        missing.Add(path);
                }
                if (missing.Count.IsPositive())
                {
                    string msg = "[RadioNav][Main] Prefab structure validation FAILED. Missing: " + string.Join(", ", missing);
                    // PlayerPrefs RadioNav.SkipValidation=1 → degraded open (ловим бандл-расхождение без краша)
                    if (PlayerPrefs.GetInt("RadioNav.SkipValidation", 0) == 1)
                        Debug.LogWarning(msg + " — SkipValidation=1, continuing degraded");
                    else
                    {
                        Debug.LogError(msg);
                        Object.Destroy(radio);
                        return;
                    }
                }

                RadioInstaller.Instantiate(radio, viewport);
                if (radio.GetComponent<StationMapOverlay>().IsNull())
                    radio.AddComponent<StationMapOverlay>();

                int hCount = radio.GetComponents<MonoBehaviour>().Length;
                Debug.Log("[RadioNav][DIAG] scene=" + scene.name +
                    " radio=created handlers=" + hCount +
                    " visible=" + (RadioHandler.Instance.IsNotNull() ? RadioHandler.Instance.IsVisible.ToString() : "no-instance"));

#if DEBUG
                DumpHierarchy(radio.transform, "[RadioNav][Main] Prefab hierarchy: ");
#endif

                // STAR_SKY: create star sky renderer (ADR-021)
                GameObject starSkyRoot = new GameObject("StarSkyRoot");
                starSkyRoot.AddComponent<StarsMesh>();
                Object.DontDestroyOnLoad(starSkyRoot);
                Debug.Log("[RadioNav][Main] StarSkyRoot created with StarsMesh component");

                // STAR_SKY: create constellation lines renderer
                GameObject constellationRoot = new GameObject("ConstellationRoot");
                constellationRoot.AddComponent<ConstellationMesh>();
                Object.DontDestroyOnLoad(constellationRoot);
                Debug.Log("[RadioNav][Main] ConstellationRoot created with ConstellationMesh component");

                // STAR_SKY: create planetary billboard renderer once
                if (GameObject.Find("PlanetRoot").IsNull())
                {
                    GameObject planetRoot = new GameObject("PlanetRoot");
                    planetRoot.AddComponent<PlanetRenderer>();
                    Object.DontDestroyOnLoad(planetRoot);
                    Debug.Log("[RadioNav][Main] PlanetRoot created with PlanetRenderer component");
                }

                LighthousePlacer.EnsureRunning();

                // Phase0 probe (STAR_SKY_PLAN:10, ADR-020) — only when PlayerPrefs RadioNav.Phase0Probe=1
                StarSkyPhase0Probe.EnsureRunning();
            }
            catch (Exception e)
            {
                Debug.LogError("[RadioNav][Main] Error: " + e.Message);
                Debug.LogException(e);
            }
        }
    }
}
