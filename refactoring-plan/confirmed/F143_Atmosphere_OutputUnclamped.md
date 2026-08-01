# F143. Atmosphere — выходной сигнал unclamped, noise выходит за [-1, 1]

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Sound · DSP · Atmosphere |
| **Файл** | `Source/Sound/DspPipeline.cs` |
| **Строки** | 67-75 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

Три источника шума суммируются без clamp:

```csharp
// DspPipeline.cs:67-75
float noise = GaussianNoise() * NoiseFloor;           // Gaussian: теоретически unbounded

if (rng.NextDouble() < ImpulseProbability)
    noise += ImpulseAmplitude * ((float)rng.NextDouble() * 2f - 1f);  // ±0.3

if (rng.NextDouble() < 0.0001)
    noise += 0.15f * ((float)rng.NextDouble() * 2f - 1f);           // ±0.15

return sample * qsb + noise;  // без clamp
```

Худший случай noise: `~0.012 (Gaussian) + 0.3 (impulse) + 0.15 (hardcoded impulse) = 0.462`. Сигнал может быть на амплитуде 1.0. Итого: до ~1.46. Это проходит в `ReceiverDsp.Process`, который выдаёт до ~1.12 (см. F142).

## Корневая причина

Нет clamp на выходе `Apply()`. Gaussian noise теоретически unbounded.

## Предлагаемое исправление

```csharp
return Mathf.Clamp(sample * qsb + noise, -1f, 1f);
```

Или лучше — clamp на финальном выходе в `ProcessAudio` перед записью в буфер.

## Зависимости

- Связан с F142 (ReceiverDsp unclamped) — следующий этап DSP pipeline
- Связан с F71 (Heavy Random in audio thread) — noise генерируется в audio thread
