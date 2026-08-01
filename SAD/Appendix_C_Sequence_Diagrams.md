# Appendix C: Sequence Diagrams — RadioNavigation Mod

> **⚠️ Deep Scan Update (2026-08-01):** Sequence diagrams содержат ссылки на несуществующие классы, annotated `⚠ phantom`. Всего **199 проблем** (F01–F199).
> - **F47:** `StationManager` → `SonneController.UpdateStationGains()`; `SignalDecoder` → `SonneController.DecodeCallsignBuffer()`; `PositionTracker` → `SonneController.UpdatePosition()`; `StationStateBuffer` → нет sync; `GameReflectionCache` → `RadioStoryIntegration`
> - **F51:** `Atmosphere.GenerateNoise()` → `Apply(sample, sampleRate)`; `ReceiverDsp.ApplyAGC()` → `Process()`; `CarrierBank` doesn't exist (CarrierGenerator is nested in DspPipeline)
> - **F45:** ADR-002/003/004/005/007 changed «Accepted» → «Deferred»
>
> Полные описания: `refactoring-plan/F45.md`..`F52.md`, `F65.md`..`F199.md`.

## 1. System Initialization

```
┌────────┐  ┌────────┐  ┌────────────┐  ┌──────────────┐  ┌──────────────┐
│  Game  │  │  Main  │  │RadioInstall│  │SonneController│  │RadioStory    │
│        │  │        │  │    er      │  │              │  │Integration   │
└───┬────┘  └───┬────┘  └─────┬──────┘  └──────┬───────┘  └──────┬───────┘
    │           │             │                 │                 │
⚠ F46: Game.OnLoaded() doesn't exist; actual entry via Harmony patches on RadioStory.ContinueStory, Locale.RetrieveString (2 overloads), RadioStory.OnDecisionSelected, RadioStory.OnStopped (5 patches total)
     │ OnLoaded()│             │                 │                 │
     │──────────►│             │                 │                 │
    │           │             │                 │                 │
    │           │ Instantiate()                 │                 │
    │           │────────────►│                 │                 │
    │           │             │                 │                 │
    │           │             │ Create Canvas   │                 │
    │           │             │────────────────►│                 │
    │           │             │                 │                 │
    │           │             │ Create Panel    │                 │
    │           │             │────────────────►│                 │
    │           │             │                 │                 │
    │           │             │ Attach Handlers │                 │
    │           │             │────────────────►│                 │
    │           │             │                 │                 │
    │           │ Awake()     │                 │                 │
    │           │─────────────│────────────────►│                 │
    │           │             │                 │                 │
    │           │             │                 │ Initialize()    │
    │           │             │                 │────────────────►│
    │           │             │                 │                 │
    │           │             │                 │  Init()         │
    │           │             │                 │◄────────────────│
    │           │             │                 │                 │
    │           │             │                 │  Initialize()   │
    │           │             │                 │◄────────────────│
    │           │             │                 │                 │
    │           │             │                 │  Cache Types    │
    │           │             │                 │────────────────►│
    │           │             │                 │                 │
```

## 2. Audio Processing Flow

```
┌────────────┐  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐
│Unity Audio │  │AudioSynthesis│  │⚠ CarrierBank │  │Atmosphere    │
│Thread      │  │Engine        │  │phantom       │  │              │
└─────┬──────┘  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘
       │                │                 │                 │
       │ OnAudioFilterRead()              │                 │
       │───────────────►│                 │                 │
       │                │                 │                 │
       │                │ ProcessAudio()  │                 │
       │                │────────────────►│                 │
       │                │                 │                 │
       │                │                 │ For each station│
       │                │                 │────────────────►│
       │                │                 │                 │
       │                │                 │ GenerateCarrier()│
       │                │                 │◄────────────────│
       │                │                 │                 │
       │                │                 │ ⚠ wrong: should be Atmosphere.Apply(sample, sampleRate)│
       │                │                 │ GenerateNoise() │
       │                │                 │────────────────►│
       │                │                 │                 │
       │                │                 │ Noise samples   │
       │                │                 │◄────────────────│
       │                │                 │                 │
       │                │                 │ ⚠ wrong: should be Atmosphere.Process()│
       │                │                 │ ApplyAGC()      │
       │                │                 │────────────────►│
       │                │                 │                 │
       │                │                 │ Filtered samples│
       │                │                 │◄────────────────│
       │                │                 │                 │
       │                │ Mix all stations│                 │
       │                │◄────────────────│                 │
       │                │                 │                 │
       │ Audio output   │                 │                 │
       │◄───────────────│                 │                 │
       │                │                 │                 │
```

