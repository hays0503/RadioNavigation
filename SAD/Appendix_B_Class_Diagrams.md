# Appendix B: Class Diagrams — RadioNavigation Mod

> **⚠️ Deep Scan Update (2026-07-30):** Диаграммы классов содержат критические расхождения, частично исправлены:
> - **F47:** Несуществующие классы в target diagrams — помечены `⚠ proposed`. `GameStrings`, `TickScheduler`, `DspPipeline` удалены
> - **F51 (исправлено):** `ProcessAudio` сигнатура исправлена на 9 params
> - **F48:** Array sizes: 4→14. **F52:** Пропущен `SignalRichTextFormatter`
> - Неисправлено: `Atmosphere.GenerateNoise(data)` реальность — `Apply(sample, sampleRate)`
> - Неисправлено: `ReceiverDsp` — 3 метода, реальность — один `Process`
> - Неисправлено: `RadioHandler.EmitFrequency(freq, zone)` — private, 0 params
> - Неисправлено: `InformationPanelHandler.uiElements` — поле не существует
>
> Полные описания: `refactoring-plan/F45.md`..`F52.md`, `F65.md`..`F76.md`

## 1. Current Class Diagram

### 1.1 Core Classes

```
┌─────────────────────────────────────────────────────────────────────┐
│                         SonneController                             │
├─────────────────────────────────────────────────────────────────────┤
│ - stations: StationState[]                                          │
│ - schedulers: StationScheduler[]                                    │
│ - keyers: MorseKeyer[]                                              │
│ - carriers: CarrierGenerator[]                                      │
│ - atmosphere: Atmosphere                                            │
│ - receiverDsp: ReceiverDsp                                          │
│ - signalEntries: List<SignalEntry>                                  │
│ - morseDecoderBuffer: List<char>                                    │
│ - decodedText: string                                               │
│ - measurements: List<Measurement>                                   │
│ - lastFix: FixResult                                                │
│ - currentFrequency: float                                           │
│ - outputVolume: float                                               │
│ - feedbackVolume: float                                             │
│ - silentClip: AudioClip                                             │
├─────────────────────────────────────────────────────────────────────┤
│ + Awake(): void                                                     │
│ + OnDestroy(): void                                                 │
│ + Update(): void                                                    │
│ + OnAudioFilterRead(data: float[], channels: int): void            │
│ + SetFrequency(freq: float): void                                   │
│ + SetVolume(vol: float): void                                       │
│ - UpdateStationGains(): void                                        │
│ - UpdateStationNavigation(): void                                   │
│ - ProcessAudio(data: float[], channels: int): void                 │
│ - InitializeAudio(): void                                           │
│ - CreateSilentClip(): AudioClip                                     │
│ - UpdatePosition(): void                                            │
│ - CalculateBearing(station: BeaconStation): float                   │
│ - GetPositionFix(): FixResult                                       │
│ - OnStationElementCompleted(station: int, element: int): void      │
│ - OnStationSequenceFinished(station: int): void                     │
│ - DecodeCallsignBuffer(): string                                    │
│ - GetSignalRichText(): string                                       │
│ - GetDecodedText(): string                                          │
│ - ClearSignalBuffer(): void                                         │
│ - GetStationDiagnostics(): string                                   │
│ - FormatDiagnosticString(): string                                  │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              │ uses
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│                       StationScheduler                              │
├─────────────────────────────────────────────────────────────────────┤
│ - currentPhase: Phase                                               │
│ - elapsedTicks: int                                                 │
│ - config: ConsolCycleConfig                                         │
│ - bearingTable: int[]                                               │
│ - currentConsolPattern: NavigationPattern                           │
├─────────────────────────────────────────────────────────────────────┤
│ + Configure(config: ConsolCycleConfig): void                        │
│ + Tick(): void                                                      │
│ + GetCurrentElement(): NavigationElement                            │
│ + GetElementCount(): int                                            │
│ CurrentPhase: Phase { get; }                                        │
│ ElapsedTicks: int { get; }                                          │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              │ uses
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│                          MorseKeyer                                 │
├─────────────────────────────────────────────────────────────────────┤
│ - currentElement: int                                               │
│ - elementTicks: int                                                 │
│ - pattern: NavigationPattern                                        │
│ - scheduler: StationScheduler                                       │
├─────────────────────────────────────────────────────────────────────┤
│ + Configure(pattern: NavigationPattern, scheduler: StationScheduler): void│
│ + Tick(): void                                                      │
│ + AdvanceElement(): void                                            │
│ + GetCurrentSoundTicks(): int                                       │
│ + GetCurrentSilenceTicks(): int                                     │
│ + IsComplete(): bool                                                │
└─────────────────────────────────────────────────────────────────────┘
```

