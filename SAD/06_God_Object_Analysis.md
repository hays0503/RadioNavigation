# God Object Analysis — RadioNavigation Mod

> **⚠️ Deep Scan Update (2026-08-02):** Аудит закрыт (F01–F207, статусы в `refactoring-plan/hypothesis/INDEX.md`). Актуальные god-объекты, отложенные в roadmap SAD 08/09 (Фаза 4): `SonneController` (F02), `InformationPanelHandler` (F03), `RadioStoryIntegration` (F04/F83 — мёртвая цепочка G1–G4), `Main.cs` (F21), `AudioSynthesisEngine` (F25). Реальные кандидаты на выделение: `SonneSignalGenerator` (146 LOC), `SignalRichTextFormatter` (86). Причины отложения — в `refactoring-plan/DEFERRED.md`. Несуществующие классы extraction plan (F47: `StationManager`, `SignalDecoder`, `AudioController`, `UIElementFactory`, `StationInfoDisplay`, `StationFinder`) — отклонены.

## 1. God Objects Identified

| Class | Lines | Methods | Fields | Severity |
|-------|-------|---------|--------|----------|
| **SonneController** | 508 | 30+ | 25+ | **Critical** |
| **InformationPanelHandler** | 474 | 20+ | 15+ | **Critical** |
| **RadioStoryIntegration** | 381 | 15+ | 9 | High |

## 2. SonneController Analysis

### 2.1 Current Structure

```
SonneController.cs (508 lines) — Source/Logic/SonneController.cs
│
├── Fields (25+)
│   ├── stations[] (StationState[14])          (F48: 4→14)
│   ├── schedulers[] (StationScheduler[14])
│   ├── keyers[] (MorseKeyer[14])
│   ├── carriers[] (CarrierGenerator[14])
│   ├── atmosphere (Atmosphere)
│   ├── receiverDsp (ReceiverDsp)
│   ├── signalEntries (List<SignalEntry>)
│   ├── morseDecoderBuffer (List<char>)
│   ├── decodedText (string)
│   ├── measurements (List<Measurement>)
│   ├── lastFix (FixResult)
│   ├── currentFrequency (float)
│   ├── outputVolume (float)
│   ├── feedbackVolume (float)
│   ├── silentClip (AudioClip)
│   ├── ... and 10+ more
│
├── Methods (~30)
│   ├── Unity Lifecycle
│   │   ├── Awake()
│   │   ├── OnDestroy()
│   │   ├── Update()
│   │   └── OnAudioFilterRead()
│   │
│   ├── Station Management
│   │   ├── UpdateStationGains()      (main thread)
│   │   ├── UpdateStationNavigation() (main thread, F77)
│   │   ├── SetActiveStation()
│   │   └── GetStationDiagnostics()
│   │
│   ├── Signal Processing
│   │   ├── OnStationElementCompleted()
│   │   ├── OnStationSequenceFinished()
│   │   ├── DecodeCallsignBuffer()
│   │   ├── GetSignalRichText()
│   │   ├── GetDecodedText()
│   │   └── ClearSignalBuffer()
│   │
│   ├── Audio
│   │   ├── ProcessAudio()             (audio thread, F13 Unity API)
│   │   ├── InitializeAudio()
│   │   └── CreateSilentClip()
│   │
│   ├── Position
│   │   ├── UpdatePosition()
│   │   └── CalculateBearing()
│   └── Navigation Pattern
│       └── CurrentConsolPattern      (F145: pre-compute in Awake)
│
└── Responsibilities (Mixed — 8 total)
    ├── Station management
    ├── Navigation calculation
    ├── Signal decoding
    ├── Audio synthesis
    ├── UI coordination
    ├── Diagnostics
    ├── Morse timing coordination
    └── Phase state coordination
```

### 2.2 Responsibility Matrix

| Responsibility | Lines | Methods | Can Extract? | F-ref |
|----------------|-------|---------|--------------|-------|
| Station Management | ~100 | 4 | ⚠ proposed (F02) | F02 |
| Navigation Calculation | ~80 | 3 | ⚠ proposed (N/A) | F77 |
| Signal Decoding | ~90 | 5 | ⚠ proposed (F02) | F02 |
| Audio Processing | ~120 | 3 | ⚠ proposed (AudioController F47) | F02 |
| UI Coordination | ~40 | 2 | ⚠️ Keep in Controller | F02 |
| Diagnostics | ~16 | 2 | ✅ DiagnosticsCollector | F02 |
| Pattern Coordination | ~30 | 2 | ⚠️ Keep in Controller | F145 |