## 3. Navigation Update Cycle

```
┌────────────┐  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐
│Unity Main  │  │SonneController│  │⚠ StationManager││StationScheduler│
│Thread      │  │              │  │phantom       │  │              │
└─────┬──────┘  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘
       │                │                 │                 │
       │ Update()       │                 │                 │
       │───────────────►│                 │                 │
       │                │ ⚠ wrong: should call StationScheduler.Tick() directly│
       │ UpdateStationGains()              │                 │
       │────────────────►│                 │                 │
       │                │                 │                 │
      │                │                 │ For each station│
      │                │                 │────────────────►│
      │                │                 │                 │
      │                │                 │ Tick()          │
      │                │                 │────────────────►│
      │                │                 │                 │
      │                │                 │                 │ Check phase
      │                │                 │                 │
      │                │                 │                 │ Phase.Pause
      │                │                 │                 │──────┐
      │                │                 │                 │      │
      │                │                 │                 │◄─────┘
      │                │                 │                 │
      │                │                 │                 │ Phase.Callsign
      │                │                 │                 │──────┐
      │                │                 │                 │      │
      │                │                 │                 │ Play callsign
      │                │                 │                 │◄─────┘
      │                │                 │                 │
      │                │                 │                 │ Phase.Navigation
      │                │                 │                 │──────┐
      │                │                 │                 │      │
      │                │                 │                 │ Play nav signal
      │                │                 │                 │◄─────┘
      │                │                 │                 │
       │                │                 │ Gain computed   │
       │                │                 │◄────────────────│
       │                │                 │                 │
⚠ F47: Station gains updated (phantom StationManager → SonneController.UpdateStationGains())
       │                │◄────────────────│                 │
       │                │                 │                 │
⚠ F47: UpdateStationNavigation (phantom StationManager → SonneController.UpdateStationNavigation())
       │                │────────────────►│                 │
       │                │                 │                 │
      │                │                 │ Calculate bearing│
      │                │                 │────────────────►│
      │                │                 │                 │
      │                │                 │ Bearing computed │
      │                │                 │◄────────────────│
      │                │                 │                 │
      │                │ Navigation updated                 │
      │                │◄────────────────│                 │
      │                │                 │                 │
```

## 4. Signal Decoding Flow