### 1.2 Data Classes

```
┌─────────────────────────────────────────────────────────────────────┐
│                        BeaconData                                   │
├─────────────────────────────────────────────────────────────────────┤
│ + Stations: BeaconStation[] { get; }                               │
├─────────────────────────────────────────────────────────────────────┤
│ + static GetDotCountToBearingTable(station: BeaconStation): int[]  │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              │ contains
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│                       BeaconStation                                 │
├─────────────────────────────────────────────────────────────────────┤
│ + Name: string                                                      │
│ + Latitude: float                                                   │
│ + Longitude: float                                                  │
│ + BearingTable: int[]                                               │
│ + Callsign: string                                                  │
│ + Color: string                                                     │
├─────────────────────────────────────────────────────────────────────┤
│ + GetDotCountToBearingTable(): int[]                                │
│ + GetCallsignPattern(): CallsignPattern                             │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                        DialConfig                                   │
├─────────────────────────────────────────────────────────────────────┤
│ - bands: SortedList<float, float>                                   │
│ - referencePoints: Dictionary<string, float[]>                      │
├─────────────────────────────────────────────────────────────────────┤
│ + static AngleToFrequency(angle: float): float                     │
│ + static FrequencyToAngle(freq: float): float                      │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                      StationState                                   │
├─────────────────────────────────────────────────────────────────────┤
│ + Index: int                                                        │
│ + Station: BeaconStation                                            │
│ + Gain: float                                                       │
│ + BeatFrequency: float                                              │
│ + NavigationStartTime: float                                        │
│ + IsActive: bool                                                    │
│ + ConsolPattern: NavigationPattern                                  │
│ + CallsignPattern: CallsignPattern                                  │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                     ConsolCycleConfig                               │
├─────────────────────────────────────────────────────────────────────┤
│ + CycleDuration: int                                                │
│ + PauseDuration: int                                                │
│ + CallsignDuration: int                                             │
│ + NavigationDuration: int                                           │
│ + PostNavDuration: int                                              │
├─────────────────────────────────────────────────────────────────────┤
│ + static GetForCycleDuration(duration: int): ConsolCycleConfig     │
└─────────────────────────────────────────────────────────────────────┘
```

### 1.3 Sound Classes

```
┌─────────────────────────────────────────────────────────────────────┐
│                    AudioSynthesisEngine                             │
├─────────────────────────────────────────────────────────────────────┤
│ - sampleRate: int                                                   │
│ - carriers: CarrierGenerator[]                                      │
│ - atmosphere: Atmosphere                                            │
│ - receiverDsp: ReceiverDsp                                          │
├─────────────────────────────────────────────────────────────────────┤
│ + ProcessAudio(data: float[], channels: int, stations: StationState[],  │
│ │             schedulers: StationScheduler[], keyers: MorseKeyer[],     │
│ │             carriers: CarrierGenerator[], radioOn: bool,              │
│ │             currentZoneIndex: int, sonneZoneIndex: int): void         │
│ + Initialize(sampleRate: int): void                                 │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              │ uses
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│                      CarrierGenerator                               │
├─────────────────────────────────────────────────────────────────────┤
│ - frequency: float                                                  │
│ - phase: float                                                      │
│ - sampleRate: int                                                   │
├─────────────────────────────────────────────────────────────────────┤
│ + NextSample(): float                                               │
│ + SetFrequency(freq: float): void                                   │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                        Atmosphere                                   │
├─────────────────────────────────────────────────────────────────────┤
│ - random: System.Random                                             │
├─────────────────────────────────────────────────────────────────────┤
│ + GenerateNoise(data: float[]): void                                │
│ + GenerateImpulse(): float                                          │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                       ReceiverDsp                                   │
├─────────────────────────────────────────────────────────────────────┤
│ - agcGain: float                                                    │
│ - filterState: float                                                │
├─────────────────────────────────────────────────────────────────────┤
│ + ApplyAGC(data: float[]): void                                     │
│ + ApplyFilter(data: float[]): void                                  │
│ + ApplySaturation(data: float[]): void                              │
└─────────────────────────────────────────────────────────────────────┘
```

