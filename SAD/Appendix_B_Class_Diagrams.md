# Appendix B: Class Diagrams — RadioNavigation Mod

> **⚠️ Deep Scan Update (2026-08-01):** Диаграммы классов содержат расхождения, частично исправлены:
> - **F47:** ⚠ proposed классы в target diagrams
> - **F51 (исправлено):** `Atmosphere.Apply(sample, sampleRate)` (было `GenerateNoise`)
> - **F51 (исправлено):** `ReceiverDsp.Process()` (было 3 метода: ApplyAGC/ApplyFilter/ApplySaturation)
> - **F51 (исправлено):** `ProcessAudio` сигнатура: 9 params
> - **F51 (частично):** `RadioHandler.EmitFrequency` — private, 0 params
> - **F48:** Array sizes: 4→14
> - **F50:** LOC: SonneController 506→508
> - **F52:** Добавлен `SignalRichTextFormatter`
> - **F158:** `RadioHandler.sonneController` — dead field (marked)
> - **F160:** `InformationPanelHandler.uiElements` — не существует (marked)
>
> Полные описания: `refactoring-plan/F45.md`..`F52.md`, `F65.md`..`F199.md`.

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
│ - ProcessAudio(data: float[], channels: int,                        │
│   StationState[] stations, StationScheduler[] schedulers,          │
│   MorseKeyer[] keyers, CarrierGenerator[] carriers,                │
│   bool radioOn, int currentZoneIndex, int sonneZoneIndex): void    │
│ - InitializeAudio(): void                                           │
│ - CreateSilentClip(): AudioClip                                     │
│ - UpdatePosition(latitude: float, longitude: float): void           │
│ - CalculateBearing(lat1: float, lon1: float,                       │
│   lat2: float, lon2: float): float [static]                        │
│ - OnStationElementCompleted(stationIndex: int,                     │
│   elemType: NavigationElementType, isCallsign: bool): void         │
│ - OnStationSequenceFinished(stationIndex: int): void                │
│ - DecodeCallsignBuffer(stationIndex: int): void                    │
│ - GetSignalRichText(): string                                       │
│ - GetDecodedText(): string                                          │
│ - ClearSignalBuffer(): void                                         │
│ + GetStationDiagnostics(index: int): StationDiagnostics             │
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
│                        Atmosphere  (nested class)                    │
├─────────────────────────────────────────────────────────────────────┤
│ - random: System.Random                                             │
├─────────────────────────────────────────────────────────────────────┤
│ + Apply(data: float[], sampleRate: int): void  [F51: was GenerateNoise]│
│ + GenerateImpulse(): float                                          │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                       ReceiverDsp  (nested class)                    │
├─────────────────────────────────────────────────────────────────────┤
│ - agcGain: float                                                    │
│ - filterState: float                                                │
├─────────────────────────────────────────────────────────────────────┤
│ + Process(data: float[], sampleRate: int): void  [F51: was ApplyAGC/ApplyFilter/ApplySaturation]│
│ + EnsureInitialized(sampleRate: int): void                          │
└─────────────────────────────────────────────────────────────────────┘
```

### 1.4 UI Classes

```
┌─────────────────────────────────────────────────────────────────────┐
│                       RadioHandler                                  │
├─────────────────────────────────────────────────────────────────────┤
│ - currentFrequency: float                                           │
│ ⚠ - sonneController: SonneController  [DEAD — unused, F158]        │
├─────────────────────────────────────────────────────────────────────┤
│ + static OnFrequencyChanged: Action<float, int>                    │
│ + SetFrequency(freq: float): void                                   │
│ ⚠ private EmitFrequency(): void  [F51: was (freq: float, zone: int), 2 params → 0 params]│
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
│ ⚠ - uiElements: Dictionary<string, GameObject>  [DOES NOT EXIST — F160]  │
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

## 2. Target Class Diagram ⚠ proposed (NOT implemented — ADR-002/003 Deferred, F02/F03/F04)

### 2.1 Decomposed SonneController ⚠ proposed (F02)

