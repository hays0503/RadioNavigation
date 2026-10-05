# F100. FindClosestStation — stale callsign from previously-closer station displayed

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | UI · InformationPanelHandler · Logic Bug |
| **Файл** | `Source/UI/InformationPanelHandler.cs` |
| **Строки** | 433-466 |

---

## Описание

В методе `FindClosestStation` переменная `bestCallsign` не сбрасывается при нахождении новой ближайшей станции:

```csharp
// Строка 440-458:
string bestCallsign = null;

for (int i = 0; i < stations.Length; i++)
{
    float? stationFreq = stations[i].History?[0].FrequencyKHz;
    if (stationFreq.IsNull()) continue;

    float delta = Math.Abs(freqKHz - stationFreq.Value);
    if (delta < bestDelta)
    {
        bestDelta = delta;
        bestFreq = stationFreq.Value;
        bestName = stations[i].Name;
        string cs = stations[i].History?[0].Callsign;
        if (!string.IsNullOrEmpty(cs))
            bestCallsign = cs;  // <-- никогда не сбрасывается в null при новой ближайшей
    }
}
```

**Сценарий:** Станция A (callsign "MWN") ближайшая с delta=5. Затем станция B (с null callsign) становится ближайшей с delta=3. `bestCallsign` по-прежнему содержит "MWN" от станции A. Дисплей покажет сигнал станции B, но callsign станции A.

## Корневая причина

`bestCallsign` обновляется только если `cs` не null/пустой. При смене ближайшей станции старое значение не очищается.

## Предлагаемое исправление

```csharp
if (delta < bestDelta)
{
    bestDelta = delta;
    bestFreq = stationFreq.Value;
    bestName = stations[i].Name;
    string cs = stations[i].History?[0].Callsign;
    bestCallsign = string.IsNullOrEmpty(cs) ? null : cs;  // <-- всегда сбрасывать
}
```

## Зависимости

- Независим от других F-проблем
