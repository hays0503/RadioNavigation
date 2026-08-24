# Target Architecture — RadioNavigation Mod

> **⚠️ Deep Scan Update (2026-08-01):** Этот документ описывает целевую архитектуру — **все классы и интерфейсы, отмеченные `⚠ proposed`, не реализованы**. Всего **207 проблем** (F01–F207). ADR-002/003/004/005/007 changed «Accepted» → «Deferred» (F45, исправлено). Пропущены `RadioStoryIntegration` (замещён фасадом + `StoryDecisionGateway.cs`, 2026-08-23), `SonneSignalGenerator` (146), `SignalRichTextFormatter` (86) теперь добавлены в текущую архитектуру вместо phantom классов. Полные описания: `refactoring-plan/hypothesis/INDEX.md`.

## 1. Target Layer Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                          UI Layer (15 files)                        │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐  │
│  │ RadioHandler│ │ToggleHandler│ │ZoneHandler  │ │GroundHandler│  │
│  └──────┬──────┘ └──────┬──────┘ └──────┬──────┘ └──────┬──────┘  │
│         │               │               │               │          │
│  ┌──────┴──────┐ ┌──────┴──────┐ ┌──────┴──────┐ ┌──────┴──────┐  │
│  │CursorHandler│ │SpinnerBase  │ │PanelDrag    │ │Information  │  │
│  └──────┬──────┘ └──────┬──────┘ └──────┬──────┘ └──────┬──────┘  │
│         │               │               │               │          │
│  ┌──────┴──────┐ ┌──────┴──────┐ ┌──────┴──────┐                  │
│  │VolumeSpinner│ │FeedbackSpin │ │ExpanderHdlr │ ⚠+SignalRich   │  │
│  │             │ │             │ │             │ │Formatter      │  │
│  └──────┬──────┘ └──────┬──────┘ └──────┬──────┘ └──────┬──────┘  │
│         │               │               │               │          │
│  ┌──────┴──────┐ ┌──────┴──────┐ ┌──────┴──────┐                  │
│  │DebugAngle   │ │RadioInstaller││PrefabNames   │                  │
│  └─────────────┘ └─────────────┘ └─────────────┘                  │
└─────────┼───────────────────────┼──────────────────────────────┘
          │                       │
          ▼                       ▼
┌─────────────────────────────────────────────────────────────────────┐
│              Service Layer (⚠ proposed — all NOT implemented)       │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐               │
│  │ ⚠ IRadioState │ │ ⚠ IStationData│ │ ⚠ IAudioOutput│               │
│  └──────┬───────┘ └──────┬───────┘ └──────┬───────┘               │
│         │               │               │                          │
│  ┌──────┴──────┐ ┌──────┴──────┐                            │
│  │ ⚠ IGameServ │ │ ⚠ IMorseDec │                            │
│  └──────┬──────┘ └──────┬──────┘                            │
└─────────┼───────────────┼────────────────────────────────────┘
          │               │
          ▼               ▼
┌─────────────────────────────────────────────────────────────────────┐
│          Logic Layer (⚠ proposed classes NOT implemented)           │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐               │
│  │ ⚠ StationMgr │ │ ⚠ SignalDec │ │⚠ PositionTrk │               │
│  │ (exists:     │ │ (exists:     │ │ (exists:     │               │
│  │ RadioStory   │ │ SonneSignal │ │ PositionFix  │               │
│  │ Integration) │ │ Generator)   │ │ Calculator)   │               │
│  └──────┬───────┘ └──────┬───────┘ └──────┬───────┘               │
│         │               │               │                          │
│  ┌──────┴──────┐ ┌──────┴──────┐ ┌──────┴──────┐                  │
│  │StationSched │ │MorseKeyer   │ │MorseAlphabet│                  │
│  │ (exists)     │ │ (exists)    │ │ (exists)    │                  │
│  └──────┬──────┘ └──────┬──────┘ └──────┬──────┘                  │
│         │               │               │                          │
│  ┌──────┴──────┐ ┌──────┴──────┐                                 │
│  │NavPattern   │ │SonneSignal  │                                 │
│  │ (exists)    │ │Generator    │                                 │
│  │             │ │ (exists)    │                                 │
│  └──────┬──────┘ └──────┬──────┘                                 │
│         │               │                                          │
│  ┌──────┴──────┐                                         │
│  │⚠ SonarCon  │  ← God Class (508 LOC, F02)              │
│  │ (exists)   │                                          │
│  └─────────────┘                                          │
└─────────────────────────────────────────────────────────────────────┘
          │               │               │
          ▼               ▼               ▼
