# Refactoring Roadmap — RadioNavigation Mod

> **⚠️ Deep Scan Update (2026-08-02):** Аудит закрыт (F01–F207, статусы в `refactoring-plan/hypothesis/INDEX.md`). Выполнено в ветках `fix/audit-and-issue-files` (42 коммита) + `refactor/deferred-code` (F12/F73/F28/F10/F173): фазы 0, 1, 2, 3 и большая часть 5 закрыты; тесты **167/167**. Остаток roadmap — Фаза 4 (god-object декомпозиция: F02/F03/F18/F21/F25, deferred в `refactoring-plan/DEFERRED.md`).

## 1. Roadmap Overview

```
Week 1: Safety & Critical Fixes (F01–F15, F65–F83, F121–F140)
Week 2-3: Architecture Decomposition (F02, F03, F04, F17, F18, F21, F23, F25, F45–F50)
Week 4-5: Polish, Quick Wins & Testing (F66–F199)
```

**Total Effort**: ~95–130 hours (P0–P3: 199 tasks)
**Risk Level**: Medium-High

### Phases Overview

| Phase | Focus | Tasks | Effort | F-numbers | Status |
|--------|-------|-------|--------|-----------|--------|
| Phase 0 | Test infrastructure | F22 | 8–12h | F22 | ✅ 2026-08-01 |
| Phase 1 | Quick wins | F77–F164 | 20–25h | ~65 tasks | ✅ 2026-08-01 |
| Phase 2 | Safety & correctness | F01–F15, F65–F83, F121–F140 | 15–25h | ~35 tasks | ✅ 2026-08-01 |
| Phase 3 | Architecture blockers | F05, F29, F01, F04, F25 | 12–18h | 5 tasks | ✅ (F25 → Фаза 4) |
| Phase 4 | God-object decomposition | F02, F03, F04, F17, F18, F21 | 12–20h | 6 tasks | ⏸ deferred (F02/F03/F18/F21) |
| Phase 5 | API design & polish | F65–F76, F165–F199 | 10–15h | ~55 tasks | ✅ + F12/F28 (deferred-code) |

## 2. Phase 1: Safety & Critical Fixes (Week 1)

### 2.1 Priority 0: Thread Safety

**Effort**: 8-12 hours
**Risk**: High
**Impact**: Critical

| Task | Files | Effort | F-ref |
|------|-------|--------|-------|
| Implement double-buffer pattern | SonneController.cs, AudioSynthesisEngine.cs | 4-6h | F01 |
| Remove Debug.Log from audio thread | AudioSynthesisEngine.cs | 30m | F13 |
| Cache AudioClip.Create | SonneController.cs | 0.5h | F13 |
| Add try/catch to OnAudioFilterRead | SonneController.cs | 0.5h | F13 |
| Add sync primitives | StationState.cs | 2-3h | F29 |
| Test with real audio output | — | 2-3h | — |

**Success Criteria**:
- [ ] No race conditions in audio thread
- [ ] No Debug.Log in production audio code
- [ ] AudioClip created once per session
- [ ] Audio exceptions handled gracefully
- [ ] Synchronization on shared state (F29)

### 2.2 Priority 1: Quick Wins

**Effort**: 4-6 hours
**Risk**: Low
**Impact**: Medium

| Task | Files | Effort |
|------|-------|--------|
| Extract magic numbers to Constants.cs | All files with magic numbers | 2h |
| Remove ComparisonExtensions.cs | ComparisonExtensions.cs, 30+ files | 2h |
| Fix BfoBase duplicate | SonneController.cs, AudioSynthesisEngine.cs | 0.5h |
| Remove dead code | ToggleHandler.cs, PositionFixCalculator.cs | 0.5h |

**Success Criteria**:
- [ ] All magic numbers in Constants.cs
- [ ] No ComparisonExtensions usage
- [ ] Single BfoBase constant
- [ ] No dead code

## 3. Phase 2: Architecture Decomposition (Weeks 2-3)

### 3.1 Priority 2: SonneController Decomposition ⚠ proposed (F02)

**Effort**: 5-8 hours
**Risk**: Very High
**Impact**: Very High

| Task | New File | From SonneController | Effort | F-ref |
|------|----------|---------------------|--------|-------|
| ⚠ Extract StationManager | StationManager.cs | stations[], UpdateStationGains(), UpdateStationNavigation() | 4-5h | F02 |
| ⚠ Extract SignalDecoder | SignalDecoder.cs | signalEntries, OnStationElementCompleted(), DecodeCallsignBuffer() | 3-4h | F02 |
| ⚠ Extract PositionTracker | PositionTracker.cs | measurements, lastFix, UpdatePosition() | 2h | F02 |
| Extract DiagnosticsCollector | DiagnosticsCollector.cs | GetStationDiagnostics() | 1h | F02 |
| Refactor SonneController | SonneController.cs | Orchestration only (~200 lines) | 2h | F02 |
| Integration testing | — | — | 2h | F02 |

