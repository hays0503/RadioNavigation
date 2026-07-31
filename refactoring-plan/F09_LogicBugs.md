# F09: Logic Bugs — Division by Zero, Пропущенные Guard-ы, Float Precision

**Приоритет:** CRITICAL / HIGH
**Область:** Sound + Logic
**Тип:** Баги

---

## 1. Division by Zero в `UpdateStationGains` — начальная частота 0 кГц

**Приоритет:** CRITICAL
**Файл:** `SonneController.cs:362-364`

```csharp
float f0 = freq.Value * 1000f;                       // частота станции в Hz
float f = currentFrequencyKHz * 1000f;               // настроенная частота в Hz
float ratio = f / f0 - f0 / f;                       // ← f может быть 0!
```

`currentFrequencyKHz` инициализирован `0f` (строка 24) и не устанавливается до первого взаимодействия пользователя. `UpdateStationGains()` бежит каждые 16мс (через `Update()`). Пока пользователь не покрутил диал:

- `f = 0.0 * 1000 = 0.0`
- `f0 / f = 100,000 / 0.0 = +Inf` (double) → `float.PositiveInfinity`
- `ratio * ratio = +Inf`
- `1f / Sqrt(1 + qf*qf*Inf) = 0f / NaN` → **NaN**
- Все `stations[i].Gain = NaN`
- `maxGain.IsSignificant(0.01f)` → false (NaN сравнивается ложно)
- Все станции молчат до первого поворота диала

**Воспроизведение:** Загрузить сцену, не трогая диал — все станции показывают Gain = NaN.

**Фикс:** Guard: `if (currentFrequencyKHz.IsNegligible(0.001f)) return;`

---

## 2. Division by Zero в `PositionFixCalculator` — cos(phi) = 0 на полюсе

**Приоритет:** HIGH
**Файл:** `PositionFixCalculator.cs:157-158, 164-165`

```csharp
double cosBrngA = (Math.Sin(phi2) - Math.Sin(phi1) * Math.Cos(dist12)) /
                  (Math.Sin(dist12) * Math.Cos(phi1));     // ← div by zero при phi1 = 90°
```

Если станция находится на широте ±90° (полюс), `Math.Cos(phi1) = 0`. В текущем датасете нет станций на полюсе, но код не защищён. То же для `phi2` на строках 164-165.

---

## 3. No try/catch в audio callback

**Приоритет:** CRITICAL
**Файл:** `SonneController.cs:443-446`, `AudioSynthesisEngine.cs:65-148`

```csharp
private void OnAudioFilterRead(float[] data, int channels)
{
    audioEngine.ProcessAudio(data, channels, stations, schedulers, keyers, carriers,
        ToggleHandler.IsRadioOn, currentZoneIndex, SonneZoneIndex);
}
```

Любой exception (NRE, IndexOutOfRange, DivByZero) внутри `ProcessAudio` убивает аудио-подсистему Unity. Аудио-тред не умеет восстанавливаться — игра теряет звук до перезапуска.

**Фикс:** try/catch внутри `OnAudioFilterRead` или `ProcessAudio`. Хотя бы логировать без повторного броска.

---

## 4. tickAccumulator не ограничен — audio hitch после зависания

**Приоритет:** HIGH
**Файл:** `AudioSynthesisEngine.cs:75-126`

```csharp
tickAccumulator += ticksPerSample;    // +0.0004535 за сэмпл
while (tickAccumulator >= 1f)
{
    for (int s = 0; s < stations.Length; s++) { /* scheduler + keyer */ }
    tickAccumulator -= 1f;
}
```

Если поток выполнения задерживается на несколько секунд (scene load, тяжелый GC), `tickAccumulator` накапливается. При `tickAccumulator = 1000` внутренний цикл бежит 1000 раз на одном аудио-буфере — каждая итерация обрабатывает 14 станций с scheduler + keyer. Это вызывает звуковой stutter на несколько секунд.

**Фикс:** `tickAccumulator = Math.Min(tickAccumulator, maxTicks)` — ограничить 2-3 тиками за буфер.

---

## 5. Float exact equality — `IsZero()` через `== 0f`

**Приоритет:** MEDIUM
**Файл:** `ComparisonExtensions.cs:17-18, 24-25`

```csharp
public static bool IsZero(this float value) => value == 0f;
public static bool IsZero(this double value) => value == 0.0;
```

Используется в:

- `CursorHandler.cs:102`: scroll delta check — колесо мыши может вернуть дробное значение на некоторых устройствах
- `PositionFixCalculator.cs:174`: `Math.Sin(alpha1).IsZero()` — синус угла редко равен точно 0.0 из-за FPU precision. Должно быть `Math.Abs(x) < epsilon`.

**Фикс:** `Math.Abs(value) < Epsilon` или переименовать в `IsExactlyZero` чтобы подчеркнуть семантику.

---

## 6. `CursorAngle` setter — точное сравнение float

**Приоритет:** LOW
**Файл:** `RadioHandler.cs:28`

```csharp
if (cursorAngle == value) return;  // exact float equality
```

После 90+ градусов поворота tiny floating-point rounding может привести к тому, что «та же» angle сравнивается как не-равна. Срабатывает `EmitFrequency()` без необходимости (хотя `UpdateStationGains` фильтрует по времени).

---

## Acceptance Criteria

- [ ] `currentFrequencyKHz = 0` не вызывает NaN в gain
- [ ] `PositionFixCalculator` не падает при cos(phi) ≈ 0
- [ ] `OnAudioFilterRead` не убивает аудио при исключении
- [ ] `tickAccumulator` ограничен макс. 3 тиками
- [ ] `IsZero` не используется для float precision checks без эпсилон
