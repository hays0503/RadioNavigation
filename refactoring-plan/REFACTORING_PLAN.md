# Consolidated Refactoring Plan — RadioNavigation Mod

**Source:** Merged from REFACTORING_PLAN.md through REFACTORING_PLAN20.md (20 plans)  
**Date:** 2026-07-27  
**Author:** testui / AI-assisted consolidation  
**Status:** All tasks pending (`[ ]`) — no plan has been executed

---

## Summary of Consolidation

All 20 plans describe the same core refactoring tasks with different names and priorities. Key naming conflicts resolved:

| Concept | Plan names used | **Chosen name** |
|---------|----------------|-----------------|
| Station management | `StationManager`, `SonneStationManager`, `StationStateManager` | `StationManager` |
| Signal decoding | `MorseDecoder`, `SignalDecoder`, `SignalProcessor` | `SignalDecoder` |
| Position/navigation | `NavigationCalculator`, `PositionTracker`, `BearingCalculator`, `PositionFixManager` | `NavigationCalculator` |
| UI builder | `InformationPanelBuilder`, `PanelBuilder`, `StationFinder` | `InformationPanelBuilder` |
| State container | `RadioState`, `RadioContext`, `IRadioState` | `RadioState` |

**Total unique tasks identified: 38** (deduplicated from ~120+ across all plans)  
**Estimated total effort: 4-7 days**

---

## P0 — Critical (stability / correctness, do first)

### [ ] T0.1 Thread-safety for audio callback
**Problem:** `OnAudioFilterRead` (audio thread) and `UpdateStationGains()` (main thread) simultaneously mutate shared arrays (`stations[]`, `keyers[]`, `schedulers[]`, `carriers[]`) without synchronization → race conditions, audio artifacts/crashes.  
**Solution:** Double-buffer pattern — audio thread reads from `readBuffer`, main thread writes to `writeBuffer`; swap at frame boundary. Move `StationScheduler.Tick()` and `MorseKeyer.Advance()` out of the audio callback into Unity `Update()`.  
**Files:** `SonneController.cs`, `AudioSynthesisEngine.cs`  
**Acceptance:** No race conditions on shared state; audio callback only reads pre-computed values.

### [ ] T0.2 Try/catch in OnAudioFilterRead
**Problem:** Exception in Unity audio callback silently kills the entire audio subsystem.  
**Solution:** Wrap `ProcessAudio` body in try/catch with error logging.  
**Files:** `SonneController.cs:441-443`  
**Acceptance:** Audio exceptions are caught and logged without killing audio pipeline.

### [ ] T0.3 Fix memory leak in PanelDragHandler
**Problem:** `Application.quitting +=` delegate never removed, accumulates per-instance.  
**Solution:** Remove quit callback; use `OnDestroy()` to clean up texture.  
**Files:** `PanelDragHandler.cs`  
**Acceptance:** No leaked delegates after scene transitions or hot-reload.

### [ ] T0.4 Fix AudioClip.Create memory leak
**Problem:** `AudioClip.Create("SonneSilent", ...)` called every `Awake()` with no caching → memory leak.  
**Solution:** Cache in static field; create once, check `if (silentClip == null)`.  
**Files:** `SonneController.cs:93`  
**Acceptance:** AudioClip created exactly once per application lifetime.

---

## P1 — High (major maintainability blockers)

### [ ] T1.1 Decompose SonneController God Class
**Problem:** 506 lines, ~30 methods, 25+ fields. Handles audio synthesis, station management, Morse decoding, position fixing, bearing calculation, diagnostics, and UI coordination.  
**Solution:** Extract into focused classes:

| New class | Responsibility | Source from SonneController |
|-----------|---------------|---------------------------|
| `StationManager` | Station array, gain computation, dominant station tracking, frequency switching | `stations[]`, `schedulers[]`, `keyers[]`, `carriers[]`, `UpdateStationGains()`, `OnFrequencyChanged` |
| `SignalDecoder` | Signal buffer, Morse decode logic, callsign decoding, rich text formatting | `signalEntries`, `morseCharCount`, `morseDecoderBuffer`, `decodedText`, `DecodeCallsignBuffer()`, `GetSignalRichText()` |
| `NavigationCalculator` | Bearing calculation, position fix, multilateration, measurement tracking | `UpdatePosition()`, bearing math, `positionFixes`, `UpdateStationNavigation()` |

