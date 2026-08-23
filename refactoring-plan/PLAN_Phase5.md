# Phase 5: Deep-Dive досье + декомпозиция нового поколения god-объектов — план без кода

> Дата: 2026-08-23. Статус: план, код не трогаем до green live-гейта (P5.2).
> Базовая линия: **253 PASS** (ADR-014 baseline, с 2026-08-22), `dotnet build -c Release` 0 errors.
> Принцип: 1 PR = 1 slice + 253+N PASS + Build OK. Продолжает Phase 4 (F04 slice DONE — commit 28f4a62).

## Контекст: что изменилось после Phase 4

- F04 закрыт: `RadioStoryIntegration` 232 ln фасад + `StoryDecisionGateway` 263 ln (вся reflection, G1–G4 fix внутри, 10 тестов).
- Graph-аудит (graphify god nodes) выявил **новое поколение** god-объектов, отсутствовавших в SAD 06:
  - `LighthouseBeamGlow` 1369 ln / 63 ребра — №1 узел графа.
  - `ChronometerOverlayHandler` 572 ln / 51 ребро — №3 узел, **пропуск Phase 4 плана**.
  - Lighthouse-семья: `LighthousePlacer` 541, `LighthouseMapMarker` 458.
- SAD/06 устарел (LOC 508/474/381 → реально 724/502/1369) — обновить в конце Phase 5.

## P5.0 Dead code sweep (~1ч, риск 0, начать здесь)

Все пункты delete-safe: zero callers подтверждено grep по `Source/` + `Tests/`.

| Where | Что | ln |
|-------|-----|----|
| LighthouseBeamGlow.cs | `IsUtilityCamera` 720–731, `FindScreenCamera` 733–750, `InstallRenderHook` 752–758, `OnAnyCameraPreCull` 787–794 (только `-=` :779, никогда `+=`), static `renderHookInstalled` :98 (всегда false), write-only `lastLoggedCamera` | ~75 |
| CameraResolver.cs | `IsMapMode` 79–83 — zero callers | 5 |
| SonneController.cs | dead public API: `UpdateTuning`, `ClearSignalBuffer`, `UpdatePosition`, `CalculateBearing(s)`, `CalculateMapBearing` — внешние callers = 0 (единственный потребитель IPH использует 8-членный контракт) | surface |
| SonneSignalGenerator.cs | `GetDotCount` | ~15 |
| LighthouseProfile.cs | `ParseFireHeight` 68–92 — только тесты | ~25 |
| UI/ToggleHandler.cs | `ImgState` :117 | ~4 |
| UI/RadioHandler.cs | `ToggleVisibility` 106–109, dead store `currentAngle` :17 | ~8 |
| UI/PanelStateStore.cs | `Clear` — prod-dead | ~5 |
| ChronometerOverlayHandler.cs | L371–372 unreachable (второй guard после return) | 2 |

Итого ~140 строк + surface. Merge критерий: 253 PASS без изменений, Build OK, `graphify update .` — ребра только убывают.

## P5.1 Frame-hot fixes (~2ч, риск низкий)

Рейтинг пер-кадрового мусора (из досье):

| # | Где | Проблема | Fix |
|---|-----|----------|-----|
| F1 | LighthouseMapMarker.cs:160 | `RadioNavDiagnostics.IsEnabled` → `PlayerPrefs.GetInt` **каждый кадр** | кэш 0.25s (1 поле + таймер) |
| F2 | ChronometerOverlayHandler.cs | Update/LateUpdate дублируют pipeline (:81–82 vs :103–104) — двойной Sync + двойной hover-poll; scale-поллинг :500–510 дублирует события ScaleControl (спасает только `lastScaleToggleFrame` dedup) | удалить LateUpdate-дубль + поллинг, оставить событийный путь; убирает 2×/кадр `Cursor.SetCursor` churn |
| F3 | LighthouseBeamGlow.cs | reason-строки строятся **до** transition-check (:290, :275, :316–318, :1068, :1097, :1101) — конкаты каждый видимый кадр при gated-логе | move-after-gate (собрать строку только при переходе) |
| F4 | LighthouseBeamGlow.cs:474–475 + 5× `CurrentMode.ToString()`/кадр | uncached `GetProperty("color")` ×3/кадр; Mono enum ToString аллоцирует | кэш MaterialPropertyID + string на кадр/режим |
| F5 | SkyVisibilityResolver.cs:95 | `IsPlayerOutside`: uncached `Type.GetType(name+", com.uboat.game")` за вызов | кэш Type в `memberCache`-стиле (ключ уже типобазный) |
| F6 | LighthousePlacer.cs:484–512 | exception-path аллоцирует `List<string>(18)`+массивы **каждый кадр** до готовности группы | hoist в поле, переиспользовать |
| F7 | CameraResolver.cs:99/:139 | `CurrentMode.ToString()` ×2/кадр (version-shield) | кэш строки на режим / int-compare |

