# F131. UpdateStationNavigation — станции без частоты в position fix

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Logic · SonneController · Position Fix |
| **Файл** | `Source/Logic/SonneController.cs` |
| **Строки** | 396-440 |

---

## Описание

`UpdateStationNavigation()` итерирует **все** `BeaconData.Stations` и безусловно добавляет `Measurement` для каждой (строки 426-433):

```csharp
// SonneController.cs:426-433
measurements.Add(new PositionFixCalculator.Measurement
{
    StationIndex = i,
    Latitude = (float)beaconStations[i].Latitude,
    Longitude = (float)beaconStations[i].Longitude,
    Bearing = bearing,
    Weight = 1f
});
```

Хотя `UpdateStationGains` корректно устанавливает `Gain = 0` для станций с `FrequencyKHz = null` (строка 354-358), измерение всё равно добавляется в список `measurements`. В результате:

- `PositionFixCalculator.Calculate` получает bearing от станции, которую игрок никогда не мог принять (Plougourvest, index 11 — `FrequencyKHz = null`)
- `Measurements.Count` в UI отражает количество **всех** станций, включая недоступные
- Точность фикса искусственно завышена за счёт фантомных данных

## Корневая причина

`UpdateStationNavigation` не проверяет наличие частоты перед добавлением измерения, в отличие от `UpdateStationGains`.

## Предлагаемое исправление

Добавить проверку перед добавлением измерения:

```csharp
for (int i = 0; i < beaconStations.Length; i++)
{
    float? freq = beaconStations[i].History?[0].FrequencyKHz;
    if (freq.IsNull()) continue; // пропустить станции без частоты

    float bearing = CalculateBearing(
        playerLatitude, playerLongitude,
        (float)beaconStations[i].Latitude, (float)beaconStations[i].Longitude);
    // ... остальной код ...
}
```

## Зависимости

- Связан с F38 (Plougourvest null frequency) — та же станция, другой аспект проблемы
- Связан с F79 (InBeam hardcoded true) — оба в UpdateStationNavigation
- Связан с F99 (stale position fix) — оба влияют на корректность position fix
