# F35: Два разных алгоритма GenerateBearingTable

**Приоритет:** P2 (Medium)
**Область:** Logic · SonneSignalGenerator · Data · BeaconData
**Тип:** Duplication · Confusion

---

## Проблема

В коде существуют **две разные реализации** `GenerateBearingTable`, дающие **принципиально разные результаты**:

1. `SonneSignalGenerator.GenerateBearingTable` (SonneSignalGenerator.cs:55) — покрывает **5° сектор**
2. `BeaconStation.GetDotCountToBearingTable` (BeaconData.cs:28) — покрывает **180° луч**

Вызывается только версия из SonneSignalGenerator (и всё равно не читается — см. F34). Вторая — мёртвый код.

---

## Evidence

SonneSignalGenerator.cs:55-70:
```csharp
public static int[] GenerateBearingTable(BeaconStation station)
{
    int maxCount = config.MaxNavigationCount;
    for (int i = 0; i <= maxCount; i++)
    {
        float localAngle = ratio * SectorWidthDeg;   // 0..5°
        float bearing = station.BaseAngle + localAngle;  // 5° range
        table[i] = (int)((bearing + 360f) % 360f);
    }
}
```

BeaconData.cs:28-42:
```csharp
public int[] GetDotCountToBearingTable()
{
    int maxCount = config.MaxNavigationCount;
    for (int i = 0; i <= maxCount; i++)
    {
        float bearing = BeamHeadingDegTrue - BeamHalfWidthDeg + ratio * 2f * BeamHalfWidthDeg;  // 180° range
        table[i] = (int)((bearing + 360f) % 360f);
    }
}
```

---

## Impact

- Два алгоритма несовместимы
- Если когда-либо будет вызван `BeaconStation.GetDotCountToBearingTable()`, пеленги будут отличаться на десятки градусов

## Fix

Определить единый алгоритм. Удалить мёртвый.