**Success Criteria**:
- [ ] SonneController < 300 lines
- [ ] Each extracted class < 150 lines
- [ ] No cross-dependencies between extracted classes
- [ ] All functionality preserved

### 3.2 Priority 3: InformationPanelHandler Decomposition ⚠ proposed (F03)

**Effort**: 3-4 hours
**Risk**: Medium-High
**Impact**: High

| Task | New File | From InformationPanelHandler | Effort | F-ref |
|------|----------|------------------------------|--------|-------|
| ⚠ Extract UIElementFactory | UIElementFactory.cs | CreateLabel(), CreateSeparator(), CreateImage() | 2-3h | F03 |
| ⚠ Extract StationInfoDisplay | StationInfoDisplay.cs | RefreshAll(), UpdateStationInfo(), UpdatePatternDisplay() | 1-2h | F03 |
| ⚠ Extract StationFinder | StationFinder.cs | FindClosestStation(), GetZoneIndex(), IsGroundActive() | 1h | F03 |
| Refactor InformationPanelHandler | InformationPanelHandler.cs | Orchestration (~200 lines) | 1h | F03 |
| Integration testing | — | — | 1h | F03 |

**Success Criteria**:
- [ ] InformationPanelHandler < 300 lines
- [ ] Each extracted class < 200 lines
- [ ] Business logic separated from UI

### 3.3 Priority 4: Service Layer

**Effort**: 6-8 hours
**Risk**: Medium
**Impact**: High

| Task | New File | Purpose | Effort |
|------|----------|---------|--------|
| Create IRadioState interface | IRadioState.cs | Central radio state | 1h |
| Implement RadioContext | RadioContext.cs | Replace static singletons | 2-3h |
| Create IStationDataSource | IStationDataSource.cs | Station data abstraction | 0.5h |
| Implement HardcodedStationRepository | HardcodedStationRepository.cs | Current hardcoded data | 1h |
| Create IGameServices interface | IGameServices.cs | Game integration abstraction | 0.5h |
| Implement GameReflectionCache | GameReflectionCache.cs | Reflection caching | 1-2h |
| Integration testing | — | — | 1h |

**Success Criteria**:
- [ ] No static singletons (except RadioContext)
- [ ] All game integration behind interfaces
- [ ] Station data behind interface

## 4. Phase 3: Polish & Testing (Week 4)

### 4.1 Priority 5: Code Cleanup

**Effort**: 4-5 hours
**Risk**: Low
**Impact**: Medium

| Task | Files | Effort |
|------|-------|--------|
| Remove code duplication (6 groups) | Multiple | 2h |
| Add unit tests for math functions | Tests/ | 2h |
| Clean up Debug.Log with #if DEBUG | 12+ files | 1h |

**Success Criteria**:
- [ ] No code duplication
- [ ] 15+ unit tests for math functions
- [ ] No Debug.Log in Release

### 4.2 Priority 6: Data Externalization

**Effort**: 4-5 hours
**Risk**: Medium
**Impact**: Medium

| Task | New File | Purpose | Effort |
|------|----------|---------|--------|
| Create beacons.json | Config/beacons.json | Station data | 1-2h |
| Implement JsonStationRepository | JsonStationRepository.cs | JSON loading | 1-2h |
| Create bands.json | Config/bands.json | Dial configuration | 1h |
| Test with real data | — | — | 1h |

**Success Criteria**:
- [ ] Station data in JSON file
- [ ] Dial configuration in JSON file
- [ ] Easy to add new stations

## 5. Detailed Task Breakdown

### 5.1 Week 1: Safety & Critical Fixes

```
Day 1-2: Thread Safety (F01, F13, F29)
├── Implement double-buffer pattern for StationState[]
├── Remove Debug.Log + Time.time from audio thread
├── Add sync primitives (volatile, MemoryBarrier)
├── Cache AudioClip.Create
└── Test with real audio output

Day 3: Null Safety & Error Paths (F07, F137, F139, F152)
├── Add null-checks for 4 confirmed NRE sites
├── Clamp Math.Acos/Asin arguments (F137, F190, F191)
├── Replace UnpatchAll → UnpatchSelf (F121, F139)
└── Add Debug.LogException in catch blocks (F152)

Day 4: Quick Wins Part 1 (F65, F66, F67, F71, F77, F78)
├── Fix stale consolPattern in MorseKeyer.Reset (F65)
├── Fix NRE in tryGetCallsign + Toggle (F66, F67)
├── Cache Type.GetType in RadioStoryIntegration (F68)
├── Update Pattern in UpdateStationNavigation (F77)
└── Fix build case mismatch (F78)

Day 5: Quick Wins Part 2 + Validation (F79-F96)
├── Fix InBeam hardcoded distance (F79)
├── Clamp Haversine inputs (F80, F128)
├── MorseTiming rounding fix (F81)
├── Exception handling + cleanup (F82, F83)
└── Full regression testing (audio, navigation, UI)
```

