# Refactoring Roadmap — RadioNavigation Mod

> **⚠️ Deep Scan Update (2026-07-30):** Проведён тотальный перекрёстный аудит (SAD vs code, git, build/config, cross-file consistency). Итого ~76 проблем (F1–F76). Оценка времени ~95–125 часов + Фаза 0 (F22: 8–12 часов тестовой инфраструктуры). Quick wins: 33 + 8 документационных. Добавлены F65–F76.

## 1. Roadmap Overview

```
Week 1: Safety & Critical Fixes
Week 2-3: Architecture Decomposition
Week 4: Polish & Testing
```

**Total Effort**: ~35-45 hours (8-13 working days)
**Risk Level**: Medium-High

## 2. Phase 1: Safety & Critical Fixes (Week 1)

### 2.1 Priority 0: Thread Safety

**Effort**: 8-12 hours
**Risk**: High
**Impact**: Critical

| Task | Files | Effort |
|------|-------|--------|
| Implement double-buffer pattern | SonneController.cs, AudioSynthesisEngine.cs | 4-6h |
| Remove Debug.Log from audio thread | AudioSynthesisEngine.cs | 1h |
| Cache AudioClip.Create | SonneController.cs | 0.5h |
| Add try/catch to OnAudioFilterRead | SonneController.cs | 0.5h |
| Test with real audio output | — | 2-3h |

**Success Criteria**:
- [ ] No race conditions in audio thread
- [ ] No Debug.Log in production audio code
- [ ] AudioClip created once per session
- [ ] Audio exceptions handled gracefully

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

### 3.1 Priority 2: SonneController Decomposition

**Effort**: 15-20 hours
**Risk**: Very High
**Impact**: Very High

| Task | New File | From SonneController | Effort |
|------|----------|---------------------|--------|
| Extract StationManager | StationManager.cs | stations[], UpdateStationGains(), UpdateStationNavigation() | 4-5h |
| Extract SignalDecoder | SignalDecoder.cs | signalEntries, OnStationElementCompleted(), DecodeCallsignBuffer() | 3-4h |
| Extract PositionTracker | PositionTracker.cs | measurements, lastFix, UpdatePosition() | 2-3h |
| Extract DiagnosticsCollector | DiagnosticsCollector.cs | GetStationDiagnostics(), FormatDiagnosticString() | 1h |
| Refactor SonneController | SonneController.cs | Orchestration only (~136 lines) | 3-4h |
| Integration testing | — | — | 2-3h |

**Success Criteria**:
- [ ] SonneController < 200 lines
- [ ] Each extracted class < 150 lines
- [ ] No cross-dependencies between extracted classes
- [ ] All functionality preserved

### 3.2 Priority 3: InformationPanelHandler Decomposition

**Effort**: 8-10 hours
**Risk**: Medium-High
**Impact**: High

| Task | New File | From InformationPanelHandler | Effort |
|------|----------|------------------------------|--------|
| Extract UIElementFactory | UIElementFactory.cs | CreateLabel(), CreateSeparator(), CreateImage() | 3-4h |
| Extract StationInfoDisplay | StationInfoDisplay.cs | RefreshAll(), UpdateStationInfo(), UpdatePatternDisplay() | 2-3h |
| Extract StationFinder | StationFinder.cs | FindClosestStation(), GetZoneIndex(), IsGroundActive() | 1-2h |
| Refactor InformationPanelHandler | InformationPanelHandler.cs | Orchestration only (~124 lines) | 1-2h |
| Integration testing | — | — | 1h |

**Success Criteria**:
- [ ] InformationPanelHandler < 200 lines
- [ ] Each extracted class < 150 lines
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

### 5.1 Week 1: Safety

```
Day 1-2: Thread Safety
├── Implement double-buffer pattern
├── Remove Debug.Log from audio thread
├── Cache AudioClip.Create
└── Test with real audio output

Day 3: Quick Wins
├── Extract magic numbers to Constants.cs
├── Remove ComparisonExtensions.cs
├── Fix BfoBase duplicate
└── Remove dead code

Day 4-5: Validation
├── Full regression testing
├── Performance testing
└── Audio quality verification
```

### 5.2 Week 2: SonneController Decomposition

```
Day 1-2: StationManager
├── Extract StationManager class
├── Move station management logic
├── Update SonneController to use StationManager
└── Test station functionality

Day 3-4: SignalDecoder
├── Extract SignalDecoder class
├── Move signal processing logic
├── Update SonneController to use SignalDecoder
└── Test signal functionality

Day 5: PositionTracker
├── Extract PositionTracker class
├── Move position calculation logic
├── Update SonneController to use PositionTracker
└── Test position functionality
```

### 5.3 Week 3: UI Decomposition & Service Layer

```
Day 1-2: InformationPanelHandler
├── Extract UIElementFactory
├── Extract StationInfoDisplay
├── Extract StationFinder
└── Test UI functionality

Day 3-4: Service Layer
├── Create IRadioState interface
├── Implement RadioContext
├── Create IStationDataSource interface
├── Implement HardcodedStationRepository
└── Update all references

Day 5: Integration
├── Full integration testing
├── Performance testing
└── Regression testing
```

### 5.4 Week 4: Polish & Testing

```
Day 1-2: Code Cleanup
├── Remove code duplication
├── Add unit tests for math functions
└── Clean up Debug.Log

Day 3-4: Data Externalization
├── Create beacons.json
├── Implement JsonStationRepository
├── Create bands.json
└── Test with real data

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
| SonneController lines | 506 | <300 | <200 | <200 |
| InformationPanelHandler lines | 474 | 474 | <200 | <200 |
| Thread safety issues | 2 | 0 | 0 | 0 |
| Static singletons | 5+ | 5+ | 1 | 1 |
| Unit tests | 0 | 5 | 10 | 15+ |
| Magic numbers | 25+ | 0 | 0 | 0 |
