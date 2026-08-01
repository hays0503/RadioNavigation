# F18: PositionFixCalculator — скрытый god object

**Приоритет:** P2 (Medium)
**Область:** Logic · PositionFixCalculator
**Тип:** Architecture

---

## Проблема

224 строки, 4 вложенных struct (2 публичных + 2 приватных), 6 методов, 2 области ответственности (расчёт пересечений + взвешивание). Статический класс без возможности тестирования или подмены логики.

---

## Evidence

PositionFixCalculator.cs:6-223:

| Элемент | Строки | Тип |
|---------|--------|-----|
| `Measurement` | 9-15 | public struct |
| `FixResult` | 17-23 | public struct |
| `Calculate()` | 25-119 | public static — weighted average + intersections |
| `FindIntersection()` | 135-201 | private static — геодезия |
| `GetDistanceKm()` | 203-217 | public static (но вызывается только внутри) |
| `ToRadians()` | 219-222 | private static — дубликат SonneController.ToRadians |

### Дубликат ToRadians

Уже в F12, но PositionFixCalculator.cs:219-222:

```csharp
private static double ToRadians(float degrees) { return degrees * Math.PI / 180.0; }
```

Идентичный метод в SonneController.cs:503-506.

### GetDistanceKm — public, но используется только внутри

```csharp
public static float GetDistanceKm(float lat1, float lon1, float lat2, float lon2)
```

Единственный вызов — строка 62, внутри Calculate(). Public static — публичный API, но нигде не используется.

---

## Предлагаемое решение

1. Сделать `FindIntersection` и `GetDistanceKm` internal или вынести в `GeoHelper`
2. Ввести интерфейс `IPositionCalculator` для тестирования
3. Вынести `ToRadians` в общий `MathHelper` (см. F12)

---

## Acceptance Criteria

- [ ] PositionFixCalculator реализует IPositionCalculator
- [ ] GeoHelper содержит общие геодезические методы
- [ ] ToRadians определён ровно один раз
