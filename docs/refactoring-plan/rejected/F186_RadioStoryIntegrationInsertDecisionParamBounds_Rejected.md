# F186: RadioStoryIntegration.Init accesses `GetParameters()[1]` without length check — REJECTED

**Статус:** ✘ ОПРОВЕРГНУТО (верификация через исходники игры, 2026-08-01)
**Приоритет (исходный):** P2 (Medium)
**Область:** Logic · RadioStoryIntegration · Reflection Fragility
**Источник (исходный):** `Source/Logic/RadioStoryIntegration.cs:63-66`

---

## Исходная гипотеза

Если в будущей версии UBOAT сигнатура `InsertDecision` изменится (например, параметров станет 0 или 1), обращение `insertMethod.GetParameters()[1]` даст `IndexOutOfRangeException` внутри `Init()` и уронит весь модуль.

## Вердикт: гипотеза опровергнута (в текущей версии игры)

Сверка с исходниками игры (`C:\code\projects\uboat`):

1. **Сигнатура `InsertDecision(int index, Decision decision)`** — 2 параметра (`UBOAT.Game.Scene\Story.cs:377`). `GetParameters()[1]` корректен в текущей версии игры.
2. **Guard уже срабатывает раньше:** `insertMethod` и так равен `null` — `GetMethod` на `RadioStory.BaseType` (= `GenericStory`) не находит унаследованный от `Story` метод (см. G2). Ветка `if (insertMethod.IsNotNull())` не выполняется → обращения к `[1]` вообще не происходит → `IndexOutOfRangeException` невозможен.
3. `Init()` завершается раньше — на проверке `decisionType.IsNull()` (строка 67-71), так что до потенциально проблемного кода не доходит.

## Что остаётся верным

Гипотетический риск на будущие версии игры сохраняется, но в текущей версии статически не проявляется и не реализуем. Реальная поломка — G2 (null-метод из-за BaseType), см. `VERIFICATION_REPORT.md`.

## Связанные проблемы

- G2 в `VERIFICATION_REPORT.md` (InsertDecision ищется не на том типе)
- F104 — опровергнута по тем же исходникам
- F23 (Reflection Fragility) — подтверждена
