# F186: `RadioStoryIntegration.Init` accesses `GetParameters()[1]` without length check

**Приоритет:** P2 (Medium)  
**Область:** Logic · RadioStoryIntegration · Reflection Fragility  
**Файл:** `Source/Logic/RadioStoryIntegration.cs:63-66`  
**Дата обнаружения:** 2026-07-31  

---

## Проблема

```csharp
insertMethod = storyType.GetMethod("InsertDecision",
    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public | BindingFlags.Instance);
if (insertMethod.IsNotNull())
    decisionType = insertMethod.GetParameters()[1].ParameterType;
```

Если в обновленной версии UBOAT метод `InsertDecision` изменит количество параметров (например, станет принимать 0 или 1 параметр), обращения по индексу `[1]` вызовет исключение `IndexOutOfRangeException` во время выполнения `Init()`, что прервет выполнение инициализации мода без понятного лога ошибок.

---

## Влияние

Падение инициализации мода при обновлениях игры, если сигнатура метода `RadioStory.InsertDecision` изменится.

---

## Исправление

Добавить валидацию длины массива параметров:

```csharp
if (insertMethod.IsNotNull())
{
    var pars = insertMethod.GetParameters();
    if (pars.Length > 1)
        decisionType = pars[1].ParameterType;
    else
        Debug.LogError("[RadioNav] RadioStory.InsertDecision method parameter count < 2");
}
```

---

## Acceptance Criteria

- [ ] Обращение к `GetParameters()[1]` защищено проверкой длины массива.
- [ ] Ошибка сигнатуры логируется безопасно.