### 2.3 Thread Safety Issues

```csharp
// CRITICAL: Audio thread writes
void OnAudioFilterRead(float[] data, int channels) {
    // Audio thread
    stations[s].gain = computedGain;          // ⚠️ Shared state
    stations[s].beatFrequency = beat;         // ⚠️ Shared state
    carriers[c].phase = newPhase;             // ⚠️ Shared state
}

// Main thread reads
void Update() {
    // Main thread
    float g = stations[s].gain;              // ⚠️ No sync
    UpdateStationGains();                     // ⚠️ No sync
}
```

## 3. InformationPanelHandler Analysis

### 3.1 Current Structure

```
InformationPanelHandler.cs (474 lines)
│
├── UI Creation (Programmatic)
│   ├── CreateLabel()
│   ├── CreateSeparator()
│   ├── EnsureLayout()
│   ├── CreateImage()
│   └── CreateText()
│
├── Data Display
│   ├── RefreshAll()
│   ├── UpdateSignalDisplay()
│   ├── UpdateStationInfo()
│   ├── UpdatePatternDisplay()
│   └── UpdateFixInfo()
│
├── Station Finding (Business Logic)
│   ├── FindClosestStation()
│   ├── GetZoneIndex()
│   ├── IsGroundActive()
│   └── GetStationColor()
│
├── Formatting
│   ├── FormatRichText()
│   ├── GetSignalRichText()
│   └── GetDecodedText()
│
└── Diagnostics
    ├── GetDiagnostics()
    └── UpdateDiagnostics()
```

### 3.2 Responsibility Matrix

| Responsibility | Lines | Methods | Can Extract? |
|----------------|-------|---------|--------------|
| UI Creation | ~150 | 5 | ✅ → UIElementFactory |
| Data Display | ~120 | 5 | ✅ → StationInfoDisplay |
| Station Finding | ~80 | 4 | ✅ → StationFinder |
| Formatting | ~60 | 3 | ✅ → SignalFormatter |
| Diagnostics | ~64 | 2 | ✅ → DiagnosticsProvider |

## 4. RadioStoryIntegration Analysis

### 4.1 Current Structure

```
RadioStoryIntegration.cs (381 lines) — Source/Logic/RadioStoryIntegration.cs
│
├── Reflection Handlers (9 cached members)
│   ├── storyType (Type)                   // typeof(RadioStory).BaseType
│   ├── insertMethod (MethodInfo)          // storyType.GetMethod("InsertDecision")
│   ├── decisionType (Type)                // insertMethod params[1]
│   ├── decisionCtor (ConstructorInfo)     // 6+ params, pars[0]=string, pars[3]=Action
│   ├── localizationKeyProp (PropertyInfo) // decisionType.GetProperty("LocalizationKey")
│   ├── stageField (FieldInfo)             // typeof(RadioStory).GetField("stage")
│   ├── decisionsProp (PropertyInfo)       // storyType.GetProperty("Decisions")
│   ├── onUpdatedMethod (MethodInfo)       // storyType.GetMethod("OnUpdated")
│   └── languageProp (PropertyInfo)        // typeof(Locale).GetProperty("CurrentLanguage")
│
├── Methods
│   ├── Init()
│   │   ├── TryInit()
│   │   └── ResetRuntimeState()
│   ├── InjectDecision()
│   ├── GetCurrentLanguage()
│   ├── RetrieveString()
│   └── NotifyUpdated()
│
└── Harmony Patches (5 targets — Main.cs, lines 95-130)
    ├── RadioStory.ContinueStory (prefix)
    ├── Locale.RetrieveString(string) (postfix)
    ├── Locale.RetrieveString(string, int) (postfix)
    ├── RadioStory.OnDecisionSelected (prefix)
    └── RadioStory.OnStopped (prefix)
```

### 4.2 Reflection Fragility

