# Executive Summary — RadioNavigation Mod Architecture

## Project Overview

**RadioNavigation** is a UBOAT mod for Sonne/Consol radio navigation, lighthouse
and native chart markers, chronometer UI, a dated celestial sky and a two-beam
PIP sextant with running/three-observation fixes. Namespace
`UBOAT.Mods.RadioNavigation`, manifest version **2.1**.

## Current State Assessment — 2026-10-04

| Область | Подтверждённое состояние |
|---|---|
| Offline behavioral tests | **1379 PASS / 0 FAIL**, xunit/custom runner; не процент coverage |
| Custom analyzers / SourceCheck | **68 / 98 PASS**; C# 7.0 и clean game API, structural gates |
| Runtime deployment | игра компилирует Source; raw-csc DLL — локальная проверка, не runtime-артефакт |
| PIP камеры | один маршрут attached/standalone, один writer внутри, единый parent frame обоих лучей, внутри full culling viewport |
| Live evidence | parent-frame ориентация, 24/24 записанных single-route открытий, Lamp GPU A/B/A′ 0→12→0 при неизменном main; ссылки в SEXTANT_PLAN §16.23–16.25 |
| Открытая приёмка | обычный hatch traversal, внешняя эстетика star/PSF, незавершённые radio/live чеклисты |
| Реестр / решения | **241** строка F001–F243 (F225/F230 свободны), **21** ADR-файл; статусы — записанные доказательства, не новый полный аудит |

Карта документации — `DOCS.md`; ответственность компонентов — `SAD/04`,
эксплуатация камер — `docs/CAMERA_GUIDE.md`, границы приборов —
`docs/live-diagnostics.md`. Документационный проход не меняет code/shaders/bundle
и не закрывает pending по отсутствию ошибки в snapshot.

## Historical State Assessment — 2026-08-24

Снимок раннего аудита ниже сохранён для истории. LOC, оценки 4/10, сроки,
проценты и предлагаемый ROI **не являются актуальными измерениями**.

| Metric | Initial SAD v2.0 estimate | Recorded 2026-08-24 |
|--------|--------------------|---------------------|
| Overall Rating | 4/10 | 4/10 |
| God Classes (>300 lines) | 2 | 6+ (SonneController, IPH, ChronometerOverlayHandler, LighthouseBeamGlow, LighthousePlacer, LighthouseMapMarker — см. `refactoring-plan/PLAN_Phase5.md`) |
| Test count (not coverage) | tests отсутствовали в исходной оценке | **263 PASS** (тогдашний прогон) |
| Magic Numbers | ~40 | частично вынесены в `Constants.cs` (F28) |
| Code Duplication Groups | 7 | сокращены (GeoMath/Constants, F12/F73) |
| Thread Safety Issues | Widespread | смягчено (volatile-флаги, main-thread keyer); остаток — Phase 5 |
| Static Singletons | 3 | 3 |
| Documented Problems | — | **207** (F01–F207), статусы в `hypothesis/INDEX.md`, закрытие в `RESOLVED_LEDGER.md` |
| Total Refactoring Effort | — | ~95–130 hours |

## Historical Key Findings (audit 2026-08)

Декомпозиция позднее выполнена **частично**: bootstrap и gateway вынесены,
Sonne/IPH получили helpers, но не весь предложенный snapshot/interface-дизайн.
Сверка — `refactoring-plan/PLAN_Phase5.md` и `refactoring-plan/RESOLVED_LEDGER.md`
§7. Следующий список — происхождение задач,
не команда повторить уже сделанные слайсы; совместимость game API всё ещё
требует version-aware guards и реальной приёмки, не обещания «Stable».

### Strengths
- **Strong Domain Logic**: Mathematically correct Sonne navigation, Morse timing, and bearing diagnostics
- **Clear Layer Separation**: Data / Logic / Sound / UI directories well-organized
- **State Machine Pattern**: Proper use in `StationScheduler` and `MorseKeyer`
- **Template Method**: `SpinnerHandlerBase` provides good extensibility point
- **Error Handling**: Graceful rollback in `Main.OnLoaded()` when patches fail

### Critical Issues

1. **Thread Safety (P0)**: Audio thread (`OnAudioFilterRead`) and main thread (`Update()`) share mutable `StationState[]` without synchronization — potential crashes and audio artifacts. *Статус 2026-08-24: смягчено — volatile-флаги, keyer переведён на main thread (F189-redesign); остаток в Phase 5.*

