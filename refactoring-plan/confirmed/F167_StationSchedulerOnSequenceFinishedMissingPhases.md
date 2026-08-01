# F167. StationScheduler.OnSequenceFinished — не обрабатывает фазы Pause/PreNavPause/PostNavSilence

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Logic · StationScheduler · State Machine |
| **Файл** | `Source/Logic/StationScheduler.cs` |
| **Строки** | 92-103 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

```csharp
// StationScheduler.cs:92-103
public void OnSequenceFinished()
{
    switch (CurrentPhase)
    {
        case Phase.Callsign:
            CurrentPhase = Phase.PreNavPause;
            pauseTicksRemaining = ToTicks(Config.PreNavPauseMs);
            break;

        case Phase.Navigation:
            CurrentPhase = Phase.PostNavSilence;
            pauseTicksRemaining = ToTicks(Config.PostNavSilenceMs);
            break;
    }
    // Pause, PreNavPause, PostNavSilence — не обрабатываются
}
```

`OnSequenceFinished` обрабатывает только `Callsign` и `Navigation`. Если вызов происходит во время `Pause`, `PreNavPause` или `PostNavSilence`, метод ничего не делает — фаза не меняется.

Это может произойти если:
1. `MorseKeyer.AdvanceElement` вызывает `scheduler.OnSequenceFinished()` когда keyer ещё не переключился в правильную фазу
2. Гонка между audio thread и main thread при вызове `Tick()`/`OnSequenceFinished()`

## Корневая причина

Неполный state machine — switch без default case.

## Предлагаемое исправление

Добавить обработку или default case:

```csharp
public void OnSequenceFinished()
{
    switch (CurrentPhase)
    {
        case Phase.Callsign:
            CurrentPhase = Phase.PreNavPause;
            pauseTicksRemaining = ToTicks(Config.PreNavPauseMs);
            break;
        case Phase.Navigation:
            CurrentPhase = Phase.PostNavSilence;
            pauseTicksRemaining = ToTicks(Config.PostNavSilenceMs);
            break;
        default:
            Debug.LogWarning("[StationScheduler] OnSequenceFinished called in unexpected phase: " + CurrentPhase);
            break;
    }
}
```

## Зависимости

- Связан с F122 (StationScheduler.Reset wrong phase)
- Связан с F135 (OnSequenceFinished no default case) — F135 описывает ту же проблему в другом контексте
