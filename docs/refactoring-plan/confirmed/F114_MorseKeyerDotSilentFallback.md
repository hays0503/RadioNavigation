# F114. MorseKeyer.GetCurrentElementType — silent Dot fallback masks potential OOB

| Поле | Значение |
|------|----------|
| **Приоритет** | P3 (Low) |
| **Область** | Logic · MorseKeyer · Defensive Coding |
| **Файл** | `Source/Logic/MorseKeyer.cs` |
| **Строки** | 203-212 |

---

## Описание

Когда `elementIndex >= consolPattern.Length` и `elementIndex >= sequence.ElementCount` (или sequence null), `GetCurrentElementType` возвращает `NavigationElementType.Dot` по умолчанию:

```csharp
// Строка 203-212:
private NavigationElementType GetCurrentElementType()
{
    if (consolPattern.IsNotNull() && elementIndex < consolPattern.Length)
        return consolPattern[elementIndex];

    if (sequence.IsNotNull() && elementIndex < sequence.ElementCount)
        return sequence.GetElement(elementIndex) ? NavigationElementType.Dash : NavigationElementType.Dot;

    return NavigationElementType.Dot;  // <-- fallback по умолчанию
}
```

Этот fallback используется `GetCurrentSoundTicks()` для определения длительности тона. Если этот путь достигнут (например, при race condition где `elementIndex` продвинулся за границы массива), keyer произведёт тон dot'а с неправильным таймингом.

Хотя этот путь не должен достигаться в нормальной работе, молчаливый fallback маскирует потенциальные ошибки выхода за границы массива, которые были бы видны как исключения.

## Корневая причина

Отсутствие явной обработки ошибки индексации.

## Предлагаемое исправление

```csharp
return NavigationElementType.Dot;
// Заменить на:
throw new InvalidOperationException(
    $"GetCurrentElementType: elementIndex {elementIndex} out of range " +
    $"(consolPattern.Length={consolPattern?.Length}, sequence.ElementCount={sequence?.ElementCount})");
```

## Зависимости

- Независим от других F-проблем
