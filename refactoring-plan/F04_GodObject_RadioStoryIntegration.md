# F04: God-Object + Reflection Fragility — RadioStoryIntegration (381 LOC)

**Приоритет:** P1 (Major)
**Область:** Logic · Story Integration
**Блокирует:** Стабильность при апдейте UBOAT, добавление новых решений/локалей

---

## Проблема

RadioStoryIntegration совмещает **5 ответственностей** в одном статическом классе. Reflection-кэш существует, но неполный: `GetCurrentLanguage()` повторяет 4 `Type.GetType()` при каждом вызове. `BindingFlags` продублированы с ошибкой в трёх местах.

Класс хрупок — если UBOAT изменит сигнатуру конструктора Decision или имя сборки, мод сломается. Silent fallback на `"english"` маскирует проблему, но не решает её.

---

## Evidence

### 5 ответственностей

| # | Ответственность | Строки |
|---|----------------|--------|
| R1 | Reflection-based game type discovery | 27-107, 295-332 |
| R2 | Decision injection into RadioStory | 109-262 |
| R3 | Locale/translation override | 264-293 |
| R4 | Custom decision handler | 334-367 |
| R5 | Radio panel lifecycle | 369-379 |

### 8 uncached Type.GetType() вызовов

`GetLanguageProperty()` (вызывается 1 раз, в Init):

- L297: `Type.GetType("DWS.Common.InjectionFramework.ScriptableObjectSingleton, com.uboat.game")`
- L298: `?? Type.GetType("DWS.Common.InjectionFramework.ScriptableObjectSingleton, Assembly-CSharp")`
- L302: `Type.GetType("UBOAT.Game.Core.Data.Locale, com.uboat.game")`
- L303: `?? Type.GetType("UBOAT.Game.Core.Data.Locale, Assembly-CSharp")`

`GetCurrentLanguage()` (вызывается при КАЖДОМ разрешении locale key):

- L314: `Type.GetType("DWS.Common.InjectionFramework.ScriptableObjectSingleton, com.uboat.game")` — копипаста
- L315: `?? Type.GetType("DWS.Common.InjectionFramework.ScriptableObjectSingleton, Assembly-CSharp")` — копипаста
- L319: `Type.GetType("UBOAT.Game.Core.Data.Locale, com.uboat.game")` — копипаста
- L320: `?? Type.GetType("UBOAT.Game.Core.Data.Locale, Assembly-CSharp")` — копипаста

**Итого: 8 вызовов `Type.GetType()`, 4 из которых повторяются при каждом обращении к локали.** Результат методов `GetCurrentLanguage()` (кэшируемое значение — строка языка) нигде не сохраняется.

### Duplicate BindingFlags

Строки 63-64, 97-98, 101-102 — три копии одного набора флагов с дубликатом `BindingFlags.Instance`:

```csharp
// Line 63-64:
insertMethod = storyType.GetMethod("InsertDecision",
    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public | BindingFlags.Instance);
//                                                                              ^ дубликат

// Line 97-98:
decisionsProp = storyType.GetProperty("Decisions",
    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public | BindingFlags.Instance);
//                                                                              ^ дубликат

// Line 100-102:
onUpdatedMethod = storyType.GetMethod("OnUpdated",
    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public | BindingFlags.Instance,
//                                                                              ^ дубликат
    null, Type.EmptyTypes, null);
```

Это не баг (битовые флаги OR дают тот же результат), но признак копипасты. Если в будущем нужно будет сменить флаги — придётся править 3 места.

### Fragile positional constructor contract

`CreateDecision()` [L201-229] создаёт объект Decision через reflection, предполагая exact signal конструктора:

```csharp
object[] ctorArgs = new object[paramCount];
ctorArgs[0] = LocaleKey;      // string
ctorArgs[1] = null;            // nullable
ctorArgs[2] = true;            // bool
ctorArgs[3] = new Action(ToggleRadio);  // Action at index 3
ctorArgs[4] = 0;               // int
ctorArgs[5] = null;            // nullable
for (int i = 6; i < paramCount; i++) { ... }
```

Если UBOAT добавит параметр до индекса 3, Action уедет на индекс 4, и `ToggleRadio` будет передан в неверный параметр. Отбора нет — только `Debug.LogError`, если конструктор не найден вообще.

---

## Предлагаемое решение

1. **Кэшировать `GetCurrentLanguage()`** — сохранять результат в `static string cachedLanguage` + сбрасывать при `ResetRuntimeState()`
2. **Убрать `BindingFlags` дубликаты** — вынести в константу
3. **Positional args → именованные** — если тип Decision известен, создать его через Activator или documented constructor
4. **Graceful degradation** — документировать game API surface, логировать при первой неудаче, не повторять

---

## Acceptance Criteria

- [ ] `GetCurrentLanguage()` дёргает `Type.GetType()` 0 раз после первого вызова (читает из кэша)
- [ ] `BindingFlags` не содержат дубликатов
- [ ] При изменении API UBOAT мод логирует понятную ошибку и отключает T3PLL38-интеграцию без падения
