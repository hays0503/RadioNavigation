# F54: AudioSynthesisEngine.UpdateStationBeats() — мёртвый код

**Приоритет:** P1 (High)  
**Область:** Sound · Dead Code  
**Файл:** `Source/Sound/AudioSynthesisEngine.cs:52-63`

---

## Проблема

Метод `UpdateStationBeats()` определён, но **никогда не вызывается** ни из одного файла проекта.

Логика, которую он должен выполнять (обновление `BeatFrequency` и `CarrierGenerator.Frequency`), дублирована в `SonneController.UpdateStationGains()` (строка 369-370).

```csharp
// AudioSynthesisEngine.cs:52-63 — DEAD CODE
public void UpdateStationBeats(StationState[] stations, CarrierGenerator[] carriers, 
    float currentFrequencyKHz, int currentZoneIndex, int sonneZoneIndex)
{
    for (int i = 0; i < stations.Length; i++)
    {
        if (stations[i].Gain.IsNegligible(0.001f)) continue;
        float offset = currentFrequencyKHz - (stations[i].BeatFrequency - BfoBase) / 1000f;
        stations[i].BeatFrequency = Mathf.Clamp(stations[i].BeatFrequency, BfoMin, BfoMax);
        carriers[i].Frequency = stations[i].BeatFrequency;
    }
}
```

F06 (Dead Code) перечисляет только `GenerateBearingTable/LookupBearing/BearingTable` в SonneSignalGenerator/BeaconStation, но пропускает этот метод.

## Исправление

Удалить метод `UpdateStationBeats()` (12 строк). 5 минут.

## Риски

- Проверить `git grep` — метод точно нигде не вызывается
- Убедиться, что вся необходимая логика покрыта `SonneController.UpdateStationGains()`
