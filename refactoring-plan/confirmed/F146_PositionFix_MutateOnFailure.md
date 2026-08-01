# F146. PositionFixCalculator.Calculate — measurements мутируются на early-return failure paths

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Logic · PositionFixCalculator · API Design |
| **Файл** | `Source/Logic/PositionFixCalculator.cs` |
| **Строки** | 59-65 vs 91-92 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

`Calculate` мутирует `.Weight` на строках 59-65:

```csharp
// PositionFixCalculator.cs:59-65
for (int i = 0; i < measurements.Count; i++)
{
    var m = measurements[i];
    float dist = GetDistanceKm(m.Latitude, m.Longitude, roughLat, roughLon);
    m.Weight = dist > 0.1f ? 1.0f / (dist / 100.0f) : 10f;
    measurements[i] = m;
}
```

Если `weightedIntersections` оказывается пустым (строка 91-92), метод возвращает `Valid = false`, но measurements уже содержит **навсегда изменённые** веса (с 1.0f на значения из формулы расстояния).

Хотя `UpdateStationNavigation` пересоздаёт measurements каждый цикл, в пределах одного цикла любой код, читающий measurements после неудачного `Calculate`, получит повреждённые данные.

Это конкретное следствие F134 (Calculate mutates input measurements), но F134 описывает проблему в общем виде, а F146 — конкретный failure path.

## Корневая причина

Мутация входных данных без возможности отката при неудачном вычислении.

## Предлагаемое исправление

Работать с копией measurements или восстанавливать веса при неудаче:

```csharp
// Вариант 1: копия
var workMeasurements = new List<Measurement>(measurements);
// ... мутации workMeasurements ...

// Вариант 2: восстановление при failure
if (weightedIntersections.Count.IsZero())
{
    // восстановить оригинальные веса
    for (int i = 0; i < measurements.Count; i++)
    {
        var m = measurements[i];
        m.Weight = 1f;
        measurements[i] = m;
    }
    return new FixResult { Valid = false };
}
```

## Зависимости

- Связан с F134 (Calculate mutates input) — общее описание проблемы
- Связан с F64 (struct copy mutation) — тот же файл, тот же паттерн мутации
- Связан с F99 (stale position fix) — неудачный Calculate оставляет невалидный fix