2. **God Classes (P0)**:
   - `SonneController` (~724 lines) — mixes navigation, audio, UI, and decoding
   - `InformationPanelHandler` (~502 lines) — mixes UI creation, station finding, and diagnostics
   - *Новое поколение (PLAN_Phase5):* `LighthouseBeamGlow` 1369, `ChronometerOverlayHandler` 572, `LighthousePlacer` 541, `LighthouseMapMarker` 458

3. **Reflection Fragility (P0)**: `RadioStoryIntegration` used 9 reflection members that broke on game updates (F23, F68; цепочка G1–G4). ***FIXED 2026-08-23*** — заменён фасадом + `StoryDecisionGateway.cs` (commit 28f4a62).

4. **Debug.Log in Audio Thread (P0)**: Non-thread-safe Unity logging in `AudioSynthesisEngine.ProcessAudio()`. *FIXED — логи удалены/обёрнуты в `#if DEBUG`.*

### SAD Documentation Status

**The SAD contained significant inaccuracies** (see F45-F52, исправлено в v2.0):
- 5 ADRs changed «Accepted» → «Deferred» (F45, исправлено)
- Harmony patch targets corrected: `Game.OnLoaded` → `RadioStory.ContinueStory` + 4 others (F46, исправлено)
- 14+ non-existent classes annotated `⚠ proposed` (F47, исправлено)
- File counts, LOC, array sizes, method signatures corrected (F48-F52, исправлено)
- Added F65–F199 (аудит завершён на **207 проблемах**, F01–F207) across all layers: documentation, data, localization, UI, sound, logic

## Historical Recommended Approach (не текущий план исполнения)

### Phase SAD: Documentation Overhaul (Priority) — 8-12 hours
- Fix all SAD inaccuracies (F45-F52)
- Update ADR statuses to reflect reality

### Phase 1: Safety (Week 1) — 8-12 hours
- Fix thread-safety with double-buffer pattern
- Remove Debug.Log from audio thread
- Cache AudioClip.Create

### Phase 2: Architecture (Weeks 2-3) — 20-25 hours
- Decompose SonneController into 4-5 focused classes
- Decompose InformationPanelHandler into 3-4 classes
- Introduce RadioContext to replace static singletons
- Create IStationRepository interface

### Phase 3: Polish (Week 4) — 8-10 hours
- Extract magic numbers to Constants.cs
- Remove code duplication
- Add unit tests for mathematical functions
- Clean up dead code

## Historical Business Impact (прогноз, не измеренный результат)

| Impact Area | Current Risk | Post-Refactoring |
|-------------|--------------|------------------|
| Feature Development | Slow — changes ripple through God classes | Fast — isolated, testable components |
| Game Update Compatibility | Fragile — reflection breaks easily | Stable — interface-based integration |
| Bug Discovery | Hard — no tests, tangled dependencies | Easy — unit tests catch regressions |
| New Developer Onboarding | Difficult — unclear responsibilities | Clear — single-responsibility classes |

## Historical Investment Summary (оценка исходного аудита)

- **Total Effort**: ~95–130 hours (12 phases, 207 tasks)
- **Risk Level**: Medium-High (audio thread changes require careful testing)
- **Expected ROI**: 3-4x faster feature development post-refactoring
- **SAD Overhaul**: ~8 hours (completed — this update)

### Phase Breakdown

| Phase | Focus | Hours | Key Tasks |
|-------|-------|-------|-----------|
| Phase 0 | Test infrastructure | 8-12h | F22: исходно предлагался NUnit; реализован xunit + custom runner |
| Phase 1 | Quick wins | 5-8h | F77-F164: Low-risk fixes |
| Phase 2 | Safety & correctness | 15-25h | F01, F13, F29: Thread safety |
| Phase 3 | Architecture blockers | 12-18h | F05, F23: Static state, reflection |
| Phase 4 | God-object decomposition | 12-20h | F02, F03: Class extraction |
| Phase 5 | API design & polish | 10-15h | F65-F207: Dead code, clamping, consistency |

---

*Document Version: 2.2*

*Last Updated: 2026-10-04 (актуальная сводка отдельно от истории аудита)*
*Note: Updated for 207 problems (F01–F207). Статусы каждой гипотезы — `refactoring-plan/hypothesis/INDEX.md`; закрытие с доказательствами — `refactoring-plan/RESOLVED_LEDGER.md`.*
