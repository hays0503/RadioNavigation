# Architectural Smells — RadioNavigation Mod

## 1. Smell Categories

| Category | Count | Severity |
|----------|-------|----------|
| Code Smells | 15 | Medium-High |
| Design Smells | 8 | High |
| Architecture Smells | 5 | Critical |
| **Total** | **28** | — |

## 2. Critical Architecture Smells

### 2.1 God Class

**Location**: `SonneController.cs` (506 lines), `InformationPanelHandler.cs` (474 lines)

**Symptoms**:
- Single class does too many things
- 30+ methods, 25+ fields
- Changes in one area affect unrelated areas

**Impact**: 
- Cannot test in isolation
- Changes ripple through entire class
- New developers cannot understand responsibilities

**Recommendation**: Extract 4-5 focused classes per God Object

### 2.2 Hubris Modularity

**Location**: `RadioStoryIntegration.cs` (381 lines)

**Symptoms**:
- 12 reflection handlers
- Direct type manipulation at runtime
- No compile-time safety

**Impact**:
- Game updates break mod silently
- No type checking until runtime
- Difficult to debug

**Recommendation**: Interface-based integration with fallbacks

### 2.3 Spaghetti Architecture

**Location**: UI Layer ↔ Logic Layer coupling

**Symptoms**:
- UI handlers directly access Logic layer
- Logic layer directly accesses UI state
- No clear dependency direction

**Impact**:
- Cannot change UI without affecting logic
- Cannot test logic without UI
- Circular dependency risk

**Recommendation**: Enforce layer boundaries with interfaces

## 3. Design Smells

### 3.1 Feature Envy

**Location**: `InformationPanelHandler` → `ZoneHandler`, `GroundHandler`

```csharp
// InformationPanelHandler.cs
private int GetZoneIndex() {
    // Envious of ZoneHandler's state
    return zoneHandler.currentIndex;
}

private bool IsGroundActive() {
    // Envious of GroundHandler's state
    return groundHandler.groundActive;
}
```

**Impact**: Logic scattered across UI components

**Recommendation**: Move business logic to dedicated service

### 3.2 Inappropriate Intimacy

**Location**: Multiple UI handlers ↔ `SonneController`

```csharp
// Tight coupling everywhere
SonneController.Instance.GetStationDiagnostics();
SonneController.Instance.GetSignalRichText();
SonneController.Instance.GetDecodedText();
```

**Impact**: UI cannot exist without SonneController

**Recommendation**: Define interfaces for data access

### 3.3 Data Clumps

**Location**: `SonneController` fields

```csharp
// These fields always travel together
StationState[] stations;
StationScheduler[] schedulers;
MorseKeyer[] keyers;
CarrierGenerator[] carriers;
```

**Impact**: Cannot change one without others

**Recommendation**: Extract to `StationBundle` or `RadioContext`

### 3.4 Primitive Obsession

**Location**: Magic numbers throughout codebase

```csharp
// Scattered everywhere
const float BfoBase = 1000f;           // 2 locations
const float BfoMin = 300f;             // 2 locations
const float BfoMax = 1800f;            // 2 locations
const float CursorAngleOffset = 90f;   // 3 locations
const float MaxSpinnerAngle = 140f;    // 3 locations
```

**Impact**: Changes require finding all locations

**Recommendation**: Centralize in `Constants.cs`

### 3.5 Switch Statements

**Location**: `StationScheduler.cs` phase transitions

```csharp
// 3 different switch blocks with same logic
switch (phase) {
    case Phase.Pause:
        // ...
        break;
    case Phase.Callsign:
        // ...
        break;
    case Phase.Navigation:
        // ...
        break;
}
```

**Impact**: Adding new phase requires modifying multiple switches

**Recommendation**: State machine pattern with polymorphism

## 4. Code Smells

### 4.1 Duplicated Code

| Duplicate 1 | Duplicate 2 | Lines |
|-------------|-------------|-------|
| `BeaconData.GetDotCountToBearingTable()` | `SonneSignalGenerator.GenerateBearingTable()` | ~30 |
| `VolumeSpinnerHandler` | `FeedbackSpinnerHandler` | ~17 |
| `MorseKeyer.GetCurrentSoundTicks()` | `MorseKeyer.GetCurrentSilenceTicks()` | ~10 |
| `BfoBase = 1000f` | `BfoBase = 1000f` | 1 |
| `ToRadians(float)` | `ToRadians(float)` | ~5 |
| `IsActiveDrag()` | `IsActiveDrag()` | ~8 |

**Impact**: 6 groups of duplicated logic