┌─────────────────────────────────────────────────────────────────────┐
│          Data Layer (5 files — all exist)                          │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐               │
│  │BeaconData    │ │DialConfig    │ │ConsolCycle   │               │
│  │ (exists)     │ │ (exists)     │ │Config        │               │
│  │              │ │              │ │ (exists)     │               │
│  └──────┬───────┘ └──────┬───────┘ └──────┬───────┘               │
│         │               │               │                          │
│  ┌──────┴──────┐ ┌──────┴──────┐                                 │
│  │StationState │ │Navigation   │                                 │
│  │ (exists)    │ │Pattern      │                                 │
│  │             │ │ (exists)    │                                 │
│  └──────┬──────┘ └──────┬──────┘                                 │
│         │               │                                          │
│  ┌──────┴──────┐                                              │
│  │⚠ GameStrings│ ⚠ proposed (F06/JSON config)                  │
│  │              │                                          │
│  └─────────────┘                                          │
└─────────────────────────────────────────────────────────────────────┘
          │               │               │
          ▼               ▼               ▼
┌─────────────────────────────────────────────────────────────────────┐
│          Sound Layer (2 files — all exist)                         │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐               │
│  │ ⚠ AudioPipe │ │ ⚠ CarrierBank│ │Atmosphere    │               │
│  │ (NOT impl)   │ │ (NOT impl)   │ │ (exists as  │               │
│  │              │ │              │ │ nested class)│               │
│  └──────┬───────┘ └──────┬───────┘ └──────┬───────┘               │
│         │               │               │                          │
│  ┌──────┴──────┐ ┌──────┴──────┐                                 │
│  │ReceiverDsp  │ │ ⚠ TickSched │                                 │
│  │ (exists as  │ │ (NOT impl)  │                                 │
│  │ nested cl)  │ │             │                                 │
│  └─────────────┘ └─────────────┘                                 │
└─────────────────────────────────────────────────────────────────────┘
```

## 2. New Components

### 2.1 Service Interfaces ⚠ proposed (NOT implemented — ADR-004 Deferred, F04)

```csharp
// ⚠ IRadioState.cs — NOT implemented (F05 — uses static singletons instead)
public interface IRadioState {
    bool IsRadioOn { get; }
    bool IsInteractable { get; }
    bool GroundActive { get; }
    float CurrentFrequency { get; }
    int CurrentZone { get; }
    
    event Action<bool> OnRadioStateChanged;
    event Action<float, int> OnFrequencyChanged;
    event Action<int> OnZoneChanged;
}

// ⚠ IStationDataSource.cs — NOT implemented
public interface IStationDataSource {
    BeaconStation[] GetAll();
    BeaconStation? GetByName(string name);
    BeaconStation? GetByIndex(int index);
}

// ⚠ IAudioOutput.cs — NOT implemented (ADR-002 Deferred, F01)
public interface IAudioOutput {
    float OutputVolume { get; set; }
    float FeedbackVolume { get; set; }
    void ProcessAudio(float[] data, int channels, StationState[] stations);
}

// ⚠ IGameServices.cs — NOT implemented (ADR-004 Deferred, F23)
public interface IGameServices {
    string GetCurrentLanguage();
    string RetrieveString(string key);
    void InjectDecision(object story, int index, object decision);
    void NotifyUpdated(object story);
}

