# Current Architecture — RadioNavigation Mod

## 1. Layer Architecture

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
┌─────────┼───────────────┼───────────────┼──────────────────────────┐
│         │          Logic Layer            │                          │
│  ┌──────┴───────────────┴───────────────┴───────┐                  │
│  │              SonneController (506 lines)       │                  │
│  │  • Station Management                          │                  │
│  │  • Navigation Calculation                      │                  │
│  │  • Signal Decoding                             │                  │
│  │  • Audio Coordination                          │                  │
│  │  • Position Tracking                           │                  │
│  └──────┬───────────────┬───────────────┬───────┘                  │
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
┌─────────┼───────────────┼───────────────┼──────────────────────────┐
│         │           Data Layer             │                          │
│  ┌──────┴──────┐ ┌──────┴──────┐ ┌──────┴──────┐                  │
│  │BeaconData   │ │DialConfig   │ │ConsolCycle  │                  │
│  │ (Stations)  │ │ (Dial)      │ │Config       │                  │
│  └──────┬──────┘ └──────┬──────┘ └──────┬──────┘                  │
│         │               │               │                          │
│  ┌──────┴──────┐ ┌──────┴──────┐ ┌──────┴──────┐                  │
│  │StationState │ │Navigation   │ │Callsign     │                  │
│  │ (State)     │ │Pattern      │ │Pattern      │                  │
│  └──────┬──────┘ └──────┬──────┘ └──────┬──────┘                  │
└─────────┼───────────────┼───────────────┼──────────────────────────┘
          │               │               │
