# F38: Plougourvest имеет null Frequency

**Приоритет:** P2 (Medium)
**Область:** Data · BeaconData
**Тип:** Data Quality · Dead Data

---

## Проблема

Станция Plougourvest (BeaconData.cs:302-324) имеет `FrequencyKHz = null` в истории. При проверке в `UpdateStationGains` (SonneController.cs:354-358) частота считается отсутствующей — `gain` устанавливается в 0, станция никогда не принимается.

---

## Evidence

BeaconData.cs:316-323:
```csharp
new BeaconHistoryEntry
{
    From = new DateTime(1944, 1, 1),
    Callsign = null,
    FrequencyKHz = null   // ← нет частоты
}
```

SonneController.cs:354-358:
```csharp
float? freq = beaconStations[i].History?[0].FrequencyKHz;
if (freq.IsNull())
{
    stations[i].Gain = 0f;   // ← станция всегда с нулевым усилением
    continue;
}
```

---

## Impact

- Plougourvest никогда не появляется в эфире — тупиковые данные
- 25 строк структуры станции загружаются в память без пользы
