# Component Architecture — RadioNavigation Mod

## 1. Component Hierarchy

```
RadioNavigation Mod (Source/)
│
├─── Entry Point
│    └── Logic/Main.cs (Harmony patches, mod initialization)
│
├─── UI Components (15 files — flat in Source/UI/)
│    ├── RadioHandler.cs (Main radio panel)
│    ├── ToggleHandler.cs (Power switch)
│    ├── ZoneHandler.cs (Zone selector)
│    ├── GroundHandler.cs (Ground toggle)
│    ├── CursorHandler.cs (Frequency dial)
│    ├── PanelDragHandler.cs (Panel dragging)
│    ├── ExpanderHandler.cs (Panel expand/collapse)
│    ├── DebugAngleHandler.cs (Debug display)
│    ├── InformationPanelHandler.cs (Info display)
│    ├── SignalRichTextFormatter.cs (Signal formatting)
│    ├── SpinnerHandlerBase.cs (Base spinner class)
│    ├── VolumeSpinnerHandler.cs (Volume control)
│    ├── FeedbackSpinnerHandler.cs (Feedback control)
│    ├── RadioInstaller.cs (UI instantiation)
│    └── PrefabNames.cs (Prefab path constants)
│
├─── Logic Components (12 files — flat in Source/Logic/)
│    ├── SonneController.cs (Main controller — God Class)
│    ├── RadioStoryIntegration.cs (Game story/locale patches)
│    ├── StationScheduler.cs (Station cycle state machine)
│    ├── MorseKeyer.cs (Morse timing state machine)
│    ├── NavigationPattern.cs (Consol patterns)
│    ├── NavigationElementType.cs (Enum)
│    ├── SonneSignalGenerator.cs (Signal generation)
│    ├── PositionFixCalculator.cs (Position calculation)
│    ├── MorseAlphabet.cs (Morse code data)
│    ├── MorseTiming.cs (Timing calculations)
│    ├── ComparisonExtensions.cs (Float comparison helpers)
│    └── Main.cs (Harmony entry point — listed above as Entry Point)
│
├─── Data Components (5 files — flat in Source/Data/)
│    ├── BeaconData.cs (Station database — 14 stations)
│    ├── DialConfig.cs (Dial calibration — 5 bands)
│    ├── ConsolCycleConfig.cs (Cycle timing)
│    ├── StationState.cs (Runtime state)
│    └── SignalTypes.cs (Signal enums)
│
└─── Sound Components (2 files, 5 classes — flat in Source/Sound/)
     ├── AudioSynthesisEngine.cs (Audio processing)
     └── DspPipeline.cs (DSP utilities — 3 classes: CarrierGenerator, Atmosphere, ReceiverDsp)
```

**NOTE**: Physical directory structure is flat — files are organized by layer in `Source/{UI,Logic,Data,Sound}/` with no subfolders. Subfolder names in the original SAD (RadioUI/, Spinners/, Installation/, Integration/, Core/, Navigation/, Morse/) were conceptual groupings, not physical directories. Removed non-existent classes: GameStrings, TickScheduler, StationManager, SignalDecoder, PositionTracker, AudioPipeline, CarrierBank, AudioController (see F47).

## 2. Component Responsibilities

### 2.1 Core Components

| Component | Responsibility | Inputs | Outputs |
|-----------|----------------|--------|---------|
| **SonneController** | Orchestrate all subsystems | Unity lifecycle events | Audio data, UI updates |
| **StationScheduler** | Manage station cycle phases | Timer ticks | Phase transitions |
| **MorseKeyer** | Generate Morse timing | Element requests | Sound/silence ticks |

### 2.2 Navigation Components

| Component | Responsibility | Inputs | Outputs |
|-----------|----------------|--------|---------|
| **NavigationPattern** | Generate consol patterns | Station config | Pattern sequences |
| **SonneSignalGenerator** | Calculate bearing tables | Station positions | Dot counts, sectors |
| **PositionFixCalculator** | Calculate position fix | Bearing measurements | Lat/Lon coordinates |

### 2.3 Data Components

