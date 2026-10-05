# F39: 3 станции на одной частоте 332.5 kHz

**Приоритет:** P2 (Medium)
**Область:** Data · BeaconData
**Тип:** Data Quality · Frequency Conflict

---

## Проблема

Три норвежские станции имеют одинаковую частоту 332.5 kHz:
- Andoya (BeaconData.cs:183)
- Bjornoya (BeaconData.cs:206)
- Jan Mayen (BeaconData.cs:229)

При настройке на 332.5 kHz все три принимаются одновременно — их сигналы смешиваются.

---

## Evidence

BeaconData.cs:182-183 (Andoya):
```csharp
FrequencyKHz = 332.5f
```

BeaconData.cs:205-206 (Bjornoya):
```csharp
FrequencyKHz = 332.5f
```

BeaconData.cs:228-229 (Jan Mayen):
```csharp
FrequencyKHz = 332.5f
```

---

## Impact

- Игрок слышит какофонию из трёх станций одновременно
- На практике Consol-станции работали в time-division на одной частоте, но мод не разделяет их по времени — все три активны одновременно
- Невозможно определить, какая станция даёт фикс