// ⚠ IMorseDecoder.cs — NOT implemented
public interface IMorseDecoder {
    char[] DecodeElements(int[] elements);
    int[] EncodeText(string text);
    string GetSignalRichText(List<SignalEntry> entries);
}
```

### 2.2 Implementation Classes ⚠ proposed (NOT implemented — F47)

```csharp
// ⚠ RadioContext.cs — NOT implemented (F05)
public class RadioContext : MonoBehaviour, IRadioState {
    public bool IsRadioOn { get; private set; }
    public bool IsInteractable { get; private set; }
    // ... events, state management
}

// ⚠ HardcodedStationRepository.cs — NOT implemented
public class HardcodedStationRepository : IStationDataSource {
    public BeaconStation[] GetAll() => BeaconData.Stations;
    // ... methods
}

// ⚠ JsonStationRepository.cs — NOT implemented (ADR-006 Proposed)
public class JsonStationRepository : IStationDataSource {
    private readonly string jsonPath;
    public BeaconStation[] GetAll() => LoadFromJson();
    // ... methods
}

// ⚠ AudioPipeline.cs — NOT implemented (F25 renames to StationAudio record)
public class AudioPipeline : IAudioOutput {
    private readonly CarrierBank carriers;     // ⚠ CarrierBank also NOT implemented
    private readonly Atmosphere atmosphere;     // Exists as nested class in DspPipeline.cs
    private readonly ReceiverDsp dsp;           // Exists as nested class in DspPipeline.cs
    // ... methods
}

// ⚠ GameReflectionCache.cs — NOT implemented (F47, F23)
// Actual: RadioStoryIntegration (381 LOC) does reflection inline
public class GameReflectionCache : IGameServices {
    private readonly TypeCache cache;
    // ... methods
}
```

## 3. Decomposed SonneController ⚠ proposed (F02)

```
BEFORE: SonneController (508 lines — ⚠ F50: was 506 in SAD)
│
└──► AFTER (⚠ proposed — F02):
    ├── RadioController.cs (~80 lines)  ← NEW, not yet created
    │   ├── Awake()
    │   ├── Update()
    │   ├── OnDestroy()
    │   └── Orchestrates subsystems
    │
    ├── ⚠ StationManager.cs (~100 lines) [proposed, F47]
    │   ├── stations[]
    │   ├── UpdateStationGains()
    │   ├── UpdateStationNavigation()
    │   └── SetActiveStation()
    │
    ├── ⚠ SignalDecoder.cs (~90 lines) [proposed, F47]
    │   ├── signalEntries
    │   ├── OnStationElementCompleted()
    │   ├── DecodeCallsignBuffer()
    │   └── GetSignalRichText()
    │
    └── ⚠ DiagnosticsCollector.cs (~30 lines) [⚠ proposed, F47]
        ├── GetStationDiagnostics()
        ⚠ FormatDiagnosticString() [⚠ non-existent method — F47]
```

## 4. Decomposed InformationPanelHandler ⚠ proposed (F03)

```
BEFORE: InformationPanelHandler (474 lines, God Class)
│
└──► AFTER (⚠ proposed — F03):
    ├── ⚠ UIElementFactory.cs (~150 lines) [proposed, F47]
    │   ├── CreateLabel()
    │   ├── CreateSeparator()
    │   ├── CreateImage()
    │   ├── CreateText()
    │   └── EnsureLayout()
    │
    ├── ⚠ StationInfoDisplay.cs (~120 lines) [proposed, F47]
    │   ├── RefreshAll()
    │   ├── UpdateStationInfo()
    │   ├── UpdatePatternDisplay()
    │   └── UpdateSignalDisplay()
    │
    ├── ⚠ StationFinder.cs (~80 lines) [proposed, F47]
    │   ├── FindClosestStation()
    │   ├── GetZoneIndex()
    │   └── IsGroundActive()
    │
    └── InformationPanelHandler.cs (~200 lines) — ORCHESTRATOR (exists, F03)
        ├── Start()
        ├── RefreshAll()
        └── Event subscriptions
