# F142. ReceiverDsp — выходной сигнал unclamped, whistle выходит за [-1, 1]

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Sound · DSP · ReceiverDsp |
| **Файл** | `Source/Sound/DspPipeline.cs` |
| **Строки** | 122, 129, 132 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

`Math.Tanh` ограничивает выход до [-1, 1], но whistle добавляет до 0.12 поверх этого:

```csharp
// DspPipeline.cs:122
float saturated = (float)Math.Tanh(SaturationDrive * prevFilter);

// DspPipeline.cs:129
saturated += whistle * WhistleLevel * 0.12f;

// DspPipeline.cs:132
return saturated;  // может быть до ~1.12 или ~-1.12
```

Значение пишется напрямую в Unity audio buffer (`data[i + c] = sample * outputVolume`). Unity clips при превышении [-1, 1], но немаркированный clipping производит harsh distortion.

## Корневая причина

Whistle компонент добавляется после tanh saturation без финального clamp.

## Предлагаемое исправление

```csharp
return Mathf.Clamp(saturated, -1f, 1f);
```

## Зависимости

- Связан с F41 (Double vs float phase) — тот же DSP pipeline
- Связан с F13 (Unity API in audio thread) — Unity audio clipping
