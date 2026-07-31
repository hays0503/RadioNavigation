# F144. NavigationPattern — конструктор принимает ElementsPerGroup=0 → DivideByZeroException

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Logic · NavigationPattern · Validation |
| **Файл** | `Source/Logic/NavigationPattern.cs` |
| **Строки** | 36-41, 46-47 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

Конструктор не валидирует `ElementsPerGroup`:

```csharp
// NavigationPattern.cs:36-41
public NavigationPattern(Group[] groups, byte elementsPerGroup, byte transitionIndex = 255)
{
    Groups = groups ?? Array.Empty<Group>();
    ElementsPerGroup = elementsPerGroup;  // нет валидации
    TransitionIndex = transitionIndex;
}
```

При `ElementsPerGroup = 0` деление на ноль в `GetElement`:

```csharp
// NavigationPattern.cs:46-47
int g = index / ElementsPerGroup;  // DivideByZeroException
int e = index % ElementsPerGroup;
```

Тот же вызов в `FormatPattern` (строка 63) и `FormatCompact` (строки 89, 96, 101).

**Текущий риск:** Все текущие callers используют `elementsPerGroup = 8`. Это latent bug — публичный API не защищён.

## Корневая причина

Отсутствие input validation в конструкторе.

## Предлагаемое исправление

```csharp
public NavigationPattern(Group[] groups, byte elementsPerGroup, byte transitionIndex = 255)
{
    Groups = groups ?? Array.Empty<Group>();
    ElementsPerGroup = elementsPerGroup == 0
        ? (byte)8  // fallback с предупреждением
        : elementsPerGroup;
    TransitionIndex = transitionIndex;
}
```

Или `throw new ArgumentException` если ноль недопустим.

## Зависимости

- Связан с F126 (ConsolCycleConfig no validation) — та же проблема отсутствия валидации
- Связан с F129 (MorseAlphabet getElementCount no word boundary) — тот же файл, другой edge case
