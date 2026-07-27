# System Context — RadioNavigation Mod

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

| Class | Method Patched | Purpose |
|-------|----------------|---------|
| `Main` | `OnLoaded` | Inject radio equipment after game loads |
| `RadioStoryIntegration` | `InjectDecision` | Override story decisions |
| `RadioStoryIntegration` | `GetCurrentLanguage` | Override locale strings |
| `RadioStoryIntegration` | `Init` | Initialize radio state |

### 2.3 Runtime Environment

```
Target Platform: Windows (x64)
Framework: .NET Framework 4.x (Unity Mono)
Unity Version: 2020.x (UBOAT)
Game Version: 2025/2026
```

## 3. Data Flow Context

```
┌─────────────────────────────────────────────────────────────────┐
│                        External Systems                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  [Game Save System] ──────► [Mod State Persistence]             │
│                                                                 │
│  [Player Input] ──────────► [UI Handlers]                       │
│                              │                                  │
│                              ▼                                  │
│                         [SonneController]                       │
│                              │                                  │
│              ┌───────────────┼───────────────┐                  │
│              ▼               ▼               ▼                  │
│     [StationManager] [AudioEngine] [PositionTracker]            │
│              │               │               │                  │
│              ▼               ▼               ▼                  │
│     [BeaconData]    [DspPipeline]  [FixCalculator]             │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

## 4. Integration Points

### 4.1 Harmony Patch Points

```csharp
// Main.cs — Entry point
Harmony.PatchAll();  // Patches all [HarmonyPatch] attributes

// Patch targets:
// 1. Game.OnLoaded() — Inject radio equipment
// 2. StoryManager.InjectDecision() — Override story choices
// 3. LocaleManager.GetCurrentLanguage() — Override locale
```

### 4.2 Unity Lifecycle Integration

```
Game Start
    │
    ▼
Main.OnLoaded() [Harmony Postfix]
    │
    ├─► RadioInstaller.Instantiate()
    │       │
    │       ├─► Create Canvas
    │       ├─► Create Radio Panel
    │       └─► Attach UI Handlers
    │
    └─► SonneController.Awake()
            │
            ├─► Initialize Stations
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
            ├─► For each station:
            │       ├─► Generate Carrier Signal
            │       ├─► Apply Atmosphere Noise
            │       └─► Apply Receiver DSP
            │
            └─► Mix all stations
```

## 5. Constraints

### 5.1 Technical Constraints

| Constraint | Impact | Mitigation |
|------------|--------|------------|
| Unity Main Thread Only | UI updates, state changes must be on main thread | Double-buffer pattern for audio data |
| No Native Plugins | Pure C# mod, no native code | Work within Unity's managed runtime |
| Assembly-CSharp Reference | Direct reference to game assembly | Interface-based integration layer |
| Harmony Patching | Runtime method interception | Fallback handling for version mismatches |

### 5.2 Game Version Constraints

| Game Version | Support Status | Notes |
|--------------|----------------|-------|
| 2025.x | ✅ Supported | Current target |
| 2026.x | ⚠️ Experimental | Requires version-gate validation |
| Earlier | ❌ Not Supported | API incompatible |

## 6. Deployment Context

```
Installation Path:
  Steam/steamapps/common/UBOAT/Mods/RadioNavigation/

Build Output:
  radionavigation.dll → UBOAT/Mods/RadioNavigation/

Dependencies:
  radionavigation.deps.json
  Assets/ (prefabs, textures)
  Config/ (beacons.json — future)
```