```

## 5. Audio Pipeline ⚠ proposed (F25)

```
BEFORE: AudioSynthesisEngine (150 lines — actual: ~170 with DspPipeline as separate file)
│
└──► AFTER (⚠ proposed — F25):
    ├── ⚠ AudioPipeline.cs (~80 lines) [proposed, F47]
    │   ├── ProcessAudio()
    │   └── Mix stations (⚠ StationAudio record, F25)
    │
    ├── ⚠ CarrierBank.cs (~50 lines) [proposed, F47 — CarrierGenerator exists as nested]
    │   ├── carriers[]
    │   ├── GenerateCarrier()
    │   └── UpdatePhases()
    │
    ├── Atmosphere.cs (~40 lines) [exists as nested class in DspPipeline.cs]
    │   ├── Apply()
    │   └── Impulse noise
    │
    ├── ReceiverDsp.cs (~60 lines) [exists as nested class in DspPipeline.cs]
    │   ├── Process()
    │   └── Clamp output  (F142)
    │
    └── ⚠ TickScheduler.cs (~30 lines) [proposed, F47]
        ├── Tick()
        └── OnSequenceFinished()
```

## 6. Data Layer

```
BEFORE: Hardcoded data in BeaconData.cs (373 lines)
│
└──► AFTER (⚠ proposed — ADR-006 Proposed):
    ├── Config/  [⚠ proposed — not created]
    │   ├── beacons.json     [⚠ proposed]
    │   ├── bands.json       [⚠ proposed]
    │   └── cycles.json      [⚠ proposed]
    │
    └── Source/Data/
        ├── ⚠ Constants.cs  [⚠ proposed — not created (F05, F56)]
        ├── PrefabNames.cs   [exists]
        └── ⚠ GameStrings.cs  [⚠ proposed — not created (F47)]
```

## 7. Thread Safety Pattern ⚠ proposed (F01, F29)

```csharp
// Double-buffer pattern for audio thread safety — ⚠ NOT implemented
// Actual: StationState[] mutated directly without sync (F01, F29)
public class StationStateBuffer {
    private StationState[] readBuffer;   // Audio thread reads
    private StationState[] writeBuffer;  // Main thread writes
    private volatile bool swapRequested;
    
    public void Swap() {
        // Called at end of frame
        var temp = readBuffer;
        readBuffer = writeBuffer;
        writeBuffer = temp;
    }
    
    public StationState[] GetReadBuffer() => readBuffer;
    public StationState[] GetWriteBuffer() => writeBuffer;
}
```

## 8. Dependency Injection ⚠ proposed (F05, ADR-004)

```csharp
// Main.cs — Composition Root (⚠ proposed — not implemented)
public class Main {
    private static ⚠ RadioContext radioContext;           // NOT created (F05)
    private static ⚠ IStationDataSource stationData;       // NOT created
    private static ⚠ IAudioOutput audioOutput;             // NOT created
    private static ⚠ IGameServices gameServices;           // NOT created
    
    // ACTUAL: Main.cs uses Harmony patches directly on RadioStory.ContinueStory,
    // Locale.RetrieveString, RadioStory.OnDecisionSelected, RadioStory.OnStopped
    // (5 patch targets, not Game.OnLoaded — see F46)
    
    [HarmonyPostfix]
    public static void OnLoaded() {
        // ⚠ Proposed: Create services (not yet implemented)
        radioContext = new RadioContext();
        stationData = new HardcodedStationRepository();  // ⚠
        audioOutput = new AudioPipeline();               // ⚠
        gameServices = new GameReflectionCache();        // ⚠
        
        // Actual: RadioInstaller.Instantiate() + SonneController.Awake()
        var installer = new RadioInstaller();
        installer.Instantiate();
    }
}
```

## 9. Metrics Comparison

| Metric | Current | Target (⚠ proposed) | Improvement |
|--------|---------|--------------------|-------------|
| Max lines per class | 508 | ~150 | **70%** |
| Max methods per class | 30+ | ~10 | **67%** |
| Max fields per class | 25+ | ~8 | **68%** |
| God Classes | 2 | 0 ⚠ | **100%** |
| Static Singletons | 3 | 0 ⚠ | **100%** |
| Reflection Handlers | 9 | 0 (cached) ⚠ | **100%** |
| Thread Safety Issues | Widespread | 0 ⚠ | **100%** |
| Testability | Low | High ⚠ | **Significant** |
