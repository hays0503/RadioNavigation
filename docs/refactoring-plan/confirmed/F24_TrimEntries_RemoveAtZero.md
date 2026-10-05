# F24: SignalRichTextFormatter.TrimEntries — O(n²) RemoveAt(0)

**Приоритет:** P1 (High)
**Область:** UI · SignalRichTextFormatter
**Тип:** Performance

---

## Проблема

`TrimEntries()` использует `List<SignalEntry>.RemoveAt(0)` в while-loop. Каждый RemoveAt(0) сдвигает все оставшиеся элементы через Array.Copy. Для MaxSignalEntries=60 это ~1770 копирований за burst.

---

## Evidence

SignalRichTextFormatter.cs:69-78:

```csharp
public static void TrimEntries(List<SignalEntry> entries, ref int morseCount)
{
    while ((morseCharCount > MaxMorseChars || entries.Count > MaxSignalEntries)
           && entries.Count.IsPositive())
    {
        if (entries[0].Type != SignalCharType.Separator)
            morseCharCount--;
        entries.RemoveAt(0);  // ← O(n) за вызов
    }
}
```

Вызывается из SonneController.cs ~5 раз/сек при активном сигнале.

### O(n²) breakdown

| Вызов | Сдвиг |
|-------|-------|
| 1st RemoveAt(0) | 59 elements |
| 2nd RemoveAt(0) | 58 elements |
| ... | ... |
| 60th RemoveAt(0) | 0 elements |
| **Total** | **~1770 copies per burst** |

---

## Предлагаемое решение

Заменить `List<SignalEntry>` на `Queue<SignalEntry>` или circular buffer:

```csharp
private readonly Queue<SignalEntry> signalEntries = new Queue<SignalEntry>();

public static void TrimEntries(Queue<SignalEntry> entries, ref int morseCount)
{
    while ((morseCharCount > MaxMorseChars || entries.Count > MaxSignalEntries)
           && entries.Count > 0)
    {
        var entry = entries.Dequeue();  // ← O(1)
        if (entry.Type != SignalCharType.Separator)
            morseCharCount--;
    }
}
```

---

## Acceptance Criteria

- [ ] TrimEntries не вызывает Array.Copy (O(1) удаление с начала)
- [ ] Signal buffer поведение идентично текущему
