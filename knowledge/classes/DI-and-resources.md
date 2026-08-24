# DI & Resources — UBOAT game internals (source-verified 2026-08-24)

Sources: `DWS.Common.InjectionFramework/{InjectionFramework.cs, SceneEventsListener.cs, ScriptableObjectSingleton.cs}`, `DWS.Common.Resources/{ResourceManager.cs, ResourceLink.cs}`, `UBOAT.Game.Core/ExecutionQueue.cs`.

## DI resolution rules
- `[Inject]` targets **static fields only** (`FindMembers(MemberTypes.Field, BindingFlags.Static|Public|NonPublic)`); assignment via `FieldInfo.SetValue(null, ...)`. Open generic types are rejected with LogError.
- Resolution is an **exact key lookup**: `typeInstances[typeof(T)]` — no base/interface walk on the resolve side. Field type must match a registered key exactly.
- Registration walks outward instead: `AddInjectableInstance(x)` registers `x.GetType()` plus every interface it implements. Registering a second instance of the same concrete type evicts the old one from `instances`.
- Late replay: each injection is recorded in `injectionsList[fieldType]`; a later `AddInjectableInstance` immediately pushes the new value into all pending fields of that type.
- Base-type walk in `InjectIntoType`: recurses into BaseType unless it equals `MonoBehaviour`, `ScriptableObjectSingleton`, `object`, or null → `[Inject]` fields on intermediate base classes resolve; the walk stops at Unity base classes.
- Destroyed-instance safety at inject time: a registered dead UnityEngine.Object is nulled before SetValue — stale refs become null silently.
- `TaggedInjectAttribute`: `GameObject.FindGameObjectWithTag(tag).GetComponent(fieldType)`; a miss parks in `missingTaggedInjections`, but retry helper `UpdateMissingTagInjections()` has **no caller anywhere** in game source → tagged misses are never retried.

## Why mods must call InjectIntoAssembly explicitly (and per scene)
- Auto-injection filter is `assembly.FullName.Contains("uboat")` (`InjectIntoLoadedAssemblies("uboat")`). Assembly named `radionavigation` fails the filter, so its `[Inject]` fields and SO singletons are invisible to framework init and scene cycles.
- Mod must call `InjectionFramework.Instance.InjectIntoAssembly(typeof(Main).Assembly)` once after load **and again every scene**: the OnSceneAwake handler does `ClearDestroyedUnityObjects(); AddInjectableInstancesFromScene(scene); injectionsList.Clear(); InjectIntoLoadedAssemblies("uboat")` — pending-replay list wiped, only "uboat" assemblies re-injected; mod statics go stale unless the mod re-runs itself:
  `SceneEventsListener.OnSceneAwake += scene => InjectionFramework.Instance.InjectIntoAssembly(modAsm);`
- SO singletons: non-abstract `ScriptableObjectSingleton` subclasses are auto-loaded+registered only for "uboat" assemblies; for mods, `InjectIntoAssembly` also runs `InstantiateAssemblyScriptableObjects(types)`.
- Scene pickup registers only `ISingleton` MonoBehaviours (+ hardcoded TOD_Sky/AudioListener/TOD_Animation/GlobalWaterInteraction), including inactive children — except "Main Scene", which uses the active-only recursive path.

## ScriptableObjectSingleton
- Load path: default `"Singletons/" + type.Name` under Resources, overridable with `[SingletonPath]`; cached in a static Type→instance dict (a failed load caches null and retries on next call).
- Lifecycle: framework calls `AwakeSingleton()` after init batch; `StartSingleton()` via hidden "Injection Framework Listener" GameObject.

## ResourceManager signatures (DWS.Common.Resources)
- `Resource<T> RetrieveAsset<T>(string resourcePath, object referent, bool logErrors = true)` — sync load, usage-counted by referent.
- `IResource RetrieveAssetAsync<T>(string resourcePath, object referent, Action<Resource<T>> onLoaded, bool logErrors = true)`.
- `GameObject InstantiatePrefab(string resourcePath, InstantiationFlags flags = InstantiationFlags.LogErrors)` — loads then instantiates; `ForceDisabled` instantiates deactivated; fires `ICreateListener.OnCreated()` on children and `ObjectInstantiated` event; returns null if asset missing.
- `UniTask<GameObject> InstantiatePrefabAsync(string resourcePath, bool logErrors = true)`; callback overload `(string, Action<GameObject>, bool)`.
- Scenes: `RetrieveScene(levelName, mode, referent, quiet, logErrors)`; `RetrieveSceneAsync(..., Action<Scene> onLoaded, float loadingDelay = 0.5f, ...)`; Single mode unloads/clears all other SceneResources.
- Freeing/lifetime: `FreeAsset(path, referent)`, `FreeAllAssets(referent)`; checks: `AssetExists(path)`, `GetAssetBundleResource(name)`.
- Mod assets enter the map via `AddAssetBundle(mod, bundlePath, simulate)` / `AddFileResource(mod, filePath)`; lookup keys are paths relative to `<mod>/Assets/`.
- Path resolution order: loaded resource cache → `AssetBundleContainedResource<T>` (GUID-mapped mod-bundle asset) → `SimpleResource<T>` (Unity Resources folder).
- Events: `BeforeResourceLoading(IResource)`, `ObjectInstantiated(UnityEngine.Object, InstantiationFlags)`.