**Files:** New: `Source/Logic/StationManager.cs`, `Source/Logic/SignalDecoder.cs`, `Source/Logic/NavigationCalculator.cs`  
**Acceptance:** SonneController reduced to < 200 lines; only Unity lifecycle + orchestration remains.

### [ ] T1.2 Decompose InformationPanelHandler God Class
**Problem:** 474 lines, ~80 UI fields, subscribes to every handler, owns `Update()` loop. Creates UI programmatically instead of using prefabs.  
**Solution:** Extract into:
- `InformationPanelBuilder` — programmatic UI creation (labels, separators, layout groups)
- ViewModel classes (`FrequencyDisplayViewModel`, `PatternDisplayViewModel`, `FixDisplayViewModel`, `SignalDisplayViewModel`) — data binding logic
- Move event subscription to `OnEnable()` / unsubscription to `OnDisable()`

**Files:** `Source/UI/InformationPanelHandler.cs`  
**Acceptance:** InformationPanelHandler reduced to < 100 lines; delegates to view models.

### [ ] T1.3 Replace static mutable state with RadioState
**Problem:** `ToggleHandler.IsRadioOn`, `ToggleHandler.IsInteractable`, `GroundHandler.SetGroundActive()` — all static mutable fields accessible from anywhere, making code untestable and coupling hidden.  
**Solution:** Create `Source/Logic/RadioState.cs`:

```csharp
public class RadioState
{
    public static RadioState Instance { get; } = new RadioState();
    public bool IsRadioOn { get; set; }
    public bool IsInteractable { get; set; }
    public bool GroundActive { get; set; }
    public event Action<bool> OnRadioStateChanged;
    public event Action<bool> OnInteractableChanged;
}
```

Migrate all consumers: `ToggleHandler` writes to `RadioState.Instance`, others read from it. Replace `ToggleHandler.IsRadioOn` → `RadioState.Instance.IsRadioOn`.

**Files:** New: `Source/Logic/RadioState.cs`; Modified: `ToggleHandler.cs`, `ZoneHandler.cs`, `SpinnerHandlerBase.cs`, `CursorHandler.cs`, `SonneController.cs`  
**Acceptance:** No static mutable state in UI components; all state flows through RadioState.

### [ ] T1.4 Harden RadioStoryIntegration reflection
**Problem:** 381 lines of reflection — resolves game types by name/count at runtime, repeats lookups on every call (e.g., `GetCurrentLanguage()`). Silent break on game update. Duplicate `BindingFlags.Instance` flags (copy-paste bug at lines 64, 98, 102).  
**Solution:**
- Cache all resolved `Type`, `FieldInfo`, `PropertyInfo`, `MethodInfo` lookups in static dictionary
- Add graceful fallback with `Debug.LogError` when type resolution fails
- Fix duplicate BindingFlags flags
- Document required game API surface

**Files:** `Source/Logic/RadioStoryIntegration.cs`  
**Acceptance:** Reflection resolved once at startup; no repeated lookups; clear error on API mismatch.

### [ ] T1.5 Decouple scheduling from audio callback
**Problem:** `StationScheduler.Tick()`, `MorseKeyer.Advance()`, and gain computation all run inside `ProcessAudio` (per buffer). Risk of pop/crackle on GC allocation in hot path.  
**Solution:** Move scheduler tick and keyer advance to frame-based logic (`Update()` or coroutine). Pre-compute gain/key state before buffer fill, pass as snapshot to DSP. `ProcessAudio` should only read pre-computed values.

**Files:** `Source/Sound/AudioSynthesisEngine.cs`, `SonneController.cs`  
**Acceptance:** Audio callback performs only sample synthesis — no scheduling mutations inside it.

---

## P2 — Medium (code quality improvements)

### [ ] T2.1 Extract constants and kill magic numbers
**Problem:** 15+ unique magic numbers scattered across files: `90f`, `140f`, `1000f`, `300f`, `1800f`, `0.5f`, `0.001f`, `20f`, `0.12f`, `600f`, etc.  
**Solution:** Create `Source/Data/RadioConstants.cs`:

```csharp
public static class RadioConstants
{
    public const float BfoBaseHz = 1000f;
    public const float BfoMinHz = 300f;
    public const float BfoMaxHz = 1800f;
    public const float WhistleFrequencyHz = 600f;
    public const float VolumeMaxAngle = 140f;
    public const float AngleOffset = 90f;
    public const float SectorWidthDegrees = 5f;
    public const float TickMs = 50f;
    public const float GainThreshold = 0.001f;
    public const float QualityFactor = 20f;
    // ... all other magic numbers
}
```

