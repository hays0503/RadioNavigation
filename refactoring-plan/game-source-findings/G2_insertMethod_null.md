# G2. insertMethod null — `InsertDecision` ищется на BaseType (GenericStory), а объявлен в Story

**Статус:** ✔ ПОДТВЕРЖДЕНО (исходники игры, 2026-08-01)
**Приоритет:** P0 (Critical) — Init() падает, весь RadioStoryIntegration мёртв
**Область:** Logic · RadioStoryIntegration · Reflection
**Источник (мод):** `Source/Logic/RadioStoryIntegration.cs:56-71`
**Источник (игра):** `C:\code\projects\uboat` — `UBOAT.Game.Scene\Story.cs:377`

---

## Суть

`GetMethod("InsertDecision")` выполняется на `typeof(RadioStory).BaseType` (= `GenericStory`), но `InsertDecision` объявлен в `Story` — на два уровня выше. `Type.GetMethod` **не трассирует базовые типы** → результат null.

## Доказательство

Мод (RadioStoryIntegration.cs:56-71):

```csharp
var storyType = typeof(RadioStory).BaseType;          // = GenericStory
insertMethod = storyType.GetMethod("InsertDecision",
    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public | BindingFlags.Instance);
if (insertMethod.IsNotNull())
    decisionType = insertMethod.GetParameters()[1].ParameterType;
if (decisionType.IsNull())
{
    Debug.LogError("[RadioNav] Decision type not found \u2014 check game version " + gameVersion);
    return;                                            // ← Init() выходит ЗДЕСЬ
}
```

Иерархия игры (исходники): `RadioStory : GenericStory : Story`.
- `InsertDecision(int index, Decision decision)` — `Story.cs:377` (protected)
- В GenericStory/RadioStory перегрузок нет (grep: единственная декларация)

`GetMethod` на GenericStory → null (метод унаследован, а поиск ограничен текущим типом). Контраст: `GetField` и `GetProperty` трассируют базы — поэтому `decisionsProp` (GetProperty("Decisions") на GenericStory) находится: `Story.cs:186`.

## Следствие

`decisionType` остаётся null → `Init()` выходит на строке 69-71 с ошибкой «Decision type not found» → `initialized` не выставляется → вся интеграция (инжекция, перехват, локализация) работает в режиме «не инициализировано». Это первая поломка в цепочке: починка G1/G3/G4 без G2 ничего не даст.

## Предлагаемый фикс (вне рамок текущей задачи)

Искать на `typeof(RadioStory)` (или `typeof(Story)`), BindingFlags `NonPublic | Instance`:

```csharp
insertMethod = typeof(RadioStory).GetMethod("InsertDecision",
    BindingFlags.NonPublic | BindingFlags.Instance);
```

## Связано

- G1 (stageField null), G3 (onUpdatedMethod null), G4 (localizationKeyProp null) — другие звенья мёртвого контура
- F104 → rejected: «риск AmbiguousMatch» опровергнут — перегрузок нет; реальная проблема именно эта (G2)
- F186 → rejected: `GetParameters()[1]` недостижим — insertMethod и так null
- Отчёт: `refactoring-plan/VERIFICATION_REPORT.md`, секция 5.2