┌─────────┼───────────────┼───────────────┼──────────────────────────┐
│         │           Sound Layer            │                          │
│  ┌──────┴──────┐ ┌──────┴──────┐ ┌──────┴──────┐                  │
│  │AudioSynth   │ │DspPipeline  │ │CarrierGen   │                  │
│  │Engine       │ │ (DSP)       │ │ (Carrier)   │                  │
│  └──────┬──────┘ └──────┬──────┘ └──────┬──────┘                  │
│         │               │               │                          │
│  ┌──────┴──────┐ ┌──────┴──────┐ ┌──────┴──────┐                  │
│  │Atmosphere   │ │ReceiverDsp  │ │SignalRich   │                  │
│  │ (Noise)     │ │ (AGC/Filter)│ │TextFormat   │                  │
│  └─────────────┘ └─────────────┘ └─────────────┘                  │
└─────────────────────────────────────────────────────────────────────┘
```

## 2. File Statistics

| Layer | Files | Total Lines | Avg Lines/File |
|-------|-------|-------------|----------------|
| UI | 12 | ~850 | ~71 |
| Logic | 14 | ~1,800 | ~129 |
| Data | 8 | ~950 | ~119 |
| Sound | 6 | ~600 | ~100 |
| **Total** | **40** | **~4,200** | **~105** |

## 3. Component Details

### 3.1 UI Layer Components

#### RadioHandler.cs (Radio UI)
- **Lines**: ~120
- **Responsibility**: Main radio panel controller
- **Dependencies**: SonneController, ToggleHandler
- **Issues**: Static singleton pattern, magic numbers

#### ToggleHandler.cs (Power Switch)
- **Lines**: ~48
- **Responsibility**: Radio on/off toggle
- **Dependencies**: RadioHandler
- **Issues**: Static properties used as global state

#### ZoneHandler.cs (Zone Selector)
- **Lines**: ~25
- **Responsibility**: Navigation zone selection
- **Dependencies**: ToggleHandler (static)
- **Issues**: Static coupling

#### GroundHandler.cs (Ground Toggle)
- **Lines**: ~83
- **Responsibility**: Ground effect toggle
- **Dependencies**: ToggleHandler (static)
- **Issues**: Mixes cursor + angle + zone logic

#### CursorHandler.cs (Dial Control)
- **Lines**: ~37
- **Responsibility**: Frequency dial input
- **Dependencies**: RadioHandler.Instance, ToggleHandler
- **Issues**: Duplicates pattern from SpinnerHandler

#### InformationPanelHandler.cs (Info Display)
- **Lines**: ~474
- **Responsibility**: Station info, signal display, diagnostics
- **Dependencies**: SonneController, ZoneHandler, GroundHandler
- **Issues**: **God Class** — mixes UI creation, station finding, diagnostics

#### SpinnerHandlerBase.cs (Base Spinner)
- **Lines**: ~15
- **Responsibility**: Base class for spinner controls
- **Issues**: Not fully utilized by subclasses

#### VolumeSpinnerHandler.cs / FeedbackSpinnerHandler.cs
- **Lines**: ~17 each
- **Responsibility**: Volume/feedback controls
- **Issues**: Nearly identical — code duplication

### 3.2 Logic Layer Components

#### SonneController.cs (Main Controller)
- **Lines**: 506
- **Methods**: 30+
- **Fields**: 25+
- **Responsibility**: **God Class** — Station management, navigation, audio, decoding
- **Issues**:
  - Thread safety violations
  - Mixes 4+ distinct responsibilities
  - Direct audio buffer manipulation

#### StationScheduler.cs (Station Scheduler)
- **Lines**: ~120
- **Responsibility**: Station cycle management (Callsign → Navigation → Pause)
- **Pattern**: State Machine
- **Issues**: Public setters, phase transitions logged externally

#### MorseKeyer.cs (Morse Keyer)
- **Lines**: ~150
- **Responsibility**: Morse code timing and element advancement
- **Pattern**: State Machine
- **Issues**: ~60 lines in AdvanceElement(), duplicated logic

#### MorseAlphabet.cs (Morse Data)
- **Lines**: ~80
- **Responsibility**: Morse code lookup tables
- **Issues**: None — clean data class

#### NavigationPattern.cs (Navigation Patterns)
- **Lines**: ~100
- **Responsibility**: Consol navigation pattern generation
- **Issues**: String concatenation in loops, 4 types in one file

#### SonneSignalGenerator.cs (Signal Generator)
- **Lines**: ~90
- **Responsibility**: Generate bearing tables and signal data
- **Issues**: Duplicates BeaconData.GetDotCountToBearingTable()

#### PositionFixCalculator.cs (Position Calculator)
- **Lines**: ~130
- **Responsibility**: Calculate position from bearing measurements
- **Issues**: ErrorKm always returns 0, mutable struct measurements

#### RadioStoryIntegration.cs (Game Integration)
- **Lines**: 381
- **Responsibility**: Harmony patches for story/locale injection
- **Issues**: **Fragile** — 12 reflection handlers, version-dependent

#### ComparisonExtensions.cs (Extensions)
- **Lines**: ~50
- **Responsibility**: Null/float comparison helpers
- **Issues**: 34 methods that duplicate C# operators

### 3.3 Data Layer Components

#### BeaconData.cs (Station Database)
- **Lines**: ~373
- **Responsibility**: Hardcoded beacon station data
- **Issues**: Static array, no JSON support, duplicates bearing table logic

#### DialConfig.cs (Dial Configuration)
- **Lines**: ~184
- **Responsibility**: Frequency dial calibration data
- **Issues**: 184 lines of static initialization with magic numbers

#### ConsolCycleConfig.cs (Cycle Configuration)
- **Lines**: ~50
- **Responsibility**: Station cycle timing configuration
- **Issues**: Dead parameter in GetForCycleDuration()

#### StationState.cs (Station State)
- **Lines**: ~30
- **Responsibility**: Runtime state for a beacon station
- **Issues**: Mutable struct — copy semantics cause bugs

### 3.4 Sound Layer Components

#### AudioSynthesisEngine.cs (Audio Engine)
- **Lines**: ~150
- **Responsibility**: Main audio processing loop
- **Issues**: Debug.Log in audio thread, thread safety

#### DspPipeline.cs (DSP Pipeline)
- **Lines**: ~100
- **Responsibility**: Digital signal processing
- **Issues**: 3 classes in one file, phase accumulation anti-pattern

#### CarrierGenerator.cs (Carrier Signal)
- **Lines**: ~40
- **Responsibility**: Generate sine wave carriers
- **Issues**: Float precision issues in phase

#### Atmosphere.cs (Atmosphere Noise)
- **Lines**: ~60
- **Responsibility**: Generate atmospheric noise
- **Issues**: None significant

#### ReceiverDsp.cs (Receiver DSP)
- **Lines**: ~70
- **Responsibility**: AGC, filtering, saturation
- **Issues**: None significant

## 4. Dependency Matrix

```
                    UI    Logic   Data   Sound
UI                  -     High    Low    Low
Logic               High  -       High   High
Data                Low   High    -      Low
Sound               Low   High    Low    -
```

## 5. Entry Points

| Entry Point | Trigger | Flow |
|-------------|---------|------|
| `Main.OnLoaded()` | Game load complete | Harmony Postfix → RadioInstaller → SonneController |
| `SonneController.Update()` | Every frame | Update navigation, UI, signals |
| `SonneController.OnAudioFilterRead()` | Audio callback | Process audio buffers |
| `RadioHandler.OnFrequencyChanged()` | Dial rotation | Update active frequency |
| `ToggleHandler.OnToggleChanged()` | Power button | Toggle radio on/off |
| `ZoneHandler.OnZoneChanged()` | Zone selector | Switch navigation zone |

## 6. State Management

### Global State Locations

| State | Location | Access Pattern |
|-------|----------|----------------|
| Radio On/Off | `ToggleHandler.IsRadioOn` | Static property |
| Interactable | `ToggleHandler.IsInteractable` | Static property |
| Ground Active | `GroundHandler.groundActive` | Static field |
| Current Frequency | `RadioHandler.currentFrequency` | Instance field |
| Cursor Angle | `CursorHandler.cursorAngle` | Instance field |
| Zone Index | `ZoneHandler.currentIndex` | Private field |

### Problems

1. **Scattered State**: Radio state split across 4+ static locations
2. **No Single Source of Truth**: Multiple places can modify same state
3. **Untestable**: Static state cannot be mocked or isolated
