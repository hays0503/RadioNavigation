# G1. stageField мёртв — InvalidCastException при `(int)`-касте boxed-перечисления RadioStoryStage

**Статус:** ⚠ УТОЧНЁН (2026-08-01, коммит 29259cb) — исходная находка «поля нет» ОШИБОЧНА
**Приоритет:** P0 (Critical) — панель мода не открывается
**Область:** Logic · RadioStoryIntegration · Reflection
**Источник (мод):** `Source/Logic/RadioStoryIntegration.cs:13, 95, 113-116, 151-156`
**Источник (игра):** `C:\code\projects\uboat` — `RadioStory.cs:83, 264-283, 303, 630`

---

## Суть (исправленная)

Поле `stage` **существует**: `private RadioStoryStage stage;` (RadioStory.cs:83) — тип `RadioStoryStage` (**enum**), а не int. Мод читал его как `(int)stageField.GetValue(__instance)` — на boxed-перечислении `(int)` бросает `InvalidCastException` → catch в `InjectT3PLL38Decision` → return. Плюс до фикса G2 `stageField` оставался null из-за early-exit в Init().

Проверка контрактом против установленной `com.uboat.game.dll` (тест `RadioStory_HasStageField_AsEnumType_NotInt`):
- `GetField("stage", NonPublic|Instance)` → НЕ null
- тип поля — enum `RadioStoryStage`
- значения: `Unknown=0, MainChoices=1, PlayMusicStation=2, LeaveStation=3, RequestResupply=4, RequestNewAssignment=5`

Оригинальная ошибка верификации: grep `\bStage\b` (case-insensitive по большой букве) пропустил строчное поле `stage` и enum `RadioStoryStage` в `RadioStory.cs`.

## Следствие

`(int)`-каст падает на любой стадии → инжекция никогда не работает (панель не открывается).

## Применённый фикс (29259cb)

Восстановлен stage-гейт с enum-безопасным чтением:

```csharp
stageField = typeof(RadioStory).GetField(StageFieldName, BindingFlags.NonPublic | BindingFlags.Instance);
...
int stageValue = Convert.ToInt32(stageField.GetValue(__instance)); // Convert.ToInt32 работает на boxed-перечислениях
return stageValue == 1; // RadioStoryStage.MainChoices
```

Инжекция T3PLL38 только на стадии MainChoices — как задумал автор.

## Связано

- G2 (insertMethod null) — причина, по которой stageField был null ДО фикса
- F23 (Reflection Fragility) — подтверждена усиленно
- Контракт-тесты: `Tests/GameContractTests.cs`
