# F190. PositionFixCalculator.FindIntersection — Math.Asin без clamp на строке 149

| Поле | Значение |
|------|----------|
| **Приоритет** | P1 (High) |
| **Область** | Logic · PositionFixCalculator · Numerical Stability |
| **Файл** | `Source/Logic/PositionFixCalculator.cs:149` |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

В `FindIntersection` формула Haversine вычисляет расстояние между двумя точками через `Math.Asin(Math.Sqrt(a))`, но **не применяет clamp** к аргументу `Math.Sqrt` перед передачей в `Math.Asin`:

```csharp
// PositionFixCalculator.cs:149-152
double dist12 = 2.0 * Math.Asin(Math.Sqrt(
    Math.Sin(deltaPhi / 2.0) * Math.Sin(deltaPhi / 2.0) +
    Math.Cos(phi1) * Math.Cos(phi2) *
    Math.Sin(deltaLam / 2.0) * Math.Sin(deltaLam / 2.0)));
```

Из-за арифметических погрешностей `float → double` сумма внутри `Math.Sqrt(...)` может превысить `1.0` (например, `1.0000000000000002`), и `Math.Sqrt(1.0000000002)` вернёт значение > 1.0. `Math.Asin(x > 1.0)` возвращает `NaN`, что приводит к `dist12 = NaN`, а затем — к `NaN` во всех последующих вычислениях.

---

## Что заявлял F137

F137 (стр. 28) утверждает, что F80 "уже имеет clamp" для `Math.Asin` в строке 149:

> "F80 (Haversine `Math.Asin` в строке 149), который уже имеет clamp"

**Это неверно**:
- **F80** о `GetDistanceKm:214` (`Math.Sqrt(1.0 - a)`) — это **другой метод**, а не `FindIntersection:149`.
- **Строка 149** **никакого clamp не имеет**. F80 не применяется к `FindIntersection`.

---

## Влияние

1. `dist12 = NaN` → `Math.Acos` на строках 160, 167 получают NaN аргумент → `brngA`/`brngB = NaN`.
2. `alpha3 = NaN` (см. F137) → `dist13 = NaN` → `phi3 = NaN` → координаты fix-а = NaN.
3. Метод возвращает `Valid = true` с NaN координатами (см. F137, строка 26).
4. Позиция лодки в UI становится NaN → `InformationPanelHandler` отображает нечисловые значения или падает.

**Сценарий воспроизведения**: две станции находятся на одинаковой широте или вблизи друг друга (deltaPhi ≈ 0, deltaLam ≈ 0), и float-precision аккумулирует ошибку в сумме sin².

---

## Исправление

Добавить clamp перед `Math.Asin`, аналогично исправлению F80:

```csharp
double a = Math.Sin(deltaPhi / 2.0) * Math.Sin(deltaPhi / 2.0) +
           Math.Cos(phi1) * Math.Cos(phi2) *
           Math.Sin(deltaLam / 2.0) * Math.Sin(deltaLam / 2.0);
a = Math.Min(Math.Max(a, 0.0), 1.0);   // clamp к [0, 1]
double dist12 = 2.0 * Math.Asin(Math.Sqrt(a));
```

---

## Зависимости

- **F80** — `GetDistanceKm` имеет аналогичный `Math.Sqrt(1.0 - a)` NaN, но в другом методе. Предлагаемое исправление здесь не пересекается с F80.
- **F73** — Duplicated Haversine: формула дублируется в `FindIntersection` и `GetDistanceKm`. При рефакторинге (F73) clamp нужно добавить в оба места.
- **F137** — `Math.Acos` на строке 179: соседний NaN-вектор в том же методе. F137 уже документирует Acos, но **ошибочно ссылается на F80** как на покрывающий Asin line 149.
- **F16** — `ErrorKm = 0`: NaN-координаты не отражаются в ошибке позиционирования.
- **F128** — анти meridian: `deltaLam` не нормализуется, увеличивая шанс NaN.

---

## Acceptance Criteria

- [ ] `Math.Asin` на строке 149 всегда получает аргумент в [-1, 1]
- [ ] `dist12` никогда не равен NaN для валидных входных данных
- [ ] F137 обновлён: кросс-референс на F80 заменён на корректную ссылку на F190
