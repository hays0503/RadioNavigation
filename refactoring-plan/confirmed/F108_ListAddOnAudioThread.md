# F108. SonneController.OnStationElementCompleted — signalEntries list modified from audio thread

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Sound · Threading · Thread Safety |
| **Файл** | `Source/Logic/SonneController.cs` |
| **Строки** | 143-146 |

---

## Описание

`signalEntries.Add(new SignalEntry(c, type))` вызывается из audio thread (через `OnStationElementCompleted`) до вызова `SignalRichTextFormatter.TrimEntries`:

```csharp
// Строка 143-146 (audio thread):
signalEntries.Add(new SignalEntry(c, type));
if (type != SignalCharType.Separator)
    morseCharCount++;
SignalRichTextFormatter.TrimEntries(signalEntries, ref morseCharCount);
```

Паттерн Add+Trim означает, что список временно растёт за пределы `MaxSignalEntries` (60). Основной поток может прочитать увеличенный список через `GetSignalRichText()` → `FormatRichText()` между Add и Trim.

Более того, `List<T>.Add` может вызвать resize внутреннего массива, аллоцируя память в audio thread.

## Корневая причина

Нет синхронизации доступа к `signalEntries` между audio и main потоками.

## Предлагаемое исправление

1. Использовать lock при доступе к `signalEntries`
2. Либо вынести обработку в main thread через очередь
3. Либо использовать `ConcurrentQueue<SignalEntry>`

## Зависимости

- Связан с F01 (audio race condition), F29 (no synchronization)
- Связан с F109 (RemoveAt(0) thread safety)
- Связан с F105 (StringBuilder allocs on audio thread)
