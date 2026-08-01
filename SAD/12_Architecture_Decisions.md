# Architecture Decisions — RadioNavigation Mod

> **⚠️ Deep Scan Update (2026-08-01):** Полный аудит выявил критические расхождения между статусами ADR и реальностью. Всего **199 проблем** (F01–F199).
> - **F45 (исправлено):** ADR-002/003/004/005/007 — статус changed «Accepted» → «Deferred»
> - **F46 (исправлено):** ADR-001 пример кода — replaced with real Harmony imperative patching
> - **F47:** Implementation examples reference proposed/non-existent classes (annotated `⚠ proposed`)
> - **F48:** ADR-003 упоминает 4 станции — реальность 14 (annotated)
>
> Полные описания: `refactoring-plan/F45.md`..`F52.md`, `F65.md`..`F76.md`.

## 1. Decision Overview

| ID | Decision | Status | Date |
|----|----------|--------|------|
| ADR-001 | Use Harmony for runtime patching | Accepted | 2026-07-26 |
| ADR-002 | Implement double-buffer pattern for audio | Deferred | 2026-07-26 |
| ADR-003 | Decompose God Classes | Deferred | 2026-07-26 |
| ADR-004 | Interface-based integration | Deferred | 2026-07-26 |
| ADR-005 | Centralize constants | Deferred | 2026-07-26 |
| ADR-006 | JSON configuration files | Proposed | 2026-07-26 |
| ADR-007 | Unit testing strategy | Deferred | 2026-07-26 |

## 2. ADR-001: Use Harmony for Runtime Patching

### Status
Accepted

### Context
The mod needs to inject code into UBOAT's runtime without modifying game files. Options:
1. Harmony library (runtime method patching)
2. Assembly replacement (fragile, breaks with updates)
3. IL injection (complex, hard to maintain)

### Decision
Use Harmony library for runtime method patching.

### Consequences

**Positive**:
- Non-invasive patching
- Easy to enable/disable patches
- Community-supported library
- Works with game updates

**Negative**:
- Reflection-based, fragile to game updates
- Performance overhead on patched methods
- Debugging complexity

### Implementation

```csharp
// Main.cs (OnSceneAwake)
var harmony = new Harmony("UBOAT.RadioNavigation");
harmony.Patch(
    original: AccessTools.Method(typeof(RadioStory), "ContinueStory"),
    prefix: new HarmonyMethod(typeof(Main), nameof(OnContinueStoryPrefix))
);
harmony.Patch(
    original: AccessTools.Method(typeof(Locale), nameof(Locale.RetrieveString), new[] { typeof(string) }),
    postfix: new HarmonyMethod(typeof(Main), nameof(OnRetrieveStringPostfix))
);
```

### Alternatives Considered

| Alternative | Pros | Cons | Decision |
|-------------|------|------|----------|
| Harmony | Non-invasive, flexible | Reflection overhead | **Selected** |
| Assembly replacement | Direct access | Breaks with updates | Rejected |
| IL injection | Low overhead | Complex, hard to maintain | Rejected |

## 3. ADR-002: Implement Double-Buffer Pattern for Audio

### Status
Deferred

### Context
Audio thread (`OnAudioFilterRead`) and main thread (`Update()`) share mutable `StationState[]` without synchronization. This causes race conditions and audio artifacts.

### Decision
Implement double-buffer pattern with lock-free ring buffer for command passing.

### Consequences

**Positive**:
- Thread-safe audio processing
- No locks in audio thread
- Predictable performance
- No deadlocks

**Negative**:
- Memory overhead (2x station data)
- Complexity in buffer management
- Potential for stale data

### Implementation

```csharp
// ⚠ proposed — StationStateBuffer not yet implemented (F47)
// Actual implementation: StationState[] mutated directly without sync (F01, F29)
public class StationStateBuffer {
    private StationState[] readBuffer;   // Audio thread reads
    private StationState[] writeBuffer;  // Main thread writes
    private volatile bool swapRequested;
    
    public void Swap() {
        var temp = readBuffer;
        readBuffer = writeBuffer;
        writeBuffer = temp;
    }
    
    public StationState[] GetReadBuffer() => readBuffer;
    public StationState[] GetWriteBuffer() => writeBuffer;
}
```

### Alternatives Considered

