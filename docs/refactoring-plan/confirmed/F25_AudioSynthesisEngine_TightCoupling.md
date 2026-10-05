# F25: AudioSynthesisEngine — Positional Array Coupling

**Приоритет:** P1 (High)
**Область:** Sound · AudioSynthesisEngine
**Тип:** Architecture · API Design

---

## Проблема

`ProcessAudio()` и `UpdateStationBeats()` принимают 4+ raw-массива с позиционной семантикой — stations[i], schedulers[i], keyers[i], carriers[i] должны быть синхронизированы по длине и индексу. Проверка отсутствует.

---

## Evidence

AudioSynthesisEngine.cs:65-72:

```csharp
public void ProcessAudio(float[] data, int channels,
    StationState[] stations,           // array #1
    StationScheduler[] schedulers,     // array #2 — match by index to #1
    MorseKeyer[] keyers,               // array #3 — match by index to #1
    CarrierGenerator[] carriers,       // array #4 — match by index to #1
    bool radioOn, int currentZoneIndex, int sonneZoneIndex)
```

Никакой проверки: `stations.Length == schedulers.Length == keyers.Length == carriers.Length`.

SonneController.cs:48-53 (caller) — ручная синхронизация:

```csharp
stations = new StationState[beaconStations.Length];
schedulers = new StationScheduler[beaconStations.Length];
keyers = new MorseKeyer[beaconStations.Length];
carriers = new CarrierGenerator[beaconStations.Length];
```

### Риски

| Риск | Вероятность | Влияние |
|------|------------|---------|
| Array length mismatch | Средняя | IndexOutOfRange или неверный audio output |
| Добавление/удаление станций | Средняя | Надо менять 4 массива одновременно |
| Новый разработчик | Высокая | Не знает про implicit invariant |

---

## Предлагаемое решение

Ввести `StationAudio` record/tuple как единый элемент:

```csharp
public record StationAudio(
    StationState State,
    StationScheduler Scheduler,
    MorseKeyer Keyer,
    CarrierGenerator Carrier
);

public void ProcessAudio(float[] data, int channels,
    IReadOnlyList<StationAudio> stations,
    bool radioOn, int currentZoneIndex, int sonneZoneIndex)
```

---

## Acceptance Criteria

- [ ] ProcessAudio принимает одну коллекцию, а не 4 массива
- [ ] Невозможно передать массивы разной длины