```
┌─────────────────────────────────────────────────────────────────────┐
│                       ⚠ RadioController [NOT created]               │
├─────────────────────────────────────────────────────────────────────┤
│ - ⚠ stationManager: StationManager     [NOT created]                │
│ - ⚠ signalDecoder: SignalDecoder     [NOT created]                │
│ - ⚠ positionTracker: PositionTracker  [NOT created]                │
│ - ⚠ audioController: AudioController   [NOT created]                │
│ - radioContext: IRadioState           [⚠ proposed]                  │
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
│⚠ StationManager│ │⚠ SignalDecoder │ │⚠ PositionTrk │ │⚠ AudioCtrlr   │
│ [NOT created] │ │ [NOT created] │ │ [NOT created] │ │ [NOT created] │
│ (F02, F47)     │ │ (F02, F47)    │ │ (F02, F47)     │ │ (F02, F47)    │
├───────────────┤ ├───────────────┤ ├───────────────┤ ├───────────────┤
│- stations[]   │ │- signalEntries│ │- measurements │ │- audioOutput  │
│- config       │ │- decoderBuffer│ │- lastFix      │ │- config       │
├───────────────┤ ├───────────────┤ ├───────────────┤ ├───────────────┤
│+ UpdateGains()│ │+ OnElement()  │ │+ Update()     │ │+ Process()    │
│+ UpdateNav()  │ │+ Decode()     │ │+ GetFix()     │ │+ Initialize() │
│+ SetActive()  │ │+ GetRichText()│ │+ Calculate()  │ │+ CreateClip() │
└───────────────┘ └───────────────┘ └───────────────┘ └───────────────┘
```

### 2.2 Service Interfaces ⚠ proposed (NOT implemented — ADR-004 Deferred, F04)

```
┌─────────────────────────────────────────────────────────────────────┐
│                       ⚠ IRadioState [NOT created]                   │
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
                               ▲  [⚠ proposed, not implemented]
                               │ implements
                               │
┌─────────────────────────────────────────────────────────────────────┐
│                       ⚠ RadioContext [NOT created]                  │
├─────────────────────────────────────────────────────────────────────┤
│ - isRadioOn: bool (actual: ToggleHandler static)                   │
│ - isInteractable: bool (actual: ToggleHandler static)              │
│ - groundActive: bool (actual: GroundHandler static)                │
│ - currentFrequency: float (actual: RadioHandler instance)            │
│ - currentZone: int (actual: ZoneHandler private)                   │
├─────────────────────────────────────────────────────────────────────┤
│ + SetRadioOn(on: bool): void                                       │
│ + SetInteractable(interactable: bool): void                        │
│ + SetGroundActive(active: bool): void                              │
│ + SetFrequency(freq: float, zone: int): void                      │
│ + SetZone(zone: int): void                                         │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                       ⚠ IStationDataSource [NOT created]            │
├─────────────────────────────────────────────────────────────────────┤
│ + GetAll(): BeaconStation[]                                         │
│ + GetByName(name: string): BeaconStation?                          │
│ + GetByIndex(index: int): BeaconStation?                           │
└─────────────────────────────────────────────────────────────────────┘
                               ▲  [⚠ proposed, not implemented]
                               │ implements
                               │
         ┌─────────────────────┴─────────────────────┐
         │                                           │
┌───────────────┐ ┌─────────────────────────────┐
│⚠ Hardcoded  │ │⚠ JsonStationRepository        │
│StationRepo   │ │ [NOT created]                 │
│[NOT created] │ │                             │
├───────────────┤ ├─────────────────────────────┤
│+ GetAll()    │ │+ GetAll()                    │
│+ GetByName() │ │+ GetByName()                 │
│+ GetByIndex()│ │+ GetByIndex()                │
└───────────────┘ └─────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                       ⚠ IAudioOutput [NOT created]                  │
├─────────────────────────────────────────────────────────────────────┤
│ + OutputVolume: float { get; set; }                                │
│ + FeedbackVolume: float { get; set; }                              │
│ + ProcessAudio(data: float[], channels: int, StationState[]): void│
└─────────────────────────────────────────────────────────────────────┘
                               ▲  [⚠ proposed, not implemented]
                               │ implements
                               │
┌─────────────────────────────────────────────────────────────────────┐
│                       ⚠ AudioPipeline [NOT created]                  │
├─────────────────────────────────────────────────────────────────────┤
│ - ⚠ carrierBank: CarrierBank [NOT created]                         │
│ - ⚠ atmosphere: Atmosphere (exists as nested)                      │
│ - ⚠ receiverDsp: ReceiverDsp (exists as nested)                  │
├─────────────────────────────────────────────────────────────────────┤
│ + ProcessAudio(...): void                                          │
│ + Initialize(sampleRate: int): void                                │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                       ⚠ IGameServices [NOT created]                 │
├─────────────────────────────────────────────────────────────────────┤
│ + GetCurrentLanguage(): string                                      │
│ + RetrieveString(key: string): string                               │
│ + InjectDecision(story, index, decision): void                      │
│ + NotifyUpdated(story): void                                        │
└─────────────────────────────────────────────────────────────────────┘
                               ▲  [⚠ proposed, not implemented]
                               │ implements
                               │
┌─────────────────────────────────────────────────────────────────────┐
│                       ⚠ GameReflectionCache [NOT created]            │
├─────────────────────────────────────────────────────────────────────┤
│ - cache: TypeCache                                                  │
│ - localeType: Type                                                  │
│ - storyType: Type                                                   │
├─────────────────────────────────────────────────────────────────────┤
│ + GetCurrentLanguage(): string                                      │
│ + RetrieveString(key: string): string                               │
│ + InjectDecision(story, index, decision): void                      │
│ + NotifyUpdated(story: void): void                                  │
│ - InitializeCache(): void                                           │
│ - FallbackReflection(): void                                        │
└─────────────────────────────────────────────────────────────────────┘
```

