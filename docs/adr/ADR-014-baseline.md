# ADR-014: Baseline 182 + farClip — точка отката для сопровождаемости

> Статус: Accepted (2026-08-19)  
> Связи: `SAD/11_Testing_Strategy.md`, `SAD/03_Current_Architecture.md`, `STAR_SKY_PLAN.md:10`, `refactoring-plan/hypothesis/INDEX.md`, `graphify-out/GRAPH_REPORT.md`

## Контекст

- 182 теста `Tests/Runner` зелёные `powershell -ExecutionPolicy Bypass -File tests.ps1` (2026-08-19, факт: `Passed:182 Failed:0`). `README.md:40` заявлял `174/174`, `AGENTS.md:40` — `No test suite` — устарело.
- `radionavigation.csproj` `netstandard2.0` `Release`+`Debug` собирается без ошибок ( warnings `MSB3277` по `Assembly-CSharp` ожидаемы).
- `graphify-out/GRAPH_REPORT.md:13` от `8b3021fa` 3473/4293/351 — помечен как свежий (`git rev-parse HEAD` == `8b3021fa`).
- Нет точки отката до `Ф1-Щит` — любой фикс без baseline = регресс вслепую (god `SonneController 49` `GRAPH_REPORT:365`).
- Требуется зафиксировать `farClipPlane` + `LerpValue` формулу `STAR_SKY_PLAN.md:55` до `Ф4 CameraResolver`.

## Решение

1. Зафиксировать baseline тег `baseline-182-20260819` на `8b3021fa` (последний зелёный коммит `8b3021f feat(chronometer)`). Любой `revert` — на этот тег.
2. Артефакт baseline — лог `tests.ps1` (`Passed:182`) + `dotnet build Release/Debug` 0 errors.
3. `farClip` замер — лог `LighthouseBeamGlow.cs:549` `LogDiagnostics` на мостике/перископе/бинокле до смены `R=3900<0.9*farClip` `STAR_SKY_PLAN:158` + `BeamGlow.cs:150`.
4. `graphify` считать свежим до первого прод-коммита; `graphify update .` после каждой фазы.

## Причина

Без baseline нельзя отличить регресс `Ф1-Ф8` от уже существующего `Failed:8` в `GameContractTests`. `AGENTS.md:28` — игра перекомпилирует `Source/*.cs` без `DEBUG`, локальный `build.ps1` — только IDE-check, поэтому `dotnet build` + `tests` — единственный локальный гейт.

## Альтернативы

- **A. Нет тега, только `v2.1.0`.** Откат на `v2.1.0` потерял бы `8b3021fa` chronometer (24 коммита). Отвергнуто.
- **B. Тег на dirty HEAD** (текущая `Bundles/testui` модиф). Dirty включал бы незакоммиченные `Leuchtfeuer.json` 1184 — невоспроизводимо. Отвергнуто — тег на чистый `8b3021fa`.
- **C. Сразу `graphify update .`** без замера. Граф 58M слов перестроит без изменений — трата, отвергнуто.

## Tradeoff

+ Воспроизводимая точка отката 1 строкой `git reset --hard baseline-182-20260819`  
+ Чёткий `182→184` инкремент `Ф1` vs дрейф `174` из `README`  
- `farClip` замер требует живой игры (1 вечер Phase0) — отложен, baseline без него

## Статус

- [x] `tests.ps1` 182 PASS (лог сохранён в `C:\Users\SERGEY~1\AppData\Local\Temp\opencode\baseline_tests.log`)
- [x] `dotnet build Release/Debug` 0 errors
- [x] `git tag baseline-182-20260819 8b3021fa`
- [ ] `farClip` живой замер — перенести в `ADR-020 StarSky Phase0` (Ф6)
- [ ] `graphify update` после `Ф1`

## Связанные

- Следующий: `ADR-015 Test Shield` (2 теста `Gain/MapBearing`)
- Откат: `git reset --hard baseline-182-20260819 && powershell -ExecutionPolicy Bypass -File tests.ps1`

```json
{
  "id": "ADR-014",
  "decision": "Зафиксировать baseline-182-20260819 на 8b3021fa с артефактами tests+build+graph, farClip замер — в ADR-020",
  "reason": "Без baseline любой фикс Ф1-Ф8 = регресс вслепую (182 теста, god 49, 0% UI). Игра компилит без DEBUG — только локальные гейты.",
  "alternatives": ["Нет тега (потеря 24 коммитов)", "Тег на dirty HEAD (невоспроизводимо)", "Сразу graphify update (трата)"],
  "tradeoff": "Точка отката 1 строкой vs farClip требует живой игры — отложен"
}
```
