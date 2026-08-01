# F145. Первый callsign cycle без callsignLength → неверная окраска/декодирование

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Logic · SonneController · AudioSynthesisEngine · Timing |
| **Файл** | `Source/Logic/SonneController.cs`, `Source/Sound/AudioSynthesisEngine.cs` |
| **Строки** | Awake flow + ProcessAudio |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

При первом запуске `SonneController.Awake()` создаёт schedulers с `CurrentConsolPattern = null`:

1. Scheduler стартует в `Phase.Pause` с `pauseTicksRemaining = 1`
2. На первом audio tick scheduler проходит Pause → Callsign
3. `sched.GetCurrentConsolPattern()` возвращает `null` (Callsign phase возвращает `CurrentConsolPattern`, который ещё не установлен)
4. `keyers[s].Load(seq, sched, config)` вызывается **без** `callsignLength`
5. `MorseKeyer.callsignLength` остаётся 0 — `IsCallsignElement` всегда `false`
6. `OnElementCompleted` fires с `isCallsign = false` для всех callsign элементов

**Результат:** During первый callsign cycle:
- UI отображает callsign dots/dashes в обычных цветах вместо callsign-colored
- Morse decoder buffer не захватывает callsign для декодирования

`UpdateStationNavigation` (который устанавливает `CurrentConsolPattern`) не запускается до 0.2 секунды. К тому моменту первый callsign уже воспроизводится.

## Корневая причина

`CurrentConsolPattern` устанавливается позже первого audio tick. Scheduler не ждёт его готовности.

## Предлагаемое исправление

Pre-compute и назначить `CurrentConsolPattern` в `Awake()` для каждой станции до первого scheduler tick:

```csharp
// В Awake(), после создания schedulers:
for (int i = 0; i < stations.Length; i++)
{
    schedulers[i].CurrentConsolPattern = stations[i].Pattern;
}
```

## Зависимости

- Связан с F113 (MorseKeyer callsign length reset) — тот же механизм callsignLength
- Связан с F77 (StationState pattern never updated) — CurrentConsolPattern не обновляется
- Связан с F65 (MorseKeyer stale consolPattern) — та же проблема с consolPattern
