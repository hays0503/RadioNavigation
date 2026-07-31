# Dependency Analysis — RadioNavigation Mod

> **⚠️ Deep Scan Update (2026-07-30):** Этот документ содержит устаревшие данные. Полный перекрестный аудит (SAD vs code, git, build/config, cross-file) выявил ~94 проблемы. Ключевые расхождения:
> - **F45:** ADR-002/003/004/005/007 имеют статус «Accepted» в SAD, но **0% реализованы** в коде (нет double-buffer, нет интерфейсов, нет Constants.cs, нет тестов)
> - **F46:** Harmony patch targets в SAD неверны — `Game.OnLoaded` → реальность `RadioStory.ContinueStory` (5 патчей, а не 3)
> - **F47:** SAD ссылается на 14+ несуществующих классов (`GameStrings`, `StationManager`, `SignalDecoder`, `PositionTracker`, `AudioController`, `AudioPipeline`, `CarrierBank`, `TickScheduler`, `StationStateBuffer`, `GameReflectionCache`, `FormatDiagnosticString`, `StationInfoDisplay`, `StationFinder`, `RadioContext`)
> - **F48:** Array sizes 4 → 14 (stations, schedulers, keyers, carriers)
> - **F49:** File counts по слоям неверны: 40→34 (UI 12→15, Logic 14→12, Data 8→5, Sound 6→2)
> - **F50:** LOC неверны для 10+ файлов (MorseKeyer 150→240, PositionFix 130→224)
> - **F52:** Диаграммы пропускают 3 реальных класса: `RadioStoryIntegration` (381 LOC), `SonneSignalGenerator` (146), `SignalRichTextFormatter` (86)
>
> Полные описания: `refactoring-plan/F45.md`..`F52.md`

> **⚠️ Deep Scan Update (2026-07-31):** Добавлены F65–F76 (всего 76 проблем). Dependency metrics refer to real classes; non-existent classes (F47) removed from analysis. ADR-002/004 «Deferred» (F45, исправлено). Полные описания: `refactoring-plan/F45.md`..`F52.md`, `F65.md`..`F76.md`.

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
RadioStoryIntegration.Init()
     │
     ├──► Type.GetType("Game.StoryManager")
     │    └──► Fallback: Assembly-CSharp
     │
     ├──► ConstructorInfo = type.GetConstructors()[0]
     │    └──► Guess by Action parameter count
     │
     ├──► FieldInfo = type.GetField("singleton")
     │    └──► Fallback: "Instance"
     │
     └──► MethodInfo = type.GetMethod("InjectDecision")
          └──► Fallback: search by parameter count
```

**Risk**: Game update breaks mod → no warning

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
| PositionFixCalculator | 2 | **Good** |
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
| Assembly-CSharp.dll | Game update breaks mod | **Critical** | Interface-based integration |
| Harmony Library | Patch target changes | High | Fallback handling |
| Unity Main Thread | Thread safety violations | **Critical** | Double-buffer pattern |
| Static Singletons | Untestable code | High | DI container |

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

// AFTER (safe)
// Double-buffer pattern
private StationState[] readBuffer;   // Audio thread reads
private StationState[] writeBuffer;  // Main thread writes
private volatile bool swapRequested;
```

### 6.2 Priority 2: Static Coupling

```csharp
// BEFORE (static)
public static bool IsInteractable { get; set; }

// AFTER (instance)
public class RadioContext : MonoBehaviour {
    public bool IsInteractable { get; set; }
}
// Inject via Main.cs or GetComponent<>()
```

### 6.3 Priority 3: Interface Extraction

```csharp
// BEFORE (concrete)
BeaconData.Stations  // Static array

// AFTER (abstract)
public interface IStationDataSource {
    BeaconStation[] GetAll();
}
// Implementations: HardcodedStationRepository, JsonStationRepository
```

## 7. Dependency Inversion Opportunities

| Current | Proposed | Benefit |
|---------|----------|---------|
| `SonneController` → `StationScheduler` | `IScheduler` interface | Testable scheduling |
| `SonneController` → `MorseKeyer` | `IKeyer` interface | Testable Morse logic |
| `AudioSynthesisEngine` → `CarrierGenerator` | `ICarrierGenerator` | Testable audio |
| `InformationPanelHandler` → `ZoneHandler` | `IZoneProvider` | Testable UI |
| `RadioStoryIntegration` → Unity API | `IGameServices` | Testable integration |
