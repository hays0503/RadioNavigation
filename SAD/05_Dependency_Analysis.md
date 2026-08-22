# Dependency Analysis — RadioNavigation Mod

> **⚠️ Deep Scan Update (2026-08-01):** Проведён тотальный перекрёстный аудит (SAD vs code, git, build/config, cross-file consistency). Всего **199 проблем** (F01–F199).
> - **F45:** ADR-002/003/004/005/007 статус «Deferred» (был «Accepted» — 0% реализовано)
> - **F46:** Harmony patch targets исправлены — `Game.OnLoaded` → реальность `RadioStory.ContinueStory` (5 патчей)
> - **F47:** 14+ несуществующих классов помечены `⚠ proposed` в секциях реализации, удалены из анализа зависимостей
> - **F48:** Array sizes 4 → 14
> - **F49:** File counts по слоям исправлены
> - **F50:** LOC неверны для 10+ файлов
> - **F52:** Добавлены пропущенные классы: `RadioStoryIntegration`, `SonneSignalGenerator`, `SignalRichTextFormatter`
>
> Полные описания: `refactoring-plan/F45.md`..`F52.md`, `F65.md`..`F199.md`.

## 1. Dependency Overview

### 1.1 External Dependencies

| Package | Version | Purpose | Risk |
|---------|---------|---------|------|
| Unity Engine | 2020.x | Runtime framework | Low |
| Assembly-CSharp.dll | UBOAT | Game-specific APIs | **High** |
| Harmony | 2.x | Runtime patching | **High** |
| .NET Framework | 4.x | Base class library | Low |

### 1.2 Internal Dependency Graph

```
                    ┌─────────────┐
                    │   Main.cs   │
                    │  (Entry)    │
                    └──────┬──────┘
                           │
           ┌───────────────┼───────────────┐
           ▼               ▼               ▼
    ┌─────────────┐ ┌─────────────┐ ┌─────────────┐
    │RadioStory   │ │RadioInstaller│ │  Sonne     │
    │Integration  │ │  (UI Setup) │ │Controller  │
    └──────┬──────┘ └──────┬──────┘ └──────┬──────┘
           │               │               │
           │               ▼               │
           │        ┌─────────────┐        │
           │        │ UI Handlers │        │
           │        └──────┬──────┘        │
           │               │               │
           ▼               ▼               ▼
    ┌─────────────────────────────────────────────┐
    │              SonneController                 │
    │  ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐          │
    │  │Stats│ │Keyer│ │Carrier│ │Scheduler│      │
    │  └──┬──┘ └──┬──┘ └──┬──┘ └──┬──┘          │
    └─────┼───────┼───────┼───────┼───────────────┘
          │       │       │       │
          ▼       ▼       ▼       ▼
    ┌─────────────────────────────────────────────┐
    │              Data Layer                      │
    │  ┌─────────┐ ┌─────────┐ ┌─────────┐       │
    │  │BeaconData│ │DialConfig│ │ConsolCycle│     │
    │  └─────────┘ └─────────┘ └─────────┘       │
    └─────────────────────────────────────────────┘
```

## 2. Critical Dependency Chains

### 2.1 Thread Safety Chain

```
Audio Thread                    Main Thread
     │                              │
     ▼                              ▼
OnAudioFilterRead()          Update()
     │                              │
     ▼                              ▼
AudioSynthesisEngine         SonneController
.ProcessAudio()              .UpdateStationGains()
     │                              │
     └──────────┬───────────────────┘
                ▼
         stations[s]
         (shared mutable state)
         ⚠️ NO SYNCHRONIZATION
```

**Risk**: Race condition → crashes, audio artifacts

### 2.2 Reflection Chain

```
RadioStoryIntegration.Init() [RadioStoryIntegration.cs:48-107]
      │
      ├──► typeof(RadioStory).BaseType               // storyType (F23/F68 — uncached reflection)
      │    └──► Debug.Log if null (F13 — Unity API in audio-safe context)
      │
      ├──► storyType.GetMethod("InsertDecision",      // insertMethod (F68)
      │    │    BindingFlags.NonPublic | BindingFlags.Instance)
      │    └──► decisionType = insertMethod.GetParameters()[1].ParameterType
      │
      ├──► decisionType.GetProperty("LocalizationKey")  // localizationKeyProp (F68)
      │
      ├──► decisionType.GetConstructors()               // decisionCtor selection (F68)
      │    └──► Match: ≥6 params, pars[0]=string, pars[3]=Action
      │
      ├──► typeof(RadioStory).GetField("stage")         // stageField
      │
      └──► storyType.GetProperty("Decisions", ...)       // decisionsProp
           └──► storyType.GetMethod("OnUpdated", ...)     // onUpdatedMethod
```

**Risk**: Game update breaks mod → no warning (F23, F68)

### 2.3 Static Coupling Chain

```
CursorHandler
     │
     ├──► RadioHandler.Instance (static)
     │
     └──► ToggleHandler.IsInteractable (static)
              │
              └──► Used by:
                   ├── ZoneHandler
                   ├── SpinnerHandlerBase
                   ├── SonneController
                   └── InformationPanelHandler
```

**Risk**: Cannot test in isolation, hidden dependencies

## 3. Dependency Violations

### 3.1 Circular Dependencies

| From | To | Violation |
|------|----|-----------|
| SonneController | UI Handlers | Logic depends on UI |
| UI Handlers | SonneController | UI depends on Logic |
| InformationPanelHandler | ZoneHandler | UI depends on UI state |
| InformationPanelHandler | GroundHandler | UI depends on UI state |

### 3.2 Level Violations

