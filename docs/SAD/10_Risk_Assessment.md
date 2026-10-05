# Risk Assessment — RadioNavigation Mod

> **Граница актуальности 2026-10-04:** вероятности/оценки/сроки и 33 риска ниже —
> историческая оценка раннего аудита, не новая текущая risk-матрица. Частичная
> декомпозиция и guards уже есть, double-buffer не внедрён; SourceCheck проверяет
> clean game API/C# 7.0, а PVS и unit-тесты не подтверждают GPU/переходы в игре.
> Реализованное и живой остаток — `PLAN_Phase5.md`, Phase6 §9,
> `docs/plans/SEXTANT_PLAN.md` §16.23–16.25, `docs/live-diagnostics.md`. Старые «ни один ADR
> не реализован» и ошибочные Harmony targets не описывают текущий Source.

> **⚠️ Deep Scan Update (2026-08-01):** Глобальный аудит выявил **207 проблем** (F01–F207). Добавлены критические риски из F65–F199:
> - **F45 (P0):** SAD документирует 5 ADR как «Accepted» — ни один не реализован
> - **F46 (P0):** SAD описывает неверные Harmony patch targets
> - **F47 (P1):** 14+ несуществующих классов в SAD — документацию невозможно использовать как reference
> - **F65-F76:** 12 новых проблем (stale consolPattern, NRE, unchached reflection, etc.)
> - **F77-F199:** 122 дополнительных проблем (NaN clamping, truncation, dead code, missing default cases, event leaks)
> - **Cross-file:** 6 dead методов в AudioSynthesisEngine, 16/17 констант PrefabNames не используются
> - **F200-F207 (новое 2026-08-01):** 8 дополнительных проблем. F200 (P0): устаревшая цепочка position fix удалена; F201-F202 (P1): двойная передача Consol-паттерна за цикл; сдвиг 90° у спиннеров. F203/F204/F207 (P2): кросс-талки при QF=20; антимеридиан-среднее долгот; перехват drag с дочерних контролов. F205-F206 (P3): кириллическая «В°»; тики по кадрам. Итого **207 проблем**.
>
> **Итого:** 207 проблем, ~95-130 часов рефакторинга + тестовая инфраструктура (F22: 8-12 часов) + F200-F207 (~4-6 часов).
> Полные описания: `refactoring-plan/hypothesis/INDEX.md` (роллап F01–F207; детали в `confirmed\`/`rejected\`).

## 1. Risk Overview

| Category | High | Medium | Low | Total |
|----------|------|--------|-----|-------|
| Technical | 5 | 6 | 4 | 15 |
| Process | 2 | 3 | 2 | 7 |
| External | 2 | 2 | 1 | 5 |
| Documentation | 1 | 2 | 3 | 6 |
| **Total** | **10** | **13** | **10** | **33** |

## 2. High-Risk Items

### 2.1 Thread Safety in Audio Processing

**Risk ID**: TECH-001
**Probability**: High
**Impact**: Critical
**Severity**: **Critical**

**Description**:
Audio thread (`OnAudioFilterRead`) and main thread (`Update()`) share mutable `StationState[]` without synchronization. This causes:
- Race conditions
- Audio artifacts
- Potential crashes

**Affected Components**:
- `SonneController.cs`
- `AudioSynthesisEngine.cs`
- `StationState.cs`

**Mitigation**:
- Implement double-buffer pattern
- Lock-free ring buffer for command passing
- Separate read/write buffers

**Effort**: 8-12 hours
**Owner**: Audio Team
**Status 2026-08-24**: частично закрыт — volatile-флаги (F01/F29), keyer/scheduler переведены на main thread (F189-redesign), `Debug.Log` из аудио-треда убран (F106/F171). Полный double-buffer не требуется в текущей архитектуре; остаточный риск пересматривается в Phase 5 (`refactoring-plan/PLAN_Phase5.md`).

### 2.2 Reflection Fragility

**Risk ID**: TECH-002
**Probability**: High
**Impact**: Critical
**Severity**: **Critical**

**Description**:
Deals with this — 9 reflection members, not 12 (F50). Any game update can break:
- Type resolution (`typeof(RadioStory).BaseType`)
- Constructor discovery (filtered by param count)
- Property access (`LocalizationKey`, `Decisions`)
- Method invocation (`InsertDecision`, `OnUpdated`)

**Affected Components**:
- `RadioStoryIntegration.cs`
- `Main.cs`

**Mitigation**:
- Interface-based integration
- Cached reflection with fallbacks
- Version-gate validation

**Effort**: 6-8 hours
**Owner**: Integration Team

### 2.3 God Class Decomposition

**Risk ID**: TECH-003
**Probability**: Medium
**Impact**: High
**Severity**: **High**

**Description**:
Decomposing `SonneController` (508 lines — F50: was 506) into 4-5 classes risks:
- Breaking existing functionality
- Missing dependencies
- Integration issues

**Affected Components**:
- `SonneController.cs`
- All dependent components

**Mitigation**:
- Comprehensive test suite before decomposition
- Incremental extraction
- Integration testing after each extraction

**Effort**: 15-20 hours
**Owner**: Architecture Team

## 3. Medium-Risk Items

### 3.1 Game Version Compatibility

**Risk ID**: EXT-001
**Probability**: Medium
**Impact**: High
**Severity**: **High**

**Description**:
UBOAT game updates may change:
- Assembly-CSharp.dll structure
- Unity version
- API methods

**Mitigation**:
- Interface-based integration
- Version detection and fallbacks
- Multi-version support

**Effort**: 4-6 hours
**Owner**: Integration Team

### 3.2 Audio Quality Regression

**Risk ID**: TECH-004
**Probability**: Medium
**Impact**: Medium
**Severity**: **Medium**

**Description**:
Refactoring audio pipeline may introduce:
- Signal degradation
- Latency issues
- Phase problems

**Mitigation**:
- Audio quality testing
- A/B comparison with original
- Performance benchmarking

**Effort**: 4-6 hours
**Owner**: Audio Team

### 3.3 UI Behavior Changes

**Risk ID**: TECH-005
**Probability**: Medium
**Impact**: Medium
**Severity**: **Medium**

**Description**:
Decomposing `InformationPanelHandler` may change:
- UI layout
- Event timing
- User interactions

**Mitigation**:
- Visual regression testing
- User acceptance testing
- Incremental changes

**Effort**: 6-8 hours
**Owner**: UI Team

### 3.4 Thread Synchronization Issues

**Risk ID**: TECH-006
**Probability**: Medium
**Impact**: Medium
**Severity**: **Medium**

**Description**:
Double-buffer pattern may introduce:
- Deadlocks
- Priority inversion
- Performance overhead

**Mitigation**:
- Lock-free patterns
- Performance testing
- Stress testing

**Effort**: 4-6 hours
**Owner**: Audio Team

### 3.5 Data Externalization

**Risk ID**: PROC-001
**Probability**: Medium
**Impact**: Low
**Severity**: **Low**

**Description**:
Moving data to JSON may cause:
- Loading failures
- Format errors
- Performance issues

**Mitigation**:
- Fallback to hardcoded data
- JSON validation
- Performance testing

**Effort**: 2-3 hours
**Owner**: Data Team

## 4. Low-Risk Items

### 4.1 Magic Number Extraction

**Risk ID**: PROC-002
**Probability**: Low
**Impact**: Low
**Severity**: **Low**

**Description**:
Extracting magic numbers may introduce:
- Typos
- Incorrect values
- Missing constants

**Mitigation**:
- Code review
- Unit tests
- Regression testing

**Effort**: 1-2 hours
**Owner**: All

### 4.2 Code Duplication Removal

**Risk ID**: PROC-003
**Probability**: Low
**Impact**: Low
**Severity**: **Low**

**Description**:
Removing duplicated code may:
- Break subtle differences
- Introduce bugs

**Mitigation**:
- Careful analysis
- Unit tests
- Integration testing

**Effort**: 2-3 hours
**Owner**: All

### 4.3 Unit Test Addition

**Risk ID**: PROC-004
**Probability**: Low
**Impact**: Low
**Severity**: **Low**

**Description**:
Adding unit tests may:
- Reveal existing bugs
- Require code changes

**Mitigation**:
- Fix bugs as discovered
- Update code to be testable

**Effort**: 3-4 hours
**Owner**: QA Team

## 5. Risk Matrix

```
Impact
  │
  │  ┌─────────┬─────────┬─────────┐
  │  │         │         │ TECH-002│
  │  │         │         │ EXT-001 │
  │  │         ├─────────┼─────────┤
  │  │         │ TECH-003│ TECH-004│
  │  │         │ TECH-005│ TECH-006│
  │  ├─────────┼─────────┼─────────┤
  │  │ PROC-002│ PROC-001│         │
  │  │ PROC-003│         │         │
  │  │ PROC-004│         │         │
  │  └─────────┴─────────┴─────────┘
  │         Low       Medium      High
  └───────────────────────────────── Probability
```

## 6. Risk Mitigation Strategies

### 6.1 Technical Risk Mitigation

| Strategy | Applicable Risks | Implementation |
|----------|------------------|----------------|
| Double-buffer pattern | TECH-001, TECH-006 | Lock-free ring buffer |
| Interface-based integration | TECH-002, EXT-001 | Abstract game APIs |
| Incremental decomposition | TECH-003 | Extract one class at a time |
| Comprehensive testing | All | Unit, integration, regression |

### 6.2 Process Risk Mitigation

| Strategy | Applicable Risks | Implementation |
|----------|------------------|----------------|
| Code review | PROC-002, PROC-003 | Peer review all changes |
| Test-driven development | PROC-004 | Write tests first |
| Version control | All | Git branching strategy |
| Documentation | All | Update docs with changes |

### 6.3 External Risk Mitigation

| Strategy | Applicable Risks | Implementation |
|----------|------------------|----------------|
| Version detection | EXT-001 | Check game version at startup |
| Fallback handling | EXT-001, TECH-002 | Graceful degradation |
| Community testing | All | Beta testing program |

## 7. Risk Monitoring

### 7.1 Key Risk Indicators

| Indicator | Threshold | Action |
|-----------|-----------|--------|
| Audio glitches | > 0 per hour | Investigate immediately |
| Game crashes | > 0 per session | Rollback to last checkpoint |
| Test failures | > 5% | Fix before proceeding |
| Performance regression | > 10% | Optimize before release |

### 7.2 Risk Review Schedule

| Review | Frequency | Participants |
|--------|-----------|--------------|
| Daily standup | Daily | All team |
| Weekly risk review | Weekly | Tech leads |
| Phase gate review | End of phase | All stakeholders |

## 8. Contingency Plans

### 8.1 Audio Thread Issues

**Trigger**: Audio glitches or crashes
**Plan**:
1. Revert to original audio implementation
2. Investigate root cause
3. Implement fix with additional testing
4. Re-deploy with monitoring

### 8.2 Game Update Breaks Mod

**Trigger**: Mod fails to load after game update
**Plan**:
1. Disable affected features
2. Investigate API changes
3. Implement compatibility layer
4. Release hotfix

### 8.3 Decomposition Breaks Functionality

**Trigger**: Tests fail after decomposition
**Plan**:
1. Revert to pre-decomposition state
2. Analyze missing dependencies
3. Fix decomposition plan
4. Redo extraction with fixes

### 8.4 Event Subscription Leaks

**Risk ID**: TECH-008
**Probability**: High
**Impact**: Medium
**Severity**: **Medium**

**Description**:
UI handlers subscribe to events but don't unsubscribe in `OnDestroy` (F14, F30, F117, F119). SonneController doesn't unsubscribe from keyer events (F117). RadioHandler doesn't unsubscribe from ZoneHandler (F119).

**Affected Components**:
- `RadioHandler.cs`, `ZoneHandler.cs`, `SonneController.cs`

**Mitigation**:
- Add unsubscribe in all `OnDestroy()` methods
- Use weak event pattern or explicit lifecycle management

**Effort**: 1-2 hours
**Owner**: UI Team

### 8.5 Dead Code in Audio Pipeline

**Risk ID**: TECH-009
**Probability**: Medium
**Impact**: Low
**Severity**: **Low**

**Description**:
`AudioSynthesisEngine.UpdateStationBeats()` — 0 callers (F54 dead code). `BearingTable` dead code in StationScheduler/SonneSignalGenerator (F34). Many `PrefabNames` constants unused (16/17).

**Affected Components**:
- `AudioSynthesisEngine.cs`, `SonneController.cs`, `PrefabNames.cs`

**Mitigation**:
- Remove dead code (F91-F96), unused methods (F34)
- Clean up PrefabNames constants

**Effort**: 1-2 hours
**Owner**: All

### 8.6 Build Configuration Conflict

**Risk ID**: PROC-005
**Probability**: Medium
**Impact**: Medium
**Severity**: **Medium**

**Description**:
`build.ps1` uses `/debug:full` + `/optimize+` simultaneously (F196). Debug symbols with optimizations causes issues with debugging and potential codegen differences.

**Affected Components**:
- `build.ps1`

**Mitigation**:
- Separate Debug (`--debug:full`, no optimize) and Release (`--optimize+`, no debug) configurations (F196)

**Effort**: 5 min
**Owner**: Build Team

## 9. Risk Budget

| Category | Hours | Budget |
|----------|-------|--------|
| Technical risk mitigation | 35-45 | High |
| Process risk mitigation | 6-9 | Medium |
| External risk mitigation | 4-6 | Medium |
| Documentation | 8-12 | Medium |
| **Total** | **53-72** | — |

## 10. Risk Acceptance

| Risk | Acceptance Criteria |
|------|---------------------|
| TECH-001 (Thread Safety) | Must be fixed before release |
| TECH-002 (Reflection) | Must be fixed before release |
| TECH-003 (Decomposition) | Can be deferred if critical |
| TECH-007 (NaN/Math) | Must be fixed before release |
| TECH-010 (Position Feed) | Must be fixed before release (or hide fix without data) |
| EXT-001 (Game Version) | Must be monitored continuously |
| All others | Acceptable with mitigation |

---

*Document Version: 2.0*  
*Last Updated: 2026-08-24 — TECH-001 получил статус частичного закрытия; счётчик проблем приведён к 207.*  
*Updated: Risk catalog expanded from 22 to 33 items. Added TECH-007 (NaN), TECH-008 (event leaks), TECH-009 (dead code), PROC-005 (build config). Risk budget increased to 53-72 hours. Documentation category added.*