**Files:** New: `Source/Data/RadioConstants.cs`; Modified: all files with magic numbers  
**Acceptance:** Zero hardcoded numeric literals in business logic — all reference named constants.

### [ ] T2.2 Unify MorseKeyer duplication
**Problem:** 4 methods (`AdvanceElement`, `GetCurrentElementType`, `GetCurrentSoundTicks`, `GetCurrentSilenceTicks`) contain near-identical branches for `consolPattern` vs `sequence` paths (~60 lines of duplicated code).  
**Solution:** Create abstraction over navigation sequences:

```csharp
internal interface IKeySequence
{
    int ElementCount { get; }
    NavigationElementType GetElement(int index);
}
```

Adapt both `NavigationPattern` and `NavigationElementType[]` to implement this interface. Remove conditional branches in MorseKeyer.

**Files:** New: `Source/Logic/IKeySequence.cs`; Modified: `MorseKeyer.cs`, `NavigationPattern.cs`  
**Acceptance:** MorseKeyer.AdvanceElement() < 30 lines; zero duplication between pattern paths.

### [ ] T2.3 Simplify ComparisonExtensions
**Problem:** 34 lines, 20 methods — most are trivial wrappers around C# operators. `IsNull()`/`IsNotNull()` mask real null problems and aren't idiomatic C#.  
**Solution:**
- Remove: `IsNull()`, `IsNotNull()`, `IsZero`, `IsNotZero`, `IsPositive`, `IsNegative`, `IsNonNegative`, `IsNonPositive` — replace with native operators (`is null`, `> 0`, etc.)
- Keep: `IsSignificant()`, `IsNegligible()` — justified domain wrappers for float comparison

**Files:** `Source/Logic/ComparisonExtensions.cs`; all files using these extensions  
**Acceptance:** Only `IsSignificant` and `IsNegligible` remain; no `IsNull()` calls in codebase.

### [ ] T2.4 Remove dead code
**Problem:** Multiple methods/functions exist but are never called:

| Dead code | Location | Issue |
|-----------|----------|-------|
| `UpdateStationBeats()` | AudioSynthesisEngine.cs:52 | Never called; beat freq managed in SonneController.UpdateStationGains |
| `GetSignalBuffer()` | SonneController.cs:217-220 | Trivial alias of GetSignalRichText() |
| `ConsolCycleConfig.GetForCycleDuration()` | ConsolCycleConfig.cs:36-39 | Always returns Cycle120s, ignores parameter |
| `FixResult.ErrorKm` | PositionFixCalculator.cs:21 | Always returns 0f; UI shows color based on it but data is fake |

**Solution:** Remove truly dead methods. For ErrorKm — either implement real RMS calculation from intersection spread or remove the accuracy indicator from UI.

**Files:** `AudioSynthesisEngine.cs`, `SonneController.cs`, `ConsolCycleConfig.cs`, `PositionFixCalculator.cs`  
**Acceptance:** No unreachable code; ErrorKm returns meaningful value or is removed from UI.

### [ ] T2.5 Guard Debug.Log in hot paths
**Problem:** 12+ files, 50+ unconditional `Debug.Log` calls — causes GC pressure and log noise in production. Critical: `AudioSynthesisEngine.cs:97-98` called at 44100Hz on audio thread (not thread-safe).  
**Solution:**
- Wrap all debug-only logs in `#if DEBUG` or use `[Conditional("DEBUG")]` attribute
- Priority: Audio callback, InformationPanelHandler.UpdateSignalDisplay(), SonneController hot paths
- Keep `Debug.LogError` / `Debug.LogWarning` in production

**Files:** 12+ files (see REFACTORING_PLAN3 for full list)  
**Acceptance:** Zero `Debug.Log` calls in Release build; audio thread is GC-free.

### [ ] T2.6 Replace string concatenation with StringBuilder
**Problem:** `result += "..."` in loops creates O(n²) allocations:
- `NavigationPattern.cs:58,82` — pattern to-string conversion
- `GroundHandler.StateLog()` — diagnostic logging

**Files:** `NavigationPattern.cs`, `GroundHandler.cs`  
**Acceptance:** No string concatenation in loops; verified via code review.