| Component | Should Depend On | Actually Depends On |
|-----------|------------------|---------------------|
| Data Layer | Nothing | Unity (none) ✅ |
| Logic Layer | Data Layer | UI Layer ❌ |
| UI Layer | Logic Layer | Data Layer ❌ |
| Sound Layer | Data Layer | UI Layer ❌ |

### 3.3 Missing Abstractions

| Current Dependency | Problem | Proposed Abstraction |
|--------------------|---------|---------------------|
| `BeaconData.Stations` | Static, hardcoded | `IStationDataSource` |
| `RadioHandler.Instance` | Singleton | `IRadioState` |
| `ToggleHandler.IsInteractable` | Static property | `IRadioContext` |
| `AudioSynthesisEngine.ProcessAudio` | Direct call | `IAudioOutput` |

## 4. Dependency Metrics

### 4.1 Coupling Between Objects (CBO)

| Component | CBO | Rating |
|-----------|-----|--------|
| SonneController | 12 | **Very High** |
| RadioStoryIntegration | 15 | **Very High** |
| InformationPanelHandler | 8 | High |
| AudioSynthesisEngine | 7 | High |
| StationScheduler | 4 | Medium |
| MorseKeyer | 3 | Low |
| MorseAlphabet | 0 | **Excellent** |

### 4.2 Afferent Coupling (Ca) — Who depends on me

| Component | Ca | Rating |
|-----------|-----|--------|
| ToggleHandler | 6 | **High** — used everywhere |
| SonneController | 8 | **Very High** |
| BeaconData | 4 | Medium |
| DialConfig | 3 | Medium |
| StationScheduler | 2 | Low |

### 4.3 Efferent Coupling (Ce) — What I depend on

| Component | Ce | Rating |
|-----------|-----|--------|
| RadioStoryIntegration | 15 | **Very High** |
| SonneController | 12 | **Very High** |
| InformationPanelHandler | 8 | High |
| AudioSynthesisEngine | 7 | High |

### 4.4 Instability (I = Ce / (Ca + Ce))

| Component | I | Rating | Meaning |
|-----------|---|--------|---------|
| ToggleHandler | 0.33 | Stable | Hard to change |
| BeaconData | 0.20 | Very Stable | Rarely changes |
| SonneController | 0.60 | Moderate | Balanced |
| RadioStoryIntegration | 0.94 | **Very Unstable** | Changes often |
| InformationPanelHandler | 0.80 | Unstable | Changes often |

## 5. Dependency Risks

### 5.1 High-Risk Dependencies

| Dependency | Risk | Impact | Mitigation |
|------------|------|--------|------------|
| Assembly-CSharp.dll | Game update breaks mod | **Critical** | GameApiCompatibilityChecker fail-fast (F23) |
| Harmony Library | Patch target changes | High | Fallback handling, UnpatchSelf (F121/F139) |
| Unity Main Thread | Thread safety violations | **Critical** | Double-buffer pattern (F01), sync primitives (F29) |
| Static Singletons | Untestable code | High | Instance-based state (F05) |
| Reflection APIs | Type changes, no caching | High | Cache Type lookups (F68) |

### 5.2 Medium-Risk Dependencies

| Dependency | Risk | Impact | Mitigation |
|------------|------|--------|------------|
| Debug.Log calls | Performance in production | Medium | Conditional compilation |
| Reflection APIs | Type changes | Medium | Cached reflection |
| float comparisons | Precision issues | Medium | Epsilon-based comparison |

## 6. Dependency Cleanup Recommendations

### 6.1 Priority 1: Thread Safety

```csharp
// BEFORE (unsafe)
stations[s].gain = computedGain;  // Audio thread
float g = stations[s].gain;      // Main thread

// AFTER (safe — ⚠ proposed, F01/F02)
// Double-buffer pattern with StationStateBuffer
private StationState[] readBuffer;   // Audio thread reads
private StationState[] writeBuffer;  // Main thread writes
private volatile bool swapRequested;
```

### 6.2 Priority 2: Static Coupling

```csharp
// BEFORE (static)
public static bool IsInteractable { get; set; }

// AFTER (instance — ⚠ proposed, F05)
public class RadioContext : MonoBehaviour {
    public bool IsInteractable { get; set; }
}
// Inject via Main.cs or GetComponent<>()
```

### 6.3 Priority 3: Interface Extraction

```csharp
// BEFORE (concrete)
BeaconData.Stations  // Static array

// AFTER (abstract — ⚠ proposed, F04/ADR-004)
public interface IStationDataSource {
    BeaconStation[] GetAll();
}
// Implementations: ⚠ HardcodedStationRepository, ⚠ JsonStationRepository (not yet created)
```

## 7. Dependency Inversion Opportunities

| Dependency Inversion | Current | Proposed Abstraction | Status |
|---------------------|---------|---------------------|--------|
| Station data | `BeaconData.Stations` | `IStationDataSource` | ⚠ proposed (F04) |
| Radio state | `RadioHandler.Instance` | `IRadioState` | ⚠ proposed (F05) |
| Interactable state | `ToggleHandler.IsInteractable` | `IRadioContext` | ⚠ proposed (F05) |
| Audio output | `AudioSynthesisEngine.ProcessAudio` | `IAudioOutput` | ⚠ proposed (ADR-002 Deferred) |
| Game integration | Direct reflection | `IGameServices` | ⚠ proposed (F04/ADR-004 Deferred) |
| Morse decoder | `MorseKeyer`/`MorseAlphabet` | `IMorseDecoder` | ⚠ proposed (F04) |

---

*Document Version: 2.0*  
*Last Updated: 2026-08-01*  
*Updated: 199 problems cataloged (F01–F199). Reflection chain corrected to match actual code. Phantom classes annotated `⚠ proposed`. Harmony targets corrected (F46).*
