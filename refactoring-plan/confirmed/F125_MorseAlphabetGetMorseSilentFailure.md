# F125. MorseAlphabet.GetMorse() молча возвращает "" для неизвестных символов

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Logic · MorseAlphabet · Correctness |
| **Файл** | `Source/Logic/MorseAlphabet.cs` |
| **Строки** | 61-66 |

---

## Описание

`GetMorse()` возвращает пустую строку для символов, не имеющих Morse-определения:

```csharp
// MorseAlphabet.cs:61-66
public static string GetMorse(char c)
{
    if (c < 0 || c >= table.Length)
        return "";
    return table[c] ?? "";
}
```

Это вызывает тихую потерю данных в `TextToPattern()` и `GetElementCount()`:

```csharp
// MorseAlphabet.cs:93-103 — TextToPattern
string mc = GetMorse(text[i]);
for (int b = 0; b < mc.Length; b++)
    elements.Add(mc[b] == '-');
// Если mc = "", цикл не выполняется → символ пропущен
```

`GenerateConsolPattern()` в `SonneSignalGenerator.cs:~92` вызывает `GetElementCount(callsign)` и `TextToPattern(callsign)`. Если callsign содержит неизвестный символ (например, дефис, пробел, спецсимвол), он **молча опускается** — паттерн содержит fewer elements чем ожидалось, что приводит к неверной timing.

## Корневая причина

Пустая строка — валидный возврат для "символ не найден", но нет механизма предупреждения.

## Предлагаемое исправление

1. Добавить логирование для неизвестных символов:

```csharp
public static string GetMorse(char c)
{
    if (c < 0 || c >= table.Length)
    {
        Debug.LogWarning("[RadioNav] MorseAlphabet: character out of range: " + (int)c);
        return "";
    }
    if (table[c].IsNull())
    {
        Debug.LogWarning("[RadioNav] MorseAlphabet: undefined character: '" + c + "' (" + (int)c + ")");
        return "";
    }
    return table[c];
}
```

2. Или выбрасывать исключение для неизвестных символов в `TextToPattern()`:

```csharp
string mc = GetMorse(text[i]);
if (mc.Length == 0)
    throw new ArgumentException($"MorseAlphabet: cannot encode character '{text[i]}'");
```

## Зависимости

- Связан с F53 (MorseAlphabet 'A' = `._`) — тот же файл
- Связан с F37 (6 станций с null callsign) — null callsign вызывает fallback на имя станции