### 5.2 Week 2: God-Object Decomposition — SonneController (F02, F17)

```
Day 1-2: Station Management (⚠ proposed — StationManager)
├── Extract station management logic (F02)
├── Update StationState usage (F17 struct→class)
├── Update SonneController to delegate
└── Test station functionality

Day 3-4: Signal & Position (⚠ proposed — SignalDecoder, PositionTracker)
├── Extract signal decoding logic (F02)
├── Extract position calculation (F02, F18)
├── Add IPositionCalculator interface (F18)
└── Test signal + position functionality

Day 5: Integration & Testing
├── Wire extracted classes together (F02)
├── Audio thread safety after refactor (F01)
└── Regression testing
```

### 5.3 Week 3: Architecture — UI Decomposition & Service Layer (F03, F04, F05, F23)

```
Day 1-2: InformationPanelHandler (F03)
├── ⚠ Extract UIElementFactory (F03)
├── ⚠ Extract StationInfoDisplay (F03)
├── ⚠ Extract StationFinder (F03)
└── Test UI functionality

Day 3-4: RadioStoryIntegration Refactor (F04, F23)
├── Extract game patches into separate patch classes (F04)
├── Add GameApiCompatibilityChecker fail-fast (F23)
├── Cache reflection Type lookups (F68)
└── Test patch reliability

Day 5: Static State → Instance (F05, F08)
├── Convert 3 static singletons to instances (F05)
├── Fix GC pressure from allocations (F08)
└── Full integration testing
```

### 5.4 Week 4: Polish, Quick Wins & Testing (F66–F199)

```
Day 1-2: Dead Code & Cleanup (F91, F92, F93, F157, F158, F159, F165, F193-F199)
├── Remove dead code: NormalizeAngle, unused fields, dead events, dead methods
├── Remove Debug.Log in Release builds (F93, F106)
├── Delete dead GetElementCount (F193)
└── Clean up dead events (F194, F195)

Day 3-4: DSP & Audio Fixes (F71, F94, F143, F153-F156, F169, F170, F177)
├── Optimize Random calls in DSP thread (F71, F177)
├── Double accumulator precision (F94)
├── Cache filter alpha (F155)
├── Clamp Atmosphere output (F143)
└── CarrierGenerator phase normalization (F169)

Day 5: Final Validation
├── Full regression testing
├── Performance benchmarking
├── Documentation update
└── Release preparation
```

## 6. Risk Mitigation

### 6.1 High-Risk Tasks

| Task | Risk | Mitigation |
|------|------|------------|
| Double-buffer pattern | Audio glitches | Test with real audio output |
| SonneController decomposition | Breaking changes | Comprehensive test suite |
| Reflection interface | Game compatibility | Fallback handling |
| Thread safety | Deadlocks | Lock-free patterns |

### 6.2 Testing Strategy

| Phase | Test Type | Coverage |
|-------|-----------|----------|
| Week 1 | Manual testing | Audio, navigation, UI |
| Week 2 | Unit + integration | Station management, signals |
| Week 3 | Integration | UI, services, full flow |
| Week 4 | Regression | Complete system |

### 6.3 Rollback Plan

```
Each phase creates a checkpoint:
- Week 1 checkpoint: Thread-safe audio
- Week 2 checkpoint: Decomposed SonneController
- Week 3 checkpoint: Decomposed UI + services
- Week 4 checkpoint: Complete system

If issues arise:
1. Stop current work
2. Revert to last checkpoint
3. Fix issues
4. Resume from checkpoint
```

## 7. Success Metrics

| Metric | Week 1 | Week 2 | Week 3 | Week 4 |
|--------|--------|--------|--------|--------|
| SonneController lines | 508 | <400 | <300 | <200* |
| InformationPanelHandler lines | 474 | 474 | <400 | <300* |
| Thread safety issues | 2 critical | 1 | 0 | 0 |
| Static singletons | 3 | 3 | 1* | 1* |
| Unit tests | 0 | 0* | 5* | 15+ |
| Magic numbers | ~40 | ~35 | ~20 | 0* |
| ADR-implemented | 0 | 0 | 0 | 1–2* |
| Documented problems (F-list) | 199 | — | — | — |

---

*Document Version: 2.0*  
*Last Updated: 2026-08-01*  
*Complete SAD audit: 199 problems cataloged (F01–F199). Effort estimate updated to ~95–130h. Phantom classes annotated `⚠ proposed` (F47). Phases restructured to match actual refactoring plan INDEX.md.*
