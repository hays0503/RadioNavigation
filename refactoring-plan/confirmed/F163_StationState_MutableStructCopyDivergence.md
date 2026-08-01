# F163. StationState — mutable struct copy-divergence

| Поле | Значение |
|------|----------|
| **Приоритет** | P3 (Low) |
| **Область** | Data · StationState · Value Type Semantics |
| **Файл** | `Source/Data/StationState.cs` |
| **Строки** | 3-15 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

```csharp
// StationState.cs:3-15
public struct StationState
{
    public bool InBeam;
    public int TransitionIndex;
    public float Gain;
    public float BeatFrequency;
    public int DotCount;
    public int CurrentBearing;
    public int CallsignLength;
    public float NavigationStartTime;
    public ConsolCycleConfig Config;     // struct
    public NavigationPattern Pattern;    // struct с reference-type Groups[]
}
```

Все поля публичные и mutable. В C# struct копируется by value:

```csharp
StationState copy = stationStates[i];
copy.Gain = 0.5f;                    // мутирует копию
stationStates[i] = copy;             // нужно обратное присваивание
// Но если забыть обратное присваивание — изменения потеряны
```

Это та же проблема, что F17 (StationState mutable struct), но F17 описывает алиасинг через `Pattern` (reference-type Groups[] внутри struct). F163 описывает общий hazard mutable struct.

## Корневая причина

Структуры с mutable state — стандартный source of bugs в C#.

## Предлагаемое исправление

Либо сделать immutable:

```csharp
public readonly struct StationState
{
    public readonly bool InBeam;
    public readonly float Gain;
    // ...
}
```

Либо сделать class.

## Зависимости

- Связан с F17 (StationState mutable struct aliasing) — тот же тип, другой аспект
- Связан с F130 (BandConfig mutable in static array) — та же проблема mutable structs
