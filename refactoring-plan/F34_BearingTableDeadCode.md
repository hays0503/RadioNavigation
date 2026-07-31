# F34: BearingTable / LookupBearing — мёртвый код

**Приоритет:** P1 (High)
**Область:** Logic · SonneSignalGenerator · SonneController
**Тип:** Dead Code · Unused

---

## Проблема

`SonneSignalGenerator.GenerateBearingTable()` вычисляет таблицу пеленгов, сохраняется в `StationScheduler.BearingTable`, но **никогда не читается**. `SonneSignalGenerator.LookupBearing()` тоже нигде не вызывается. Весь конвейер dot-count → bearing — dead code.

PositionFixCalculator вместо этого получает истинный пеленг от игрока к станции через `CalculateBearing(playerPos, stationPos)`.

---

## Evidence

SonneController.cs:61-68 — BearingTable сохраняется:
```csharp
var bearingTable = SonneSignalGenerator.GenerateBearingTable(beaconStations[i]);
var scheduler = new StationScheduler(callsign)
{
    Config = config,
    BearingTable = bearingTable    // ← saved, never read
};
```

SonneController.cs:426-433 — фикс использует истинный пеленг, не таблицу:
```csharp
measurements.Add(new PositionFixCalculator.Measurement
{
    StationIndex = i,
    Latitude = (float)beaconStations[i].Latitude,
    Longitude = (float)beaconStations[i].Longitude,
    Bearing = bearing,   // ← from CalculateBearing(), not from BearingTable
    Weight = 1f
});
```

SonneSignalGenerator.cs:72-78 — LookupBearing определён, вызывается 0 раз.

---

## Impact

- ~40 строк мёртвого кода
- Вводит в заблуждение: создаёт иллюзию Consol-навигации через dot-count, но фикс использует истинные координаты игрока

## Fix

Удалить `GenerateBearingTable`, `LookupBearing`, поле `BearingTable` из `StationScheduler`.
