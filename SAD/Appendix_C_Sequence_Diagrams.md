# Appendix C: Sequence Diagrams — RadioNavigation Mod

## 1. System Initialization

```
┌────────┐  ┌────────┐  ┌────────────┐  ┌──────────────┐  ┌──────────────┐
│  Game  │  │  Main  │  │RadioInstall│  │SonneController│  │RadioStory    │
│        │  │        │  │    er      │  │              │  │Integration   │
└───┬────┘  └───┬────┘  └─────┬──────┘  └──────┬───────┘  └──────┬───────┘
    │           │             │                 │                 │
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
│Unity Audio │  │AudioSynthesis│  │CarrierBank   │  │Atmosphere    │
│Thread      │  │Engine        │  │              │  │              │
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
      │                │                 │ GenerateNoise() │
      │                │                 │────────────────►│
      │                │                 │                 │
      │                │                 │ Noise samples   │
      │                │                 │◄────────────────│
      │                │                 │                 │
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
│Unity Main  │  │SonneController│  │StationManager│  │StationScheduler│
│Thread      │  │              │  │              │  │              │
└─────┬──────┘  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘
      │                │                 │                 │
      │ Update()       │                 │                 │
      │───────────────►│                 │                 │
      │                │                 │                 │
      │                │ UpdateStationGains()              │
      │                │────────────────►│                 │
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
      │                │ Station gains updated              │
      │                │◄────────────────│                 │
      │                │                 │                 │
      │                │ UpdateStationNavigation()          │
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
│SonneControl│  │SignalDecoder │  │MorseKeyer    │  │MorseAlphabet │
│ler         │  │              │  │              │  │              │
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
      │                │ DecodeCallsignBuffer()            │
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
│SonneControl│  │PositionTracker│  │PositionFix   │  │BeaconData    │
│ler         │  │              │  │Calculator    │  │              │
└─────┬──────┘  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘
      │                │                 │                 │
      │ UpdatePosition()                 │                 │
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
│   Game     │  │RadioStory    │  │GameReflection│  │LocaleManager │
│   Story    │  │Integration   │  │Cache         │  │              │
└─────┬──────┘  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘
      │                │                 │                 │
      │ InjectDecision()                 │                 │
      │───────────────►│                 │                 │
      │                │                 │                 │
      │                │ Get decisions   │                 │
      │                │────────────────►│                 │
      │                │                 │                 │
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
│Unity Audio │  │StationState  │  │SonneController│  │StationManager│
│Thread      │  │Buffer        │  │(Main Thread) │  │              │
└─────┬──────┘  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘
      │                │                 │                 │
      │                │                 │ UpdateStationGains()              │
      │                │                 │────────────────►│
      │                │                 │                 │
      │                │                 │                 │ Compute gains
      │                │                 │                 │──────┐
      │                │                 │                 │      │
      │                │                 │                 │◄─────┘
      │                │                 │                 │
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
│          │ duration = 2 seconds                                     │
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
│          │ duration = PostNavDuration                               │
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
│   Main     │  │RadioStory    │  │GameReflection│  │   Logger     │
│            │  │Integration   │  │Cache         │  │              │
└─────┬──────┘  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘
      │                │                 │                 │
      │ OnLoaded()     │                 │                 │
      │───────────────►│                 │                 │
      │                │                 │                 │
      │                │ Init()          │                 │
      │                │────────────────►│                 │
      │                │                 │                 │
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