| Alternative | Pros | Cons | Decision |
|-------------|------|------|----------|
| Double-buffer | Lock-free, predictable | Memory overhead | **Selected** |
| Lock/Monitor | Simple | Deadlock risk, audio thread blocking | Rejected |
| ConcurrentQueue | Thread-safe | Allocation pressure, latency | Rejected |
| Immutable data | Safe | GC pressure, copy overhead | Rejected |

## 4. ADR-003: Decompose God Classes

### Status
Deferred

### Context
Two God Classes exist:
- `SonneController` (508 lines, 30+ methods)
- `InformationPanelHandler` (474 lines, 20+ methods)

These are hard to test, maintain, and understand.

### Decision
Decompose God Classes into focused, single-responsibility classes.

### Consequences

**Positive**:
- Better testability
- Clearer responsibilities
- Easier maintenance
- Faster feature development

**Negative**:
- Initial refactoring effort
- Potential for breaking changes
- More files to manage

### Implementation

**SonneController Decomposition** (⚠ proposed — classes not yet implemented, F47):

| New Class | Responsibility | Lines | Status |
|-----------|----------------|-------|--------|
| ⚠ StationManager | Station management | ~100 | Proposed |
| ⚠ SignalDecoder | Signal processing | ~90 | Proposed |
| ⚠ PositionTracker | Position calculation | ~60 | Proposed |
| DiagnosticsCollector | Diagnostics | ~30 | Existing (inline) |
| SonneController | Orchestration | ~136 | Existing (508 LOC) |

**InformationPanelHandler Decomposition** (⚠ proposed — classes not yet implemented, F47):

| New Class | Responsibility | Lines | Status |
|-----------|----------------|-------|--------|
| ⚠ UIElementFactory | UI creation | ~150 | Proposed |
| ⚠ StationInfoDisplay | Data display | ~120 | Proposed |
| ⚠ StationFinder | Business logic | ~80 | Proposed |
| InformationPanelHandler | Orchestration | ~124 | Existing (474 LOC) |

### Alternatives Considered

| Alternative | Pros | Cons | Decision |
|-------------|------|------|----------|
| Decompose | Better architecture | Refactoring effort | **Selected** |
| Leave as-is | No risk | Continued maintenance pain | Rejected |
| Delete and rewrite | Clean slate | High risk, lose working code | Rejected |

## 5. ADR-004: Interface-Based Integration

### Status
Deferred

### Context
Game integration uses reflection directly, making it fragile to game updates.

### Decision
Create interfaces for all game integrations with fallback implementations.

### Consequences

**Positive**:
- Compile-time safety
- Easy to mock for testing
- Clear API boundaries
- Version compatibility

**Negative**:
- Additional abstraction layer
- More code to maintain
- Initial implementation effort

### Implementation

```csharp
// ⚠ proposed — GameReflectionCache not yet implemented (F47)
// Actual implementation: RadioStoryIntegration (381 LOC) uses Type.GetType/FieldInfo directly
// Interfaces (IGameServices, etc.) not yet created (F04, ADR-004 Deferred)

// Interfaces
public interface IGameServices {
    string GetCurrentLanguage();
    string RetrieveString(string key);
    void InjectDecision(object story, int index, object decision);
}

// Implementation with fallback
public class GameReflectionCache : IGameServices {
    private readonly TypeCache cache;
    
    public string GetCurrentLanguage() {
        // Try cache first, fallback to reflection
    }
}
```

### Alternatives Considered

| Alternative | Pros | Cons | Decision |
|-------------|------|------|----------|
| Interface-based | Testable, safe | Abstraction overhead | **Selected** |
| Direct reflection | Simple | Fragile, untestable | Rejected |
| Code generation | Type-safe | Complex build process | Rejected |

## 6. ADR-005: Centralize Constants

### Status
Deferred

### Context
Magic numbers are scattered throughout the codebase (25+ unique values).

### Decision
Centralize all constants in `Data/Constants.cs`.

### Consequences

**Positive**:
- Single source of truth
- Easy to modify values
- Better documentation
- No duplication

**Negative**:
- Additional file to maintain
- Import required in all files

### Implementation

