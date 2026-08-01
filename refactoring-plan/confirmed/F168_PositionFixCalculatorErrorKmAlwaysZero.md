# F168. PositionFixCalculator.Calculate — всегда возвращает ErrorKm = 0f

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Logic · PositionFixCalculator · Algorithm |
| **Файл** | `Source/Logic/PositionFixCalculator.cs` |
| **Строки** | 95 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

```csharp
// PositionFixCalculator.cs:95
return new FixResult
{
    Latitude = calcLat,
    Longitude = calcLon,
    ErrorKm = 0f,  // <-- всегда 0
    Valid = true
};
```

`Calculate` вычисляет взвешенное среднее пересечений, но всегда возвращает `ErrorKm = 0f`, независимо от реальной точности. Это значит, что `InformationPanelHandler.UpdateFixDisplay()` (строка 237) всегда показывает "Error: 0.0 km" и окрашивает текст в зелёный:

```csharp
fixText.color = err < 15f ? Color.green : (err < 35f ? Color.yellow : Color.red);
```

Пользователь видит "0.0 km" даже когда реальная ошибка может быть значительной (например, при малом количестве измерений или при плохой геометрии пересечения).

## Корневая причина

Вычисление реальной ошибки требует дополнительного анализа (например, разброса пересечений или condition number), который не был реализован.

## Предлагаемое исправление

Вычислить приблизительную ошибку на основе разброса пересечений:

```csharp
// После вычисления roughLat/roughLon:
float maxDist = 0f;
for (int i = 0; i < rawIntersections.Count; i++)
{
    float d = GetDistanceKm(
        rawIntersections[i].Latitude, rawIntersections[i].Longitude,
        roughLat, roughLon);
    if (d > maxDist) maxDist = d;
}
float errorKm = maxDist;
```

## Зависимости

- Связан с F16 (PositionFix error km zero) — F16 описывает тот же симптом, но предлагает другой fix
- Связан с F134 (Calculate mutates input measurements)
- Связан с F80 (PositionFix haversine NaN)
