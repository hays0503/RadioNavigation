# System Context — RadioNavigation Mod

## Действующий контекст (2026-10-05)

- Game API: `com.uboat.game.dll` / `com.uboat.modapi.dll`, Unity **2021.3.45f2**,
  Harmony **2.2.2**. `Assembly-CSharp.dll` в ранней диаграмме — устаревшее имя.
- Мод C# **7.0**, .NET Standard 2.0/Unity Mono. Локально raw-csc `build.ps1` с
  PVS; SDK-проекты net48 нужны анализу, не являются runtime-сборкой мода.
- `Main` — IUserMod-фасад, `ModBootstrapper` владеет DI, сценами и ресурсами;
  `HarmonyPatchInstaller` — preflight/установка. Помимо пяти radio/locale-целей
  есть render/sky-патчи; «5» ниже не полный размер всех текущих hooks.
- CAP 59: **пять** станций из C# `BeaconData`, не 14 из раннего аудита. Панель
  сохраняет предпочтения в PlayerPrefs; нативные чарт-точки — в БД сейва игры,
  активная worksheet-серия секстанта и frozen FIX/LOP/скрытие — в отдельном
  `SceneObject` того же сейва (формат v1, `docs/astro-save.md`). Это не архив
  старых безымянных точек: отсутствующие наблюдения не выдумываются.
- Рабочая установка — `%USERPROFILE%/AppData/LocalLow/Deep Water Studio/UBOAT/Mods/RadioNavigation`.
  Игра компилирует более новый Source в свой Temp; DLL локальной сборки туда не
  копируется. `Bundles/testui` обязателен, `deps.json` для этого пути не нужен.
- Совместимость проверена на **2026.1-patch21**. Канарейка версии/preflight есть,
  Manifest supportedGameVersions не обеспечивает runtime enforcement. Отсутствие
  ошибок snapshot не является испытанием другой версии игры.

Актуальные цепочки всего мода — `SAD/03`, `SAD/04`; deployment и PIP —
`docs/CAMERA_GUIDE.md`, сохранность/приёмка — `docs/live-diagnostics.md`.
Далее — **исторические radio-диаграммы 2026-07/08**, не полная схема секстанта,
маяков, native map или точный размер текущего проекта.

## 1. System Boundary

```
┌─────────────────────────────────────────────────────────────────────┐
│                        UBOAT Game Engine                            │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐              │
│  │   Unity      │  │  Assembly-   │  │   Harmony    │              │
│  │   Runtime    │  │  CSharp.dll  │  │   Library    │              │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘              │
│         │                 │                 │                       │
│  ┌──────┴─────────────────┴─────────────────┴───────┐              │
│  │              Game API Layer                        │              │
│  │  • MonoBehaviour lifecycle                        │              │
│  │  • AudioClip / AudioSource                        │              │
│  │  • UI System (Canvas, Image, Text)                │              │
│  │  • Scene Management                               │              │
│  │  • Save/Load System                               │              │
│  └──────────────────────────┬───────────────────────┘              │
└─────────────────────────────┼───────────────────────────────────────┘
                              │
┌─────────────────────────────┼───────────────────────────────────────┐
│                     RadioNavigation Mod                             │
│  ┌──────────────────────────┴───────────────────────┐              │
│  │              Harmony Patches (Main.cs)            │              │
│  │  • Inject radio equipment into submarine         │              │
│  │  • Override story decisions                       │              │
│  │  • Modify locale strings                          │              │
│  └──────────────────────────┬───────────────────────┘              │
│                              │                                     │
│  ┌──────────────┐  ┌────────┴────────┐  ┌──────────────┐          │
│  │   Data       │  │     Logic       │  │    Sound     │          │
│  │  Layer       │  │     Layer       │  │    Layer     │          │
│  └──────┬───────┘  └────────┬────────┘  └──────┬───────┘          │
│         │                   │                   │                   │
│  ┌──────┴───────────────────┴───────────────────┴───────┐          │
│  │                    UI Layer                           │          │
│  └──────────────────────────────────────────────────────┘          │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│                     Player Interaction                              │
│  • Mouse/keyboard input for radio operation                        │
│  • Visual feedback (frequency display, signal strength)            │
│  • Audio feedback (Sonne beacon signals)                           │
└─────────────────────────────────────────────────────────────────────┘
```

## 2. External Dependencies

### 2.1 Game Engine Dependencies

| Dependency | Usage | Risk Level |
|------------|-------|------------|
| Unity MonoBehaviour | Lifecycle management, component system | Low |
| UnityEngine.AudioClip | Audio buffer management | Medium |
| UnityEngine.AudioSource | Audio output | Low |
| UnityEngine.UI | Canvas, Image, Text, ScrollRect | Low |
| Assembly-CSharp.dll | Game-specific APIs | **High** |
| Harmony Library | Runtime method patching | **High** |

