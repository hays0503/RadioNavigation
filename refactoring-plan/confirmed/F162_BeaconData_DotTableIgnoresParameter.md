# F162. BeaconData.GetDotCountToBearingTable — игнорирует параметр CycleDurationSeconds

| Поле | Значение |
|------|----------|
| **Приоритет** | P3 (Low) |
| **Область** | Data · BeaconData · API Design |
| **Файл** | `Source/Data/BeaconData.cs` |
| **Строки** | 28-42 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

```csharp
// BeaconData.cs:28-42
public int[] GetDotCountToBearingTable()
{
    var config = ConsolCycleConfig.GetForCycleDuration(CycleDurationSeconds);
    //                                      ^^^^^^^^^^^^^^^^^^^^^^^^
    //                                      параметр передаётся, но GetForCycleDuration
    //                                      всегда возвращает Cycle120s (см. F55/F86)
    int maxCount = config.MaxNavigationCount;
    // ...
}
```

Метод принимает `CycleDurationSeconds` через поле станции, но `ConsolCycleConfig.GetForCycleDuration()` всегда возвращает `Cycle120s` (F55/F86). Если когда-нибудь будет добавлен второй cycle config, этот метод молча проигнорирует реальную длительность.

## Корневая причина

Stub implementation `GetForCycleDuration` делает параметр бесполезным.

## Предлагаемое исправление

Исправить `GetForCycleDuration` (F55/F86) или удалить метод, если различия в cycle duration не поддерживаются.

## Зависимости

- Связан с F55 (ConsolCycleConfig ignored parameter) — корневая причина
- Связан с F86 (ConsolCycleConfig stub) — тот же stub
- Связан с F35 (Duplicate bearing algorithms) — другой bearing algorithm
