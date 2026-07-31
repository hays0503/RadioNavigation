# F156. Atmosphere — недокументированный второй impulse path с magic constants

| Поле | Значение |
|------|----------|
| **Приоритет** | P3 (Low) |
| **Область** | Sound · DSP · Atmosphere · Maintainability |
| **Файл** | `Source/Sound/DspPipeline.cs` |
| **Строки** | 72-73 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

В `Atmosphere.Apply` два независимых impulse noise механизма:

| Параметр | Path 1 (configurable) | Path 2 (hardcoded) |
|----------|----------------------|---------------------|
| Threshold | `ImpulseProbability` (0.001) | 0.0001 (magic) |
| Amplitude | `ImpulseAmplitude` (0.3) | 0.15 (magic) |
| Purpose | Regular impulse crackle | "Loud" rare crackle |
| Configurable? | Yes | **No** |

```csharp
// DspPipeline.cs:69-73
if (rng.NextDouble() < ImpulseProbability)          // Path 1: configurable
    noise += ImpulseAmplitude * ((float)rng.NextDouble() * 2f - 1f);

if (rng.NextDouble() < 0.0001)                      // Path 2: hardcoded (НЕДОКУМЕНТИРОВАН)
    noise += 0.15f * ((float)rng.NextDouble() * 2f - 1f);
```

Path 2 fires ~4.4 раз/сек (0.0001 × 44100). Не имеет именованных констант, документации, и не может быть отключён. Разработчик, модифицирующий impulse system, увидит только Path 1 и пропустит Path 2.

## Корневая причина

Hardcoded магические константы без документации или именования.

## Предлагаемое исправление

```csharp
private const float LoudCrackleProbability = 0.0001f;
private const float LoudCrackleAmplitude = 0.15f;

if (rng.NextDouble() < LoudCrackleProbability)
    noise += LoudCrackleAmplitude * ((float)rng.NextDouble() * 2f - 1f);
```

## Зависимости

- Связан с F28 (Magic Numbers scattered) — та же проблема magic constants
- Связан с F71 (Heavy Random in audio thread) — оба path используют RNG