### 1.4 UI Classes

```
┌─────────────────────────────────────────────────────────────────────┐
│                       RadioHandler                                  │
├─────────────────────────────────────────────────────────────────────┤
│ - currentFrequency: float                                           │
│ - sonneController: SonneController                                  │
├─────────────────────────────────────────────────────────────────────┤
│ + static OnFrequencyChanged: Action<float, int>                    │
│ + SetFrequency(freq: float): void                                   │
│ + EmitFrequency(freq: float, zone: int): void                      │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                       ToggleHandler                                 │
├─────────────────────────────────────────────────────────────────────┤
│ + static IsRadioOn: bool { get; set; }                             │
│ + static IsInteractable: bool { get; set; }                        │
├─────────────────────────────────────────────────────────────────────┤
│ + OnToggleChanged(isOn: bool): void                                 │
│ + SetGroundActive(active: bool): void                               │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                       ZoneHandler                                   │
├─────────────────────────────────────────────────────────────────────┤
│ - currentIndex: int                                                 │
│ + OnZoneChanged: Action<int>                                        │
├─────────────────────────────────────────────────────────────────────┤
│ + NextZone(): void                                                  │
│ + PreviousZone(): void                                              │
│ + SetZone(index: int): void                                         │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                      InformationPanelHandler                        │
├─────────────────────────────────────────────────────────────────────┤
│ - sonneController: SonneController                                  │
│ - zoneHandler: ZoneHandler                                          │
│ - groundHandler: GroundHandler                                      │
│ - uiElements: Dictionary<string, GameObject>                        │
├─────────────────────────────────────────────────────────────────────┤
│ + Start(): void                                                     │
│ + RefreshAll(): void                                                │
│ + UpdateSignalDisplay(): void                                       │
│ + UpdateStationInfo(): void                                         │
│ + FindClosestStation(): BeaconStation                               │
│ + GetZoneIndex(): int                                               │
│ + IsGroundActive(): bool                                            │
│ - CreateLabel(text: string): Text                                   │
│ - CreateSeparator(): Image                                          │
│ - EnsureLayout(go: GameObject): VerticalLayoutGroup                 │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                      SpinnerHandlerBase                             │
├─────────────────────────────────────────────────────────────────────┤
│ - currentValue: float                                               │
│ - targetValue: float                                                │
│ - sensitivity: float                                                │
├─────────────────────────────────────────────────────────────────────┤
│ + OnEnable(): void                                                  │
│ + OnDisable(): void                                                 │
│ + OnPointerDown(event: PointerEventData): void                      │
│ + OnDrag(event: PointerEventData): void                             │
│ - IsActiveDrag(): bool                                              │
│ - UpdateValue(): void                                               │
└─────────────────────────────────────────────────────────────────────┘
                              ▲
                              │ inherits
                              │
        ┌─────────────────────┴─────────────────────┐
        │                                           │
┌───────┴─────────┐                     ┌───────────┴─────────┐
│VolumeSpinnerHdlr│                     │FeedbackSpinnerHdlr  │
└─────────────────┘                     └─────────────────────┘
```

## 2. Target Class Diagram

### 2.1 Decomposed SonneController

```
┌─────────────────────────────────────────────────────────────────────┐
│                       RadioController                               │
├─────────────────────────────────────────────────────────────────────┤
│ - stationManager: StationManager                                    │
│ - signalDecoder: SignalDecoder                                      │
│ - positionTracker: PositionTracker                                  │
│ - audioController: AudioController                                  │
│ - radioContext: IRadioState                                         │
├─────────────────────────────────────────────────────────────────────┤
│ + Awake(): void                                                     │
│ + Update(): void                                                    │
│ + OnDestroy(): void                                                 │
│ - InitializeSubsystems(): void                                      │
│ - CoordinateUpdates(): void                                         │
└─────────────────────────────────────────────────────────────────────┘
        │           │           │           │
        │           │           │           │
        ▼           ▼           ▼           ▼
┌───────────────┐ ┌───────────────┐ ┌───────────────┐ ┌───────────────┐
│StationManager │ │SignalDecoder  │ │PositionTracker│ │AudioController│
├───────────────┤ ├───────────────┤ ├───────────────┤ ├───────────────┤
│- stations[]   │ │- signalEntries│ │- measurements │ │- audioPipeline│
│- config       │ │- decoderBuffer│ │- lastFix      │ │- sampleRate   │
├───────────────┤ ├───────────────┤ ├───────────────┤ ├───────────────┤
│+ UpdateGains()│ │+ OnElement()  │ │+ Update()     │ │+ Process()    │
│+ UpdateNav()  │ │+ Decode()     │ │+ GetFix()     │ │+ Initialize() │
│+ SetActive()  │ │+ GetRichText()│ │+ Calculate()  │ │+ CreateClip() │
└───────────────┘ └───────────────┘ └───────────────┘ └───────────────┘
```

