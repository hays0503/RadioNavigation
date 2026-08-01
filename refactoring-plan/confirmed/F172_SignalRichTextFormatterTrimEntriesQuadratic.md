# F172. SignalRichTextFormatter.TrimEntries — O(n²) из-за RemoveAt(0)

| Поле | Значение |
|------|----------|
| **Приоритет** | P3 (Low) |
| **Область** | UI · SignalRichTextFormatter · Performance |
| **Файл** | `Source/UI/SignalRichTextFormatter.cs` |
| **Строки** | 61-68 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

```csharp
// SignalRichTextFormatter.cs:61-68
public static void TrimEntries(List<SignalEntry> entries, ref int morseCharCount)
{
    while ((morseCharCount > MaxMorseChars || entries.Count > MaxSignalEntries)
        && entries.Count.IsPositive())
    {
        if (entries[0].Type != SignalCharType.Separator)
            morseCharCount--;
        entries.RemoveAt(0);  // <-- O(n) shift
    }
}
```

`List<T>.RemoveAt(0)` вызывает сдвиг всех оставшихся элементов на одну позицию — O(n). Если нужно удалить k элементов из списка размера n, общая сложность O(k*n). При `MaxSignalEntries = 60` и частых вызовах (каждый morse element) это может стать узким местом.

## Корневая причина

Использование List как очереди (FIFO) через RemoveAt(0).

## Предлагаемое исправление

Использовать `RemoveRange(0, removeCount)` — один O(n) вызов вместо k вызовов:

```csharp
public static void TrimEntries(List<SignalEntry> entries, ref int morseCharCount)
{
    int removeCount = 0;
    while (removeCount < entries.Count &&
           (morseCharCount > MaxMorseChars || entries.Count - removeCount > MaxSignalEntries))
    {
        if (entries[removeCount].Type != SignalCharType.Separator)
            morseCharCount--;
        removeCount++;
    }
    if (removeCount > 0)
        entries.RemoveRange(0, removeCount);  // O(n) one-time
}
```

## Зависимости

- Связан с F24 (TrimEntries RemoveAtZero)
- Связан с F108 (ListAddOnAudioThread)
