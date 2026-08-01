# F141. CarrierGenerator — смена Frequency без phase smoothing → аудио-клики

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Sound · DSP · CarrierGenerator |
| **Файл** | `Source/Sound/DspPipeline.cs` |
| **Строки** | 11-15, 28 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

Свойство `Frequency` мгновенно применяется без сглаживания:

```csharp
// DspPipeline.cs:11-15
public float Frequency
{
    get => frequency;
    set => frequency = Math.Max(0f, value);   // мгновенное изменение
}
```

В `NextSample()` аргумент синуса пересчитывается мгновенно:

```csharp
// DspPipeline.cs:28-29
float sample = (float)Math.Sin(2.0 * Math.PI * frequency * phase / sampleRate);
phase += 1.0;
```

При переходе с 500 Hz на 600 Hz синусоида прыгает с `sin(2π·500·n/44100)` на `sin(2π·600·n/44100)` без интерполяции. Это создаёт фазовый разрыв (phase discontinuity), который проявляется как аудио-щелчок/pop.

**Вызывается из:** `SonneController.UpdateStationGains` (главный поток), читается из `OnAudioFilterRead` (аудио поток).

## Корневая причина

Нет плавного перехода (smoothing) при изменении частоты. Мгновенная смена частоты в синусоиде создаёт фазовый разрыв.

## Предлагаемое исправление

Добавить one-pole interpolator:

```csharp
private float targetFrequency;
private float smoothedFrequency;

public float Frequency
{
    get => smoothedFrequency;
    set => targetFrequency = Math.Max(0f, value);
}

public float NextSample()
{
    smoothedFrequency += (targetFrequency - smoothedFrequency) * 0.1f;
    float sample = (float)Math.Sin(2.0 * Math.PI * smoothedFrequency * phase / sampleRate);
    phase += 1.0;
    if (phase >= sampleRate) phase -= sampleRate;
    return sample;
}
```

## Зависимости

- Связан с F40 (CarrierGenerator frequency без верхней границы) — тот же класс
- Связан с F41 (Double vs float phase) — тот же класс, другой аспект точности
- Связан с F29 (No Synchronization) — частота пишется из main thread, читается из audio thread
