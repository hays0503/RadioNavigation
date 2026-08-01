# F134. PositionFixCalculator.Calculate — мутация входного списка measurements

| Поле | Значение |
|------|----------|
| **Приоритет** | P3 (Low) |
| **Область** | Logic · PositionFixCalculator · API Design |
| **Файл** | `Source/Logic/PositionFixCalculator.cs` |
| **Строки** | 59-65 |

---

## Описание

Метод `Calculate()` принимает `List<Measurement>` и перезаписывает поля `Weight` каждого элемента:

```csharp
// PositionFixCalculator.cs:59-65
for (int i = 0; i < measurements.Count; i++)
{
    var m = measurements[i];
    float dist = GetDistanceKm(m.Latitude, m.Latitude, roughLat, roughLon);
    m.Weight = dist > 0.1f ? 1.0f / (dist / 100.0f) : 10f;
    measurements[i] = m;
}
```

Метод «Calculate» по контракту не должен мутировать входные данные. Сегодня это безвредно — `SonneController.UpdateStationNavigation` создаёт новый список каждый вызов (строка 399: `measurements.Clear()` + повторное заполнение). Однако:

- При рефакторинге (F120 — GC pressure) список может стать переиспользуемым
- Любой будущий вызов, передающий свой список, получит мутацию Weight
- Нарушает принцип наименьших удивлений: Calculate = чистая функция

## Корневая причина

Метод модифицирует существующий список вместо работы с копией.

## Предлагаемое исправление

Работать с локальной копией:

```csharp
public static FixResult Calculate(List<Measurement> measurements)
{
    // ...粗略ный fix...
    var localMeasurements = new List<Measurement>(measurements);
    for (int i = 0; i < localMeasurements.Count; i++)
    {
        var m = localMeasurements[i];
        float dist = GetDistanceKm(m.Latitude, m.Longitude, roughLat, roughLon);
        m.Weight = dist > 0.1f ? 1.0f / (dist / 100.0f) : 10f;
        localMeasurements[i] = m;
    }
    // ... работать с localMeasurements ...
}
```

## Зависимости

- Связан с F64 (PositionFixCalculator struct copy mutation) — другой аспект хрупкости struct-операций в том же методе
- Связан с F73 (duplicated Haversine) — тот же файл
- Связан с F120 (SonneController GC pressure) — рефакторинг может сделать список переиспользуемым
