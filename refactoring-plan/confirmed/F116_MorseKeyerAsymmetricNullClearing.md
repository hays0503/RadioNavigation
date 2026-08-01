# F116. MorseKeyer — asymmetric null-clearing between Load/LoadConsol

| Поле | Значение |
|------|----------|
| **Приоритет** | P3 (Low) |
| **Область** | Logic · MorseKeyer · API Design |
| **Файл** | `Source/Logic/MorseKeyer.cs` |
| **Строки** | 31-43 vs 51-65 |

---

## Описание

`LoadConsol` явно устанавливает `sequence = null` (строка 53) при переключении в consol mode. Однако `Load` **не** очищает `consolPattern`:

```csharp
// LoadConsol (строка 51-65):
public void LoadConsol(...)
{
    sequence = null;       // <-- очищает sequence
    this.scheduler = scheduler;
    this.config = config;
    elementIndex = 0;
    ticksInState = 0;
    state = State.Idle;
    consolPattern = pattern;
    this.callsignLength = callsignLength;
    IsActive = pattern.IsNotNull() && pattern.Length.IsPositive();
    if (IsActive) StartElement();
}

// Load (строка 31-44):
public void Load(...)
{
    this.sequence = sequence;
    this.scheduler = scheduler;
    this.config = config;
    elementIndex = 0;
    ticksInState = 0;
    state = State.Idle;
    callsignLength = 0;
    // <-- НЕ очищает consolPattern
    IsActive = sequence.IsNotNull() && sequence.ElementCount.IsPositive();
    if (IsActive) StartElement();
}
```

Если `Load` вызван после `LoadConsol`, старый `consolPattern` остаётся установленным. Свойство `IsCallsignElement` (строка 26) проверяет `consolPattern`:

```csharp
public bool IsCallsignElement => consolPattern.IsNotNull() && elementIndex < callsignLength;
```

Это может привести к неправильной классификации элементов.

F65 документирует проблему `Load` не очищающего `consolPattern`. Данный issue охватывает **асимметричный дизайн** в целом — один метод очищает поле другого, но не наоборот.

## Корневая причина

Асимметричное состояние: `LoadConsol` очищает `sequence`, но `Load` не очищает `consolPattern`.

## Предлагаемое исправление

Добавить `consolPattern = null` в `Load`:

```csharp
public void Load(IKeySequence sequence, StationScheduler scheduler = null, ConsolCycleConfig config = default(ConsolCycleConfig))
{
    this.sequence = sequence;
    this.scheduler = scheduler;
    this.config = config;
    elementIndex = 0;
    ticksInState = 0;
    state = State.Idle;
    callsignLength = 0;
    consolPattern = null;   // <-- добавить
    IsActive = sequence.IsNotNull() && sequence.ElementCount.IsPositive();
    if (IsActive) StartElement();
}
```

## Зависимости

- Связан с F65 (stale consolPattern)
