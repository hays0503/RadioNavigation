using DWS.Common.InjectionFramework;
using DWS.Common.Resources;
using HarmonyLib;
using System;
using System.Reflection;
using UBOAT.Game.Core.Serialization;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Owns the mod's bootstrap lifecycle (P5.5, extracted from Main.cs):
    /// Harmony patch installation, scene-event subscription, per-scene DI
    /// injection, and Main-Scene radio-panel construction. Main is now a thin
    /// IUserMod facade that delegates here.
    /// </summary>
    [NonSerializedInGameState]
    public sealed class ModBootstrapper
    {
        // F176: single source of truth for the game UI container lookup.
        private const string MainSceneViewportPath = "/GUI/Viewport";
        private const string RadioRootName = "RadioNavigationPanel";

        private readonly ResourceManager resourceManager;
        private Harmony harmony;
        private bool loaded;

        public ModBootstrapper(ResourceManager resourceManager)
        {
            this.resourceManager = resourceManager;
        }

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
            HarmonyPatchInstaller.Apply(harmony);

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
                Transform viewport = EnsureViewport(scene);
                if (viewport.IsNull())
                    return;

                if (resourceManager.IsNull())
                {
                    Debug.LogError("[RadioNav][Main] ResourceManager not injected \u2014 DI failed");
                    return;
                }

                if (HasExistingPanel(viewport))
                    return;

                GameObject radio = InstantiateRadio(viewport);
                if (radio.IsNull())
                    return;

                AddMapComponents(radio);

                int hCount = radio.GetComponents<MonoBehaviour>().Length;
                Debug.Log("[RadioNav][DIAG] scene=" + scene.name +
                    " radio=created handlers=" + hCount +
                    " visible=" + (RadioHandler.Instance.IsNotNull() ? RadioHandler.Instance.IsVisible.ToString() : "no-instance"));

                CreateStarSky();
                CreatePlanetRoot();
                CreateSunDiscRoot();

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

        private Transform EnsureViewport(Scene scene)
        {
            if (scene.name != "Main Scene")
            {
                Debug.Log("[RadioNav][Main] Scene '" + scene.name + "' is not Main Scene, skipping");
                return null;
            }

            // F176: the GUI hierarchy path is a single source of truth
            // here; keep it in one constant and fail loudly if the game
            // renames the container.
            Transform viewport = GameObject.Find(MainSceneViewportPath)?.transform;
            if (viewport.IsNull())
            {
                Debug.LogWarning("[RadioNav][Main] '" + MainSceneViewportPath + "' not found \u2014 GUI hierarchy may have changed in this UBOAT version");
                return null;
            }
            return viewport;
        }

        private bool HasExistingPanel(Transform viewport)
        {
            if (RadioHandler.Instance.IsNotNull() && RadioHandler.Instance.gameObject.IsNotNull())
            {
                Debug.Log("[RadioNav][Main] Reusing existing Radio panel");
                return true;
            }

            if (viewport.Find(RadioRootName).IsNotNull())
            {
                Debug.Log("[RadioNav][Main] Existing stable RadioNavigation panel found");
                return true;
            }
            return false;
        }

        private GameObject InstantiateRadio(Transform viewport)
        {
            Debug.Log("[RadioNav][Main] InstantiatePrefab(\"Radio\") from resourceManager \u2014 AssetBundle Bundles/testui expected to be loaded by game mod loader");
            GameObject radio = resourceManager.InstantiatePrefab("Radio");
            if (radio.IsNull())
            {
                Debug.LogError("[RadioNav][Main] Prefab 'Radio' not found in AssetBundle or InstantiatePrefab returned null. Check Bundles/testui");
                return null;
            }

            radio.name = RadioRootName;

            // ADR-019: централизовано через PrefabNames (best practice, не строковые литералы) + degraded open
            var missing = PrefabValidator.FindMissing(path => radio.transform.Find(path).IsNotNull());
            if (missing.Count.IsPositive())
            {
                string msg = "[RadioNav][Main] Prefab structure validation FAILED. Missing: " + string.Join(", ", missing);
                // PlayerPrefs RadioNav.SkipValidation=1 → degraded open (ловим бандл-расхождение без краша)
                if (PlayerPrefs.GetInt(PrefabValidator.SkipValidationKey, 0) == 1)
                    Debug.LogWarning(msg + " — SkipValidation=1, continuing degraded");
                else
                {
                    Debug.LogError(msg);
                    Object.Destroy(radio);
                    return null;
                }
            }

            RadioInstaller.Instantiate(radio, viewport);
            return radio;
        }

        private void AddMapComponents(GameObject radio)
        {
            if (radio.GetComponent<StationMapOverlay>().IsNull())
                radio.AddComponent<StationMapOverlay>();
            if (radio.GetComponent<LighthouseMapMarker>().IsNull())
                radio.AddComponent<LighthouseMapMarker>();
            if (radio.GetComponent<StationMapMarker>().IsNull())
                radio.AddComponent<StationMapMarker>();
            if (radio.GetComponent<FixMapMarker>().IsNull())
                radio.AddComponent<FixMapMarker>(); // SEXTANT_PLAN v1.10
            if (radio.GetComponent<ConstellationToggleButton>().IsNull())
                radio.AddComponent<ConstellationToggleButton>();
            if (radio.GetComponent<SextantController>().IsNull())
                radio.AddComponent<SextantController>(); // SEXTANT_PLAN §7.9
        }

        private void CreateStarSky()
        {
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
        }

        private void CreatePlanetRoot()
        {
            // STAR_SKY: create planetary billboard renderer once
            if (GameObject.Find("PlanetRoot").IsNull())
            {
                GameObject planetRoot = new GameObject("PlanetRoot");
                planetRoot.AddComponent<PlanetRenderer>();
                Object.DontDestroyOnLoad(planetRoot);
                Debug.Log("[RadioNav][Main] PlanetRoot created with PlanetRenderer component");
            }
        }

        private void CreateSunDiscRoot()
        {
            // SEXTANT: real-size filtered sun disc (layer 7, PIP cameras only)
            if (GameObject.Find("SunDiscRoot").IsNull())
            {
                GameObject sunDiscRoot = new GameObject("SunDiscRoot");
                sunDiscRoot.AddComponent<SunDiscRenderer>();
                Object.DontDestroyOnLoad(sunDiscRoot);
                Debug.Log("[RadioNav][Main] SunDiscRoot created with SunDiscRenderer component");
            }
        }
    }
}
