# G3. onUpdatedMethod null — `OnUpdated()` объявлен в Story, ищется на GenericStory

**Статус:** ✔ ПОДТВЕРЖДЕНО (исходники игры, 2026-08-01)
**Приоритет:** P1 (High) — UI не обновится после инжекции (если она заработает)
**Область:** Logic · RadioStoryIntegration · Reflection
**Источник (мод):** `Source/Logic/RadioStoryIntegration.cs:100-102, 244-262`
**Источник (игра):** `C:\code\projects\uboat` — `UBOAT.Game.Scene\Story.cs:438`

---

## Суть

Та же ошибка, что в G2: `GetMethod("OnUpdated")` на `RadioStory.BaseType` (= GenericStory), а `OnUpdated()` объявлен в `Story`.

## Доказательство

Мод (RadioStoryIntegration.cs:100-102):

```csharp
onUpdatedMethod = storyType.GetMethod("OnUpdated",
    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public | BindingFlags.Instance,
    null, Type.EmptyTypes, null);
```

Игра: `protected void OnUpdated()` — `Story.cs:438` (в GenericStory/RadioStory не переопределён, вызов `OnUpdated()` в GenericStory.cs:593 резолвится в Story.OnUpdated).

`GetMethod` не трассирует базы → null.

## Следствие

`NotifyUpdated` (RSI.cs:244-262) логирует warning «OnUpdated method not found — UI may not reflect new decision» и ничего не вызывает. Если инжекция заработает (после фикса G1/G2), вставленное решение может не отрисоваться в UI до следующего пересбора решений.

## Предлагаемый фикс (вне рамок текущей задачи)

```csharp
onUpdatedMethod = typeof(RadioStory).GetMethod("OnUpdated",
    BindingFlags.NonPublic | BindingFlags.Instance,
    null, Type.EmptyTypes, null);
```

## Связано

- G2 — та же корневая причина (поиск на BaseType)
- Отчёт: `refactoring-plan/VERIFICATION_REPORT.md`, секция 5.2
