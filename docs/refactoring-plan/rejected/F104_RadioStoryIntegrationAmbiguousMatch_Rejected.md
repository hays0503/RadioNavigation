# F104: RadioStoryIntegration.Init — AmbiguousMatchException risk from combined NonPublic+Public BindingFlags — REJECTED

**Статус:** ✘ ОПРОВЕРГНУТО (верификация через исходники игры, 2026-08-01)
**Приоритет (исходный):** P2 (Medium)
**Область:** Logic · RadioStoryIntegration · Reflection
**Источник (исходный):** `Source/Logic/RadioStoryIntegration.cs:63-64`

---

## Исходная гипотеза

`GetMethod("InsertDecision", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public | BindingFlags.Instance)` — совмещение NonPublic и Public в одном вызове грозит `AmbiguousMatchException`, если у типа несколько перегрузок `InsertDecision`.

## Вердикт: гипотеза опровергнута, но вскрылась реальная проблема

Сверка с исходниками игры (`C:\code\projects\uboat`):

1. **Перегрузок нет.** `InsertDecision(int index, Decision decision)` объявлен ровно один раз — `UBOAT.Game.Scene\Story.cs:377`. `AmbiguousMatchException` невозможен в текущей версии игры.
2. **Реальная проблема иная (G2):** `RadioStoryIntegration.cs:56` делает `typeof(RadioStory).BaseType` (= `GenericStory`), а `GetMethod` **не трассирует базовые типы** — `InsertDecision` объявлен в `Story` (на уровень выше). → `insertMethod == null` → `decisionType == null` → `Init()` выходит на строке 69-71 с ошибкой «Decision type not found» → вся инжекция решения T3PLL38 мертва. Это отдельный дефект, см. G2 в `VERIFICATION_REPORT.md` (секция «Game source verification»).

## Что остаётся верным

Хрупкость рефлексии (связь с F23, F68) — подтверждается, но конкретно риск `AmbiguousMatchException` в текущей версии игры отсутствует.

## Связанные проблемы

- G1 (stageField null), G2 (insertMethod null), G3 (OnUpdated null), G4 (LocalizationKey — поле, GetProperty → null) — см. `VERIFICATION_REPORT.md`
- F186 — также опровергнута по тем же исходникам игры
- F23 (Reflection Fragility) — подтверждена усиленно
