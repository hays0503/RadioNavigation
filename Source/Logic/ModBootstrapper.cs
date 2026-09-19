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
        // Stable identity of the bootstrap type across hot-reload assemblies
        // (AssemblyQualifiedName changes per load; FullName does not).
        private const string BootstrapTypeFullName = "UBOAT.Mods.RadioNavigation.ModBootstrapper";
        public ModBootstrapper(ResourceManager resourceManager)
        {
            this.resourceManager = resourceManager;
        }

        public void OnLoaded()
        {
            Debug.Log("[RadioNav][Main] RadioNavigation v" + Assembly.GetExecutingAssembly().GetName().Version + ", UBOAT " + Application.version);

            // Reset cross-scene state on EVERY load. The game creates a new
            // Main instance per hot reload (Mod.InitializeAssembly), so an
            // instance 'loaded' flag is never true on the second call — the old
            // guard was dead and left stale statics/hooks behind.
            bool reload = harmony != null;
            if (reload)
                harmony.UnpatchAll(harmony.Id); // F139: bare UnpatchAll() removes other mods' patches; 0Harmony 2.2.2 has no UnpatchSelf
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
            TypeMemberCache.Clear();
            SkyVisibilityResolver.Reset();
            WarnOnce.Reset();
            FixMapMarker.Clear();
            if (reload)
                Debug.Log("[RadioNav][Main] Previous patches removed, runtime state reset (reload)");

            harmony = new Harmony("UBOAT.Mods.RadioNavigation");
            HarmonyPatchInstaller.Apply(harmony);

            // Dev-console diagnostics (calibration probe: rn_lighthouse_height)
            LighthouseHeightProbe.EnsureRegistered();

            // Dev-console diagnostic (land/water sweep: rn_lighthouse_landprobe)
            LighthouseLandSweep.EnsureRegistered();

            // Leuchtfeuer fleet registry (map markers for all, 3D gated by
            // LighthousePlacer). Same data directory as the Sonne tables.
            // Dedupe only against ENABLED curated profiles — a disabled
            // curated entry must not shadow its own DB twin.
            var enabledCurated = new System.Collections.Generic.List<LighthouseProfile>();
            for (int i = 0; i < LighthouseProfiles.All.Length; i++)
            {
                if (LighthouseProfiles.All[i].Enabled)
                {
                    enabledCurated.Add(LighthouseProfiles.All[i]);
                }
            }

            string lighthouseDataDirectory = System.IO.Path.Combine(
                Application.persistentDataPath,
                "Mods",
                "RadioNavigation",
                "Source",
                "Data");
            if (!LeuchtfeuerRegistry.Initialize(lighthouseDataDirectory, enabledCurated))
            {
                Debug.LogError("[RadioNav][Lighthouse] Leuchtfeuer registry failed: " +
                    LeuchtfeuerRegistry.LastLoadError + " — continuing with curated profiles only");
            }
            else
            {
                Debug.Log("[RadioNav][Lighthouse] Leuchtfeuer registry loaded: " +
                    LeuchtfeuerRegistry.Profiles.Count + " theater profiles");
            }

            // A hot reload loads a NEW assembly without unloading the old one;
            // the old instance's SceneEventsListener handlers survive and root
            // it. Purge them before re-subscribing (F219).
            PurgeStaleSceneHandlers();
            SceneEventsListener.OnSceneStart -= SceneEventsListener_OnSceneStart;
            SceneEventsListener.OnSceneAwake -= SceneEventsListener_OnSceneAwake;
            SceneEventsListener.OnSceneStart += SceneEventsListener_OnSceneStart;
            SceneEventsListener.OnSceneAwake += SceneEventsListener_OnSceneAwake;

            Debug.Log("[RadioNav][DIAG] version=" + Assembly.GetExecutingAssembly().GetName().Version +
                " game=" + Application.version +
                " di=" + (InjectionFramework.Instance.IsNotNull() ? "ok" : "null") +
                " resourceManager=" + (resourceManager.IsNotNull() ? "ok" : "null"));
        }

        /// <summary>
        /// Remove stale <see cref="SceneEventsListener"/> handlers left by a
        /// previous hot-reload assembly. The game loads a new assembly on
        /// recompile but never unloads the old one, so its subscriptions stay
        /// alive and would build a duplicate panel each reload.
        /// </summary>
        private static void PurgeStaleSceneHandlers()
        {
            PurgeEvent(typeof(SceneEventsListener), "OnSceneStart", "SceneEventsListener_OnSceneStart");
            PurgeEvent(typeof(SceneEventsListener), "OnSceneAwake", "SceneEventsListener_OnSceneAwake");
        }

        private static void PurgeEvent(Type eventOwner, string eventName, string handlerName)
        {
            try
            {
                FieldInfo field = eventOwner.GetField(eventName,
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null)
                    return;

                if (!(field.GetValue(null) is Delegate current))
                    return;

                Delegate kept = SceneEventHandlerPurge.RemoveAll(current, BootstrapTypeFullName, handlerName);
                if (!ReferenceEquals(kept, current))
                {
                    field.SetValue(null, kept);
                    Debug.Log("[RadioNav][Main] purged stale " + eventName + " handler(s) from a previous reload");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RadioNav][Main] stale handler purge failed for " + eventName + ": " + e.Message);
            }
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
                if (radio == null)
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

        private const string BundleFileName = "testui";
        private const string RadioPrefabName = "Radio";
        private const string RadioPrefabPath = "Assets/Packages/testui/Radio.prefab";
        // Reflection handle: AssetBundle lives in UnityEngine.AssetBundleModule,
        // which the mod's compile reference set (and possibly the game's mod
        // compiler) does not include, so the type must not be named directly.
        private static object fallbackBundle;

        private GameObject InstantiateRadio(Transform viewport)
        {
            Debug.Log("[RadioNav][Main] InstantiatePrefab(\"Radio\") from resourceManager \u2014 AssetBundle Bundles/testui expected to be loaded by game mod loader");
            GameObject radio = resourceManager.IsNotNull() ? resourceManager.InstantiatePrefab("Radio") : null;
            if (radio.IsNull())
            {
                // F229: on 2026.1-patch21 the game does not register the mod's
                // AssetBundle in the ResourceManager (assetToBundleMap stays
                // empty), so InstantiatePrefab falls back to a SimpleResource
                // and fails. Self-heal by loading the bundle directly; the
                // fallback is skipped whenever the game path works.
                radio = InstantiateFromBundleFallback();
            }
            if (radio == null)
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

        /// <summary>
        /// F229 fallback: load Bundles/testui directly and instantiate the Radio
        /// prefab. Used only when the game's ResourceManager lookup failed.
        /// Reuses an already-loaded bundle (hot reload / double load) instead of
        /// throwing from AssetBundle.LoadFromFile.
        /// </summary>
        private static GameObject InstantiateFromBundleFallback()
        {
            try
            {
                Type bundleType = GameTypeCache.Resolve("UnityEngine.AssetBundle", "UnityEngine.AssetBundleModule");
                if (bundleType == null)
                {
                    Debug.LogError("[RadioNav][Main] fallback: UnityEngine.AssetBundle type not found");
                    return null;
                }

                if (fallbackBundle == null)
                    fallbackBundle = FindLoadedBundle(bundleType);

                if (fallbackBundle == null)
                {
                    string path = System.IO.Path.Combine(
                        Application.persistentDataPath, "Mods", "RadioNavigation", "Bundles", BundleFileName);
                    if (!System.IO.File.Exists(path))
                    {
                        Debug.LogError("[RadioNav][Main] fallback bundle missing: " + path);
                        return null;
                    }

                    MethodInfo loadFromFile = FindMethod(bundleType, "LoadFromFile", isStatic: true, parameterCount: 1);
                    if (loadFromFile == null)
                    {
                        Debug.LogError("[RadioNav][Main] fallback: AssetBundle.LoadFromFile not found");
                        return null;
                    }
                    fallbackBundle = loadFromFile.Invoke(null, new object[] { path });
                }

                if (fallbackBundle == null)
                {
                    Debug.LogError("[RadioNav][Main] fallback AssetBundle.LoadFromFile returned null");
                    return null;
                }

                // Non-generic LoadAsset(string): the generic LoadAsset<T>(string)
                // shares the name+signature and makes Type.GetMethod ambiguous.
                MethodInfo loadAsset = FindMethod(bundleType, "LoadAsset", isStatic: false, parameterCount: 1);
                if (loadAsset == null)
                {
                    Debug.LogError("[RadioNav][Main] fallback: AssetBundle.LoadAsset not found");
                    return null;
                }

                UnityEngine.Object prefab = loadAsset.Invoke(fallbackBundle, new object[] { RadioPrefabName }) as UnityEngine.Object
                    ?? loadAsset.Invoke(fallbackBundle, new object[] { RadioPrefabPath }) as UnityEngine.Object;
                if (prefab == null)
                {
                    Debug.LogError("[RadioNav][Main] fallback bundle has no 'Radio' prefab");
                    return null;
                }

                GameObject instance = Object.Instantiate(prefab) as GameObject;
                Debug.LogWarning("[RadioNav][Main] Instantiated 'Radio' via direct AssetBundle fallback (game ResourceManager did not register the mod bundle)");
                return instance;
            }
            catch (Exception e)
            {
                Debug.LogError("[RadioNav][Main] direct bundle fallback failed: " + e.Message);
                Debug.LogException(e);
                return null;
            }
        }

        private static object FindLoadedBundle(Type bundleType)
        {
            MethodInfo all = FindMethod(bundleType, "GetAllLoadedAssetBundles", isStatic: true, parameterCount: 0);
            if (all == null)
                return null;
            if (!(all.Invoke(null, null) is System.Collections.IEnumerable bundles))
                return null;
            PropertyInfo nameProperty = bundleType.GetProperty(
                "name", BindingFlags.Public | BindingFlags.Instance, null, typeof(string), Type.EmptyTypes, null);
            foreach (object bundle in bundles)
            {
                if (bundle == null)
                    continue;
                string name = nameProperty != null ? nameProperty.GetValue(bundle) as string : null;
                if (name == BundleFileName)
                    return bundle;
            }
            return null;
        }

        /// <summary>
        /// Loads a single asset (by name, then by full bundle path) from the
        /// mod's already-loaded AssetBundle. F229: on 2026.1-patch21 the game
        /// never registers the mod bundle in the ResourceManager, so every
        /// ResourceManager.RetrieveAsset() call degrades to Resources.Load()
        /// and returns null for mod assets (GPU-verified 2026-09-17: the
        /// ocular views lost their opaque material and were alpha-gated by the
        /// stock UI shader). This is the direct-bundle escape hatch for such
        /// callers; the bundle itself is loaded by InstantiateFromBundleFallback.
        /// </summary>
        internal static UnityEngine.Object LoadBundleAsset(string assetName)
        {
            try
            {
                Type bundleType = GameTypeCache.Resolve("UnityEngine.AssetBundle", "UnityEngine.AssetBundleModule");
                if (bundleType == null)
                    return null;

                if (fallbackBundle == null)
                    fallbackBundle = FindLoadedBundle(bundleType);
                if (fallbackBundle == null)
                    return null;

                MethodInfo loadAsset = FindMethod(bundleType, "LoadAsset", isStatic: false, parameterCount: 1);
                if (loadAsset == null)
                    return null;

                // Unity accepts both the asset name and its full project path.
                UnityEngine.Object asset = loadAsset.Invoke(fallbackBundle, new object[] { assetName }) as UnityEngine.Object
                    ?? loadAsset.Invoke(fallbackBundle, new object[] { "Assets/Packages/testui/" + assetName }) as UnityEngine.Object;
                return asset;
            }
            catch (Exception e)
            {
                if (WarnOnce.ShouldLog("ModBootstrapper.LoadBundleAsset"))
                    Debug.LogWarning("[RadioNav][Main] LoadBundleAsset('" + assetName + "') failed: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// First non-generic public method with the given name/static/arity.
        /// Avoids Type.GetMethod AmbiguousMatchException when an overload shares
        /// the name (e.g. AssetBundle.LoadAsset vs LoadAsset&lt;T&gt;).
        /// </summary>
        private static MethodInfo FindMethod(Type type, string name, bool isStatic, int parameterCount)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (method.Name != name || method.IsGenericMethodDefinition)
                    continue;
                if (method.IsStatic != isStatic)
                    continue;
                if (method.GetParameters().Length != parameterCount)
                    continue;
                return method;
            }
            return null;
        }
    }
}
