# F123. StationState.NavigationStartTime не инициализируется в Awake

| Поле | Значение |
|------|----------|
| **Приоритет** | P1 (High) |
| **Область** | Logic · SonneController · Station State |
| **Файл** | `Source/Logic/SonneController.cs` |
| **Строки** | 76-87 (Awake), ~458 (IsStationNavigating) |

---

## Описание

В `Awake()` структура `StationState` инициализируется без `NavigationStartTime`:

```csharp
// SonneController.cs:76-87
stations[i] = new StationState
{
    InBeam = false,
    TransitionIndex = config.MaxNavigationCount,
    Gain = 0f,
    BeatFrequency = BfoBase,
    Config = config,
    DotCount = 0,
    CurrentBearing = 0,
    Pattern = new NavigationPattern(Array.Empty<Group>(), 8),
    CallsignLength = callsignLen
    // <-- NavigationStartTime не задан, остаётся 0f (default для float)
};
```

`NavigationStartTime` задаётся **только** в `AudioSynthesisEngine.ProcessAudio` (audio thread):

```csharp
// AudioSynthesisEngine.cs:~96
stations[s].NavigationStartTime = Time.time;
```

Проверка `IsStationNavigating` требует `NavigationStartTime.IsPositive()`:

```csharp
// SonneController.cs:~458-460
bool IsStationNavigating(int stationIndex) =>
    stations[stationIndex].NavigationStartTime.IsPositive() &&
    stations[stationIndex].InBeam;
```

**Проблема:** если станция ещё не прошла через audio path (первый цикл), `NavigationStartTime = 0f`, `IsPositive()` возвращает `false` → станция **никогда** не报告 как "navigating" до первого audio callback'а.

## Корневая причина

`NavigationStartTime` устанавливается в audio thread, но проверяется в main thread до того, как audio thread успел записать значение.

## Предлагаемое исправление

Установить `NavigationStartTime` в `Awake()` или при начале навигации в main thread:

```csharp
stations[i] = new StationState
{
    InBeam = false,
    TransitionIndex = config.MaxNavigationCount,
    Gain = 0f,
    BeatFrequency = BfoBase,
    Config = config,
    DotCount = 0,
    CurrentBearing = 0,
    Pattern = new NavigationPattern(Array.Empty<Group>(), 8),
    CallsignLength = callsignLen,
    NavigationStartTime = 0f  // явно — будет обновлено при старте навигации
};
```

Или, лучше — установить `NavigationStartTime` в `StationScheduler.OnSequenceFinished()` при входе в фазу `Navigation`, а не в audio thread:

```csharp
// В SonneController, при обработке перехода в фазу Navigation:
stations[s].NavigationStartTime = Time.time;
```

## Зависимости

- Связан с F77 (StationState.Pattern never updated) — аналогичная проблема инициализации
- Связан с F01 (Audio Race Condition) — audio thread пишет в main thread state