### 2.2 Service Interfaces

```
┌─────────────────────────────────────────────────────────────────────┐
│                       IRadioState                                   │
├─────────────────────────────────────────────────────────────────────┤
│ + IsRadioOn: bool { get; }                                         │
│ + IsInteractable: bool { get; }                                    │
│ + GroundActive: bool { get; }                                      │
│ + CurrentFrequency: float { get; }                                 │
│ + CurrentZone: int { get; }                                        │
├─────────────────────────────────────────────────────────────────────┤
│ + event OnRadioStateChanged: Action<bool>                          │
│ + event OnFrequencyChanged: Action<float, int>                     │
│ + event OnZoneChanged: Action<int>                                 │
└─────────────────────────────────────────────────────────────────────┘
                              ▲
                              │ implements
                              │
┌─────────────────────────────────────────────────────────────────────┐
│                       RadioContext                                  │
├─────────────────────────────────────────────────────────────────────┤
│ - isRadioOn: bool                                                  │
│ - isInteractable: bool                                             │
│ - groundActive: bool                                               │
│ - currentFrequency: float                                          │
│ - currentZone: int                                                 │
├─────────────────────────────────────────────────────────────────────┤
│ + SetRadioOn(on: bool): void                                       │
│ + SetInteractable(interactable: bool): void                        │
│ + SetGroundActive(active: bool): void                              │
│ + SetFrequency(freq: float, zone: int): void                      │
│ + SetZone(zone: int): void                                         │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                     IStationDataSource                              │
├─────────────────────────────────────────────────────────────────────┤
│ + GetAll(): BeaconStation[]                                         │
│ + GetByName(name: string): BeaconStation?                          │
│ + GetByIndex(index: int): BeaconStation?                           │
└─────────────────────────────────────────────────────────────────────┘
                              ▲
                              │ implements
                              │
        ┌─────────────────────┴─────────────────────┐
        │                                           │
┌───────┴───────────────┐             ┌─────────────┴─────────────┐
│HardcodedStationRepo   │             │JsonStationRepository      │
├───────────────────────┤             ├───────────────────────────┤
│+ GetAll(): Station[]  │             │+ GetAll(): Station[]      │
│+ GetByName(): Station?│             │+ GetByName(): Station?    │
│+ GetByIndex(): Station│             │+ GetByIndex(): Station    │
└───────────────────────┘             └───────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                       IAudioOutput                                  │
├─────────────────────────────────────────────────────────────────────┤
│ + OutputVolume: float { get; set; }                                │
│ + FeedbackVolume: float { get; set; }                              │
│ + ProcessAudio(data: float[], channels: int, stations: StationState[]): void│
└─────────────────────────────────────────────────────────────────────┘
                              ▲
                              │ implements
                              │
┌─────────────────────────────────────────────────────────────────────┐
│                       AudioPipeline                                 │
├─────────────────────────────────────────────────────────────────────┤
│ - carrierBank: CarrierBank                                          │
│ - atmosphere: Atmosphere                                            │
│ - receiverDsp: ReceiverDsp                                          │
├─────────────────────────────────────────────────────────────────────┤
│ + ProcessAudio(data: float[], channels: int, stations: StationState[]): void│
│ + Initialize(sampleRate: int): void                                 │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                       IGameServices                                 │
├─────────────────────────────────────────────────────────────────────┤
│ + GetCurrentLanguage(): string                                      │
│ + RetrieveString(key: string): string                               │
│ + InjectDecision(story: object, index: int, decision: object): void│
│ + NotifyUpdated(story: object): void                                │
└─────────────────────────────────────────────────────────────────────┘
                              ▲
                              │ implements
                              │
┌─────────────────────────────────────────────────────────────────────┐
│                     GameReflectionCache                             │
├─────────────────────────────────────────────────────────────────────┤
│ - cache: TypeCache                                                  │
│ - localeType: Type                                                  │
│ - storyType: Type                                                   │
├─────────────────────────────────────────────────────────────────────┤
│ + GetCurrentLanguage(): string                                      │
│ + RetrieveString(key: string): string                               │
│ + InjectDecision(story: object, index: int, decision: object): void│
│ + NotifyUpdated(story: object): void                                │
│ - InitializeCache(): void                                           │
│ - FallbackReflection(): void                                        │
└─────────────────────────────────────────────────────────────────────┘
```

