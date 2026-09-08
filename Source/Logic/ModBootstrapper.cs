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
    /// injection, and Main-Scene radio-panel construction. Scene content
    /// itself (map components, sky roots, runners) lives in
    /// SceneContentBuilder. Main is now a thin IUserMod facade that
    /// delegates here.
    /// </summary>
    [NonSerializedInGameState]
    public sealed class ModBootstrapper
    {
        // F176: single source of truth for the game UI container lookup.
        private const string MainSceneViewportPath = "/GUI/Viewport";
        private const string RadioRootName = "RadioNavigationPanel";

        private ResourceManager resourceManager;
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
                // Lifecycle sweep (risk register iter2): stale cross-scene
                // state that the four resets above do not cover — camera hook
                // + cached cameras, radio power latch, type/member caches
                // (game update may rename), stale fix pin.
                CameraResolver.ResetForReload();
                RadioPowerBus.Reset();
                GameTypeCache.Clear();
                WarnOnce.Reset();
                FixMapMarker.Clear();
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

        /// Self-recovery for a missing/evicted DI registration of the
        /// ResourceManager singleton: DI only registers SO singletons from
        /// "uboat"-filtered assemblies at framework init and never
        /// re-registers after UnloadUnusedAssets destroys the instance.
        /// LoadSingleton is the game's own resolution path (public API).
        private bool EnsureResourceManager()
        {
            if (resourceManager.IsNotNull())
                return true;
            if (InjectionFramework.Instance.IsNotNull())
            {
                ResourceManager viaDi = InjectionFramework.Instance.GetInstance<ResourceManager>();
                if (viaDi.IsNotNull())
                {
                    resourceManager = viaDi;
                    return true;
                }
            }
            ResourceManager direct = ScriptableObjectSingleton.LoadSingleton<ResourceManager>();
            if (direct.IsNotNull())
            {
                resourceManager = direct;
                if (InjectionFramework.Instance.IsNotNull())
                    InjectionFramework.Instance.AddInjectableInstance(direct);
                Debug.LogWarning("[RadioNav][Main] ResourceManager resolved via LoadSingleton fallback (DI registration missing)");
                return true;
            }
            return false;
        }

        private void SceneEventsListener_OnSceneStart(Scene scene)
        {
            try
            {
                Transform viewport = EnsureViewport(scene);
                if (viewport.IsNull())
                    return;

                if (!EnsureResourceManager())
                {
                    Debug.LogError("[RadioNav][Main] ResourceManager not injected \u2014 DI failed");
                    return;
                }

                if (HasExistingPanel(viewport))
                    return;

                GameObject radio = InstantiateRadio(viewport);
                if (radio.IsNull())
                    return;

                SceneContentBuilder.Build(radio);

                int hCount = radio.GetComponents<MonoBehaviour>().Length;
                Debug.Log("[RadioNav][DIAG] scene=" + scene.name +
                    " radio=created handlers=" + hCount +
                    " visible=" + (RadioHandler.Instance.IsNotNull() ? RadioHandler.Instance.IsVisible.ToString() : "no-instance"));
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
    }
}
