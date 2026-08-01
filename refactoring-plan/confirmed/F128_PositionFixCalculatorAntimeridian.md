# F128. PositionFixCalculator.FindIntersection не обрабатывает анти meridian

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Logic · PositionFixCalculator · Geography |
| **Файл** | `Source/Logic/PositionFixCalculator.cs` |
| **Строки** | 135-201 |

---

## Описание

Алгоритм great-circle intersection вычисляет долготу пересечения через:

```csharp
// PositionFixCalculator.cs:195
double lam3 = lam01 + deltaLam13;

// Строка 198 — нормализация:
float lon3 = (float)((lam3 * (180.0 / Math.PI) + 540.0) % 360.0 - 180.0);
```

Нормализация `+540 % 360 - 180` корректно приводит longitude к диапазону [-180, 180]. Однако промежуточные вычисления `deltaLam` (строка 147) и `brngA`/`brngB` (строки 161, 168) используют **сырую разницу** долгот:

```csharp
double deltaLam = lam02 - lam01; // строка 147
```

Если станция 1 на longitude 179°, а станция 2 на longitude -179°, `deltaLam` = -358° вместо +2°. Это даёт неверные `brngA`/`brngB` → неверное пересечение.

**Сценарий:** позиционирование в Беринговом проливе (станции по разные стороны 180° meridian) или в районе Чукотского моря.

## Корневая причина

`deltaLam` не нормализуется к [-180°, 180°] перед использованием в тригонометрических формулах.

## Предлагаемое исправление

Нормализовать `deltaLam` перед вычислениями:

```csharp
double deltaLam = lam02 - lam01;
// Нормализация к [-PI, PI]:
while (deltaLam > Math.PI) deltaLam -= 2.0 * Math.PI;
while (deltaLam < -Math.PI) deltaLam += 2.0 * Math.PI;
```

Или использовать `Math.Atan2(Math.Sin(deltaLam), Math.Cos(deltaLam))` для нормализации.

## Зависимости

- Связан с F80 (PositionFixCalculator Haversine NaN) — тот же файл
- Связан с F73 (PositionFixCalculator duplicated Haversine) — дублированный код
- Связан с F16 (PositionFixCalculator.ErrorKm = 0) — ошибка не может быть корректно вычислена при неверном пересечении
