# F137. PositionFixCalculator — Math.Acos аргумент выходит за [-1,1] → NaN

| Поле | Значение |
|------|----------|
| **Приоритет** | P1 (High) |
| **Область** | Logic · PositionFixCalculator · Numerical Stability |
| **Файл** | `Source/Logic/PositionFixCalculator.cs` |
| **Строки** | 179-181 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

В `FindIntersection` аргумент `Math.Acos` вычисляется через spherical law of cosines:

```csharp
// PositionFixCalculator.cs:179-181
double alpha3 = Math.Acos(
    -Math.Cos(alpha1) * Math.Cos(alpha2) +
    Math.Sin(alpha1) * Math.Sin(alpha2) * Math.Cos(dist12));
```

Из-за погрешности float→double преобразования и накопления ошибок округления в выражении `-Cos(a1)*Cos(a2) + Sin(a1)*Sin(a2)*Cos(dist12)` значение может выйти за пределы [-1.0, 1.0] (например, 1.0000000000000002). `Math.Acos` возвращает `NaN` для любых значений вне этого диапазона.

**Результат:** `alpha3 = NaN` → `dist13 = NaN` → `phi3 = NaN` → `lam3 = NaN` → `FixResult.Latitude/Longitude = NaN`. Метод возвращает `Valid = true` с NaN координатами.

Это **отдельный** вектор NaN от F190 (`Math.Asin` в строке 149), у которого также **нет clamp** (F190 документирует и исправление). И F191 (`Math.Asin` в строке 187 для `phi3`), также без clamp. F80 покрывает `Math.Sqrt(1.0 - a)` в `GetDistanceKm` — другом методе.

## Корневая причина

Отсутствие clamp аргумента перед `Math.Acos` в отличие от строк 159 и 166, где clamp уже есть.

## Предлагаемое исправление

```csharp
double cosAlpha3 = -Math.Cos(alpha1) * Math.Cos(alpha2) +
    Math.Sin(alpha1) * Math.Sin(alpha2) * Math.Cos(dist12);
cosAlpha3 = Math.Max(-1.0, Math.Min(1.0, cosAlpha3));  // clamp
double alpha3 = Math.Acos(cosAlpha3);
```

## Зависимости

- Связан с F190 (Asin NaN в `FindIntersection:149`) — аналогичный NaN-вектор в том же методе, также без clamp.
- Связан с F191 (Asin NaN в `FindIntersection:187`, `phi3`) — третий NaN-вектор в том же методе.
- Связан с F80 (Haversine NaN в `GetDistanceKm`) — другой метод, другой NaN-вектор.
- Связан с F73 (Duplicated Haversine) — общая проблема численной стабильности.
- Связан с F16 (ErrorKm = 0) — NaN в координатах не отражается в ошибке.

---

## Acceptance Criteria

- [ ] Аргумент `Math.Acos` на строке 179 clamp'ится к [-1, 1] перед вызовом
- [ ] `alpha3` никогда не равен NaN для валидных входных данных
- [ ] См. также F190 (Asin line 149) и F191 (Asin line 187) — те же NaN-векторы в `FindIntersection`