### [ ] T2.7 Fix O(n) RemoveAt(0) in SignalRichTextFormatter
**Problem:** `List.RemoveAt(0)` is O(n) — called on every signal update, creating quadratic behavior as list grows.  
**Solution:** Replace with `Queue<T>` or use index-based removal from end of reversed list.

**Files:** `Source/UI/SignalRichTextFormatter.cs:76`  
**Acceptance:** Signal buffer operations are O(1) amortized.

### [ ] T2.8 Fix frame-rate-dependent Lerp in SpinnerHandlerBase
**Problem:** `Mathf.Lerp(current, target, 0.5f)` without `Time.deltaTime` — spinner speed varies with frame rate.  
**Solution:** Use exponential decay: `Mathf.Lerp(current, target, 1f - Mathf.Exp(-sensitivity * Time.deltaTime))`.

**Files:** `Source/UI/SpinnerHandlerBase.cs:94`  
**Acceptance:** Spinner animation is frame-rate independent.

---

## P3 — Low (polish and long-term improvements)

### [ ] T3.1 Migrate BeaconData to JSON configuration
**Problem:** 373 lines of hardcoded station data in `BeaconData.cs`. Adding a new station requires recompilation.  
**Solution:** Create `Bundles/beacon_stations.json` with station definitions. `BeaconData.cs` becomes a deserializer/loader.

**Files:** New: `Bundles/beacon_stations.json`; Modified: `Source/Data/BeaconData.cs`  
**Acceptance:** All 14 stations load from JSON; adding new stations requires no code changes.

### [ ] T3.2 Replace hardcoded prefab strings with PrefabNames constants
**Problem:** `RadioInstaller`, `ZoneHandler`, `InformationPanelHandler` hardcode `"CursorSpinner"`, `"Toggle"`, etc. instead of using `PrefabNames.cs`.  
**Solution:** Add missing constants to `PrefabNames.cs` and replace all hardcoded string references.

**Files:** New/Modified: `Source/UI/PrefabNames.cs`; Modified: `RadioInstaller.cs`, `ZoneHandler.cs`, `InformationPanelHandler.cs`  
**Acceptance:** Zero hardcoded prefab path strings in codebase.

### [ ] T3.3 Unify VolumeSpinnerHandler and FeedbackSpinnerHandler
**Problem:** Two identical 17-line subclasses of `SpinnerHandlerBase`.  
**Solution:** Single parameterized class with `Action<float> OnValueChanged` or `string tag` for identification. Remove trivial subclasses.

**Files:** Modified: `SpinnerHandlerBase.cs`; Deleted: `VolumeSpinnerHandler.cs`, `FeedbackSpinnerHandler.cs`  
**Acceptance:** One spinner handler class; both use cases work via configuration.

### [ ] T3.4 Fix RadioHandler.Instance null-safety
**Problem:** `RadioHandler.Instance` accessed without null checks in scroll/drag paths → NRE on scene transitions.  
**Solution:** Add null guards at all call sites (`CursorHandler.OnScroll()`, `CursorHandler.OnDrag()`). Consider replacing singleton with injected reference via `Main.Instance`.

**Files:** `Source/UI/CursorHandler.cs`, `Source/Sound/SonneController.cs`  
**Acceptance:** Zero unguarded `RadioHandler.Instance` accesses.

### [ ] T3.5 Enable nullable reference types
**Problem:** `<NoWarn>CS8600;CS8601;CS8602;CS8603;CS8604;CS8618;CS8625</NoWarn>` in csproj disables all null-safety warnings — hides real NPE bugs.  
**Solution:** Remove NoWarn; add `#nullable enable`; fix resulting warnings incrementally.

**Files:** `radionavigation.csproj`, all 34 .cs files  
**Acceptance:** Project compiles with zero nullable warnings.

### [ ] T3.6 Add XML documentation
**Problem:** Zero `<summary>` comments on any public API across 34 source files.  
**Solution:** Add `<summary>` to all public classes, methods, properties. Priority: `IKeySequence`, `DialConfig`, `StationManager`, `NavigationCalculator`, `PositionFixCalculator`, `SignalDecoder`.

**Files:** All 34 .cs files (priority order)  
**Acceptance:** Public API surface fully documented; IntelliSense shows descriptions in Unity editor.

