# F191. PositionFixCalculator.FindIntersection — Math.Asin без clamp на строке 187 (phi3)

| Поле | Значение |
|------|----------|
| **Приоритет** | P1 (High) |
| **Область** | Logic · PositionFixCalculator · Numerical Stability |
| **Файл** | `Source/Logic/PositionFixCalculator.cs:187` |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

В `FindIntersection` вычисление широты точки пересечения (`phi3`) использует `Math.Asin` без clamp аргумента:

```csharp
// PositionFixCalculator.cs:187-189
double phi3 = Math.Asin(
    Math.Sin(phi1) * Math.Cos(dist13) +
    Math.Cos(phi1) * Math.Sin(dist13) * Math.Cos(brng13));
```

Аргумент `Math.Sin(phi1) * Math.Cos(dist13) + Math.Cos(phi1) * Math.Sin(dist13) * Math.Cos(brng13)` — это закон синусов в сферической геометрии и теоретически должен быть в [-1, 1]. Однако из-за арифметических погрешностей `float → double` (входные `brng1`, `brng2` — `float`, а также аккумуляция ошибок в `dist12`, `brngA`, `alpha1` и т.д.), значение может выйти за границы [-1, 1] (например, `1.0000000000000002`).

`Math.Asin(x > 1.0)` или `Math.Asin(x < -1.0)` возвращает `NaN`.

---

## Влияние

1. `phi3 = NaN` → `lam3 = NaN` → `lat3/long3 = NaN`.
2. Метод возвращает `GeoPoint { Valid = true, Latitude = NaN, Longitude = NaN }`.
3. `InformationPanelHandler` и `PositionFixCalculator.Calculate` используют NaN-координаты → искажение позиции или падение.
4. **F137** покрывает `Math.Acos` на строке 179, но **не упоминает** `Math.Asin` на строке 187 — этот NaN-вектор остаётся незадокументирован.

---

## Сценарий воспроизведения

1. `dist13` становится NaN (из-за NaN в `dist12` см. F190, или из-за NaN в `alpha3` см. F137).
2. Или: `brng13` имеет погрешность из-за `float → double` и накопления в `alpha1`, `alpha2`.
3. Аргумент `Math.Asin` выходит за [-1, 1] → `phi3 = NaN`.

---

## Исправление

```csharp
double sinPhi3 = Math.Sin(phi1) * Math.Cos(dist13) +
                 Math.Cos(phi1) * Math.Sin(dist13) * Math.Cos(brng13);
sinPhi3 = Math.Max(-1.0, Math.Min(1.0, sinPhi3));   // clamp к [-1, 1]
double phi3 = Math.Asin(sinPhi3);
```

---

## Зависимости

- **F190** — `Math.Asin` на строке 149: соседний NaN-вектор в том же методе. F190 исправляет `dist12`, но `phi3` на строке 187 также нуждается в clamp независимо (даже при валидном `dist12`, погрешность `brng13` может дать аргумент вне [-1, 1]).
- **F137** — `Math.Acos` на строке 179: покрывает один из трёх NaN-векторов в `FindIntersection`. F137 некорректно ссылается на F80 как на покрывающий Asin:149 (см. F190).
- **F80** — Haversine NaN в `GetDistanceKm`: аналогичная проблема, но в другом методе.
- **F73** — Duplicated Haversine: при объединении формул clamp должен быть в общем методе.

---

## Acceptance Criteria

- [ ] Аргумент `Math.Asin` на строке 187 clamp'ится к [-1, 1] перед вызовом
- [ ] `phi3` никогда не равен NaN для валидных входных данных
- [ ] `lat3/long3` не содержат NaN
