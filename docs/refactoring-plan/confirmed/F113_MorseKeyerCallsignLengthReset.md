# F113. MorseKeyer.AdvanceElement — callsignLength reset to 0 on consol pattern transition

| Поле | Значение |
|------|----------|
| **Приоритет** | P3 (Low) |
| **Область** | Logic · MorseKeyer · Correctness |
| **Файл** | `Source/Logic/MorseKeyer.cs` |
| **Строки** | 155-159 |

---

## Описание

Когда consol pattern заканчивается и планировщик предоставляет новый, `callsignLength` безусловно сбрасывается в 0:

```csharp
// Строка 152-159:
var nextPattern = scheduler.GetCurrentConsolPattern();
if (nextPattern.IsNotNull() && nextPattern.Length.IsPositive())
{
    consolPattern = nextPattern;
    elementIndex = 0;
    callsignLength = 0;   // <-- сброс без передачи нового значения
    StartElement();
    return;
}
```

Это означает, что callsign элементы нового pattern'а не будут отличаться от навигационных. Свойство `IsCallsignElement` (строка 26) зависит от `callsignLength`:

```csharp
public bool IsCallsignElement => consolPattern.IsNotNull() && elementIndex < callsignLength;
```

## Корневая причина

`callsignLength` не передаётся из station data при смене pattern'а.

## Предлагаемое исправление

Передавать `callsignLength` через `scheduler` или station state:

```csharp
callsignLength = scheduler.CallsignElementCount;  // или аналогичное свойство
```

## Зависимости

- Связан с F65 (stale consolPattern)
