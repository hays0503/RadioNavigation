# Target Architecture — RadioNavigation Mod

## 1. Target Layer Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                          UI Layer                                   │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐  │
│  │ RadioHandler│ │ToggleHandler│ │ZoneHandler  │ │GroundHandler│  │
│  │ (Radio UI)  │ │ (Power)     │ │ (Zone)      │ │ (Ground)    │  │
│  └──────┬──────┘ └──────┬──────┘ └──────┬──────┘ └──────┬──────┘  │
│         │               │               │               │          │
│  ┌──────┴──────┐ ┌──────┴──────┐ ┌──────┴──────┐ ┌──────┴──────┐  │
│  │CursorHandler│ │SpinnerBase  │ │PanelDrag    │ │Information  │  │
│  │ (Dial)      │ │ (Spinners)  │ │ (Drag)      │ │Panel (Info) │  │
│  └──────┬──────┘ └──────┬──────┘ └──────┬──────┘ └──────┬──────┘  │
│         │               │               │               │          │
│  ┌──────┴──────┐ ┌──────┴──────┐ ┌──────┴──────┐                  │
│  │VolumeSpinner│ │FeedbackSpin │ │ExpanderHdlr │                  │
│  │ (Volume)    │ │ (Feedback)  │ │ (Expand)    │                  │
│  └──────┬──────┘ └──────┬──────┘ └──────┬──────┘                  │
└─────────┼───────────────┼───────────────┼──────────────────────────┘
          │               │               │
          ▼               ▼               ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    Service Layer (Interfaces)                       │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐               │
│  │ IRadioState  │ │IStationData  │ │ IAudioOutput │               │
│  │ (State)      │ │ (Data)       │ │ (Audio)      │               │
│  └──────┬───────┘ └──────┬───────┘ └──────┬───────┘               │
│         │               │               │                          │
│  ┌──────┴──────┐ ┌──────┴──────┐ ┌──────┴──────┐                  │
│  │IGameServices│ │IPositionCalc│ │IMorseDecoder│                  │
│  │ (Game)      │ │ (Position)  │ │ (Morse)     │                  │
│  └──────┬──────┘ └──────┬──────┘ └──────┬──────┘                  │
└─────────┼───────────────┼───────────────┼──────────────────────────┘
          │               │               │
          ▼               ▼               ▼
┌─────────────────────────────────────────────────────────────────────┐
│                         Logic Layer                                 │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐               │
│  │StationManager│ │SignalDecoder │ │PositionTracker│               │
│  │ (Stations)   │ │ (Signals)    │ │ (Position)   │               │
│  └──────┬───────┘ └──────┬───────┘ └──────┬───────┘               │
│         │               │               │                          │
│  ┌──────┴──────┐ ┌──────┴──────┐ ┌──────┴──────┐                  │
│  │StationSched │ │MorseKeyer   │ │MorseAlphabet│                  │
│  │ (Scheduler) │ │ (Keyer)     │ │ (Morse)     │                  │
│  └──────┬──────┘ └──────┬──────┘ └──────┬──────┘                  │
│         │               │               │                          │
│  ┌──────┴──────┐ ┌──────┴──────┐ ┌──────┴──────┐                  │
│  │NavPattern   │ │SonneSignal  │ │PositionFix  │                  │
│  │ (Patterns)  │ │Generator    │ │Calculator   │                  │
│  └──────┬──────┘ └──────┬──────┘ └──────┬──────┘                  │
└─────────┼───────────────┼───────────────┼──────────────────────────┘
          │               │               │
          ▼               ▼               ▼
┌─────────────────────────────────────────────────────────────────────┐
│                         Data Layer                                  │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐               │
│  │StationRepo   │ │DialConfig    │ │ConsolCycle   │               │
│  │ (JSON/Static)│ │ (JSON)       │ │Config        │               │
│  └──────┬───────┘ └──────┬───────┘ └──────┬───────┘               │
│         │               │               │                          │
│  ┌──────┴──────┐ ┌──────┴──────┐ ┌──────┴──────┐                  │
│  │StationState │ │Navigation   │ │Callsign     │                  │
│  │ (State)     │ │Pattern      │ │Pattern      │                  │
│  └──────┬──────┘ └──────┬──────┘ └──────┬──────┘                  │
└─────────┼───────────────┼───────────────┼──────────────────────────┘
          │               │               │
          ▼               ▼               ▼
