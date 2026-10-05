# Architecture Decisions — RadioNavigation Mod

> **Сверка 2026-10-04:** статус принятия решения и степень его реализации — разные
> вещи. Даты и исходные статусы ADR сохранены; дополнения ниже описывают текущий
> Source. Отдельных файлов ADR-001…007 нет, ADR-008…013 не использовались;
> `docs/adr/` содержит 21 файл ADR-014…034. Это не новый аудит всего проекта.

> **⚠️ Deep Scan Update (2026-08-02):** Полный аудит выявил критические расхождения между статусами ADR и реальностью. Всего **207 проблем** (F01–F207); статусы каждой — в `refactoring-plan/hypothesis/INDEX.md`.
> - **F45 (закрыто):** ADR-002/003/004/005/007 — статусы исправлены: «Accepted» → «Deferred»
> - **F46 (закрыто):** ADR-001 пример кода — заменён на реальное императивное Harmony-патчирование
> - **F47 (отклонено):** примеры реализации ссылались на несуществующие классы — убраны/аннотированы
> - **F48 (отклонено):** ADR-003 упоминал 4 станции — реальность 14 (аннотировано)
>
> Этот Deep Scan — снимок 2026-08-02. Его LOC, нереализованные примеры и выводы
> о roadmap не заменяют текущую сверку реализации в §1.1.

## 1. Decision Overview

| ID | Decision | Status | Date |
|----|----------|--------|------|
| ADR-001 | Use Harmony for runtime patching | Accepted | 2026-07-26 |
| ADR-002 | Implement double-buffer pattern for audio | Deferred; double-buffer не реализован | 2026-07-26 |
| ADR-003 | Decompose God Classes | Deferred; частичные выносы реализованы | 2026-07-26 |
| ADR-004 | Interface-based integration | Deferred | 2026-07-26 |
| ADR-005 | Centralize constants | Deferred; Constants и локальные контракты реализованы частично | 2026-07-26 |
| ADR-006 | JSON configuration files | Proposed; перенос радиостанций не выполнен | 2026-07-26 |
| ADR-007 | Unit testing strategy | Accepted (F22 baseline 182) | 2026-08-19 |
| ADR-014 | Baseline 182 + farClip tag | Accepted | 2026-08-19 |
| ADR-015 | Test Shield Gain/Geo | Accepted | 2026-08-19 |
| ADR-016 | Bushmills 89.9 data fix | Accepted | 2026-08-19 |
| ADR-017 | Lighthouse ResetStatics | Accepted | 2026-08-19 |
| ADR-018 | CameraResolver (маяк+звезда) | Accepted | 2026-08-19 |
| ADR-019 | Panel Hardening (PrefabNames+clamp) | Accepted | 2026-08-19 |
| ADR-020 | StarSky Phase0 log only | Proposed; исходный протокол дат не закрыт | 2026-08-19 |
| ADR-021 | StarSky catalog (5046 строк, эпоха 1926.0) | Accepted; актуальное дополнение в ADR | 2026-08-19 |
| ADR-022 | Созвездия (215 линий) | Accepted | 2026-08-19 |
| ADR-023 | Планеты (кеплеровы элементы + renderer) | Accepted | 2026-08-19 |
| ADR-024 | Луна (теперь Meeus + топоцентрический фазовый диск) | Accepted; исходный подход заменён P2 | 2026-08-19 |
| ADR-025 | STAR_SKY использует GameTime.GmtDateTime | Accepted | 2026-08-19 |
| ADR-026 | STAR_SKY visibility по Weather.Sky | Accepted | 2026-08-19 |
| ADR-027 | Собственный маркер маяка на карте (LighthouseMapMarker) | Accepted | 2026-08-22 |
| ADR-028 | Геометрия бандла = истина; свет на Spot Light; flare без лимита в device-режиме | Accepted | 2026-08-22 |
| ADR-029 | Coupling Assessment: 3 концентратора + static mutable; лечение через слайсы PLAN_Phase5, big-bang перенос отвергнут | Accepted | 2026-08-24 |
| ADR-030 | Land curation маяков — ручная правка `LeuchtfeuerExcluded.json` | Accepted | 2026-09-09 |
| ADR-031 | Fire-height curation — высота огня <15° через `LeuchtfeuerMinor.json` | Accepted | 2026-09-09 |
| ADR-032 | Screen-space деклаттер маркеров (greedy min-gap) | Accepted | 2026-09-09 |
| ADR-033 | Селектор тел для пеленга: Солнце, Луна, звезды | Accepted | 2026-09-16 |
| ADR-034 | Хронометр: двухзонная курсорная политика + expanded drag | Accepted | 2026-08-23 |

> **История нумерации.** До 2026-09-26 существовали **два** файла с номером ADR-023:
> `ADR-023-planets.md` (планеты, 2026-08-19) и файл хронометра, озаглавленный
> «ADR-023: курсорные зоны и расширенный драг» (2026-08-23; ныне ADR-034).
> Номер оставлен у более раннего ADR; хронометр переименован в **ADR-034** (свободный
> номер), ссылки в `SAD/04_Component_Architecture.md` и `AGENTS.md` обновлены.

### 1.1 Реализация и границы решения (2026-10-04)

