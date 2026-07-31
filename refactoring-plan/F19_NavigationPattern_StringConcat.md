# F19: NavigationPattern — O(n²) string concat в циклах

**Приоритет:** P3 (Low)
**Область:** Logic · NavigationPattern
**Тип:** Performance

---

## Проблема

`FormatPattern()` и `FormatCompact()` используют конкатенацию строк в циклах. Каждая `result += "..."` создаёт новую строку. На типичном паттерне (10-14 groups × 8 elements = ~100 символов) — десятки временных строк.

---

## Evidence

NavigationPattern.cs:58-74:

```csharp
public static string FormatPattern(NavigationPattern pattern)
{
    string result = "";
    for (int g = 0; g < pattern.Groups.Length; g++)
    {
        if (g.IsPositive()) result += " ";   // ← аллокация
        result += "[";                         // ← аллокация
        for (int e = 0; e < pattern.ElementsPerGroup; e++)
        {
            if (g == pattern.TransitionIndex)
                result += "=";                // ← аллокация × elements/g
            else if (pattern.Groups[g].GetElement(e))
                result += "-";                // ← аллокация × elements/g
            else
                result += ".";                // ← аллокация × elements/g
        }
        result += "]";                         // ← аллокация
    }
    return result;
}
```

Та же проблема в `FormatCompact()` (строки 82-107).

Оба метода вызываются из InformationPanelHandler.UpdatePatterns() ~5 раз/сек.

---

## Предлагаемое решение

Заменить на `StringBuilder`:

```csharp
var sb = new StringBuilder(pattern.Groups.Length * (pattern.ElementsPerGroup + 3));
for (int g = 0; g < pattern.Groups.Length; g++)
{
    if (g > 0) sb.Append(' ');
    sb.Append('[');
    // ...
    sb.Append(']');
}
return sb.ToString();
```

---

## Acceptance Criteria

- [ ] FormatPattern не аллоцирует промежуточные строки (одна финальная строка)
- [ ] FormatCompact использует StringBuilder