**Recommendation**: Extract to shared utilities

### 4.2 Dead Code

| Location | Code | Issue |
|----------|------|-------|
| `PositionFixCalculator.ErrorKm` | Always returns 0 | Never implemented |
| `ToggleHandler.ImgState()` | Defined but never called | Dead method |
| `ConsolCycleConfig.GetForCycleDuration()` | Ignores parameter | Dead parameter |
| `ComparisonExtensions.IsNull()` | Duplicates `== null` | Unnecessary |

**Impact**: Confusion, maintenance burden

**Recommendation**: Remove or implement

### 4.3 Long Parameter List

**Location**: `AudioSynthesisEngine.ProcessAudio()`

```csharp
public void ProcessAudio(
    float[] data,
    int channels,
    StationState[] stations,
    CarrierGenerator[] carriers,
    Atmosphere atmosphere,
    ReceiverDsp receiverDsp,
    float outputVolume,
    float feedbackVolume
)
```

**Impact**: Hard to call, hard to test

**Recommendation**: Group into context object

### 4.4 Refused Bequest

**Location**: `SpinnerHandlerBase` subclasses

```csharp
// Base class has template method
public abstract class SpinnerHandlerBase {
    protected virtual void OnEnable() { }
    protected virtual void OnDisable() { }
}

// Subclasses don't use inheritance properly
public class VolumeSpinnerHandler : SpinnerHandlerBase {
    // Duplicates logic from base
}
```

**Impact**: Inheritance hierarchy doesn't add value

**Recommendation**: Use composition or flatten hierarchy

### 4.5 Message Chains

**Location**: Multiple places

```csharp
// Long chain
SonneController.Instance
    .GetStationDiagnostics()
    .FormatDiagnosticString();

// Another chain
transform.Find("Background")
    .GetComponent<Image>()
    .color.a;
```

**Impact**: Brittle, hard to refactor

**Recommendation**: Introduce intermediate methods

### 4.6 Speculative Generality

**Location**: `ComparisonExtensions.cs`

```csharp
// 34 methods, most never used
public static bool IsNull(this object obj) => obj == null;
public static bool IsNotNull(this object obj) => obj != null;
public static bool IsPositive(this float f) => f > 0;
public static bool IsNegative(this float f) => f < 0;
public static bool IsZero(this float f) => f == 0f;
// ... and 29 more
```

**Impact**: Noise, confusion

**Recommendation**: Remove unused methods

## 5. Smell Heatmap

```
File                              Smells  Severity
─────────────────────────────────────────────────
SonneController.cs                8       ██████████ Critical
InformationPanelHandler.cs        6       ████████   Critical
RadioStoryIntegration.cs          5       ███████    High
AudioSynthesisEngine.cs           4       █████      High
ComparisonExtensions.cs           3       ████       Medium
StationScheduler.cs               2       ███        Medium
MorseKeyer.cs                     2       ███        Medium
BeaconData.cs                     2       ███        Medium
DialConfig.cs                     1       ██         Low
PositionFixCalculator.cs          1       ██         Low
```

## 6. Smell Resolution Priority

### Priority 1: Critical (Fix First)

| Smell | Location | Effort | Impact |
|-------|----------|--------|--------|
| God Class | SonneController | High | Very High |
| God Class | InformationPanelHandler | Medium | High |
| Thread Safety | SonneController + AudioEngine | Medium | Critical |
| Reflection Fragility | RadioStoryIntegration | High | High |

### Priority 2: High (Fix Soon)

| Smell | Location | Effort | Impact |
|-------|----------|--------|--------|
| Duplicated Code | 6 groups | Low | Medium |
| Magic Numbers | Throughout | Low | Medium |
| Static Coupling | UI handlers | Medium | High |
| Dead Code | Multiple | Low | Low |

### Priority 3: Medium (Fix When Touching)

| Smell | Location | Effort | Impact |
|-------|----------|--------|--------|
| Long Parameter List | AudioSynthesisEngine | Medium | Medium |
| Switch Statements | StationScheduler | Medium | Low |
| Message Chains | Multiple | Low | Low |
| Feature Envy | InformationPanelHandler | Medium | Medium |

## 7. Smell Metrics

| Metric | Current | Target |
|--------|---------|--------|
| God Classes (>300 lines) | 2 | 0 |
| Duplicated Code Groups | 6 | 0 |
| Magic Numbers | 25+ | 0 |
| Dead Code Items | 4+ | 0 |
| Static Coupling Points | 5+ | 1 |
| Reflection Handlers | 12 | 0 |
| Thread Safety Issues | 2 | 0 |
