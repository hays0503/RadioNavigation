# F165. MorseKeyer.Reset — не очищает поле sequence

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Logic · MorseKeyer · State Management |
| **Файл** | `Source/Logic/MorseKeyer.cs` |
| **Строки** | 107-115 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

```csharp
// MorseKeyer.cs:107-115
public void Reset()
{
    elementIndex = 0;
    ticksInState = 0;
    state = State.Idle;
    IsActive = false;
    consolPattern = null;
    // <-- sequence НЕ очищается
}
```

`Reset()` очищает `consolPattern`, но не очищает `sequence`. После вызова `Reset()` поле `sequence` продолжает хранить ссылку на предыдущий объект.

Это проблема, потому что:
1. `Load()` и `LoadConsol()` — оба метода устанавливают `IsActive = false` и перезаписывают оба поля. `Reset()` делает это непоследовательно.
2. Свойство `IsCallsignElement` проверяет `consolPattern`, но `GetCurrentElementType()` и `GetCurrentSoundTicks()` проверяют `sequence` — после `Reset()` они могут вернуть данные из старого sequence.

Данная проблема дополняет F116 (асимметрия Load/LoadConsol) и F65 (stale consolPattern) — та же корневая причина, но в методе `Reset()`.

## Корневая причина

`Reset()` написан до добавления `LoadConsol` и не был обновлён при добавлении dual-source логики.

## Предлагаемое исправление

```csharp
public void Reset()
{
    elementIndex = 0;
    ticksInState = 0;
    state = State.Idle;
    IsActive = false;
    sequence = null;
    consolPattern = null;
}
```

## Зависимости

- Связан с F65 (MorseKeyer.Load stale consolPattern)
- Связан с F116 (асимметрия Load/LoadConsol)
- Связан с F113 (callsignLength reset)