### ResourceLink<T>
- Lazy handle: `new ResourceLink<T>("path")`; `.Asset` getter or implicit cast to T triggers sync `RetrieveAsset<T>` with the link as referent; `LoadAsync(...)` variants exist; `Free()` releases usage; `.Valid` means asset != null.

## ExecutionQueue listener kinds (UBOAT.Game.Core, sealed : ScriptableObjectSingleton)
Pump: hidden DontDestroyOnLoad "Execution Queue Listener" drives Update/LateUpdate/FixedUpdate. Listeners run last-added-first; duplicate adds ignored (per-kind HashSet dedup; DateListeners NOT deduped); a throwing listener is logged and iteration resumes below it.

| API | Fires | Threading |
|---|---|---|
| `AddUpdateListener(Action)` / Remove | every Update | off-main calls buffered under lock, applied next frame |
| `AddLateUpdateListener(Action)` / Remove | every LateUpdate | no guard — main thread expected |
| `AddFixedUpdateListener(Action)` / Remove | every FixedUpdate | throws InvalidOperationException off main thread |
| `AddTimedUpdateListener(Func<float>, delay = 0f, TimedUpdatesQueue queue = Normal)` | when due | no thread guard, main thread assumed |
| `RemoveTimedUpdateListener(Func<float>, queue)` | — | linear heap scan |
| `AddDateListener(Action, DateTime, shouldStopTimeSkipping = false, type = null)` | story time reached | serialized into savegames |

- Timed Func returns next delay in seconds; return NaN to self-remove. Delay converted to ticks (×10^7).
- Queues: `Normal` = gameTicks (scaled dt; paused while LoadingUI context == "Loading"), `Continuous` = continuousGameTicks (same gating, separate clock), `UnscaledContinuous` = unscaledDeltaTime ticks (runs even during loading screens).
- All three clocks zero on GameState transition to Gameplay (non-restored); date listeners cleared on Running and Quitting state changes.
- Save/load: delegates serialized via WriteDelegate/ReadDelegate — instance delegates of serializable types survive reload; destroyed UnityObject targets dropped on deserialize. Mod lambdas capturing serializable objects will persist across saves.

## Patching hooks for mods
Do not Harmony-patch these classes for control flow; use the public surface:
1. Init: register services (`AddInjectableInstance`) any time, then `InjectIntoAssembly(asm)` — replay makes order irrelevant.
2. Per scene: subscribe `SceneEventsListener.OnSceneAwake` (execution order −9000) → re-run `InjectIntoAssembly`; instantiate UI prefab there via `GetInstance<ResourceManager>().InstantiatePrefab(path, InstantiationFlags.ForceDisabled)`.
3. Dynamically spawned prefab containing ISingleton components: `AddInjectableInstancesFromDynamicallySpawnedPrefab(go)`.
4. Frame/timer work: `GetInstance<ExecutionQueue>()` + `AddUpdateListener`/`AddTimedUpdateListener` instead of custom MonoBehaviour Update loops.
5. Teardown: pair every RetrieveAsset/InstantiatePrefab referent with `FreeAsset`/`FreeAllAssets(referent)` to release bundle memory.

## Bundle icon lookup (verified 2026-08-25, station markers)

- Mod bundle assets are indexed by the embedded Manifest descriptor Paths (bare extension-less names, case-exact ordinal); game never calls GetAllAssetNames and never loads sub-assets (LoadAssetWithSubAssets: 0 uses in game source).
- Folder-level bundle assignment: Assets/Packages/testui.meta carries assetBundleName: testui — all children inherit (per-asset metas stay blank). New prefab IconSonne.prefab (SpriteRenderer + sone sprite, guid fe63a8ed...) shipped by simply rebuilding.
- Sprite retrieval: .png imported with textureType 8 (Sprite) resolves via RetrieveAsset<Sprite>(bareName) — game precedent BaseActivatorBanner.cs:1058, PeriscopeUI.cs:1413. Default-imported png → only RetrieveAsset<Texture2D> works; wrap via Sprite.Create. Safest mod pattern (LighthouseMapMarker/StationMapMarker): prefab first (Image.sprite/SpriteRenderer.sprite), then Sprite, then Texture2D+Sprite.Create.
- Bundle rebuild (batch, no editor UI): Unity.exe -batchmode -quit -projectPath C:\Users\Sergey Pimenov\Desktop\TestUI -executeMethod UBOAT.Editor.ModdingTools.RadioNavBundleBuilder.Build → output lands in Assets/Mods/testui/Bundles (project folder name!), manual copy to live mod Bundles/. Live bundle check without editor: byte-scan for asset name ([Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes(path)).Contains("IconSonne")).
