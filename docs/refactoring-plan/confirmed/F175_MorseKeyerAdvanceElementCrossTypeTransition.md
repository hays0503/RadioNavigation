# F175. MorseKeyer.AdvanceElement — некорректный переход между типами sequence

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Logic · MorseKeyer · State Machine |
| **Файл** | `Source/Logic/MorseKeyer.cs` |
| **Строки** | 142-180 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

```csharp
// MorseKeyer.cs:142-180
private void AdvanceElement()
{
    elementIndex++;

    if (consolPattern.IsNotNull() && elementIndex >= consolPattern.Length)
    {
        OnSequenceFinished?.Invoke();
        if (scheduler.IsNotNull())
        {
            scheduler.OnSequenceFinished();
            var nextPattern = scheduler.GetCurrentConsolPattern();
            if (nextPattern.IsNotNull() && nextPattern.Length.IsPositive())
            {
                consolPattern = nextPattern;
                elementIndex = 0;
                callsignLength = 0;  // <-- сброс callsignLength
                StartElement();
                return;
            }
            // ...
        }
    }
    else if (sequence.IsNotNull() && elementIndex >= sequence.ElementCount)
    {
        OnSequenceFinished?.Invoke();
        if (scheduler.IsNotNull())
        {
            scheduler.OnSequenceFinished();
            var nextSeq = scheduler.GetCurrentSequence();
            if (nextSeq.IsNotNull() && nextSeq.ElementCount.IsPositive())
            {
                sequence = nextSeq;
                elementIndex = 0;
                // <-- НЕ сбрасывает callsignLength
                // <-- НЕ очищает consolPattern (если был)
                StartElement();
                return;
            }
            // ...
        }
    }
    // ...
}
```

При переходе от consol pattern к sequence (ветка `consolPattern`):
- `callsignLength = 0` — сбрасывается
- `consolPattern` остаётся установленным (не очищается)

При переходе от sequence к другому sequence (ветка `sequence`):
- `callsignLength` НЕ сбрасывается
- `consolPattern` НЕ очищается (если был установлен до этого)

Это создаёт состояние, когда:
1. После consol→sequence перехода, `consolPattern` всё ещё не null
2. `IsCallsignElement` (строка 26) проверяет `consolPattern` — после consol→sequence перехода, элементы sequence будут классифицироваться как callsign если `elementIndex < callsignLength`

Это та же корневая проблема что F113, но в контексте перехода между типами sequence.

## Корневая причина

`AdvanceElement` не учитывает, что sequence и consolPattern — взаимоисключающие состояния. При переходе от одного к другому, старое поле не очищается.

## Предлагаемое исправление

```csharp
// В ветке consolPattern → nextPattern:
consolPattern = nextPattern;
sequence = null;          // <-- добавить
elementIndex = 0;
callsignLength = scheduler.CallsignElementCount; // <-- взять из scheduler
StartElement();

// В ветке sequence → nextSeq:
sequence = nextSeq;
consolPattern = null;     // <-- добавить
elementIndex = 0;
callsignLength = 0;       // <-- добавить
StartElement();
```

## Зависимости

- Связан с F65 (MorseKeyer.Load stale consolPattern)
- Связан с F113 (callsignLength reset)
- Связан с F116 (асимметрия Load/LoadConsol)
- Связан с F165 (Reset не очищает sequence)
