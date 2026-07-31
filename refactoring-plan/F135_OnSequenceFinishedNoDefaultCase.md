# F135. StationScheduler.OnSequenceFinished — нет default case для неожиданных фаз

| Поле | Значение |
|------|----------|
| **Приоритет** | P3 (Low) |
| **Область** | Logic · StationScheduler · Defensive Coding |
| **Файл** | `Source/Logic/StationScheduler.cs` |
| **Строки** | 84-98 |

---

## Описание

`OnSequenceFinished()` обрабатывает только `Phase.Callsign` и `Phase.Navigation`:

```csharp
// StationScheduler.cs:84-98
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
        // <-- нет default case
    }
}
```

Если вызван во время `Phase.Pause`, `Phase.PreNavPause` или `Phase.PostNavSilence`, метод **молча ничего не делает**. Это может произойти при timing edge case:
- Audio thread продвинул scheduler через `Tick()` (строка 83 в MorseKeyer.Advance)
- Scheduler перешёл в `PreNavPause`
- `OnSequenceFinished` вызван до завершения паузы

В результате scheduler оказывается в неожиданной фазе без перехода и без логирования.

## Корневая причина

Нет defensive handling для неожиданных вызовов. Неявный контракт: `OnSequenceFinished` вызывается только в `Callsign` и `Navigation`.

## Предлагаемое исправление

Добавить default case с предупреждением:

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
            Debug.LogWarning($"[StationScheduler] OnSequenceFinished called during {CurrentPhase}, ignoring");
            break;
    }
}
```

## Зависимости

- Связан с F122 (StationScheduler.Reset wrong phase) — тот же класс, другой метод
- Связан с F98 (scheduler double tick) — гонка может вызвать неожиданный вызов
