# F189: Cross-thread data race on `signalEntries` / `morseDecoderBuffer` between Audio Thread and UI Thread

**Приоритет:** P1 (High)  
**Область:** Sound · UI · Thread Safety  
**Файл:** `Source/Logic/SonneController.cs:143`, `Source/UI/InformationPanelHandler.cs:402`  
**Дата обнаружения:** 2026-07-31  

---

## Проблема

В `SonneController.cs`:

1. Обработчик `OnStationElementCompleted` вызывается из метода `MorseKeyer.Advance()`, выполняющегося в **Unity Audio Thread** (`OnAudioFilterRead`). В нем происходят записи:
   ```csharp
   signalEntries.Add(new SignalEntry(c, type));
   morseDecoderBuffer.Append('.');
   ```
2. В это же время **Main UI Thread** (в `InformationPanelHandler.UpdateSignalDisplay()`, вызванном из `Update()`) вызывает:
   ```csharp
   string rich = sonneController.GetSignalRichText();
   ```
   который итерирует по списку `signalEntries` без каких-либо примитивов синхронизации (`lock`, `ConcurrentQueue`, double-buffering).

Это классический Data Race между потоком генерации звука и потоком отрисовки UI.

---

## Влияние

1. Исключение `InvalidOperationException: Collection was modified; enumeration operation may not execute` на главном потоке при обновлении текста сигналов.
2. Повреждение данных в `StringBuilder morseDecoderBuffer` при одновременной записи в аудио-потоке и вызове `Clear()` / `ToString()` в основном потоке.

---

## Исправление

Использовать блокировку `lock` или потокобезопасную очередь событий (`ConcurrentQueue<SignalEntry>`) для передачи символов из аудио-потока в основной поток:

```csharp
private readonly object signalLock = new object();

private void OnStationElementCompleted(...)
{
    lock (signalLock)
    {
        signalEntries.Add(new SignalEntry(c, type));
    }
}

public string GetSignalRichText()
{
    lock (signalLock)
    {
        return SignalRichTextFormatter.FormatRichText(signalEntries);
    }
}
```

---

## Acceptance Criteria

- [ ] Все обращения к `signalEntries` и `morseDecoderBuffer` защищены примитивом синхронизации.
- [ ] Ошибки модификации коллекции во время обхода в UI отсутствуют.
