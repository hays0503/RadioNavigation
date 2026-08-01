# F153. Atmosphere.GaussianNoise — Box-Muller тратит 50% RNG-вывода

| Поле | Значение |
|------|----------|
| **Приоритет** | P3 (Low) |
| **Область** | Sound · DSP · Atmosphere · RNG |
| **Файл** | `Source/Sound/DspPipeline.cs` |
| **Строки** | 78-83 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

Box-Muller трансформер производит **два** независимых normal-сэмпла из двух uniform-сэмплов:

```csharp
// DspPipeline.cs:78-83
private float GaussianNoise()
{
    double u1 = 1.0 - rng.NextDouble();    // RNG вызов #1
    double u2 = 1.0 - rng.NextDouble();    // RNG вызов #2
    return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2));
    //                                                    ^^^^^ используется только sin
    //                            cos(2.0 * Math.PI * u2)  <-- ВЫБРАСЫВАЕТСЯ
}
```

Реализация использует только `Z1 = sqrt(-2*ln(u1)) * sin(2π*u2)`. `Z0 = sqrt(-2*ln(u1)) * cos(2π*u2)` отбрасывается. Это ~25% overhead на RNG в audio thread (44100 вызовов/сек).

## Корневая причина

Классический Box-Muller производит 2 сэмпла, но используется только 1.

## Предлагаемое исправление

Кэшировать второй сэмпл:

```csharp
private float cachedGaussian;
private bool hasCachedGaussian = false;

private float GaussianNoise()
{
    if (hasCachedGaussian)
    {
        hasCachedGaussian = false;
        return cachedGaussian;
    }

    double u1 = 1.0 - rng.NextDouble();
    double u2 = 1.0 - rng.NextDouble();
    double sqrtTerm = Math.Sqrt(-2.0 * Math.Log(u1));
    cachedGaussian = (float)(sqrtTerm * Math.Cos(2.0 * Math.PI * u2));
    hasCachedGaussian = true;
    return (float)(sqrtTerm * Math.Sin(2.0 * Math.PI * u2));
}
```

## Зависимости

- Связан с F71 (Heavy Random in audio thread) — общая проблема RNG в real-time thread
- Связан с F143 (Atmosphere output unclamped) — тот же метод
