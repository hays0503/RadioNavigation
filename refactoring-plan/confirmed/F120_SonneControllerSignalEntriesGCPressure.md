# F120. SonneController — signalEntries + measurements GC pressure в hot path

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Logic · SonneController · Performance · GC |
| **Файл** | `Source/Logic/SonneController.cs` |
| **Строки** | 383-386, 399 |

---

## Описание

В `UpdateStationGains()` (вызывается каждые 0.016с) и `UpdateStationNavigation()` (каждые 0.2с) выполняются `.Clear()` на списках:

```csharp
// SonneController.cs:383-386 (UpdateStationGains, ~60Hz)
signalEntries.Clear();
morseCharCount = 0;
morseDecoderBuffer.Clear();
decodedText.Clear();

// SonneController.cs:399 (UpdateStationNavigation, ~5Hz)
measurements.Clear();
```

`List<T>.Clear()` **не освобождает** внутренний массив — это OK для переиспользования. Однако `signalEntries` растёт через `signalEntries.Add(new SignalEntry(...))` в `OnStationElementCompleted` (audio thread, ~60 раз/сек). При превышении capacity `List<T>` **аллоцирует новый массив** ×2 — это happens в audio thread → **GC pressure на audio thread** (F08, F13).

Дополнительно: `morseDecoderBuffer` и `decodedText` — `StringBuilder`, которые при `.Clear()` **не освобождают** буфер. При длительной работе (>1 час) буфер может вырасти до `MaxDecodedChars` (30) × `sizeof(char)` = 60 байт — не критично, но неоптимально.

## Корневая причина

`signalEntries` не имеет предустановленного capacity. `List<T>` удваивает capacity при переполнении → аллокация в hot path.

## Предлагаемое исправление

1. Задать начальный capacity:
```csharp
private readonly List<SignalEntry> signalEntries = new List<SignalEntry>(SignalRichTextFormatter.MaxSignalEntries);
```

2. Использовать `TrimEntries` с `RemoveRange(0, n)` вместо `RemoveAt(0)` в цикле (F24 уже документирует O(n²) RemoveAt(0)):
```csharp
// SignalRichTextFormatter.TrimEntries — текущий код:
while ((morseCharCount > MaxMorseChars || entries.Count > MaxSignalEntries)
    && entries.Count.IsPositive())
{
    if (entries[0].Type != SignalCharType.Separator)
        morseCharCount--;
    entries.RemoveAt(0);  // O(n) каждый вызов
}

// Исправление:
int removeCount = 0;
while (removeCount < entries.Count &&
       (morseCharCount > MaxMorseChars || entries.Count - removeCount > MaxSignalEntries))
{
    if (entries[removeCount].Type != SignalCharType.Separator)
        morseCharCount--;
    removeCount++;
}
if (removeCount > 0)
    entries.RemoveRange(0, removeCount);  // O(n) один раз
```

3. `measurements` — задать capacity:
```csharp
private readonly List<PositionFixCalculator.Measurement> measurements =
    new List<PositionFixCalculator.Measurement>(BeaconData.Stations.Length);
```

## Зависимости

- Связан с F08 (GC Pressure) — конкретный site
- Связан с F24 (RemoveAt O(n²)) — TrimEntries
- Связан с F108/F109 (audio thread allocations)
