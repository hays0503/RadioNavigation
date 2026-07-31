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

Это **отдельный** вектор NaN от F80 (Haversine `Math.Asin` в строке 149), который уже имеет clamp:

```csharp
// Строка 159: F80 исправлен — clamp перед Math.Acos
cosBrngA = Math.Max(-1.0, Math.Min(1.0, cosBrngA));
double brngA = Math.Acos(cosBrngA);  // ← безопасно
```

Но строка 179 **не имеет** такого clamp.

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

- Связан с F80 (Haversine NaN) — тот же файл, другой NaN-вектор, исправлен clamp'ом
- Связан с F73 (Duplicated Haversine) — общая проблема численной стабильности
- Связан с F16 (ErrorKm = 0) — NaN в координатах не отражается в ошибке
