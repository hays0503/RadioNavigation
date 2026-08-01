# F74: ComparisonExtensions — несимметричные пороги IsSignificant / IsNegligible

**Приоритет:** P2 (Medium)  
**Область:** Logic · ComparisonExtensions  
**Файл:** `Source/Logic/ComparisonExtensions.cs:31-32`

---

## Проблема

Пороги для `IsSignificant` и `IsNegligible` различаются на **6 порядков**:

```csharp
public static bool IsSignificant(this float value)  => Math.Abs(value) > 0.001f;     // 1e-3
public static bool IsNegligible(this float value)   => Math.Abs(value) < 1e-9f;      // 1e-9
```

Значения в диапазоне `[1e-9, 1e-3)` не являются ни significant, ни negligible:

```csharp
float x = 0.000001f;       // 1e-6
x.IsSignificant();          // false (1e-6 < 1e-3)
x.IsNegligible();           // false (1e-6 > 1e-9)
// x — ни то, ни другое!
```

Логически эти методы должны быть взаимодополняющими:
- `IsSignificant(x) == !IsNegligible(x)` — ожидаемо
- `IsNegligible(x) == !IsSignificant(x)` — ожидаемо

С текущими порогами:
- `IsSignificant(x) || IsNegligible(x)` может быть `false`

## Исправление

Сделать пороги симметричными:

```csharp
private const float SignificanceThreshold = 1e-6f;

public static bool IsSignificant(this float value) => Math.Abs(value) > SignificanceThreshold;
public static bool IsNegligible(this float value)  => Math.Abs(value) < SignificanceThreshold;
```

Или выбрать единый порог, соответствующий точности float (≈ 1e-6).

## Риски

- Изменение порогов может повлиять на поведение вызывающего кода
- Текущие caller-ы: PositionFixCalculator (проверки на полярные широты), StationScheduler
- Нужно проверить, не было ли пороговое значение выбрано намеренно
