# F174. NavigationPattern.GetElement — нет проверки отрицательного индекса

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Logic · NavigationPattern · Correctness |
| **Файл** | `Source/Logic/NavigationPattern.cs` |
| **Строки** | 48-55 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

```csharp
// NavigationPattern.cs:48-55
public bool GetElement(int index)
{
    if (Groups.Length.IsZero()) return false;
    int g = index / ElementsPerGroup;  // <-- отрицательный index даёт отрицательное g
    int e = index % ElementsPerGroup;  // <-- отрицательный index даёт отрицательное e
    if (g >= Groups.Length) return false;
    if (g == TransitionIndex) return false;
    return Groups[g].GetElement(e);   // <-- Groups[-1] → IndexOutOfRange
}
```

При `index < 0`:
- `g = index / ElementsPerGroup` даёт отрицательное значение (в C# целочисленное деление отрицательных даёт отрицательное)
- `Groups[g]` выбросит `IndexOutOfRangeException`

Хотя текущий вызывающий код (`MorseKeyer`) обычно передаёт положительные индексы, `elementIndex` может стать отрицательным если `AdvanceElement` вызван при `elementIndex = 0`.

Дополнительно: `Group.GetElement(int index)` (строка 38) тоже не проверяет отрицательный индекс — `Bits >> отрицательное` даёт неопределённое поведение в C#.

## Корневая причина

Отсутствие defensive check в публичном методе.

## Предлагаемое исправление

```csharp
public bool GetElement(int index)
{
    if (Groups.Length.IsZero()) return false;
    if (index.IsNegative()) return false;
    int g = index / ElementsPerGroup;
    int e = index % ElementsPerGroup;
    if (g >= Groups.Length) return false;
    if (g == TransitionIndex) return false;
    return Groups[g].GetElement(e);
}
```

## Зависимости

- Связан с F114 (MorseKeyer dot silent fallback masks OOB)
- Связан с F70 (FormatCompact transition misrepresentation)
