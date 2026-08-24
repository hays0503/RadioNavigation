# ADR-029: Coupling Assessment — оценка связанности, мешающей росту

> Статус: Accepted (2026-08-24)
> Связи: `refactoring-plan/PLAN_Phase5.md` (слайсы-исполнители), SAD/05_Dependency_Analysis.md, F05/F29, F02/F03/F21/F179, `../RESOLVED_LEDGER.md` §5
> Метод замера: graphify-граф 2026-08-24 (4214 узлов / 5651 рёбер) + grep-инвентарь статического состояния и точек сцепки

## Контекст

Вопрос: мешает ли росту мода то, что код не разнесён по доменам? Замер показал:

**Здоровое (не трогать):**
- Слои Data/Logic/Sound/UI — направление зависимостей чистое почти везде.
- Весь `Data/`-статик — readonly-каталоги (`BeaconData.Stations`, `StarCatalog.Stars[5046]`, `ConstellationLines.Polylines[215]`, `LighthouseProfiles.All`, `ConsolCycleConfig.Cycle40/60/120s`) — нулевой риск связанности.
- `StoryDecisionGateway` — reflection собран в один полюс с кэшем и тестами.

**Три концентратора (уровень 1):**

| Узел | Факт | Тормозящий механизм |
|------|------|---------------------|
| `ToggleHandler.IsRadioOn/IsInteractable` (static mutable) | 10 потребителей в 7 файлах, включая `SonneController.cs:460,672` | Logic читает вверх в UI (инверсия слоёв); каждый новый контрол обязан знать про toggle (F05/F29) |
| `SonneController` | 49 рёбер; `UpdateStations(dt, time, stations, schedulers, keyers, carriers, isOn)` — 6 коллекций одним вызовом | Любая новая звуковая/сигнальная фича проходит через класс и сигнатуру-«швейцарский нож» |
| `ChronometerOverlayHandler` | **72 ребра — №1 графа**; 3 точки резолва `GameInput` (:526,:534,:843), reflection в чужие приваты (`chronometerToolDragState`), DI к `ShipControlUI` | Хронометр вырос без границ внутри общего слоя; изменения игрового ввода бьют напрямую |

**Умеренное (уровень 2):**
- Двусторонний цикл Panel↔Chronometer: `PanelDragHandler.cs:125` → `chronometerOverlay.IsChronometerElement()`; guard-список OnBeginDrag растёт линейно с каждым UI-кластером (F207 дописан вручную).
- `RadioHandler.Instance`: 9 внешних записей `IsVisible`/`CursorAngle` (Main ×2, CursorHandler ×3, RadioStoryIntegration ×5) — hot-reload и story правят UI мимо событий.
- Россыпь reflection вне gateway: 12+ собственных `GetField(...)` в LighthouseBeamGlow, StationMapOverlay, PlayerPositionProvider, SkyVisibilityResolver — связь с версиями игры размазана по 4 файлам.

**Отвергнутая интерпретация:** «код не разнесён по доменам вообще» — неверна. Нарезка по техническим слоям есть; отсутствует только доменная нарезка *внутри* слоёв, и её отсутствие болит ровно в перечисленных точках, а не повсеместно.

## Решение

1. **Отдельный big-bang рефакторинг связанности НЕ проводится.** Исторический прецедент F47/F45: архитектурные классы «для красоты» отвергнуты; перенос файлов создаёт дрейф file:line-якорей (см. RESOLVED_LEDGER, досье confirmed/, SAD §10).
2. **Связанность рвётся слайсами PLAN_Phase5**, где файлы всё равно переписываются:

| Связка | Исполнитель |
|--------|-------------|
| Toggle-статик → state service / RadioContext | P5 (RadioContext) |
| SonneController-хаб | P5.6 StationManager + AudioCoordinator |
| Chrono-центр | P5 ChronometerCursorArt/presenter |
| Guard-список PanelDragHandler | интерфейс `IDragBoundary` вместо перечисления типов |
| Reflection-россыпь | общий GameReflectionCache (инфраструктурный helper, не доменный класс — отличие от отвергнутого F47) |

3. **Правило роста**: новая фича = новая подпапка домена (`Logic/Lighthouse/`, `UI/Chronometer/`, ...), создаётся лениво — при первом переезде/декомпозиции файла, не заранее.
4. **Запрет на новые глобальные точки**: новые static mutable поля запрещены; состояние — через DI/контекст или события. Readonly-каталоги Data/ разрешены.

## Последствия

- Положительные: рост фич перестаёт увеличивать fan-out концентраторов; Logic становится тестируемым без UI; reflection-поверхность игры сходится в одну точку (устойчивость к обновлениям игры).
- Отрицательные/риски: слайсы Phase5 дороже точечных патчей; до их выполнения правила п.3–4 соблюдаются дисциплиной, а не компилятором.
- Нейтральные: SAD/05 и этот ADR — источник правды по связанности; RESOLVED_LEDGER §5 — текущий список из 22 открытых гипотез, среди которых 6 — прямые следствия оценённых здесь связок (F02, F03, F21, F179, F32 + хвост).

## Метрика пересмотра

Пересматривать при любом из условий: god-node №1 графа меняется; `UpdateStations` меняет сигнатуру; появляется новый static mutable; число открытых гипотез §5 падает ниже 10.
