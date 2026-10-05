# F122. StationScheduler.Reset() устанавливает неправильную начальную фазу

| Поле | Значение |
|------|----------|
| **Приоритет** | P1 (High) |
| **Область** | Logic · StationScheduler · State Machine |
| **Файл** | `Source/Logic/StationScheduler.cs` |
| **Строки** | 100-104 |

---

## Описание

`Reset()` устанавливает `CurrentPhase = Phase.PostNavSilence` вместо `Phase.Pause`:

```csharp
// StationScheduler.cs:100-104
public void Reset()
{
    CurrentPhase = Phase.PostNavSilence;
    pauseTicksRemaining = ToTicks(Config.PostNavSilenceMs);
}
```

Конструктор инициализирует фазу как `Pause`:

```csharp
// StationScheduler.cs:28-29
CurrentPhase = Phase.Pause;
pauseTicksRemaining = 1;
```

После `Reset()` + `Tick()` цикл фаз идёт:
`PostNavSilence → (tick) → Pause → Callsign → ...`

Но ожидаемое поведение после сброса — начать с Pause:
`Pause → Callsign → ...`

Это означает, что после сброса станция **пропускает** PostNavSilence фазу с реальным молчанием и сразу переходит к вызову. Длительность PostNavSilenceMs (1000ms) добавляется к паузе вместо того, чтобы быть отдельной фазой.

## Корневая причина

`Reset()` устанавливает фазу в `PostNavSilence` (конец цикла) вместо `Pause` (начало цикла). Вероятно, это ошибка копирования из `OnSequenceFinished()`.

## Предлагаемое исправление

```csharp
public void Reset()
{
    CurrentPhase = Phase.Pause;
    pauseTicksRemaining = 1;
}
```

## Зависимости

- Связан с F98 (Scheduler ticked 2-3x per audio cycle) —共享 phase state machine
- Независим от других F-проблем для исправления
