# F80: PositionFixCalculator.GetDistanceKm — NaN из-за Math.Sqrt(1.0 - a)

**Приоритет:** P1 (High)
**Область:** Logic · PositionFixCalculator
**Файл:** `PositionFixCalculator.cs:214`
**Дата обнаружения:** 2026-07-31

---

## Проблема

```csharp
double a = Math.Sin(deltaPhi / 2.0) * Math.Sin(deltaPhi / 2.0) +
           Math.Cos(phi1) * Math.Cos(phi2) *
           Math.Sin(deltaLam / 2.0) * Math.Sin(deltaLam / 2.0);
double c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
```

Из-за floating-point precision, `a` может быть **незначительно > 1.0** (например, 1.0000000000000002) → `1.0 - a` отрицательный → `Math.Sqrt` = NaN → расстояние = NaN.

---

## Влияние

Редкий edge case (когда две точки очень близко или идентичны), но может давать NaN расстояния → NaN в position fix.

---

## Исправление

```csharp
a = Math.Min(a, 1.0);
```

Перед `Math.Sqrt(1.0 - a)`.

---

## Acceptance Criteria

- [ ] GetDistanceKm никогда не возвращает NaN
- [ ] Для идентичных точек расстояние ≈ 0
