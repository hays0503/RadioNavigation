# G1. stageField мёртв — поле `stage` отсутствует в RadioStory (инжекция T3PLL38 не работает)

**Статус:** ✔ ПОДТВЕРЖДЕНО (исходники игры, 2026-08-01)
**Приоритет:** P0 (Critical) — панель мода не открывается
**Область:** Logic · RadioStoryIntegration · Reflection
**Источник (мод):** `Source/Logic/RadioStoryIntegration.cs:13, 95, 113-116, 151-156`
**Источник (игра):** `C:\code\projects\uboat` — `RadioStory.cs`, `GenericStory.cs`, `Story.cs`

---

## Суть

Мод читает поле `stage` у `RadioStory` для определения фазы истории (1 = MainChoices), но в текущей версии игры такого поля нет.

## Доказательство

Мод (RadioStoryIntegration.cs):

```csharp
// :13
private const string StageFieldName = "stage";
// :95
stageField = typeof(RadioStory).GetField(StageFieldName, BindingFlags.NonPublic | BindingFlags.Instance);
// :113-116 — guard
if (!TryInit() || decisionCtor.IsNull() || stageField.IsNull())
{
    Debug.LogWarning("[RadioNav] Init failed, cannot inject T3PLL38");
    return;
}
// :151-156
private static bool IsStageOne(RadioStory __instance)
{
    int stageValue = (int)stageField.GetValue(__instance);
    return stageValue == 1;
}
```

Игра: `GetField("stage")` на RadioStory и его базах (GenericStory, Story) — **нигде нет**. Case-insensitive grep `\bStage\b` по всей игре: единственное вхождение — `HelpBehaviour.cs:27` (не связано). `GetField` трассирует базовые типы — и там поля нет.

## Следствие

`stageField` всегда null → `InjectT3PLL38Decision` (постфикс на `RadioStory.ContinueStory`) всегда выходит на строке 113-116 с warning → решение `Radio/OpenT3PLL38` никогда не появляется в меню радио-истории → **панель не открывается**. Даже при починке G2 (insertMethod) путь останется заблокированным.

## Предлагаемый фикс (вне рамок текущей задачи)

- Убрать гейтинг по stage (RadioStory в текущей версии не имеет стадий — решения строятся в `ContinueStory` каждый вызов), либо
- Определять «первый шаг» по `Decisions.Count` / отсутствию T3PLL38-решения (дедупликация уже есть — `DecisionAlreadyExists`).

## Связано

- G2 (insertMethod null) — независимая поломка того же контура
- G4 (localizationKeyProp null) — даже после инжекции перехват не сработал бы
- F23 (Reflection Fragility) — подтверждена усиленно
- Отчёт: `refactoring-plan/VERIFICATION_REPORT.md`, секция 5.2