### 2.3 Decomposed InformationPanelHandler ⚠ proposed (F03)

```
┌─────────────────────────────────────────────────────────────────────┐
│                    ⚠ InformationPanelHandler (reduced)              │
├─────────────────────────────────────────────────────────────────────┤
│ ⚠ - uiElementFactory: UIElementFactory [NOT created]              │
│ ⚠ - stationInfoDisplay: StationInfoDisplay [NOT created]          │
│ ⚠ - stationFinder: StationFinder [NOT created]                    │
│ - radioContext: IRadioState [⚠ proposed]                          │
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
│⚠ UIElementFac│ │⚠ StationInfo  │ │⚠ StationFinder│
│ [NOT created] │ │ [NOT created] │ │ [NOT created] │
│ (F03, F47)     │ │ (F03, F47)    │ │ (F03, F47)     │
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

### 3.2 Target Relationships (⚠ proposed — not implemented, F47)

```
⚠ RadioController [NOT created] ───────────┬──── uses ──── ⚠ StationManager [NOT created]
                         │
                         ├──── uses ──── ⚠ SignalDecoder [NOT created]
                         │
                         ├──── uses ──── ⚠ PositionTracker [NOT created]
                         │
                         ├──── uses ──── ⚠ AudioController [NOT created]
                         │
                         └──── uses ──── IRadioState

⚠ StationManager [NOT created] ───────┬──── uses ──── IStationDataSource
                         │
                         └──── uses ──── StationScheduler

⚠ SignalDecoder [NOT created] ────────┬──── uses ──── MorseKeyer
                         │
                         └──── uses ──── MorseAlphabet

⚠ PositionTracker [NOT created] ──────┬──── uses ──── PositionFixCalculator
                         │
                         └──── uses ──── IStationDataSource

⚠ AudioController [NOT created] ──────┬──── uses ──── IAudioOutput
                         │
                         └──── uses ──── ⚠ CarrierBank [NOT created]

InformationPanelHandler ──┬──── uses ──── ⚠ UIElementFactory [NOT created]
                         │
                         ├──── uses ──── ⚠ StationInfoDisplay [NOT created]
                         │
                         ├──── uses ──── ⚠ StationFinder [NOT created]
                         │
                         └──── uses ──── IRadioState
```

---

*Document Version: 2.0*  
*Last Updated: 2026-08-01*  
*Updated: All target/proposed classes annotated `⚠ NOT created` (F47). API signatures corrected: `Atmosphere.Apply(sample, sampleRate)` (F51), `ReceiverDsp.Process()` (F51), `RadioHandler.EmitFrequency` private 0-params (F51). Dead fields marked (F158, F160). LOC corrected 506→508 (F50). All target diagrams updated to reflect actual current API.*
