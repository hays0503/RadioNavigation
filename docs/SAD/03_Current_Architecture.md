# Current Architecture — RadioNavigation Mod

## 0. Действующие цепочки (2026-10-04)

| Цепочка | Ответственность |
|---|---|
| Main → ModBootstrapper / SceneContentBuilder | patching, DI, bundle fallback, scene wiring и double-load guards |
| SonneController → StationManager / SignalDecoder / audio | координаты/выбор станции, морзе, audio-thread без Unity/аллокаций; радиопанель читает фасад |
| SextantController → PIP factory / pose / interior mirror | input/lifecycle, один attached либо standalone route, root writer внутри, parent-frame оба луча |
| SextantStarId → worksheet / running fix / card | hold/selection, отсчёт барабана по I, три слота, snapshot-pure расчётная секция |
| CelestialEphemeris / AstroMath → sky / ID / reduction | эпоха 1926.0 → дата, UT/TT, топоцентрическая Луна, единые направления тел |
| NavMapMarkerBase → MapMarkerPointBridge / MapSaveSanitizer | родные MapPointUI, includeInactive-реконсиляция, family ownership, save-дедуп и explicit removal |

Полные component contracts — `SAD/04_Component_Architecture.md` §10.12–10.15;
камера/лампа/open-frame — `docs/CAMERA_GUIDE.md`, доказательства — `docs/plans/SEXTANT_PLAN.md`
§16.23–16.25. Игра компилирует Source при загрузке; локальная DLL из raw csc
не используется как runtime-деплой.

**Разделы 1–6 ниже — исторический снимок радиоядра 2026-08**, не полный список
текущих классов/дефектов. LOC/старые API в нём нельзя использовать как текущий
контракт; извлечённые фасады и satellites описаны в §0 и главе 04.

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
│         │               │               │                          │
│  ┌──────┴──────┐ ┌──────┴──────┐ ┌──────┴──────┐                  │
│  │SignalRich   │ │DebugAngle   │ │PrefabNames  │                  │
│  │TextFmt      │ │Handler      │ │ (Constants) │                  │
│  └─────────────┘ └─────────────┘ └─────────────┘                  │
└─────────┼───────────────┼───────────────┼──────────────────────────┘
          │               │               │
┌─────────┼───────────────┼───────────────┼──────────────────────────┐
│         │          Logic Layer            │                          │
│  ┌──────┴───────────────┴───────────────┴───────┐                  │
│  │              SonneController (508 lines)       │                  │
│  │  • Station Management                          │                  │
│  │  • Navigation Calculation                      │                  │
│  │  • Signal Decoding                             │                  │
│  │  • Audio Coordination                          │                  │
│  │  • Bearing Diagnostics                         │                  │
│  └──────┬───────────────┬───────────────┬───────┘                  │
│         │               │               │                          │
│  ┌──────┴──────┐ ┌──────┴──────┐ ┌──────┴──────┐                  │
│  │StationSched │ │MorseKeyer   │ │MorseAlphabet│                  │
│  │ (Scheduler) │ │ (Keyer)     │ │ (Morse)     │                  │
│  └──────┬──────┘ └──────┬──────┘ └──────┬──────┘                  │
│         │               │               │                          │
│  ┌──────┴──────┐ ┌──────┴──────┐                            │
│  │NavPattern   │ │SonneSignal  │                            │
│  │ (Patterns)  │ │Generator    │                            │
│  └──────┬──────┘ └──────┬──────┘                            │
│         │               │                                    │
│  ┌──────┴──────┐ ┌──────┴──────┐                                 │
│  │RadioStory   │ │Comparison   │                                 │
│  │Integration  │ │Extensions   │                                 │
│  └─────────────┘ └─────────────┘                                 │
└─────────┼───────────────┼───────────────┼──────────────────────────┘
          │               │               │
