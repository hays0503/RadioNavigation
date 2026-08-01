# F184: `MorseKeyer` duplicates pattern/sequence branching in multiple internal getters

**Приоритет:** P3 (Low)  
**Область:** Logic · MorseKeyer · Duplication  
**Файл:** `Source/Logic/MorseKeyer.cs:203-236`  
**Дата обнаружения:** 2026-07-31  

---

## Проблема

В `MorseKeyer.cs` три приватных метода имеют одинаковую структуру ветвления для выбора текущего элемента с радиосигналом (`consolPattern` vs `sequence`):

```csharp
private NavigationElementType GetCurrentElementType()
{
    if (consolPattern.IsNotNull() && elementIndex < consolPattern.Length)
        return consolPattern[elementIndex];
    if (sequence.IsNotNull() && elementIndex < sequence.ElementCount)
        return sequence.GetElement(elementIndex) ? NavigationElementType.Dash : NavigationElementType.Dot;
    return NavigationElementType.Dot;
}

private int GetCurrentSoundTicks()
{
    bool isDash = false;
    if (consolPattern.IsNotNull() && elementIndex < consolPattern.Length)
        isDash = consolPattern[elementIndex] == NavigationElementType.Dash;
    else if (sequence.IsNotNull() && elementIndex < sequence.ElementCount)
        isDash = sequence.GetElement(elementIndex);
    return isDash ? MorseTiming.GetDashSoundTicks(config) : MorseTiming.GetDotSoundTicks(config);
}

private int GetCurrentSilenceTicks()
{
    bool isDash = false;
    if (consolPattern.IsNotNull() && elementIndex < consolPattern.Length)
        isDash = consolPattern[elementIndex] == NavigationElementType.Dash;
    else if (sequence.IsNotNull() && elementIndex < sequence.ElementCount)
        isDash = sequence.GetElement(elementIndex);
    return isDash ? MorseTiming.GetDashSilenceTicks(config) : MorseTiming.GetDotSilenceTicks(config);
}
```

---

## Влияние

Дублирование логики проверки источников данных. При изменении внутренней структуры данных или добавлении нового типа источника сигнала придется правили три места.

---

## Исправление

Вынести определение флага `isDash` / типа элемента в единый вспомогательный метод:

```csharp
private bool IsCurrentElementDash()
{
    if (consolPattern.IsNotNull() && elementIndex < consolPattern.Length)
        return consolPattern[elementIndex] == NavigationElementType.Dash;
    if (sequence.IsNotNull() && elementIndex < sequence.ElementCount)
        return sequence.GetElement(elementIndex);
    return false;
}
```

И использовать его в `GetCurrentSoundTicks()` и `GetCurrentSilenceTicks()`.

---

## Acceptance Criteria

- [ ] Ветвление проверки `consolPattern` и `sequence` объединено в один метод.
- [ ] Поведение ключера Морзе не изменилось.
