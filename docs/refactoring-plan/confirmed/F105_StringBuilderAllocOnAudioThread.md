# F105. Audio thread StringBuilder allocations — GC pressure from morseDecoderBuffer and decodedText

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Sound · Threading · GC Pressure |
| **Файл** | `Source/Logic/SonneController.cs` |
| **Строки** | 143, 148-158, 195-206 |

---

## Описание

`StringBuilder.Append('.')` (строка 151), `StringBuilder.Append('-')` (строка 153) и `StringBuilder.ToString(pos, mc.Length)` (строка 205) вызываются из audio thread через `OnStationElementCompleted` → `DecodeCallsignBuffer`:

```csharp
// Строка 148-158 (audio thread):
if (isCallsign)
{
    if (elemType == NavigationElementType.Dot)
        morseDecoderBuffer.Append('.');       // <-- может триггернуть realloc
    else if (elemType == NavigationElementType.Dash)
        morseDecoderBuffer.Append('-');       // <-- может триггернуть realloc
}
```

`StringBuilder.Append` может вызвать внутреннее увеличение буфера (удвоение capacity), а `ToString()` аллоцирует новую строку. Всё это происходит в `OnAudioFilterRead` callback, где GC аллокации вызывают audio glitches.

Та же проблема с `signalEntries.Add()` на строке 143 — `List<T>.Add` может триггернуть resize внутреннего массива.

## Корневая причина

StringBuilder и List не имеют предварительного выделения буфера, их операции аллоцируют память в audio thread.

## Предлагаемое исправление

1. Предварительно выделить буфер: `morseDecoderBuffer.Capacity = 64;`
2. Использовать `StringBuilder.Clear()` вместо `new StringBuilder()`
3. Рассмотреть ring buffer вместо `List<SignalEntry>` с `RemoveAt(0)`
4. Вынести `DecodeCallsignBuffer` в main thread через очередь

## Зависимости

- Связан с F01 (audio race condition), F08 (GC pressure), F13 (Unity API in audio thread)
- Связан с F108 (List.Add on audio thread)