┌─────────────────────────────────────────────────────────────────────┐
│                         Sound Layer                                 │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐               │
│  │AudioPipeline │ │CarrierBank   │ │Atmosphere    │               │
│  │ (Pipeline)   │ │ (Carriers)   │ │ (Noise)      │               │
│  └──────┬───────┘ └──────┬───────┘ └──────┬───────┘               │
│         │               │               │                          │
│  ┌──────┴──────┐ ┌──────┴──────┐                               │
│  │ReceiverDsp  │ │TickScheduler│                               │
│  │ (DSP)       │ │ (Timing)    │                               │
│  └─────────────┘ └─────────────┘                               │
└─────────────────────────────────────────────────────────────────────┘
```

## 2. New Components

### 2.1 Service Interfaces

```csharp
// IRadioState.cs — Central radio state
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

// IStationDataSource.cs — Station data access
public interface IStationDataSource {
    BeaconStation[] GetAll();
    BeaconStation? GetByName(string name);
    BeaconStation? GetByIndex(int index);
}

// IAudioOutput.cs — Audio processing
public interface IAudioOutput {
    float OutputVolume { get; set; }
    float FeedbackVolume { get; set; }
    void ProcessAudio(float[] data, int channels, StationState[] stations);
}

// IGameServices.cs — Game integration
public interface IGameServices {
    string GetCurrentLanguage();
    string RetrieveString(string key);
    void InjectDecision(object story, int index, object decision);
    void NotifyUpdated(object story);
}

// IPositionCalculator.cs — Position fixing
public interface IPositionCalculator {
    FixResult Calculate(List<Measurement> measurements);
    float GetDistanceKm(float lat1, float lon1, float lat2, float lon2);
}

// IMorseDecoder.cs — Morse decoding
public interface IMorseDecoder {
    char[] DecodeElements(int[] elements);
    int[] EncodeText(string text);
    string GetSignalRichText(List<SignalEntry> entries);
}
```

### 2.2 Implementation Classes

```csharp
// RadioContext.cs — Implements IRadioState
public class RadioContext : MonoBehaviour, IRadioState {
    public bool IsRadioOn { get; private set; }
    public bool IsInteractable { get; private set; }
    // ... events, state management
}

// HardcodedStationRepository.cs — Implements IStationDataSource
public class HardcodedStationRepository : IStationDataSource {
    public BeaconStation[] GetAll() => BeaconData.Stations;
    // ... methods
}

// JsonStationRepository.cs — Future JSON support
public class JsonStationRepository : IStationDataSource {
    private readonly string jsonPath;
    public BeaconStation[] GetAll() => LoadFromJson();
    // ... methods
}

// AudioPipeline.cs — Implements IAudioOutput
public class AudioPipeline : IAudioOutput {
    private readonly CarrierBank carriers;
    private readonly Atmosphere atmosphere;
    private readonly ReceiverDsp dsp;
    // ... methods
}

// GameReflectionCache.cs — Implements IGameServices
public class GameReflectionCache : IGameServices {
    private readonly TypeCache cache;
    // ... methods
}
```

## 3. Decomposed SonneController

```
BEFORE: SonneController (506 lines, God Class)
│
└──► AFTER:
    ├── RadioController.cs (~80 lines)
    │   ├── Awake()
    │   ├── Update()
    │   ├── OnDestroy()
    │   └── Orchestrates subsystems
    │
    ├── StationManager.cs (~100 lines)
    │   ├── stations[]
    │   ├── UpdateStationGains()
    │   ├── UpdateStationNavigation()
    │   └── SetActiveStation()
    │
    ├── SignalDecoder.cs (~90 lines)
    │   ├── signalEntries
    │   ├── OnStationElementCompleted()
    │   ├── DecodeCallsignBuffer()
    │   └── GetSignalRichText()
    │
    ├── PositionTracker.cs (~60 lines)
    │   ├── measurements
    │   ├── lastFix
    │   ├── UpdatePosition()
    │   └── GetPositionFix()
    │
    └── DiagnosticsCollector.cs (~30 lines)
        ├── GetStationDiagnostics()
        └── FormatDiagnosticString()
