# F155. ReceiverDsp.Process — filter alpha пересчитывается 44100 раз/сек без необходимости

| Поле | Значение |
|------|----------|
| **Приоритет** | P3 (Low) |
| **Область** | Sound · DSP · ReceiverDsp · Performance |
| **Файл** | `Source/Sound/DspPipeline.cs` |
| **Строки** | 119 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

Вычисление alpha-коэффициента low-pass фильтра:

```csharp
// DspPipeline.cs:119
float alpha = FilterCutoff / (FilterCutoff + sampleRate / (2f * (float)Math.PI));
```

Эта операция выполняется 44100 раз/сек. `FilterCutoff` (800 Hz) и `sampleRate` (44100) фактически константны во время обработки. Результат `alpha ≈ 0.1023` не меняется.

## Корневая причина

Alpha вычисляется каждый сэмпл вместо кэширования.

## Предлагаемое исправление

Кэшировать alpha:

```csharp
private float cachedAlpha;
private float lastFilterCutoff;

public float FilterCutoff
{
    get => filterCutoff;
    set { filterCutoff = value; cachedAlpha = value / (value + sampleRate / (2f * (float)Math.PI)); }
}
```

## Зависимости

- Связан с F102 (CursorHandler per frame GetComponent) — та же проблема per-frame вычислений
- Связан с F103 (Font create per label) — та же проблема per-frame allocations
