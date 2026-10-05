# F16: PositionFixCalculator.ErrorKm всегда 0

**Приоритет:** P1 (High)
**Область:** Logic · PositionFixCalculator
**Тип:** Logic bug

---

## Проблема

`FixResult.ErrorKm` всегда равен 0 даже при `Valid = true`. Пользователь видит "Error: 0 km" — дезинформация, нет доверительного интервала для позиции.

---

## Evidence

PositionFixCalculator.cs:112-118:

```csharp
return new FixResult
{
    Latitude = calcLat,
    Longitude = calcLon,
    ErrorKm = 0f,       // ← всегда 0
    Valid = true
};
```

Метод вычисляет взвешенное среднее из всех пересечений (weighted average), но никогда не вычисляет радиус разброса (стандартное отклонение или среднее расстояние от центра).

---

## Предлагаемое решение

Вычислить ErrorKm как среднеквадратичное отклонение weighted фиксов от центра:

```csharp
float sumSq = 0f;
for (int i = 0; i < weightedIntersections.Count; i++)
{
    var wp = weightedIntersections[i];
    float d = GetDistanceKm(calcLat, calcLon, wp.Latitude, wp.Longitude);
    sumSq += wp.Weight * d * d;
}
float errorKm = (float)Math.Sqrt(sumSq / totalW);
return new FixResult { Latitude = calcLat, Longitude = calcLon, ErrorKm = errorKm, Valid = true };
```

---

## Acceptance Criteria

- [ ] ErrorKm > 0 для non-trivial набора измерений
- [ ] ErrorKm растёт при расходящихся измерениях
- [ ] ErrorKm ≈ 0 при консенсусных измерениях