```
┌────────────┐  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐
│SonneControl│  │⚠ SignalDecoder│  │MorseKeyer    │  │MorseAlphabet │
│ler         │  │phantom       │  │              │  │              │
└─────┬──────┘  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘
      │                │                 │                 │
      │ OnStationElementCompleted()      │                 │
      │───────────────►│                 │                 │
      │                │                 │                 │
      │                │ Add to signalEntries              │
      │                │────────────────►│                 │
      │                │                 │                 │
      │                │                 │ Get element     │
      │                │                 │────────────────►│
      │                │                 │                 │
      │                │                 │ Element data    │
      │                │                 │◄────────────────│
      │                │                 │                 │
      │                │ Check if callsign complete        │
      │                │────────────────►│                 │
      │                │                 │                 │
      │                │                 │ IsComplete()?   │
      │                │                 │──────┐          │
      │                │                 │      │          │
      │                │                 │◄─────┘          │
      │                │                 │                 │
      │                │ Complete signal │                 │
      │                │◄────────────────│                 │
      │                │                 │                 │
⚠ F47: DecodeCallsignBuffer — phantom SignalDecoder → SonneController.DecodeCallsignBuffer()
       │                │────────────────►│                 │
      │                │                 │                 │
      │                │                 │ DecodeElements()│
      │                │                 │────────────────►│
      │                │                 │                 │
      │                │                 │ Decoded text    │
      │                │                 │◄────────────────│
      │                │                 │                 │
      │                │ Decoded callsign│                 │
      │                │◄────────────────│                 │
      │                │                 │                 │
      │                │ GetSignalRichText()               │
      │                │────────────────►│                 │
      │                │                 │                 │
      │                │                 │ Format entries  │
      │                │                 │──────┐          │
      │                │                 │      │          │
      │                │                 │◄─────┘          │
      │                │                 │                 │
      │                │ Rich text       │                 │
      │                │◄────────────────│                 │
      │                │                 │                 │
```

## 5. UI Interaction Flow

```
┌────────────┐  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐
│   Player   │  │CursorHandler │  │RadioHandler  │  │SonneController│
│            │  │              │  │              │  │              │
└─────┬──────┘  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘
      │                │                 │                 │
      │ Rotate dial    │                 │                 │
      │───────────────►│                 │                 │
      │                │                 │                 │
      │                │ OnDrag()        │                 │
      │                │────────────────►│                 │
      │                │                 │                 │
      │                │                 │ Calculate angle │
      │                │                 │──────┐          │
      │                │                 │      │          │
      │                │                 │◄─────┘          │
      │                │                 │                 │
      │                │                 │ Angle to freq   │
      │                │                 │──────┐          │
      │                │                 │      │          │
      │                │                 │◄─────┘          │
      │                │                 │                 │
      │                │                 │ EmitFrequency() │
      │                │                 │────────────────►│
      │                │                 │                 │
      │                │                 │                 │ Set frequency
      │                │                 │                 │──────┐
      │                │                 │                 │      │
      │                │                 │                 │◄─────┘
      │                │                 │                 │
      │                │                 │                 │ Update stations
      │                │                 │                 │──────┐
      │                │                 │                 │      │
      │                │                 │                 │◄─────┘
      │                │                 │                 │
      │                │                 │ Frequency changed│
      │                │                 │◄────────────────│
      │                │                 │                 │
      │                │                 │ Update UI       │
      │                │                 │──────┐          │
      │                │                 │      │          │
      │                │                 │◄─────┘          │
      │                │                 │                 │
```

## 6. Position Fix Calculation

```
┌────────────┐  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐
│SonneControl│  │⚠ PositionTracker│ │PositionFix   │  │BeaconData    │
│ler         │  │phantom       │  │Calculator    │  │              │
└─────┬──────┘  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘
      │                │                 │                 │
⚠ F47: UpdatePosition — phantom PositionTracker → SonneController.UpdatePosition()
       │───────────────►│                 │                 │
      │                │                 │                 │
      │                │ Get measurements│                 │
      │                │──────┐          │                 │
      │                │      │          │                 │
      │                │◄─────┘          │                 │
      │                │                 │                 │
      │                │ For each station│                 │
      │                │────────────────►│                 │
      │                │                 │                 │
      │                │                 │ Get bearing     │
      │                │                 │────────────────►│
      │                │                 │                 │
      │                │                 │ Bearing table   │
      │                │                 │◄────────────────│
      │                │                 │                 │
      │                │                 │ Calculate angle │
      │                │                 │──────┐          │
      │                │                 │      │          │
      │                │                 │◄─────┘          │
      │                │                 │                 │
      │                │ Measurement     │                 │
      │                │◄────────────────│                 │
      │                │                 │                 │
      │                │ Calculate()     │                 │
      │                │────────────────►│                 │
      │                │                 │                 │
      │                │                 │ Weighted least  │
      │                │                 │ squares         │
      │                │                 │──────┐          │
      │                │                 │      │          │
      │                │                 │◄─────┘          │
      │                │                 │                 │
      │                │                 │ Find intersection│
      │                │                 │──────┐          │
      │                │                 │      │          │
      │                │                 │◄─────┘          │
      │                │                 │                 │
      │                │ FixResult       │                 │
      │                │◄────────────────│                 │
      │                │                 │                 │
      │ lastFix updated│                 │                 │
      │◄───────────────│                 │                 │
      │                │                 │                 │
```

