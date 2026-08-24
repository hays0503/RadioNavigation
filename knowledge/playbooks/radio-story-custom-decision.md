# Плейбук: кастомное решение (Decision) в игровой RadioStory

Добавить свой пункт выбора в диалог радиостанции (`UBOAT.Game.Scene.Stories.RadioStory`).
Проверено по исходникам игры: `UBOAT.Game.Scene\Story.cs`, `UBOAT.Game.Scene.Stories\RadioStory.cs`.

## Точки патча

| Цель | Где | Зачем |
|---|---|---|
| `RadioStory.ContinueStory` (override, L262) | postfix | вставить Decision после `base.ContinueStory()` и до `OnUpdated()` |
| `Story.OnDecisionSelected(Decision)` (L332) / override (L402) | prefix | перехват клика по своему ключу локализации |
| `Locale.RetrieveString` (оба оверлоада) | prefix | подмена/добавление текста своего решения |
| `RadioStory.OnStopped` (L126) / `Story.Stop()` (L274) | postfix | очистка состояния |

## 1. Чтение stage — ТОЛЬКО через boxed enum

Поле `stage` приватное, тип — приватный вложенный enum `RadioStoryStage`
(`Unknown=0, MainChoices=1, PlayMusicStation=2, LeaveStation=3, RequestResupply=4, RequestNewAssignment=5`).
Рефлексия вернёт **boxed enum**; `(int)obj` даст InvalidCastException.

```csharp
static readonly FieldInfo StageField =
    typeof(RadioStory).GetField("stage", BindingFlags.NonPublic | BindingFlags.Instance);

int ReadStage(object story) // 1 = MainChoices — единственная стадия с меню решений
{
    var boxed = StageField.GetValue(story);
    return boxed == null ? 0 : System.Convert.ToInt32(boxed); // безопасно для enum
}
```

## 2. Конструирование Decision

`Decision` — вложенный класс `Story.Decision` (Story.cs:22), все поля readonly,
3 публичных конструктора (L44, L57, L70). Удобен вариант с Action<Decision>:

```csharp
var decision = new Story.Decision(
    "Mod/RadioNavigation/CustomAction",    // LocalizationKey
    null, null,                            // FallbackText/FallbackTooltip (null → из Locale)
    "icon_name",                           // Icon
    true,                                  // Enabled
    (Story.Decision d) => OnMyDecision(d), // callback
    index: 0,                              // Index — игра читает как данные!
    costs: new IRewardInfo[0]);            // Costs
```

Игра читает `decision.Index` как данные (`OnDecodeTransmission`, `OnCancelSideMission`) — не используйте для своих нужд.

## 3. InsertDecision через рефлексию на typeof(Story)

`InsertDecision(int, Decision)` — protected, объявлен в `Story` (L377), НЕ в RadioStory.
`GetMethod` базовые классы не обходит — резолвить именно на `typeof(Story)`:

```csharp
static readonly MethodInfo InsertDecisionRef =
    typeof(Story).GetMethod("InsertDecision", BindingFlags.NonPublic | BindingFlags.Instance);
```

## 4. Postfix на ContinueStory + обновление UI

```csharp
[HarmonyPostfix]
[HarmonyPatch(typeof(RadioStory), nameof(RadioStory.ContinueStory))]
static void ContinueStory_Postfix(RadioStory __instance)
{
    if (!__instance.Running || ReadStage(__instance) != 1 /* MainChoices */) return;

    var decisions = __instance.Decisions;
    for (int i = 0; i < decisions.Count; i++)
        if (decisions[i].LocalizationKey == CustomKey) return; // дедуп!

    var d = BuildDecision();                       // см. п.2
    InsertDecisionRef.Invoke(__instance, new object[] { decisions.Count - 1, d });

    typeof(Story).GetMethod("OnUpdated",           // protected, Story.cs:438 → Updated event
        BindingFlags.NonPublic | BindingFlags.Instance).Invoke(__instance, null);
}
```

Без `OnUpdated()` список кнопок останется прежним до следующего апдейта истории.

## 5. Обработка клика — prefix на OnDecisionSelected

Игра свитчит по `decision.LocalizationKey` (RadioStory.cs:402, ключи вида `"Radio/LeaveStation"`):

```csharp
[HarmonyPrefix]
[HarmonyPatch(typeof(Story), "OnDecisionSelected")]
static bool OnDecisionSelected_Prefix(Story __instance, Story.Decision decision)
{
    if (decision.LocalizationKey != CustomKey) return true; // пропустить чужие
    DoMyAction();
    return false; // свой ключ не отдавать игре
}
```

## 6. Локализация текста

FallbackText = null → игра идёт в `Locale.RetrieveString`. Свою строку отдаём префиксом (патчить **оба** оверлоада — string и string,string):

```csharp
[HarmonyPrefix]
[HarmonyPatch(typeof(Locale), nameof(Locale.RetrieveString), new[] { typeof(string) })]
static bool RetrieveString_Prefix(string key, ref string __result)
{
    if (key != CustomKey && key != CustomKey + ".Tooltip") return true;
    __result = key.EndsWith(".Tooltip") ? "Моя подсказка" : "Моё решение";
    return false;
}
```

## 7. Очистка на OnStopped

История пересоздаётся при каждом открытии радио — сбрасывайте кэш/подписки:

```csharp
[HarmonyPostfix]
[HarmonyPatch(typeof(RadioStory), "OnStopped")]
static void OnStopped_Postfix(RadioStory __instance) => injected.Clear(); // кэш instance→decision
```

Дедуп из п.4 обязателен: ContinueStory дергается многократно за одну сессию и каждый раз заново строит список решений.

## Грабли

- `(int)`-каст boxed enum → InvalidCastException. Только `Convert.ToInt32`/`Enum.ToObject`.
- `typeof(RadioStory).GetMethod("InsertDecision")` → null: метод в `Story`.
- `LocalizationKey` в игре — readonly **field**, не property.
- Префикс OnDecisionSelected должен возвращать `false` только для СВОЕГО ключа.
