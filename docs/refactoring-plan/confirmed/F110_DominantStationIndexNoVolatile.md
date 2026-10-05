# F110. SonneController — dominantStationIndex cross-thread, no volatile

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Sound · Threading · Memory Visibility |
| **Файл** | `Source/Logic/SonneController.cs` |
| **Строки** | 16, 388, 453-456 |

---

## Описание

`dominantStationIndex` записывается `UpdateStationGains` (main thread, строка 388) и читается `IsProcessingDominantStation` (audio thread, строка 453-456):

```csharp
// Строка 16 (объявление):
private int dominantStationIndex = -1;

// Строка 388 (main thread, запись):
dominantStationIndex = maxIndex;

// Строка 453-456 (audio thread, чтение):
private bool IsProcessingDominantStation(int stationIndex) =>
    IsInSonneZone
    && !stations[stationIndex].Gain.IsNegligible(0.001f)
    && stationIndex == dominantStationIndex;
```

Без `volatile` или memory barrier C# memory model не гарантирует, что audio thread увидит последнее значение. На x86/x64 это маловероятно из-за строгой модели памяти, но на ARM (если игра когда-нибудь запустится на ARM) может привести к обработке неправильной станции.

## Корневая причина

Отсутствие `volatile` на поле, читаемом из другого потока.

## Предлагаемое исправление

```csharp
private volatile int dominantStationIndex = -1;
```

## Зависимости

- Часть F01/F29 (audio race condition / no synchronization)
