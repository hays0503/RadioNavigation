# Component Architecture — RadioNavigation Mod

## 1. Component Hierarchy

```
RadioNavigation Mod
│
├─── Entry Point
│    └── Main.cs (Harmony patches, mod initialization)
│
├─── UI Components
│    ├── RadioUI/
│    │   ├── RadioHandler.cs (Main radio panel)
│    │   ├── ToggleHandler.cs (Power switch)
│    │   ├── ZoneHandler.cs (Zone selector)
│    │   ├── GroundHandler.cs (Ground toggle)
│    │   ├── CursorHandler.cs (Frequency dial)
│    │   ├── PanelDragHandler.cs (Panel dragging)
│    │   ├── ExpanderHandler.cs (Panel expand/collapse)
│    │   ├── DebugAngleHandler.cs (Debug display)
│    │   └── InformationPanelHandler.cs (Info display)
│    │
│    ├── Spinners/
│    │   ├── SpinnerHandlerBase.cs (Base class)
│    │   ├── VolumeSpinnerHandler.cs (Volume control)
│    │   └── FeedbackSpinnerHandler.cs (Feedback control)
│    │
│    └── Installation/
│        └── RadioInstaller.cs (UI instantiation)
│
├─── Logic Components
│    ├── Core/
│    │   ├── SonneController.cs (Main controller — God Class)
│    │   ├── StationScheduler.cs (Station cycle state machine)
│    │   └── MorseKeyer.cs (Morse timing state machine)
│    │
│    ├── Navigation/
│    │   ├── NavigationPattern.cs (Consol patterns)
│    │   ├── CallsignPattern.cs (Callsign patterns)
│    │   ├── SonneSignalGenerator.cs (Signal generation)
│    │   └── PositionFixCalculator.cs (Position calculation)
│    │
│    └── Morse/
│        └── MorseAlphabet.cs (Morse code data)
│
├─── Data Components
│    ├── BeaconData.cs (Station database)
│    ├── DialConfig.cs (Dial calibration)
│    ├── ConsolCycleConfig.cs (Cycle timing)
│    ├── StationState.cs (Runtime state)
│    ├── PrefabNames.cs (Asset names)
│    ├── GameStrings.cs (Game string constants)
│    └── ComparisonExtensions.cs (Extensions)
│
├─── Sound Components
│    ├── AudioSynthesisEngine.cs (Audio processing)
│    ├── DspPipeline.cs (DSP utilities)
│    ├── CarrierGenerator.cs (Sine oscillator)
│    ├── Atmosphere.cs (Noise generation)
│    └── ReceiverDsp.cs (AGC, filtering)
│
└─── Integration
     └── RadioStoryIntegration.cs (Game story/locale patches)
```

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
| **CallsignPattern** | Generate callsign patterns | Station config | Callsign sequences |
| **SonneSignalGenerator** | Calculate bearing tables | Station positions | Dot counts, sectors |
| **PositionFixCalculator** | Calculate position fix | Bearing measurements | Lat/Lon coordinates |

### 2.3 Data Components

| Component | Responsibility | Inputs | Outputs |
|-----------|----------------|--------|---------|
| **BeaconData** | Store station definitions | Static data | Station array |
| **DialConfig** | Map angles to frequencies | Angle value | Frequency value |
| **ConsolCycleConfig** | Define cycle timing | Cycle duration | Timing config |
| **StationState** | Track station runtime state | Update commands | State values |

### 2.4 Sound Components

| Component | Responsibility | Inputs | Outputs |
|-----------|----------------|--------|---------|
| **AudioSynthesisEngine** | Main audio processing loop | Audio buffer | Processed audio |
| **DspPipeline** | DSP utilities | Signal data | Processed signal |
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

### 3.2 Proposed Interfaces

```csharp
// IStationDataSource — extract from BeaconData
public interface IStationDataSource {
    BeaconStation[] GetAll();
    BeaconStation? GetByName(string name);
}

// IAudioOutput — extract from AudioSynthesisEngine
public interface IAudioOutput {
    float OutputVolume { get; set; }
    void ProcessAudio(float[] data, int channels, StationState[] stations);
}

// IPositionTracker — extract from PositionFixCalculator
public interface IPositionTracker {
    FixResult Calculate(List<Measurement> measurements);
    float GetDistanceKm(float lat1, float lon1, float lat2, float lon2);
}

// IMorseDecoder — extract from MorseKeyer
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
  ├──► StationScheduler[] (4 stations)
  ├──► MorseKeyer[] (4 keyers)
  ├──► CarrierGenerator[] (4 carriers)
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

| Component | Lines | Methods | Fields | Complexity |
|-----------|-------|---------|--------|------------|
| SonneController | 506 | 30+ | 25+ | Very High |
| InformationPanelHandler | 474 | 20+ | 15+ | Very High |
| RadioStoryIntegration | 381 | 15+ | 12+ | High |
| BeaconData | 373 | 3 | 1 | Low |
| DialConfig | 184 | 3 | 2 | Medium |
| MorseKeyer | 150 | 10+ | 8 | Medium |
| PositionFixCalculator | 130 | 5 | 2 | Medium |
| AudioSynthesisEngine | 150 | 5 | 8 | High |
| StationScheduler | 120 | 8 | 6 | Medium |

### 5.2 Coupling Metrics

| Component | Efferent Coupling | Afferent Coupling | Instability |
|-----------|-------------------|-------------------|-------------|
| SonneController | 12 | 8 | 0.60 |
| InformationPanelHandler | 8 | 2 | 0.80 |
| RadioStoryIntegration | 15 | 1 | 0.94 |
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
1. Main.OnLoaded() [Harmony Postfix]
   │
   2. RadioInstaller.Instantiate()
   │   ├── Create Canvas
   │   ├── Create Radio Panel
   │   └── Attach UI Handlers
   │
   3. SonneController.Awake()
   │   ├── Load BeaconData
   │   ├── Initialize Stations
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
       ├── For each station:
       │   ├── Generate Carrier
       │   ├── Apply Atmosphere
       │   └── Apply Receiver DSP
       └── Mix all stations
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
