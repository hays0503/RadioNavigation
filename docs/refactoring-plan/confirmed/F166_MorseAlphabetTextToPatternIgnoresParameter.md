# F166. MorseAlphabet.TextToPattern — игнорирует параметр elementDurationTicks

| Поле | Значение |
|------|----------|
| **Приоритет** | P3 (Low) |
| **Область** | Logic · MorseAlphabet · API |
| **Файл** | `Source/Logic/MorseAlphabet.cs` |
| **Строки** | 89 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

```csharp
// MorseAlphabet.cs:89
public static NavigationPattern TextToPattern(string text, int elementDurationTicks = 1)
{
    // ... тело метода ...
    return new NavigationPattern(groups, (byte)groupSize, 255);
    // elementDurationTicks нигде не используется
}
```

Метод принимает параметр `elementDurationTicks`, но нигде его не использует. Создаваемый `NavigationPattern` всегда получает `ElementsPerGroup = 8` (hardcoded `groupSize`), а `elementDurationTicks` просто отбрасывается.

Это вводит в заблуждение: вызывающий код может думать, что настраивает длительность элемента, но на практике это не работает.

## Корневая причина

Параметр был добавлен как заглушка для будущей функциональности, но так и не был реализован.

## Предлагаемое исправление

Удалить неиспользуемый параметр или реализовать его использование:

```csharp
// Вариант 1: удалить параметр
public static NavigationPattern TextToPattern(string text)

// Вариант 2: использовать параметр
public static NavigationPattern TextToPattern(string text, int elementDurationTicks = 1)
{
    // ... существующий код ...
    return new NavigationPattern(groups, (byte)elementDurationTicks, 255);
}
```

## Зависимости

- Вызывается из `CallsignPattern` constructor (NavigationPattern.cs:111)
- Связан с F70 (FormatCompact transition misrepresentation)
