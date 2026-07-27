# God Object Analysis — RadioNavigation Mod

## 1. God Objects Identified

| Class | Lines | Methods | Fields | Severity |
|-------|-------|---------|--------|----------|
| **SonneController** | 506 | 30+ | 25+ | **Critical** |
| **InformationPanelHandler** | 474 | 20+ | 15+ | **Critical** |
| **RadioStoryIntegration** | 381 | 15+ | 12+ | High |

## 2. SonneController Analysis

### 2.1 Current Structure

```
SonneController.cs (506 lines)
│
├── Fields (25+)
│   ├── stations[] (StationState)
│   ├── schedulers[] (StationScheduler)
│   ├── keyers[] (MorseKeyer)
│   ├── carriers[] (CarrierGenerator)
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
├── Methods (30+)
│   ├── Unity Lifecycle
│   │   ├── Awake()
│   │   ├── OnDestroy()
│   │   ├── Update()
│   │   └── OnAudioFilterRead()
│   │
│   ├── Station Management
│   │   ├── UpdateStationGains()
│   │   ├── UpdateStationNavigation()
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
│   │   ├── ProcessAudio()
│   │   ├── InitializeAudio()
│   │   └── CreateSilentClip()
│   │
│   └── Position
│       ├── UpdatePosition()
│       ├── CalculateBearing()
│       └── GetPositionFix()
│
└── Responsibilities (Mixed)
    ├── Station management
    ├── Navigation calculation
    ├── Signal decoding
    ├── Audio synthesis
    ├── Position tracking
    ├── UI coordination
    └── Diagnostics
```

### 2.2 Responsibility Matrix

| Responsibility | Lines | Methods | Can Extract? |
|----------------|-------|---------|--------------|
| Station Management | ~100 | 4 | ✅ → StationManager |
| Navigation Calculation | ~80 | 3 | ✅ → NavigationProcessor |
| Signal Decoding | ~90 | 5 | ✅ → SignalDecoder |
| Audio Processing | ~120 | 3 | ✅ → AudioController |
| Position Tracking | ~60 | 3 | ✅ → PositionTracker |
| UI Coordination | ~40 | 2 | ⚠️ Keep in Controller |
| Diagnostics | ~16 | 2 | ✅ → DiagnosticsCollector |

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
RadioStoryIntegration.cs (381 lines)
│
├── Reflection Handlers (12)
│   ├── singletonType (Type)
│   ├── singletonInstance (object)
│   ├── injectDecisionMethod (MethodInfo)
│   ├── decisionsField (FieldInfo)
│   ├── updatedMethod (MethodInfo)
│   ├── localeType (Type)
│   ├── localeInstance (object)
│   ├── getCurrentLanguageMethod (MethodInfo)
│   ├── retrieveStringMethod (MethodInfo)
│   ├── ... and 3 more
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
└── Harmony Patches
    ├── [HarmonyPatch("StoryManager", "InjectDecision")]
    └── [HarmonyPatch("LocaleManager", "GetCurrentLanguage")]
```

### 4.2 Reflection Fragility

| Reflection Call | Risk | Fallback |
|-----------------|------|----------|
| `Type.GetType("Game.StoryManager")` | Type renamed | Search Assembly-CSharp |
| `type.GetConstructors()[0]` | Constructor changed | Guess by parameter count |
| `type.GetField("singleton")` | Field renamed | Try "Instance" |
| `type.GetMethod("InjectDecision")` | Method renamed | Search by parameters |

## 5. Extraction Plan

### 5.1 SonneController Decomposition

```
BEFORE: SonneController (506 lines)
│
└──► AFTER:
    ├── StationManager (~100 lines)
    │   ├── stations[]
    │   ├── UpdateStationGains()
    │   ├── UpdateStationNavigation()
    │   └── SetActiveStation()
    │
    ├── SignalDecoder (~90 lines)
    │   ├── signalEntries
    │   ├── morseDecoderBuffer
    │   ├── OnStationElementCompleted()
    │   ├── DecodeCallsignBuffer()
    │   └── GetSignalRichText()
    │
    ├── AudioController (~120 lines)
    │   ├── ProcessAudio()
    │   ├── InitializeAudio()
    │   └── CreateSilentClip()
    │
    ├── PositionTracker (~60 lines)
    │   ├── measurements
    │   ├── lastFix
    │   ├── UpdatePosition()
    │   └── GetPositionFix()
    │
    └── SonneController (~136 lines) — ORCHESTRATOR
        ├── Awake()
        ├── Update()
        ├── OnDestroy()
        └── Event coordination
```

### 5.2 InformationPanelHandler Decomposition

```
BEFORE: InformationPanelHandler (474 lines)
│
└──► AFTER:
    ├── UIElementFactory (~150 lines)
    │   ├── CreateLabel()
    │   ├── CreateSeparator()
    │   ├── CreateImage()
    │   └── CreateText()
    │
    ├── StationInfoDisplay (~120 lines)
    │   ├── RefreshAll()
    │   ├── UpdateStationInfo()
    │   └── UpdatePatternDisplay()
    │
    ├── StationFinder (~80 lines)
    │   ├── FindClosestStation()
    │   ├── GetZoneIndex()
    │   └── IsGroundActive()
    │
    └── InformationPanelHandler (~124 lines) — ORCHESTRATOR
        ├── Start()
        ├── RefreshAll()
        └── Event subscriptions
```

## 6. Benefits of Decomposition

### 6.1 Testability

| Before | After |
|--------|-------|
| Cannot test station logic without Unity | StationManager testable with mocks |
| Cannot test signal decoding in isolation | SignalDecoder testable with unit tests |
| Cannot test audio without game running | AudioController testable with mocks |

### 6.2 Maintainability

| Before | After |
|--------|-------|
| Change in station logic affects audio | Changes isolated to StationManager |
| Change in UI affects navigation | Changes isolated to UIElementFactory |
| Hard to find specific functionality | Clear responsibility per class |

### 6.3 Reusability

| Before | After |
|--------|-------|
| Station logic tied to SonneController | StationManager reusable |
| UI creation tied to InformationPanel | UIElementFactory reusable |
| Position logic tied to SonneController | PositionTracker reusable |

## 7. Metrics Comparison

| Metric | Before | After | Improvement |
|--------|--------|-------|-------------|
| Max lines per class | 506 | ~150 | **70% reduction** |
| Max methods per class | 30+ | ~10 | **67% reduction** |
| Max fields per class | 25+ | ~8 | **68% reduction** |
| Testability | Low | High | **Significant** |
| Coupling | Very High | Medium | **Significant** |
