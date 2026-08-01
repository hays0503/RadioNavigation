# F154. ReceiverDsp — конструктор принимает sampleRate, но не использует

| Поле | Значение |
|------|----------|
| **Приоритет** | P3 (Low) |
| **Область** | Sound · DSP · ReceiverDsp · API Design |
| **Файл** | `Source/Sound/DspPipeline.cs` |
| **Строки** | 98-103 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

Конструктор принимает `sampleRate`, но не сохраняет его:

```csharp
// DspPipeline.cs:98-103
public ReceiverDsp(float sampleRate)   // ← параметр принят
{
    agcGain = 1f;
    prevFilter = 0f;
    whistlePhase = 0.0;
    // sampleRate НЕ сохраняется и НЕ используется
}
```

`sampleRate` требуется повторно в каждом вызове `Process()` (строка 105). Это вводящий в заблуждение API — caller предполагает, что конфигурация происходит в конструкторе.

## Корневая причина

Параметр конструктора не сохраняется в поле.

## Предлагаемое исправление

Либо сохранить `sampleRate` как поле, либо убрать параметр из конструктора:

```csharp
// Вариант 1: убрать параметр (если sampleRate всегда передаётся в Process)
public ReceiverDsp()
{
    agcGain = 1f;
    prevFilter = 0f;
    whistlePhase = 0.0;
}
```

## Зависимости

- Связан с F41 (Double vs float phase) — тот же класс
- Связан с F25 (AudioSynthesisEngine tight coupling) — та же проблема API design
