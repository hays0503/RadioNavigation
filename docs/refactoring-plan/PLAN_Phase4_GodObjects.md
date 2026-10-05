# Phase 4: God-Object декомпозиция (SAD 08/09) — план без кода

> **Статус 2026-08-24:** Slice 4.1 (F04 → `StoryDecisionGateway.cs`) ВЫПОЛНЕН — commit 28f4a62; остаток слайсов superseded планом `PLAN_Phase5.md` (P5.1–P5.8). Этот документ — исторический.

> **Уточнение 2026-10-04:** G1–G4 исправлены именно gateway-фиксом 2026-08-23;
> ранняя фраза «уже закрыто 2026-08-01» ниже была неверной оценкой исходного плана.
> Часть слайсов Phase5 реализована, часть предложений отсутствует — читать его
> актуальную шапку. Для мода использовать raw-csc `build.ps1`, не `dotnet build`.
> Preflight подтверждён поздними загрузками (5/5), но остальные live-критерии
> §4.0 не закрываются этим результатом. Hot-reload заменён restart.

> Дата: 2026-08-21. Статус: план, код не трогаем до green live 1-3.
> Принцип: 1 PR = 1 god-object slice + 208 PASS + Build OK (best practice).

## Почему deferred

- `DEFERRED.md` (2026-08-02) пометил F02/F03/F04/F21/F25 как SAD 08 roadmap — требуют 12-20ч и меняют публичную поверхность (`SonneController`, `InformationPanelHandler`, `Main`, `AudioSynthesisEngine`).
- Live 1-3 (lighthouse, Phase0 probe, F71/callsign/DASH) ещё не прогнаны в игре — любой slice сейчас удвоит регрессию.
- F04 reflection fragility (G1-G4) уже закрыт: `RadioStoryIntegration.cs` 2026-08-01 содержит `FindMethodDeep` (G2/G3), `localizationKeyField` (G4), `Convert.ToInt32(stageField)` (G1) — `git diff main -- RSI` 0 строк, 208 PASS. Остаток F04 — только архитектурный split, как у F02/F03.

## Что остаётся (оценка)

| ID | God | LOC | Ответств. | Slice | Час | Риск | Зависит |
|----|-----|-----|-----------|-------|-----|------|---------|
| F02 | SonneController 508 | 9 (sched/keyer/carrier/gain/nav/bearing/signal/decode/audio) | `StationManager` + `SignalDecoder` + `AudioCoordinator` | 6-8 | Высокий (audio thread) | F01/F05 уже DONE, нужен Phase0 live |
| F03 | InformationPanelHandler 474 | 9 (labels/layers/beacon/zone/ground/volume/feedback/cursor) | `UIElementFactory` + `PanelState` + `DiagnosticsBinder` | 4-6 | Средний (prefab paths) | F21, PrefabNames |
| F04 | RadioStoryIntegration 435 | 3 (reflect/type/locale) | `StoryDecisionGateway` + `LocaleResolver` | 2 | Низкий (изолирован) | — |
| F21 | Main.cs 277 | 5 (harmony/DI/prefab/sky/lighthouse) | `ModBootstrapper` + `PrefabValidator` | 2-3 | Средний (DI) | F03/F04 |
| F18 | PositionFixCalculator god | 60 | 3 (haversine/intersection/error) | 2 | Низкий | F22 тесты |
| F25 | AudioSynthesisEngine coupling | — | `DspPipeline` inject `IReadOnlyStationState` | 3-4 | Высокий (DSP) | F02 |

**Итого 19-25ч** — не начинать до `LIVE_VALIDATION_CHECKLIST.md` live галочек.

## Порядок по best practice (1 slice = 1 PR)

### 4.0 Gate (обязательно)
- [x] Preflight `target methods not found` — поздний live `Patches applied: 5/5`; не подтверждение отсутствия любых ошибок всего проекта.
- [ ] `StarSkyPhase0Probe` 2 даты vs Stellarium → `ADR-020 Accepted`
- [ ] `F71` pause/fast-forward live ок, `callsign` один, `DASH ▬→◆◆→•` live ок