## 7. Game Story Integration

```
┌────────────┐  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐
│   Game     │  │RadioStory    │  │⚠ GameReflection││LocaleManager │
│   Story    │  │Integration   │  │Cache phantom │  │              │
└─────┬──────┘  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘
      │                │                 │                 │
      │ InjectDecision()                 │                 │
      │───────────────►│                 │                 │
      │                │                 │                 │
      │                │ Get decisions   │                 │
      │                │────────────────►│                 │
      │                │                 │                 │
⚠ F47: TryGetDecisions, Field access, Method invoke — phantom GameReflectionCache → RadioStoryIntegration
       │                │                 │ TryGetDecisions()│
       │                │                 │──────┐          │
      │                │                 │      │          │
      │                │                 │◄─────┘          │
      │                │                 │                 │
      │                │                 │ Field access    │
      │                │                 │──────┐          │
      │                │                 │      │          │
      │                │                 │◄─────┘          │
      │                │                 │                 │
      │                │ Decisions list  │                 │
      │                │◄────────────────│                 │
      │                │                 │                 │
      │                │ Inject decision │                 │
      │                │────────────────►│                 │
      │                │                 │                 │
      │                │                 │ Method invoke   │
      │                │                 │──────┐          │
      │                │                 │      │          │
      │                │                 │◄─────┘          │
      │                │                 │                 │
      │                │ Decision injected│                │
      │                │◄────────────────│                 │
      │                │                 │                 │
      │                │ NotifyUpdated() │                 │
      │                │────────────────►│                 │
      │                │                 │                 │
      │                │                 │ Method invoke   │
      │                │                 │──────┐          │
      │                │                 │      │          │
      │                │                 │◄─────┘          │
      │                │                 │                 │
      │                │ Updated         │                 │
      │                │◄────────────────│                 │
      │                │                 │                 │
      │                │ GetCurrentLanguage()              │
      │                │──────────────────────────────────►│
      │                │                 │                 │
      │                │                 │ Get language    │
      │                │                 │──────┐          │
      │                │                 │      │          │
      │                │                 │◄─────┘          │
      │                │                 │                 │
      │                │ Language string  │                 │
      │                │◄──────────────────────────────────│
      │                │                 │                 │
```

## 8. Thread Safety Pattern

```
┌────────────┐  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐
│Unity Audio │  │⚠ StationStateBuf││SonneController│  │⚠ StationManager│
│Thread      │  │fer phantom   │  │(Main Thread) │  │phantom       │
└─────┬──────┘  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘
      │                │                 │                 │
⚠ F47: SonneController doesn't call StationManager; it updates StationState[] directly
       │                │                 │ UpdateStationGains()              │
       │                │                 │────────────────►│
       │                │                 │                 │
       │                │                 │                 │ Compute gains
       │                │                 │                 │──────┐
       │                │                 │                 │      │
       │                │                 │                 │◄─────┘
       │                │                 │                 │
⚠ F47: Phantom StationStateBuffer doesn't exist; SonneController writes to StationState.stationGains directly
       │                │                 │ Write to writeBuffer              │
       │                │                 │────────────────►│
      │                │                 │                 │
      │                │                 │                 │ Swap buffers
      │                │                 │                 │──────┐
      │                │                 │                 │      │
      │                │                 │                 │◄─────┘
      │                │                 │                 │
      │ OnAudioFilterRead()              │                 │
      │───────────────►│                 │                 │
      │                │                 │                 │
      │                │ Read from readBuffer              │
      │                │──────┐          │                 │
      │                │      │          │                 │
      │                │◄─────┘          │                 │
      │                │                 │                 │
      │                │ Process audio   │                 │
      │                │──────┐          │                 │
      │                │      │          │                 │
      │                │◄─────┘          │                 │
      │                │                 │                 │
      │ Audio output   │                 │                 │
      │◄───────────────│                 │                 │
      │                │                 │                 │
```