┌─────────┼───────────────┼───────────────┼──────────────────────────┐
│         │           Data Layer             │                          │
│  ┌──────┴──────┐ ┌──────┴──────┐ ┌──────┴──────┐                  │
│  │BeaconData   │ │DialConfig   │ │ConsolCycle  │                  │
│  │ (Stations)  │ │ (Dial)      │ │Config       │                  │
│  └──────┬──────┘ └──────┬──────┘ └──────┬──────┘                  │
│         │               │               │                          │
│  ┌──────┴──────┐ ┌──────┴──────┐                                 │
│  │StationState │ │Navigation   │                                 │
│  │ (State)     │ │Pattern      │                                 │
│  └──────┬──────┘ └──────┬──────┘                                 │
│         │               │                                          │
│  ┌──────┴──────┐ ┌──────┴──────┐                                 │
│  │SignalTypes  │ │MorseTiming  │                                 │
│  │ (Enums)     │ │ (Timing)    │                                 │
│  └─────────────┘ └─────────────┘                                 │
└─────────┼───────────────┼───────────────┼──────────────────────────┘
          │               │               │
┌─────────┼───────────────┼───────────────┼──────────────────────────┐
│         │           Sound Layer            │                          │
│  ┌──────┴──────┐ ┌──────┴──────┐                                 │
│  │AudioSynth   │ │DspPipeline  │                                 │
│  │Engine       │ │ (DSP)       │                                 │
│  └──────┬──────┘ └──────┬──────┘                                 │
│         │               │                                          │
│         │        ┌──────┴──────┐                                  │
│         │        │CarrierGen   │ (embedded in DspPipeline.cs)     │
│         │        ├─────────────┤                                  │
│         │        │Atmosphere   │ (embedded in DspPipeline.cs)     │
│         │        ├─────────────┤                                  │
│         │        │ReceiverDsp  │ (embedded in DspPipeline.cs)     │
│         │        └─────────────┘                                  │
└─────────┼───────────────┼───────────────┼──────────────────────────┘
```

## 2. File Statistics

**NOTE**: Corrected from original SAD which had wrong counts (see F49). Counts refreshed 2026-08-22 (lighthouse + star sky subsystems added).

| Layer | Files | Total Lines | Avg Lines/File |
|-------|-------|-------------|----------------|
| UI | 21 | ~3,494 | ~166 |
| Logic | 29 | ~6,423 | ~221 |
| Data | 10 | ~6,495 | ~650 |
| Sound | 2 | ~367 | ~184 |
| **Total** | **62** | **~16,779** | **~271** |

## 3. Component Details

### 3.1 UI Layer Components (15 files)

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

#### PanelDragHandler.cs (Panel Drag)
- **Lines**: ~20
- **Responsibility**: Drag radio panel
- **Issues**: Static Texture2D never destroyed (F42)

#### ExpanderHandler.cs (Expand/Collapse)
- **Lines**: ~54
- **Responsibility**: Panel expand/collapse

#### DebugAngleHandler.cs (Debug)
- **Lines**: ~50
- **Responsibility**: Debug angle slider
- **Issues**: Uses TMPro which may not be in all UBOAT builds

#### SignalRichTextFormatter.cs (Formatting)
- **Lines**: ~86
- **Responsibility**: Rich text formatting for signal display

#### PrefabNames.cs (Names)
- **Lines**: ~34
- **Responsibility**: Prefab path constants
- **Issues**: 16/17 constants unused (F26)

#### RadioInstaller.cs (Installation)
- **Lines**: ~80
- **Responsibility**: UI instantiation from prefab
- **Issues**: AudioSource not ensured (F43)

### 3.2 Logic Layer Components (12 files)

#### SonneController.cs (Main Controller)
- **Lines**: 508
- **Methods**: ~29
- **Fields**: ~20
- **Responsibility**: **God Class** — Station management, navigation, audio, decoding
- **Issues**:
  - Thread safety violations
  - Mixes 4+ distinct responsibilities
  - Direct audio buffer manipulation

#### StationScheduler.cs (Station Scheduler)
- **Lines**: ~120
- **Responsibility**: Station cycle management
- **Pattern**: State Machine
- **Issues**: Public setters, BearingTable property never read (F60)

#### MorseKeyer.cs (Morse Keyer)
- **Lines**: 240
- **Responsibility**: Morse code timing and element advancement
- **Pattern**: State Machine
- **Issues**: ~60 lines in AdvanceElement(), duplicated logic

#### MorseAlphabet.cs (Morse Data)
- **Lines**: 126
- **Responsibility**: Morse code lookup tables
- **Issues**: None — clean data class

#### NavigationPattern.cs (Navigation Patterns)
- **Lines**: ~100
- **Responsibility**: Consol navigation pattern generation
- **Issues**: String concatenation in loops, 4 types in one file

#### SonneSignalGenerator.cs (Signal Generator)
- **Lines**: 146
- **Responsibility**: Generate bearing tables and signal data
- **Issues**: Duplicates BeaconData.GetDotCountToBearingTable(). BearingTable is dead code (F34/F35)

#### RadioStoryIntegration.cs (Game Integration)
- **Lines**: 381
- **Responsibility**: Harmony patches for story/locale injection
- **Issues**: **Fragile** — 9 reflection handlers, version-dependent (F23, F68)

#### ComparisonExtensions.cs (Extensions)
- **Lines**: 34
- **Responsibility**: Null/float comparison helpers
- **Issues**: 25 methods that duplicate C# operators

#### MorseTiming.cs
- **Lines**: 46
- **Responsibility**: Morse timing calculations
- **Issues**: None significant

#### NavigationElementType.cs
- **Lines**: 12
- **Responsibility**: Enum for Dot/Dash/Equisignal

### 3.3 Data Layer Components (5 files)

#### BeaconData.cs (Station Database)
- **Lines**: ~373
- **Responsibility**: Hardcoded beacon station data (14 stations)
- **Issues**: Static array, no JSON support, 6 null callsigns (F37), 1 null frequency (F38), 3 stations on 332.5 kHz (F39)

#### DialConfig.cs (Dial Configuration)
- **Lines**: 158
- **Responsibility**: Frequency dial calibration data (5 bands)
- **Issues**: Magic numbers, overlapping band ranges (F20)

#### ConsolCycleConfig.cs (Cycle Configuration)
- **Lines**: ~50
- **Responsibility**: Station cycle timing configuration
- **Issues**: Dead parameter in GetForCycleDuration()

#### StationState.cs (Station State)
- **Lines**: 16
- **Responsibility**: Runtime state for a beacon station
- **Issues**: Mutable struct — copy semantics cause bugs

#### SignalTypes.cs
- **Lines**: 23
- **Responsibility**: Signal entry types and enums

### 3.4 Sound Layer Components (2 files, 5 classes)

#### AudioSynthesisEngine.cs
- **Lines**: ~150
- **Responsibility**: Main audio processing loop
- **Issues**: Debug.Log in audio thread, thread safety, UpdateStationBeats() is dead code (F59)

#### DspPipeline.cs (5 classes in 1 file)
- **Lines**: ~100
- **Classes**:
  - `CarrierGenerator` — Sine wave generation (~40 lines), float→double precision issue (F41)
  - `Atmosphere` — Atmospheric noise (~60 lines)
  - `ReceiverDsp` — AGC/filtering/saturation (~70 lines)
- **Issues**: 3 classes in one file, CarrierGenerator.NextSample() frequency not clamped (F40)

## 4. Dependency Matrix

```
                    UI    Logic   Data   Sound
UI                  -     High    Low    Low
Logic               High  -       High   Low
Data                Low   High    -      Low
Sound               Low   High    Low    -
```

## 5. Entry Points

| Entry Point | Trigger | Flow |
|-------------|---------|------|
| `Main.OnLoaded()` | Game load complete | IUserMod interface → RadioInstaller → SonneController |
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

---

*Document Version: 2.1*

*Last Updated: 2026-10-04 (действующие цепочки §0; исходный аудит ниже сохранён)*
*Corrections: Fixed UBOOT→UBOAT, reflection handler count 8→9, updated footer to 199 problems. See F45-F52, F65-F199.*
