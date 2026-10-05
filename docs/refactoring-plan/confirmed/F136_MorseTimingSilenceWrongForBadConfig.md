# F136. MorseTiming — молча неверная длительность тишины при несогласованных конфигах

| Поле | Значение |
|------|----------|
| **Приоритет** | P3 (Low) |
| **Область** | Logic · MorseTiming · Validation |
| **Файл** | `Source/Logic/MorseTiming.cs` |
| **Строки** | 21-33 |

---

## Описание

`GetDotSilenceTicks` и `GetDashSilenceTicks` вычисляют длительность тишины:

```csharp
// MorseTiming.cs:21-26
public static int GetDotSilenceTicks(ConsolCycleConfig config)
{
    float period = config.ElementPeriodMs.IsPositive() ? config.ElementPeriodMs : 1000f;
    float sound = config.DotSoundMs.IsPositive() ? config.DotSoundMs : 125f;
    return Math.Max(1, (int)((period - sound) / TickMs));
}
```

Если `ElementPeriodMs < DotSoundMs` (оба положительные), `(period - sound)` даёт отрицательный float. Приведение к `int` даёт 0 или отрицательное. `Math.Max(1, ...)` clamps до 1 тика (50ms).

**Результат:** Итоговая длительность элемента = `sound_ms + 50ms`, что **больше** настроенной `ElementPeriodMs`.

Пример: `DotSoundMs=125, ElementPeriodMs=100, DashSoundMs=875, EquisignalMs=1000`
- Dot silence: `(100 - 125) / 50` = `-0.5` → `(int)(-0.5)` = `0` → `Math.Max(1, 0)` = 1 тик = 50ms
- Dot duration: 125 + 50 = 175ms вместо 100ms
- Dash silence: `(100 - 875) / 50` = `-15.5` → `(int)(-15.5)` = `-15` → `Math.Max(1, -15)` = 1 тик = 50ms
- Dash duration: 875 + 50 = 925ms вместо 1000ms

Ни одно из этих значений не выводит предупреждение.

## Корневая причина

Нет валидации согласованности `ElementPeriodMs` с `DotSoundMs` / `DashSoundMs`. Нет предупреждения при неверных результатах.

## Предлагаемое исправление

Добавить проверку и предупреждение:

```csharp
public static int GetDotSilenceTicks(ConsolCycleConfig config)
{
    float period = config.ElementPeriodMs.IsPositive() ? config.ElementPeriodMs : 1000f;
    float sound = config.DotSoundMs.IsPositive() ? config.DotSoundMs : 125f;
    float silenceMs = Math.Max(0, period - sound);
    if (silenceMs <= 0)
        Debug.LogWarning($"[MorseTiming] ElementPeriodMs ({period}) < DotSoundMs ({sound}), timing inaccurate");
    return Math.Max(1, (int)(silenceMs / TickMs));
}
```

## Зависимости

- Связан с F126 (ConsolCycleConfig no validation) — корневая причина в конфиге, следствие в MorseTiming
- Связан с F81 (MorseTiming integer truncation) — тот же файл, другой аспект точности
