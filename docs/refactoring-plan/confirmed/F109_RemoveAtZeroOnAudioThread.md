# F109. SignalRichTextFormatter.TrimEntries — RemoveAt(0) on audio thread while main thread reads

| Поле | Значение |
|------|----------|
| **Приоритет** | P1 (High) |
| **Область** | Sound · Threading · Data Corruption |
| **Файлы** | `Source/UI/SignalRichTextFormatter.cs` (76), `Source/Logic/SonneController.cs` (146) |

---

## Описание

`RemoveAt(0)` сдвигает все элементы влево на одну позицию. Вызывается из audio thread (через `OnStationElementCompleted` → `TrimEntries`). Основной поток одновременно читает тот же список через `GetSignalRichText()` → `FormatRichText()`:

```csharp
// SignalRichTextFormatter.cs, строка 76 (audio thread):
entries.RemoveAt(0);  // O(n) shift, модифицирует список

// SonneController.cs, строка 214-216 (main thread):
public string GetSignalRichText()
{
    return SignalRichTextFormatter.FormatRichText(signalEntries);  // читает тот же список
}
```

F24 документирует O(n²) проблему `RemoveAt(0)`, но аспект потокобезопасности мутации общего `List<T>` из audio thread — отдельная проблема.

**Результат:** `ArgumentOutOfRangeException` или повреждённые данные если список ресайзится во время сдвига.

## Корневая причина

Нет синхронизации между audio thread (мутация) и main thread (чтение).

## Предлагаемое исправление

1. Использовать lock при доступе к `signalEntries`
2. Использовать ring buffer вместо List с RemoveAt(0)
3. Вынести все мутации в один поток

## Зависимости

- Связан с F24 (RemoveAt O(n²)), F01 (audio race condition), F29 (no synchronization)
- Связан с F108 (List.Add on audio thread)
