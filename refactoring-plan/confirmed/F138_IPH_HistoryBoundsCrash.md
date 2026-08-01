# F138. InformationPanelHandler — History[0] без проверки длины → IndexOutOfRangeException

| Поле | Значение |
|------|----------|
| **Приоритет** | P1 (High) |
| **Область** | UI · InformationPanelHandler · Bounds Check |
| **Файл** | `Source/UI/InformationPanelHandler.cs` |
| **Строки** | 446, 455 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

В `FindClosestStation` обращение к `History[0]` через null-conditional `?.` проверяет только `null`, но не длину массива:

```csharp
// InformationPanelHandler.cs:446
float? stationFreq = stations[i].History?[0].FrequencyKHz;

// InformationPanelHandler.cs:455
string cs = stations[i].History?[0].Callsign;
```

Если `History` установлен в пустой массив (`new BeaconHistoryEntry[0]`), оператор `?.` пропускает null-проверку (массив не null), но `[0]` выбрасывает `IndexOutOfRangeException`.

**Сравнение:** `SonneController.cs:470-71` корректно проверяет длину:

```csharp
if (station.History.IsNotNull() && station.History.Length.IsPositive())
{
    var entry = station.History[0];
}
```

## Корневая причина

Null-conditional `?.` не заменяет проверку границ массива. Код предполагает, что если `History` не null, то он всегда содержит хотя бы один элемент.

## Предлагаемое исправление

```csharp
float? stationFreq = stations[i].History?.Length > 0
    ? stations[i].History[0].FrequencyKHz
    : null;
// ...
string cs = stations[i].History?.Length > 0
    ? stations[i].History[0].Callsign
    : null;
```

## Зависимости

- Связан с F67 (InformationPanelHandler Toggle NRE) — тот же файл, другой null-reference risk
- Связан с F07 (Null Reference Risks) — общая проблема null-safe доступа
- Связан с F37 (6 станций с null callsign) — некоторые станции могут иметь пустую History