| Reflection Call | Risk | Fallback | F-ref |
|-----------------|------|----------|-------|
| `typeof(RadioStory).BaseType` | BaseType null | Debug.LogError + return (F23) | F23 |
| `storyType.GetMethod("InsertDecision")` | Method renamed | None — silent failure | F68 |
| `decisionType.GetConstructors()` | Ctor changed | Param count + type matching | F68 |
| `typeof(RadioStory).GetProperty("Decisions")` | Name changed | None | F23 |
| `typeof(Locale).GetProperty("CurrentLanguage")` | Renamed | None | F23 |

## 5. Extraction Plan

### 5.1 SonneController Decomposition ⚠ proposed (F02)

```
BEFORE: SonneController (508 lines) — Source/Logic/SonneController.cs
│
└──► AFTER (⚠ proposed — F02):
    ├── ⚠ StationManager (~100 lines)
    │   ├── stations[]
    │   ├── UpdateStationGains()
    │   ├── UpdateStationNavigation()
    │   └── SetActiveStation()
    │
    ├── ⚠ SignalDecoder (~90 lines)
    │   ├── signalEntries
    │   ├── morseDecoderBuffer
    │   ├── OnStationElementCompleted()
    │   ├── DecodeCallsignBuffer()
    │   └── GetSignalRichText()
    │
    ├── ⚠ AudioController (~120 lines) [non-existent — F47]
    │   ├── ProcessAudio()
    │   ├── InitializeAudio()
    │   └── CreateSilentClip()
    │
    └── SonneController (~200 lines) — ORCHESTRATOR
        ├── Awake()
        ├── Update()
        ├── OnDestroy()
        ├── OnAudioFilterRead()
        └── Event coordination
```

### 5.2 InformationPanelHandler Decomposition ⚠ proposed (F03)

```
BEFORE: InformationPanelHandler (474 lines) — Source/UI/InformationPanelHandler.cs
│
└──► AFTER (⚠ proposed — F03):
    ├── ⚠ UIElementFactory (~150 lines)
    │   ├── CreateLabel()
    │   ├── CreateSeparator()
    │   ├── CreateImage()
    │   └── CreateText()
    │
    ├── ⚠ StationInfoDisplay (~120 lines)
    │   ├── RefreshAll()
    │   ├── UpdateStationInfo()
    │   └── UpdatePatternDisplay()
    │
    ├── ⚠ StationFinder (~80 lines)
    │   ├── FindClosestStation()
    │   ├── GetZoneIndex()
    │   └── IsGroundActive()
    │
    └── InformationPanelHandler (~200 lines) — ORCHESTRATOR
        ├── Start()
        ├── RefreshAll()
        └── Event subscriptions
```

## 6. Benefits of Decomposition

### 6.1 Testability

| Before | After (⚠ proposed — F02/F03) |
|--------|------------------------------|
| Cannot test station logic without Unity | ⚠ StationManager testable with mocks |
| Cannot test signal decoding in isolation | ⚠ SignalDecoder testable with unit tests |
| Cannot test audio without game running | ⚠ AudioController testable with mocks |

### 6.2 Maintainability

| Before | After (⚠ proposed — F02/F03) |
|--------|------------------------------|
| Change in station logic affects audio | Changes isolated to ⚠ StationManager |
| Change in UI affects navigation | Changes isolated to ⚠ UIElementFactory |
| Hard to find specific functionality | Clear responsibility per class |

### 6.3 Reusability

| Before | After (⚠ proposed — F02/F03) |
|--------|------------------------------|
| Station logic tied to SonneController | ⚠ StationManager reusable |
| UI creation tied to InformationPanel | ⚠ UIElementFactory reusable |

## 7. Metrics Comparison

| Metric | Before | After (⚠ proposed — F02/F03) | Improvement |
|--------|--------|------|-------------|
| Max lines per class | 508 | ~200 | **61% reduction** |
| Max methods per class | 30+ | ~12 | **60% reduction** |
| Max fields per class | 25+ | ~10 | **60% reduction** |
| Testability | Low | High | **Significant** |
| Coupling | Very High | Medium | **Significant** |

---

*Document Version: 2.0*  
*Last Updated: 2026-08-01*  
*Corrected: LOC 506→508 (F50), Harmony targets StoryManager→RadioStory.ContinueStory (F46), Reflection chain matches actual code (F23/F68). Phantom extraction classes annotated `⚠ proposed` (F47). Updated for 199 problems (F65–F199).*