| Component | Responsibility | Inputs | Outputs |
|-----------|----------------|--------|---------|
| **BeaconData** | Store station definitions (14) | Static data | Station array |
| **DialConfig** | Map angles to frequencies (5 bands) | Angle value | Frequency value |
| **ConsolCycleConfig** | Define cycle timing | Cycle duration | Timing config |
| **StationState** | Track station runtime state | Update commands | State values |

### 2.4 Sound Components

| Component | Responsibility | Inputs | Outputs |
|-----------|----------------|--------|---------|
| **AudioSynthesisEngine** | Main audio processing loop | Audio buffer | Processed audio |
| **CarrierGenerator** | Generate sine waves | Frequency, phase | Audio samples |
| **Atmosphere** | Generate atmospheric noise | Random state | Noise samples |
| **ReceiverDsp** | Apply AGC and filtering | Raw signal | Filtered signal |

## 3. Component Interfaces

### 3.1 Current Interfaces (Implicit)

```csharp
// SonneController — no formal interface
public class SonneController : MonoBehaviour {
    // Called by Unity
    void Update();
    void OnAudioFilterRead(float[] data, int channels);
    
    // Called by UI
    public void SetFrequency(float freq);
    public void SetVolume(float vol);
    
    // Internal
    private void UpdateStationGains();
    private void UpdateStationNavigation();
    private void ProcessAudio(float[] data, int channels);
}
```

### 3.2 Proposed Interfaces (NOT IMPLEMENTED — see F45/ADR-004)

```csharp
// IStationDataSource — extract from BeaconData
public interface IStationDataSource {
    BeaconStation[] GetAll();
    BeaconStation? GetByName(string name);
}

// IAudioOutput — extract from AudioSynthesisEngine
public interface IAudioOutput {
    float OutputVolume { get; set; }
    void ProcessAudio(float[] data, int channels, StationState[] stations,
        StationScheduler[] schedulers, MorseKeyer[] keyers,
        CarrierGenerator[] carriers, bool radioOn,
        int currentZoneIndex, int sonneZoneIndex);
}

// IPositionTracker — extract from PositionFixCalculator
public interface IPositionTracker {
    FixResult Calculate(List<Measurement> measurements);
    float GetDistanceKm(float lat1, float lon1, float lat2, float lon2);
}

// IMorseDecoder — extract from MorseKeyer (and MorseAlphabet)
public interface IMorseDecoder {
    char[] DecodeElements(int[] elements);
    int[] EncodeText(string text);
}
```

## 4. Component Dependencies

### 4.1 Current Dependencies

```
Main.cs
  └──► RadioStoryIntegration (reflection)
  └──► RadioInstaller (instantiation)

SonneController
  ├──► StationScheduler[] (14 stations)
  ├──► MorseKeyer[] (14 keyers)
  ├──► CarrierGenerator[] (14 carriers)
  ├──► AudioSynthesisEngine (audio)
  ├──► BeaconData (station data)
  ├──► DialConfig (frequency mapping)
  └──► ConsolCycleConfig (timing)

InformationPanelHandler
  ├──► SonneController (data source)
  ├──► ZoneHandler (zone state)
  └──► GroundHandler (ground state)

RadioHandler
  ├──► SonneController (frequency control)
  └──► ToggleHandler (power state)
```

### 4.2 Dependency Issues

| Issue | Severity | Description |
|-------|----------|-------------|
| Circular dependency risk | Medium | UI ↔ Logic tight coupling |
| Static coupling | High | ToggleHandler.IsInteractable used everywhere |
| God object fan-out | High | SonneController depends on everything |
| No abstraction | High | Direct class references, no interfaces |

## 5. Component Metrics

### 5.1 Size Metrics

**NOTE**: Corrected from original SAD — LOC were wrong for multiple components (F50).

| Component | Lines | Methods | Fields | Complexity |
|-----------|-------|---------|--------|------------|
| SonneController | 508 | ~29 | ~20 | Very High |
| InformationPanelHandler | 474 | 20+ | 15+ | Very High |
| RadioStoryIntegration | 381 | 15+ | 8+ | High |
| BeaconData | 373 | 3 | 1 | Low |
| PositionFixCalculator | 224 | 5 | 2 | Medium |
| MorseKeyer | 240 | 10+ | 8 | Medium |
| DialConfig | 158 | 3 | 2 | Medium |
| SonneSignalGenerator | 146 | 6 | 0 | Medium |
| AudioSynthesisEngine | 150 | 5 | 8 | High |
| MorseAlphabet | 126 | 5 | 2 | Low |
| StationScheduler | 120 | 8 | 6 | Medium |

