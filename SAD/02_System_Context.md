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

**IMPORTANT**: This section was corrected from the original SAD which listed wrong targets (Game.OnLoaded, StoryManager.InjectDecision, LocaleManager.GetCurrentLanguage). Actual targets:

| Class | Method Patched | Patch Type | Purpose |
|-------|----------------|------------|---------|
| `RadioStory` | `ContinueStory` | Prefix (Main.cs:39) | Lock radio story state during processing |
| `Locale` | `RetrieveString` (2 overloads) | Postfix + Prefix (Main.cs:40-41) | Override locale strings with mod names |
| `RadioStory` | `OnDecisionSelected` | Postfix (Main.cs:42) | Handle custom decision selection |
| `RadioStory` | `OnStopped` | Postfix (Main.cs:43) | Hide radio when story stops |

**Total**: 5 patches (2 prefix, 3 postfix) on 2 classes using imperative `harmony.Patch()` API.

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
│     [StationScheduler] [AudioSynthesisEngine] [FixCalculator]   │
│              │               │               │                  │
│              ▼               ▼               ▼                  │
│     [BeaconData]    [DspPipeline]  [PositionFixCalculator]      │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

## 4. Integration Points

### 4.1 Harmony Patch Points

```csharp
// Main.cs — Entry point (imperative API, NOT attributes)
private void PatchHarmony() {
    harmony.Patch(
        typeof(RadioStory).GetMethod("ContinueStory"),
        prefix: new HarmonyMethod(typeof(Main), nameof(RadioStoryContinueStoryPrefix))
    );
    harmony.Patch(
        typeof(Locale).GetMethod("RetrieveString", new[] { typeof(string) }),
        postfix: new HarmonyMethod(typeof(Main), nameof(LocaleRetrieveStringPostfix))
    );
    harmony.Patch(
        typeof(Locale).GetMethod("RetrieveString", new[] { typeof(string), typeof(string) }),
        prefix: new HarmonyMethod(typeof(Main), nameof(LocaleRetrieveString2Prefix))
    );
    harmony.Patch(
        typeof(RadioStory).GetMethod("OnDecisionSelected"),
        postfix: new HarmonyMethod(typeof(Main), nameof(RadioStoryOnDecisionSelectedPostfix))
    );
    harmony.Patch(
        typeof(RadioStory).GetMethod("OnStopped"),
        postfix: new HarmonyMethod(typeof(Main), nameof(RadioStoryOnStoppedPostfix))
    );
}
```

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

## 6. Deployment Context

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