### 2.2 Assembly-CSharp Usage Points

**IMPORTANT**: This section was corrected from the original SAD which listed wrong targets (Game.OnLoaded, StoryManager.InjectDecision, LocaleManager.GetCurrentLanguage). Actual targets:

| Class | Method Patched | Patch Type | Purpose |
|-------|----------------|------------|---------|
| `RadioStory` | `ContinueStory` | Postfix | Inject custom radio decision |
| `Locale` | `RetrieveString` (2 overloads) | Prefix + Prefix | Override locale strings with mod names |
| `RadioStory` | `OnDecisionSelected` | Prefix | Handle custom decision selection |
| `RadioStory` | `OnStopped` | Postfix | Hide radio when story stops |

**Radio/locale total**: 5 targets (3 prefix, 2 postfix), imperative `harmony.Patch()`
in `HarmonyPatchInstaller`; this does not count the additional render/sky hooks.

### 2.3 Runtime Environment

```
Target Platform: Windows (x64)
Framework: .NET Standard 2.0 (Unity Mono)
Build: csc.exe / Roslyn via PowerShell (not dotnet build)
Game Version: 2026.1
```

## 3. Data Flow Context

```
┌─────────────────────────────────────────────────────────────────┐
│                        External Systems                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  [Game Save System] (no state persistence — see F15)            │
│                                                                 │
│  [Player Input] ──────────► [UI Handlers]                       │
│                              │                                  │
│                              ▼                                  │
│                         [SonneController]                       │
│                              │                                  │
│              ┌───────────────┼───────────────┐                  │
│              ▼               ▼               ▼                  │
│     [StationScheduler] [AudioSynthesisEngine] [SonneSignal]     │
│              │               │               │                  │
│              ▼               ▼               ▼                  │
│     [BeaconData]    [DspPipeline]  [SonneBeam tables]            │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

## 4. Integration Points

### 4.1 Harmony Patch Points

Устаревший пример handlers в Main удалён из справочного пути: действующая
реализация — `Source/Logic/HarmonyPatchInstaller.cs` (`ResolveTargets`,
`ApplyTargets`, preflight/rollback) и `Source/Logic/RadioStoryIntegration.cs`.
`OnLoaded` — IUserMod callback, не Harmony target; scene injection не выполняется
патчем `ContinueStory`.

### 4.2 Unity Lifecycle Integration

```
Game Start
    │
    ▼
Main.OnLoaded() [IUserMod interface method, NOT a patch]
    │
    ├─► RadioInstaller.Instantiate()
    │       │
    │       ├─► Instantiate prefab
    │       │   (Canvas + Panel already in prefab)
    │       └─► Attach UI Handlers to prefab children
    │
    └─► SonneController.Awake()
            │
            ├─► Initialize Stations (14)
            ├─► Create Silent Audio Clip
            └─► Start Navigation Loop
```

### 4.3 Audio Thread Integration

```
Unity Audio Thread
    │
    ▼
OnAudioFilterRead(float[] data, int channels)
    │
    └─► AudioSynthesisEngine.ProcessAudio()
            │
            └─► Single loop: per-sample mix across all stations
                    ├─► Generate Carrier Signal
                    ├─► Apply Atmosphere Noise
                    └─► Apply Receiver DSP
```

## 5. Constraints

### 5.1 Technical Constraints

| Constraint | Impact | Mitigation |
|------------|--------|------------|
| Unity Main Thread Only | UI updates, state changes must be on main thread | Double-buffer pattern for audio data (not implemented) |
| No Native Plugins | Pure C# mod, no native code | Work within Unity's managed runtime |
| Assembly-CSharp Reference | Direct reference to game assembly | Interface-based integration layer (not implemented) |
| Harmony Patching | Runtime method interception | Fallback handling for version mismatches |

### 5.2 Game Version Constraints

| Game Version | Support Status | Notes |
|--------------|----------------|-------|
| 2025.x | ❌ Unknown | Not tested |
| 2026.x | ✅ Supported (2026.1) | Version-gate validation not implemented |
| Earlier | ❌ Not Supported | API incompatible |

## 6. Исторический deployment context (отменён; читать актуальную шапку)

```
Installation Path:
  Steam/steamapps/common/UBOAT/Mods/RadioNavigation/

Build Output:
  radionavigation.dll → UBOOT/Mods/RadioNavigation/

Dependencies:
  radionavigation.deps.json
  Bundles/testui (46 MB Unity AssetBundle)
  Config/ (beacons.json — future, not implemented)
```

---

*Document Version: 1.2*  
*Last Updated: 2026-07-31*  
*Corrections: Fixed Harmony targets (F46), patch count, Installer flow, audio thread diagram. See F45-F52, F65-F76.*
