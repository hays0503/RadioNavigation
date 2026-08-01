# F170. MorseAlphabet — фиксированная таблица на 128 символов, overflow при > ASCII

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Logic · MorseAlphabet · Data Structure |
| **Файл** | `Source/Logic/MorseAlphabet.cs` |
| **Строки** | 11, 57-59, 61-63 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

```csharp
// MorseAlphabet.cs:11
private static readonly string[] table = new string[128];

// MorseAlphabet.cs:57-59
private static void Define(char c, string morse)
{
    table[c] = morse;  // <-- если c >= 128, ArrayIndexOutOfBounds
    if (c >= 'A' && c <= 'Z')
        table[c + 32] = morse;  // <-- если c > 95,同样 overflow
}

// MorseAlphabet.cs:61-63
public static string GetMorse(char c)
{
    if (c < 0 || c >= table.Length)
        return "";
    return table[c] ?? "";
}
```

Таблица `table` имеет фиксированный размер 128. `GetMorse` проверяет `c >= table.Length`, но `TextToPattern` и `GetElementCount` вызывают `GetMorse` без предварительной проверки.

Если `text` содержит символ Unicode (например, эмодзи, кириллицу с комбинирующими знаками, или символы > U+007F), `GetMorse` вернёт пустую строку. Это не крашит программу, но создаёт невидимые элементы в паттерне — морзе-код будет содержать пустые элементы без звуков.

Дополнительно: `Define` метод (строка 57) не проверяет `c >= table.Length`.

## Корневая причина

Таблица создана для ASCII-only. Unicode символы не поддерживаются.

## Предлагаемое исправление

Использовать Dictionary для поддержки произвольных символов:

```csharp
private static readonly Dictionary<char, string> table = new Dictionary<char, string>();
```

Или добавить валидацию в `Define`:

```csharp
private static void Define(char c, string morse)
{
    if (c >= table.Length) return;
    table[c] = morse;
    if (c >= 'A' && c <= 'Z' && c + 32 < table.Length)
        table[c + 32] = morse;
}
```

## Зависимости

- Связан с F53 (MorseAlphabetA underscore bug)
- Связан с F76 (MorseAlphabet GetElementCount int overflow)
- Связан с F129 (GetElementCount no word boundary)