| ADR | Что есть в Source / что не следует считать выполненным |
|---|---|
| 001 | `ModBootstrapper` владеет Harmony owner `UBOAT.Mods.RadioNavigation`, снимает только свои патчи; `HarmonyPatchInstaller` валидирует пять radio/locale-целей и отдельно устанавливает render/sky-патчи. Инъекция решения — **postfix** `ContinueStory`, Locale — prefix. |
| 002 | Main-thread scheduler/keyer и audio-thread DSP разделены, есть volatile-поля и проверки длины массивов. `StationStateBuffer`/lock-free command ring не реализованы; это не доказательство отсутствия всех cross-thread рисков. |
| 003 | Уже есть `StoryDecisionGateway`/`RadioLocale`, `ModBootstrapper`/`HarmonyPatchInstaller`/`SceneContentBuilder`/`PrefabValidator`, `SignalDecoder`/`StationManager`/`StationFleet`, UI/chronometer/lighthouse helpers. Исходные LOC и названия слайсов ниже — история; весь план Phase5 не объявляется завершённым. |
| 004 | Используются конкретные bridge/cache и `IPlayerPositionProvider`; универсальные `IGameServices`/`GameReflectionCache` из примера **не реализованы**. SourceCheck проверяет game API, но не отменяет runtime reflection. |
| 005 | `Source/Logic/Constants.cs` существует; математика/layout/оптика имеют отдельные семантические контракты. Исторический пример ниже не является буквальным содержимым Constants и не требует перенести туда все локальные числа. |
| 006 | `BeaconData.Stations` — пять CAP 59 станций в C#; lighthouse/curation/Sonne-данные используют JSON. Это не реализация предложенного `Config/beacons.json`. |
| 007 / 014 | xunit 2.9 + собственный раннер: **1379 PASS**, анализаторы **68**, SourceCheck **98**; текущий процент покрытия не измерен. Исторические NUnit-примеры и цели coverage не являются результатом. |
| 020 / 021 / 024 | Исходный Phase0 протокол остаётся Proposed; production-каталог, StarsMesh и топоцентрическая Луна уже реализованы. Дополнения ADR-021/024 отменяют старые epoch/renderer/time описания, но не переписывают дату решения. |

Действующие PIP-контракты и поздние доказательства — `docs/CAMERA_GUIDE.md`,
`docs/plans/SEXTANT_PLAN.md` §16.23–16.25. Приёмка лампы, cloud stage и компиляции не закрывает
обычный проход через люк, внешнюю эстетику PSF или остатки radio/live-чеклиста.
Далее §§2–10 сохраняют контекст исходных решений; примеры с пометкой proposed
не надо копировать как работающий API.

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
// Сокращённая иллюстрация; полный preflight/rollback — HarmonyPatchInstaller.
var harmony = new Harmony("UBOAT.Mods.RadioNavigation");
harmony.Patch(
    original: AccessTools.Method(typeof(RadioStory), "ContinueStory"),
    postfix: new HarmonyMethod(typeof(RadioStoryIntegration), "InjectT3PLL38Decision")
);
harmony.Patch(
    original: AccessTools.Method(typeof(Locale), nameof(Locale.RetrieveString), new[] { typeof(string) }),
    prefix: new HarmonyMethod(typeof(RadioStoryIntegration), "OverrideLocaleKey")
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
// Историческая схема, не текущий аудио-контракт; см. §1.1 (F01, F29).
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

**SonneController Decomposition** (исходное предложение, не текущий состав; см. §1.1):

| New Class | Responsibility | Lines | Status |
|-----------|----------------|-------|--------|
| ⚠ StationManager | Station management | ~100 | Proposed |
| ⚠ SignalDecoder | Signal processing | ~90 | Proposed |
| DiagnosticsCollector | Diagnostics | ~30 | Existing (inline) |
| SonneController | Orchestration | ~136 | Existing (508 LOC) |

**InformationPanelHandler Decomposition** (исходное предложение; UIElementFactory уже реализован):

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
// Исторический контекст 381 LOC; сейчас reflection делегируется StoryDecisionGateway.
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
// Исторический illustrative пример, не содержимое существующего Data/Constants.cs.
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
Accepted (2026-08-19, baseline ADR-014: 182 PASS)

> **Update 2026-08-19 (ADR-014):** F22 закрыто — `Tests/Runner` 182 PASS `powershell -ExecutionPolicy Bypass -File tests.ps1`, `dotnet build Release/Debug` 0 errors, tag `baseline-182-20260819` на `8b3021fa`. Цель `SAD/11` >80% Data / >70% Logic — частично достигнута (чистая математика покрыта, UI 0% — см. `docs/adr/ADR-014-baseline.md`).
>
> **Историческое состояние (2026-08-24):** 263 PASS. Текущие 1379 / 68 / 98 и
> отсутствие измеренного coverage — в §1.1 и `SAD/11_Testing_Strategy.md`.

### Context (на дату исходного предложения)
No unit tests existed; bugs were discovered in production. Это не текущее состояние.

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
public class SonneBeamTests {
    [Test]
    public void BuildPattern_UsesSixtyElements() {
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
| 2026-07-26 | ADR-003: Decompose | God classes hinder development |
| 2026-07-26 | ADR-004: Interfaces | Game updates break reflection |
| 2026-07-26 | ADR-005: Constants | Magic numbers cause bugs |
| 2026-07-26 | ADR-006: JSON | Configuration flexibility |
| 2026-07-26 | ADR-007: Testing | No tests = high risk |

## 10. Историческое предложение графика review (не текущие назначения)

| ADR | Review Date | Reviewer | Status |
|-----|-------------|----------|--------|
| ADR-001 | 2026-08-01 | Tech Lead | Active |
| ADR-002 | 2026-08-01 | Audio Lead | Deferred |
| ADR-003 | 2026-08-15 | Architect | Deferred |
| ADR-004 | 2026-08-15 | Integration Lead | Deferred |
| ADR-005 | 2026-08-01 | All | Deferred |
| ADR-006 | 2026-08-15 | Data Lead | Proposed |
| ADR-007 | 2026-08-01 | QA Lead | Deferred |