### 2.3 Decomposed InformationPanelHandler

```
┌─────────────────────────────────────────────────────────────────────┐
│                    InformationPanelHandler                          │
├─────────────────────────────────────────────────────────────────────┤
│ - uiElementFactory: UIElementFactory                                │
│ - stationInfoDisplay: StationInfoDisplay                            │
│ - stationFinder: StationFinder                                      │
│ - radioContext: IRadioState                                         │
├─────────────────────────────────────────────────────────────────────┤
│ + Start(): void                                                     │
│ + RefreshAll(): void                                                │
│ - SubscribeToEvents(): void                                         │
│ - UnsubscribeFromEvents(): void                                     │
└─────────────────────────────────────────────────────────────────────┘
        │           │           │
        │           │           │
        ▼           ▼           ▼
┌───────────────┐ ┌───────────────┐ ┌───────────────┐
│UIElementFactory│ │StationInfoDisp│ │StationFinder  │
├───────────────┤ ├───────────────┤ ├───────────────┤
│- gameObject   │ │- sonneCtrl    │ │- stationData  │
├───────────────┤ ├───────────────┤ ├───────────────┤
│+ CreateLabel()│ │+ RefreshAll() │ │+ FindClosest()│
│+ CreateSep()  │ │+ UpdateInfo() │ │+ GetZone()    │
│+ CreateImage()│ │+ UpdateSignal()│ │+ IsGround()  │
│+ CreateText() │ │+ UpdatePattern│ │               │
│+ EnsureLayout()│ │               │ │               │
└───────────────┘ └───────────────┘ └───────────────┘
```

## 3. Relationship Diagram

### 3.1 Current Relationships

```
SonneController ──────┬──── uses ──── StationScheduler
                      │
                      ├──── uses ──── MorseKeyer
                      │
                      ├──── uses ──── CarrierGenerator
                      │
                      ├──── uses ──── AudioSynthesisEngine
                      │
                      ├──── uses ──── BeaconData
                      │
                      ├──── uses ──── DialConfig
                      │
                      └──── uses ──── ConsolCycleConfig

InformationPanelHandler ──┬──── uses ──── SonneController
                          │
                          ├──── uses ──── ZoneHandler
                          │
                          └──── uses ──── GroundHandler

RadioHandler ─────────────┬──── uses ──── SonneController
                          │
                          └──── uses ──── ToggleHandler

CursorHandler ────────────┬──── uses ──── RadioHandler (static)
                          │
                          └──── uses ──── ToggleHandler (static)
```

### 3.2 Target Relationships

```
RadioController ────────┬──── uses ──── StationManager
                        │
                        ├──── uses ──── SignalDecoder
                        │
                        ├──── uses ──── PositionTracker
                        │
                        ├──── uses ──── AudioController
                        │
                        └──── uses ──── IRadioState

StationManager ─────────┬──── uses ──── IStationDataSource
                        │
                        └──── uses ──── StationScheduler

SignalDecoder ──────────┬──── uses ──── MorseKeyer
                        │
                        └──── uses ──── MorseAlphabet

PositionTracker ────────┬──── uses ──── PositionFixCalculator
                        │
                        └──── uses ──── IStationDataSource

AudioController ────────┬──── uses ──── IAudioOutput
                        │
                        └──── uses ──── CarrierBank

InformationPanelHandler ┬──── uses ──── UIElementFactory
                        │
                        ├──── uses ──── StationInfoDisplay
                        │
                        ├──── uses ──── StationFinder
                        │
                        └──── uses ──── IRadioState
```