## 9. State Machine Transitions

```
┌─────────────────────────────────────────────────────────────────────┐
│                    StationScheduler State Machine                   │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│     ┌──────────┐                                                    │
│     │  Pause   │                                                    │
│     └────┬─────┘                                                    │
│          │                                                          │
│          │ duration = PauseDuration                                 │
│          ▼                                                          │
│     ┌──────────┐                                                    │
│     │ Callsign │                                                    │
│     └────┬─────┘                                                    │
│          │                                                          │
│          │ duration = CallsignDuration                              │
│          ▼                                                          │
│     ┌──────────┐                                                    │
│     │PreNavPause│                                                   │
│     └────┬─────┘                                                    │
│          │                                                          │
│          │ duration = 3000ms (PreNavPauseMs)                      │
│          ▼                                                          │
│     ┌──────────┐                                                    │
│     │Navigation│                                                   │
│     └────┬─────┘                                                    │
│          │                                                          │
│          │ duration = NavigationDuration                            │
│          ▼                                                          │
│     ┌──────────┐                                                    │
│     │PostNavSilence│                                                │
│     └────┬─────┘                                                    │
│          │                                                          │
│          │ duration = 1000ms (PostNavSilenceMs)                     │
│          ▼                                                          │
│     ┌──────────┐                                                    │
│     │  Pause   │ ◄── Cycle repeats                                  │
│     └──────────┘                                                    │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

## 10. Error Handling Flow

```
┌────────────┐  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐
│   Main     │  │RadioStory    │  │⚠ GameReflection││   Logger     │
│            │  │Integration   │  │Cache phantom │  │              │
└─────┬──────┘  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘
      │                │                 │                 │
      │ OnLoaded()     │                 │                 │
      │───────────────►│                 │                 │
      │                │                 │                 │
      │                │ Init()          │                 │
      │                │────────────────►│                 │
      │                │                 │                 │
⚠ F47: Try to resolve type, Type not found, Fallback, Cache type — all phantom GameReflectionCache → RadioStoryIntegration
       │                │                 │ Try to resolve  │
       │                │                 │ type            │
       │                │                 │──────┐          │
       │                │                 │      │          │
       │                │                 │      │ Type not  │
       │                │                 │      │ found     │
       │                │                 │◄─────┘          │
       │                │                 │                 │
       │                │                 │ LogWarning()    │
       │                │                 │────────────────►│
       │                │                 │                 │
       │                │                 │ Fallback to     │
       │                │                 │ Assembly-CSharp │
       │                │                 │──────┐          │
      │                │                 │      │          │
      │                │                 │◄─────┘          │
      │                │                 │                 │
      │                │                 │ Try again       │
      │                │                 │──────┐          │
      │                │                 │      │          │
      │                │                 │      │ Success   │
      │                │                 │◄─────┘          │
      │                │                 │                 │
      │                │                 │ Cache type      │
      │                │                 │──────┐          │
      │                │                 │      │          │
      │                │                 │◄─────┘          │
      │                │                 │                 │
      │                │ Init complete   │                 │
      │                │◄────────────────│                 │
      │                │                 │                 │
      │                │ LogInfo()       │                 │
      │                │────────────────►│                 │
      │                │                 │                 │
```