### [ ] T3.7 Fix ConsolCycleConfig.GetForCycleDuration
**Problem:** Method always returns `Cycle120s`, ignoring its parameter — misleading contract.  
**Solution:** Either remove the parameter (if only 120s cycle is supported) or implement a lookup table for multiple cycle durations with `Debug.LogWarning` for unknown values.

**Files:** `Source/Data/ConsolCycleConfig.cs:36-39`  
**Acceptance:** Method contract matches implementation; no silent parameter ignoring.

### [ ] T3.8 Replace hardcoded Steam path in build system
**Problem:** `"C:\Program Files (x86)\Steam\steamapps\common\UBOAT"` hardcoded in `build.ps1:2` and csproj (15 locations).  
**Solution:** Use environment variable fallback: `$env:UBOAT_PATH ?? "default"`.

**Files:** `build.ps1`, `radionavigation.csproj`  
**Acceptance:** Build system respects UBOAT_PATH environment variable; default path documented.

### [ ] T3.9 Fix version mismatch in deps.json
**Problem:** `radionavigation.deps.json` has version `1.0.0` while `Manifest.json` has `2.0`.  
**Solution:** Update to `2.0.0`.

**Files:** `radionavigation.deps.json`  
**Acceptance:** Version matches Manifest.json.

---

## Execution Order and Dependencies

```
P0 (Critical — 1-2 days)
├── T0.3 PanelDragHandler memory leak          ← independent
├── T0.4 AudioClip.Create caching              ← independent
├── T0.2 Try/catch in audio callback           ← independent
└── T0.1 Thread-safety (double-buffer)         ← depends on T0.2

P1 (High — 2-3 days)
├── T1.5 Decouple scheduling from audio        ← prerequisite for T0.1 to work cleanly
├── T1.4 Harden RadioStoryIntegration          ← independent
├── T1.3 Replace static state with RadioState  ← enables testability
├── T1.1 Decompose SonneController             ← depends on T1.3, T2.1
└── T1.2 Decompose InformationPanelHandler     ← depends on T1.3

P2 (Medium — 1-2 days)
├── T2.1 Extract constants                     ← independent
├── T2.2 Unify MorseKeyer                      ← depends on T1.1
├── T2.3 Simplify ComparisonExtensions         ← independent
├── T2.4 Remove dead code                      ← independent
├── T2.5 Guard Debug.Log                       ← independent
├── T2.6 StringBuilder in loops                ← independent
├── T2.7 Fix O(n) RemoveAt(0)                  ← independent
└── T2.8 Fix frame-rate Lerp                   ← independent

P3 (Low — 1-2 days)
├── T3.5 Enable nullable types                 ← depends on T2.3
├── T3.6 Add XML documentation                 ← independent
├── T3.1 BeaconData → JSON                     ← independent
├── T3.2 PrefabNames constants                 ← independent
├── T3.3 Unify spinner handlers                ← independent
├── T3.4 Fix RadioHandler null-safety          ← depends on T1.3
├── T3.7 Fix ConsolCycleConfig contract        ← independent
├── T3.8 Fix hardcoded Steam path              ← independent
└── T3.9 Fix version mismatch                  ← independent
```

---

## Success Criteria

- [ ] `SonneController` < 200 lines (from 506)
- [ ] `InformationPanelHandler` < 100 lines (from 474)
- [ ] No static mutable state in UI components
- [ ] MorseKeyer.AdvanceElement() < 30 lines (from ~60)
- [ ] ErrorKm returns meaningful value or is removed from UI
- [ ] All magic numbers replaced with named constants
- [ ] Zero `Debug.Log` calls in Release build
- [ ] Audio callback performs only sample synthesis — no scheduling mutations
- [ ] Reflection lookups cached at startup (no repeated Type.GetType)
- [ ] Project compiles with zero nullable warnings
- [ ] All public API documented with XML comments
- [ ] Project compiles and runs without regressions

---

## Metrics: Before / Target

| Metric | Current | Target |
|--------|---------|--------|
| Max file size (LOC) | 506 (`SonneController`) | < 200 |
| God classes (>300 LOC) | 2 | 0 |
| Static mutable fields in UI | 4+ | 0 |
| Magic numbers | 15+ unique values | 0 (all in RadioConstants) |
| Debug.Log calls | 50+ across 12 files | ~5 (errors/warnings only) |
| Dead code methods | 4 confirmed | 0 |
| Nullable warnings suppressed | 7 codes | 0 |
| XML doc comments | 0 | All public API |