```

## 4. Decomposed InformationPanelHandler

```
BEFORE: InformationPanelHandler (474 lines, God Class)
│
└──► AFTER:
    ├── UIElementFactory.cs (~150 lines)
    │   ├── CreateLabel()
    │   ├── CreateSeparator()
    │   ├── CreateImage()
    │   ├── CreateText()
    │   └── EnsureLayout()
    │
    ├── StationInfoDisplay.cs (~120 lines)
    │   ├── RefreshAll()
    │   ├── UpdateStationInfo()
    │   ├── UpdatePatternDisplay()
    │   └── UpdateSignalDisplay()
    │
    ├── StationFinder.cs (~80 lines)
    │   ├── FindClosestStation()
    │   ├── GetZoneIndex()
    │   └── IsGroundActive()
    │
    └── InformationPanelHandler.cs (~124 lines) — ORCHESTRATOR
        ├── Start()
        ├── RefreshAll()
        └── Event subscriptions
```

## 5. Audio Pipeline

```
BEFORE: AudioSynthesisEngine (150 lines)
│
└──► AFTER:
    ├── AudioPipeline.cs (~80 lines)
    │   ├── ProcessAudio()
    │   └── Mix stations
    │
    ├── CarrierBank.cs (~50 lines)
    │   ├── carriers[]
    │   ├── GenerateCarrier()
    │   └── UpdatePhases()
    │
    ├── Atmosphere.cs (~40 lines)
    │   ├── GenerateNoise()
    │   └── Impulse noise
    │
    ├── ReceiverDsp.cs (~60 lines)
    │   ├── ApplyAGC()
    │   ├── ApplyFilter()
    │   └── ApplySaturation()
    │
    └── TickScheduler.cs (~30 lines)
        ├── Tick()
        └── OnSequenceFinished()
```

## 6. Data Layer

```
BEFORE: Hardcoded data
│
└──► AFTER:
    ├── Config/
    │   ├── beacons.json
    │   │   {
    │   │     "stations": [
    │   │       { "name": "Flensburg", "lat": 54.79, "lon": 9.43, ... }
    │   │     ]
    │   │   }
    │   │
    │   ├── bands.json
    │   │   {
    │   │     "bands": [
    │   │       { "id": "I", "min": 70, "max": 150, ... }
    │   │     ]
    │   │   }
    │   │
    │   └── cycles.json
    │       {
    │         "cycles": [
    │           { "duration": 120, "phases": [...] }
    │         ]
    │       }
    │
    └── Source/Data/
        ├── Constants.cs (all magic numbers)
        ├── PrefabNames.cs (asset names)
        └── GameStrings.cs (game strings)
```

## 7. Thread Safety Pattern

```csharp
// Double-buffer pattern for audio thread safety
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

## 8. Dependency Injection

```csharp
// Main.cs — Composition Root
public class Main {
    private static RadioContext radioContext;
    private static IStationDataSource stationData;
    private static IAudioOutput audioOutput;
    private static IGameServices gameServices;
    
    [HarmonyPostfix]
    public static void OnLoaded() {
        // Create services
        radioContext = new RadioContext();
        stationData = new HardcodedStationRepository();
        audioOutput = new AudioPipeline();
        gameServices = new GameReflectionCache();
        
        // Create UI
        var installer = new RadioInstaller();
        installer.Instantiate(radioContext, stationData);
        
        // Create controller
        var controller = new RadioController(
            radioContext,
            stationData,
            audioOutput,
            gameServices
        );
    }
}
```

## 9. Metrics Comparison

| Metric | Current | Target | Improvement |
|--------|---------|--------|-------------|
| Max lines per class | 506 | ~150 | **70%** |
| Max methods per class | 30+ | ~10 | **67%** |
| Max fields per class | 25+ | ~8 | **68%** |
| God Classes | 2 | 0 | **100%** |
| Static Singletons | 5+ | 1 | **80%** |
| Reflection Handlers | 12 | 0 | **100%** |
| Thread Safety Issues | 2 | 0 | **100%** |
| Testability | Low | High | **Significant** |