Не трогать: audio-thread (0 аллокаций — эталон), `DspPipeline` Random per sample (F71).
Merge критерий: 253 PASS, Build OK, profiler-проверка GC/frame до/после (опционально live).

## P5.2 Live gate (обязательно, ~30 мин игры)

Повторяет `LIVE_VALIDATION_CHECKLIST.md` B/C — pending:
- [ ] F71 pause/×2/×32 в игре
- [ ] preflight log без `target methods not found`
- [ ] DASH order `▬→◆◆→•` live
- [ ] F83 hot-reload (вкл/выкл мода в игре)
- [ ] event leaks после reload

## P5.3 ChronometerOverlayHandler slices (№3 god-node, 6–8ч, риск средний)

42 поля / 30 методов / 4 concern'а. Seams по убыванию развязки:

| Slice | Строки | Действие | Тесты |
|-------|--------|----------|-------|
| CH-1 Cursor art | 392–465 | → `ChronometerCursorArt.cs` (чистая статика, 0 state) | юнит-тесты пикселей сразу |
| CH-2 Native bridge | 16–30, 121–197 | → `ShipControlUIBridge.cs` (reflection wrapper ~80 ln) | stub-тесты по образцу Gateway |
| CH-3 Scale subsystem | 35–67, 310–381, 467–486 | → `ChronometerScaleSubsystem.cs`; retarget owner в satellites; **закрыть утечку**: ScaleControl-GameObject переживает restore внутри нативной иерархии — удалить/отцепить на restore | — |
| CH-4 Drag protocol | 548–615 | → `ChronometerDragProtocol.cs` (пара к DragShield); убрать per-frame `Enum.ToObject` box :607–608 (кэш Enum) | — |

Остаток: layer raise/restore (110–119, 199–308, 513–539, 617–636) — остаётся в CH как оркестратор. Цель: 572 → ~300 ln.

## P5.4 LighthouseBeamGlow slices (№1 god-node, 8–10ч, риск средний-высокий)

1369 ln / 63 ребра / 0 тестов. Seams:

| Slice | Действие |
|-------|----------|
| LBG-1 Dead + statics | P5.0 уже убрал render-hook мёртвину; далее: mutable statics L93–98 (`[Inject] weather`, `dotTexture`, `streakTexture`, `renderingCamera`) → instance fields / DI |
| LBG-2 Dot UI view | per-frame dot/streak rendering core → `LighthouseDotView.cs` (использует VisualModel) |
| LBG-3 Diagnostics | LogDiagnostics ~132 ln → `LighthouseDiagnostics.cs` (move-after-gate из P5.1 F3 внутри) |
| LBG-4 Camera tier | camera resolve/visibility-тесты :661/:711/:755/:784 → в `CameraResolver` (уже там частично) |

Цель: 1369 → ~600 ln. Каждый slice — отдельный PR, 253+N PASS.
Замечание: BeamGlow = вычислительный хаб lighthouse-кластера (VisualModel/SkyVis/CamRes/Diag) — циклов нет, но все inbound-рёбра идут сюда; тесты только через Unity runtime/stubs.

## P5.5 F21 — Main.cs slices (2ч, риск средний)

По плану Phase 4 без изменений:
- `ModBootstrapper` L34–149 (Harmony setup + SceneEventsListener + InjectIntoAssembly).
- `PrefabValidator` L155–235 (hardcoded-paths validation).
- Остаток star-sky L246–270 остаётся в Main.
- Бонус: закрывает латентный дефект hot-reload — `Reset()` null-ит только `instance`, не уничтожает holder-GO (сейчас безопасно лишь потому, что scene unload убивает GO).

## P5.6 F02 — SonneController slices (15–20ч, риск высокий — audio thread)

724 ln, 9 ответственностей. Внешний контракт = 8 public-членов, единственный потребитель IPH.