```csharp
// ⚠ proposed — Data/Constants.cs not yet implemented (F47)
// Actual: magic numbers scattered across all files (~40 unique values, F05)
public static class Constants {
    // Audio
    public const float BfoBaseFrequency = 1000f;
    public const float BfoMinFrequency = 300f;
    public const float BfoMaxFrequency = 1800f;
    
    // UI
    public const float CursorAngleOffset = 90f;
    public const float MaxSpinnerAngle = 140f;
    
    // Navigation
    public const float SectorWidthDeg = 5f;
    public const int SignalsPerCycle = 60;
    
    // Thresholds
    public const float GainThreshold = 0.001f;
    public const float NoiseThreshold = 0.01f;
}
```

### Alternatives Considered

| Alternative | Pros | Cons | Decision |
|-------------|------|------|----------|
| Centralized constants | Single source | Import overhead | **Selected** |
| Configuration files | Runtime changeable | Loading complexity | Deferred |
| Enum values | Type-safe | Not suitable for floats | Rejected |

## 7. ADR-006: JSON Configuration Files

### Status
Proposed

### Context
Station data is hardcoded in `BeaconData.cs` (373 lines). Any change requires recompilation.

### Decision
Move configuration data to JSON files.

### Consequences

**Positive**:
- Easy to modify without recompilation
- Community can contribute data
- Multiple configurations possible
- Version control friendly

**Negative**:
- Loading complexity
- Error handling required
- Performance overhead (minimal)

### Implementation

```json
// Config/beacons.json
{
  "stations": [
    {
      "name": "Flensburg",
      "latitude": 54.79,
      "longitude": 9.43,
      "bearingTable": [0, 0, 0, ...]
    }
  ]
}
```

### Alternatives Considered

| Alternative | Pros | Cons | Decision |
|-------------|------|------|----------|
| JSON files | Easy to edit, versionable | Loading complexity | **Proposed** |
| ScriptableObjects | Unity-native, visual editor | Unity-specific, harder to share | Rejected |
| XML | Mature, schema validation | Verbose, less readable | Rejected |
| Hardcoded | No loading overhead | Requires recompilation | Current (to be replaced) |

## 8. ADR-007: Unit Testing Strategy

### Status
Deferred

### Context
No unit tests exist (0% coverage). Bugs are discovered in production.

### Decision
Implement unit tests for all pure C# logic.

### Consequences

**Positive**:
- Catch bugs early
- Safe refactoring
- Documentation of behavior
- Faster debugging

**Negative**:
- Initial implementation effort
- Test maintenance
- Learning curve for team

### Implementation

```csharp
[TestFixture]
public class PositionFixCalculatorTests {
    [Test]
    public void Calculate_TwoStations_FindsIntersection() {
        // Test implementation
    }
}
```

### Test Coverage Targets

| Layer | Target | Priority |
|-------|--------|----------|
| Data | >80% | High |
| Logic | >70% | High |
| Sound | >60% | Medium |
| UI | >50% | Low |

### Alternatives Considered

| Alternative | Pros | Cons | Decision |
|-------------|------|------|----------|
| Unit tests | Fast, isolated | Implementation effort | **Selected** |
| Integration tests | Real-world scenarios | Slower, more complex | Deferred |
| Manual testing | Flexible | Slow, error-prone | Current |
| No tests | No effort | High risk | Rejected |

## 9. Decision Log

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-07-26 | ADR-001: Harmony | Industry standard for Unity modding |
| 2026-07-26 | ADR-002: Double-buffer | Thread safety critical for audio |
| 2026-07-26 | ADR-003: Decompose | God classes阻碍 development |
| 2026-07-26 | ADR-004: Interfaces | Game updates break reflection |
| 2026-07-26 | ADR-005: Constants | Magic numbers cause bugs |
| 2026-07-26 | ADR-006: JSON | Configuration flexibility |
| 2026-07-26 | ADR-007: Testing | No tests = high risk |

## 10. Review Schedule

| ADR | Review Date | Reviewer | Status |
|-----|-------------|----------|--------|
| ADR-001 | 2026-08-01 | Tech Lead | Active |
| ADR-002 | 2026-08-01 | Audio Lead | Deferred |
| ADR-003 | 2026-08-15 | Architect | Deferred |
| ADR-004 | 2026-08-15 | Integration Lead | Deferred |
| ADR-005 | 2026-08-01 | All | Deferred |
| ADR-006 | 2026-08-15 | Data Lead | Proposed |
| ADR-007 | 2026-08-01 | QA Lead | Deferred |