### 4.1 F04 slice 1 — `StoryDecisionGateway` (2ч, начать здесь после gate)
- Вынести `FindMethodDeep`, `InsertDecision`, `onUpdatedMethod`, `stageField`/`decisionsProp` в `StoryDecisionGateway.cs` (pure reflection, 100% cover via `Tests/Stubs/StoryStub.cs`).
- `RadioStoryIntegration` остаётся фасадом 60 LOC, делегирует gateway + `LocaleResolver`.
- Тесты: `RadioStoryGatewayTests` — injection, dedup, `IsStageMainChoices` boxed enum, `GetLocalizationKey` field vs prop.
- Merge критерий: 208→212 PASS, `git diff -- RSI` только делегирование.

### 4.2 F21 slice — `ModBootstrapper` (2ч)
- Вынести `Harmony` setup + `SceneEventsListener` wiring + `InjectIntoAssembly` из `Main.cs` в `ModBootstrapper.cs`.
- `Main` остаётся `IUserMod` 40 LOC.
- Тесты: `MainBootstrapTests` — pre-flight missing target → error, `UnpatchAll(Id)` не трогает чужие.

### 4.3 F02 slice 1 — `SignalDecoder` (3ч)
- Вынести `signalEntries/morseDecoderBuffer/decodedText`, `OnStationElementCompleted/DecodeCallsignBuffer/GetSignalRichText` в `SignalDecoder.cs` (main-thread only).
- `SonneController` держит `SignalDecoder` как поле, проксирует `GetReceivedNavigationDotCount`.
- Тесты: уже есть `SignalRichTextFormatterTests` + `MorseKeyerTests`; добавить `SignalDecoderTests` 6 кейсов (callsign last-only, DASH order, TrimEntries cap 20/60).

### 4.4 F02 slice 2 — `StationManager` (3ч)
- Вынести `stations/schedulers/keyers/carriers`, `UpdateStationNavigation/UpdateStationGains/CalculateMapBearing` в `StationManager.cs`.
- API: `IReadOnlyList<StationState>` + `DominantStationIndex` → `AudioSynthesisEngine` получает интерфейс, не массив.

### 4.5 F03 slice 1 — `UIElementFactory` (2ч)
- Вынести 5 `GetComponentInChildren` + `CreateLabel` + font fallback (`LegacyRuntime.ttf`) в `UIElementFactory.cs`.
- `InformationPanelHandler` 474→300 LOC.

### 4.6 Остальное — по необходимости после 4.1-4.5 green

## Чего НЕ делать сейчас
- Не менять `DspPipeline` Random per sample (F71) — звук меняется, нужен live listening.
- Не трогать `Constants`/`ComparisonExtensions` массово (93 call sites) — стайл, не блокер.
- Не вводить `netstandard2.1` / `Span<T>` — игра `netstandard2.0`.

## Критерий выхода Phase 4
- Каждый slice: `powershell -File tests.ps1` 208+N PASS, `dotnet build -c Release` 0 errors, `build.ps1` OK, `graphify update .` без топо-изменений кроме вынесенного класса.
- Итог: `SAD/06` god-граф пересчитан, `hypothesis/INDEX.md` F02/F03/F04 `⏸→✅` с ссылкой на PR, `DEFERRED.md` строка удалена.

## Следующий шаг <2 мин (после gate)
```powershell
git checkout -b refactor/f04-gateway
# 1 файл: Source/Logic/StoryDecisionGateway.cs + Tests/StoryDecisionGatewayTests.cs
powershell -File tests.ps1
```

Refs: `SAD/06_God_Object_Analysis.md`, `SAD/08_Target_Architecture.md`,
`archive/09_Refactoring_Roadmap.md`, `hypothesis/INDEX.md` F02-F04,
`game-source-findings/` (G1–G4), `LIVE_VALIDATION_CHECKLIST.md`.