| Slice | Строки | Действие |
|-------|--------|----------|
| SC-1 SignalDecoder | L174–318 | → `SignalDecoder.cs` (main-thread only; тесты: callsign last-only, DASH order, TrimEntries cap) |
| SC-2 StationManager | L463–612 + 719–722 | → `StationManager.cs`; API `IReadOnlyList<StationState>` + `DominantStationIndex` |
| SC-3 AudioCoordinator | L670–674 (OnAudioFilterRead) + 326–335 + 39–46 | → `AudioCoordinator.cs`; **audio-thread discipline: zero аллокаций сохранить** |

Цель: 724 → ~250 ln фасад. Порядок строго SC-1 → SC-2 → SC-3 (SC-3 после F25 или вместе).

## P5.7 F03 — InformationPanelHandler slices (4–6ч, риск средний)

502 ln, все члены private, единственный внешний ref `RadioInstaller.cs:78`.

| Slice | Строки | Действие |
|-------|--------|----------|
| IPH-1 UIElementFactory | L121–209 | 5× GetComponentInChildren + CreateLabel + font fallback (LegacyRuntime.ttf) |
| IPH-2 PanelState | L31–39 + L91–102 + L491–500 | open/close/visibility state |
| IPH-3 DiagnosticsBinder | L317–444 | binding диагностических полей |

Цель: 502 → ~280 ln. Зависит: PrefabNames стабильность (hierarchy validation Main.cs).

## P5.8 F25 — AudioSynthesisEngine boundary (3–4ч, риск высокий)

Подтверждено аудитом:
- 3 параллельных массива (`stations`/`keyers`/`carriers`) по индексу без length-check (AES:66/132).
- 7 несинхронных cross-thread пар полей — все 32-бит, только visibility-риск; 5 volatiles существуют.
- Zero аллокаций на audio thread (эталон).

Fix: `IReadOnlyStationState` boundary — SC-2 (StationManager) публикует snapshot-интерфейс, AudioSynthesisEngine + DspPipeline получают интерфейс вместо тройки массивов; 8 call-sites. Делать **после** SC-2. Тесты: race-free read через `Volatile.Read`/snapshot-копию на main thread.

## Покрытие тестами (карта пробелов)

232 атрибута [Fact]/[Theory] → 253 executed. Покрыто хорошо: математика, Data, DSP, keyer, scheduler (StationScheduler — лидер, 17), gateway (10).
**Не покрыто совсем** (всё Mono/scene-bound): SonneController сам класс, IPH, Main, весь visual-стек (BeamGlow, Placer, MapMarker, CameraResolver, SkyVis), весь UI-слой, ChronometerOverlay.
Действие: не гнать юниты на MonoBehaviour; расширять `Tests/Stubs/` для bridge-классов (CH-2 по образцу StoryDecisionGateway) + GameContractTests уже пиннит метаданные игры.

## Чего НЕ делать

- Не менять `DspPipeline` Random per sample (F71) без live listening.
- Не вводить netstandard2.1 / Span<T> — игра netstandard2.0.
- Не трогать audio-thread аллокации ради стиля — там 0, держать 0.
- Не массово менять Constants/ComparisonExtensions (93 call sites) — стайл, не блокер.

## Критерий выхода Phase 5

- Каждый slice: `powershell -File tests.ps1` 253+N PASS, `dotnet build -c Release` 0 errors, `build.ps1` OK, `graphify update .`.
- Итог: LighthouseBeamGlow ≤600 ln, ChronometerOverlayHandler ≤300 ln, SonneController ≤250 ln, IPH ≤280 ln; SAD/06 пересчитан (LOC + ребра); `hypothesis/INDEX.md` — новые находки (CH god-class, lighthouse-семья) со статусами; `DEFERRED.md` обновлён.

## Следующий шаг <2 мин

```powershell
git checkout -b refactor/p5-dead-sweep
# P5.0: удалить строки из таблицы выше (9 файлов), tests.ps1 → 253 PASS без изменений
powershell -ExecutionPolicy Bypass -File tests.ps1
```

Refs: `PLAN_Phase4_GodObjects.md`, `SAD/06_God_Object_Analysis.md` (устарел по LOC), `LIVE_VALIDATION_CHECKLIST.md`, `DEFERRED.md`, `hypothesis/INDEX.md`, graphify-out/graph.json (god nodes с явным project_path).