### 5.2 Coupling Metrics

| Component | Efferent Coupling | Afferent Coupling | Instability |
|-----------|-------------------|-------------------|-------------|
| SonneController | 12 | 8 | 0.60 |
| InformationPanelHandler | 8 | 2 | 0.80 |
| RadioStoryIntegration | 8 | 1 | 0.89 |
| ToggleHandler | 3 | 6 | 0.33 |
| BeaconData | 1 | 4 | 0.20 |
| DialConfig | 1 | 3 | 0.25 |

## 6. Component Communication Patterns

### 6.1 Event-Based Communication

```csharp
// Current: Static events
public static event Action<float, int> OnFrequencyChanged;  // RadioHandler
public event Action<bool> OnRadioStateChanged;               // ToggleHandler
public event Action<int> OnZoneChanged;                      // ZoneHandler
```

### 6.2 Direct Method Calls

```csharp
// Current: Direct coupling
SonneController.SetFrequency(freq);
SonneController.UpdateStationGains();
SonneController.UpdateStationNavigation();
```

### 6.3 Shared Mutable State

```csharp
// Current: Shared state (problematic)
stations[s].gain = computedGain;          // Audio thread
stations[s].beatFrequency = beat;        // Main thread
stations[s].navigationStartTime = time;  // Both threads
```

## 7. Component Lifecycle

### 7.1 Initialization Order

```
1. Main.OnLoaded() [IUserMod interface]
   │
   2. RadioInstaller.Instantiate()
   │   ├── Instantiate prefab (Canvas + Panel from AssetBundle)
   │   └── Attach UI Handlers
   │
   3. SonneController.Awake()
   │   ├── Load BeaconData (14 stations)
   │   ├── Initialize Stations (14 schedulers, keyers, carriers)
   │   ├── Create Silent Audio Clip
   │   └── Start Navigation Loop
   │
   4. UI Handler.Start()
       ├── Subscribe to Events
       └── Initialize UI State
```

### 7.2 Update Order

```
Each Frame:
1. SonneController.Update()
   ├── UpdateStationGains()
   ├── UpdateStationNavigation()
   └── Update UI references
   
2. UI Handler.Update()
   ├── Process Input
   └── Update Visual State
   
Audio Thread:
1. OnAudioFilterRead()
   └── AudioSynthesisEngine.ProcessAudio()
       └── Single per-sample loop across all 14 stations
           ├── Generate Carrier
           ├── Apply Atmosphere
           └── Apply Receiver DSP
```

## 8. Component Testing

### 8.1 Testable Components (Pure C#)

| Component | Testability | Notes |
|-----------|-------------|-------|
| PositionFixCalculator | ✅ High | Pure math, no Unity deps |
| MorseAlphabet | ✅ High | Pure data, no deps |
| MorseTiming | ✅ High | Pure logic |
| NavigationPattern | ✅ High | Pure data |
| SonneSignalGenerator | ✅ High | Pure math |
| StationScheduler | ✅ High | Pure logic |
| DialConfig | ✅ High | Pure mapping |

### 8.2 Untestable Components (Unity-dependent)

| Component | Testability | Dependencies |
|-----------|-------------|--------------|
| SonneController | ❌ Low | MonoBehaviour, Time.time |
| AudioSynthesisEngine | ⚠️ Medium | Mathf.Clamp (minimal) |
| RadioStoryIntegration | ❌ Low | UnityEngine.Application |
| InformationPanelHandler | ❌ Low | UI, Transforms |

**Note**: 0% test coverage (no test project exists). See F22, F45 (ADR-007 — changed to Deferred).

----

*Document Version: 1.2*  
*Last Updated: 2026-08-01*  
*Corrections: Fixed component hierarchy (flat structure, added ComparisonExtensions), fixed array sizes 4→14 (F48), file counts (F49), added missing classes (F52). See F65–F199 (total 199 issues). See F45–F52 for SAD drift corrections.*
