# F169. CarrierGenerator.NextSample — дрифт фазы из-за float-точности

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Sound · DspPipeline · Numerical |
| **Файл** | `Source/Sound/DspPipeline.cs` |
| **Строки** | 16-25 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

```csharp
// DspPipeline.cs:16-25
public float NextSample()
{
    if (frequency.IsNonPositive())
        return 0f;

    float sample = (float)Math.Sin(2.0 * Math.PI * frequency * phase / sampleRate);
    phase += 1.0;

    if (phase >= sampleRate)
        phase -= sampleRate;

    return sample;
}
```

`phase` — это `double`, но вычитание `phase -= sampleRate` при большом накоплении вызывает потерю значащих цифр. При sampleRate = 44100 и частоте 1000 Hz, phase растёт до ~44100 перед вычитанием. После многих часов работы дрифт фазы становится заметным — частота гетеродина смещается.

Дополнительно: `sample = (float)Math.Sin(...)` — промежуточное вычисление в `double`, но результат обрезается до `float`. Это теряет ~7 значащих цифр точности.

## Корневая причина

Использование `float` для phase accumulator при большом sampleRate. В стандартных DSP-библиотеках phase accumulator — это `double` или нормализован в диапазон [0, 1].

## Предлагаемое исправление

Нормализовать phase в [0, 1]:

```csharp
private double phase; // нормализовано в [0, 1)

public float NextSample()
{
    if (frequency.IsNonPositive())
        return 0f;

    float sample = (float)Math.Sin(2.0 * Math.PI * phase);
    phase += frequency / sampleRate;

    if (phase >= 1.0)
        phase -= 1.0;

    return sample;
}
```

## Зависимости

- Связан с F41 (DoubleVsFloatPhase) — F41 описывает double/float mismatch в другом месте
- Связан с F94 (AudioSynthesisEngine tick accumulator float drift)
