# F151. DialConfig.AngleToFrequency — отсутствует интерполяция между AngleStart и первой reference point

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Data · DialConfig · Interpolation |
| **Файл** | `Source/Data/DialConfig.cs` |
| **Строки** | 112-140 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

`AngleToFrequency` обрабатывает `angle <= AngleStart` и `angle >= AngleEnd` как edge cases (строки 112-115), но если `angle` находится между `AngleStart` и углом первой reference point, цикл `foreach` выдаёт неверный результат:

```csharp
// DialConfig.cs:120-136
float prevFreq = 0, prevAngle = 0;
bool found = false;

foreach (var kvp in points)
{
    float freq = kvp.Key;
    float ang = kvp.Value;

    if (ang >= angle)
    {
        if (!found)
            return freq;  // ← возвращает частоту первой reference point без интерполяции
        float t = (angle - prevAngle) / (ang - prevAngle);
        return prevFreq + t * (freq - prevFreq);
    }

    prevFreq = freq;
    prevAngle = ang;
    found = true;
}
```

Когда `AngleStart < angle < first_ang`, `!found == true` и метод возвращает `freq` (частоту первой reference point) **без интерполяции** между `(AngleStart, RangeMin)` и первой точкой.

**Пример (Band IV):** `AngleStart=12°`, первая reference point `{720, 12f}`. При `angle=15°` метод вернёт `720` (первая точка), хотя правильная интерполяция между `AngleStart=12°→720kHz` и `{740, 18.8f}` дала бы ~732 kHz.

Для Band I (`AngleStart=15f`, первая точка `{70, 15f}`) проблема не проявляется, потому что `AngleStart` совпадает с углом первой точки.

## Корневая причина

`prevFreq`/`prevAngle` инициализируются нулями, но `found` ещё `false` при первой итерации. Нет interpolation между `(AngleStart, RangeMin)` и первой reference point.

## Предлагаемое исправление

Инициализировать `prevFreq = band.RangeMin` и `prevAngle = band.AngleStart`:

```csharp
float prevFreq = band.RangeMin;
float prevAngle = band.AngleStart;
bool found = false;

foreach (var kvp in points)
{
    float freq = kvp.Key;
    float ang = kvp.Value;

    if (ang >= angle)
    {
        float t = (angle - prevAngle) / (ang - prevAngle);
        return prevFreq + t * (freq - prevFreq);
    }

    prevFreq = freq;
    prevAngle = ang;
}
```

Убрать `if (!found) return freq;`.

## Зависимости

- Связан с F20 (DialConfig band inconsistencies) — тот же файл
- Связан with F58 (DialConfig NaN propagation) — NaN может возникнуть при edge case
- Связан с F150 (Overlapping bands) — перекрытие усугубляет проблему
