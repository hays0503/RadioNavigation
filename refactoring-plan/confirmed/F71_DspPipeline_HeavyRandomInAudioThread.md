# F71: DspPipeline — 2-3 Random.NextDouble() на сэмпл в audio thread

**Приоритет:** P2 (Medium)  
**Область:** Sound · DSP · Performance  
**Файл:** `Source/Sound/DspPipeline.cs:69,72,80`

---

## Проблема

`Atmosphere.Apply()` вызывает `rng.NextDouble()` **дважды** на каждый сэмпл, плюс ещё один внутри `GaussianNoise()`:

```csharp
// Atmosphere.Apply (строка 69, 72):
if (rng.NextDouble() < ImpulseProbability)    // ← 1-й Random
    sample = ...;

if (rng.NextDouble() < 0.0001)                 // ← 2-й Random
    sample = ...;                              //     whistle generation

// GaussianNoise (строка 80):
return Math.Sqrt(-2.0 * Math.Log(rng.NextDouble())) * ...  // ← 3-й Random
```

При частоте дискретизации 44100 Гц:
- `Atmosphere.Apply`: 88200 вызовов `NextDouble()`/сек
- `GaussianNoise`: 44100 вызовов `NextDouble()`/сек (плюс `Log`, `Sqrt`, `Sin`)
- **Итого: ~132300 `NextDouble()` вызовов/сек**

`System.Random.NextDouble()` не предназначен для высокопроизводительного DSP. Каждый вызов включает:
- Обновление внутреннего состояния (умножение + сложение на 64-битных числах)
- Приведение к `double`

## Дополнительно

- `Math.Sin` в `CarrierGenerator.NextSample()` (строка 28) и QSB-модуляции (строка 61) — ещё один дорогой вызов на сэмпл
- Всё это выполняется в real-time audio callback — задержки недопустимы

## Влияние

- На слабых системах возможны audio dropouts (xruns)
- Потребление CPU выше необходимого
- `Random` — не thread-safe по дизайну, что усугубляет риски из F01

## Исправление

1. **Для шума:** Предвычислять Gaussian noise таблицу (или использовать `MathNet.Numerics`), читать из буфера
2. **Для импульсов/свиста:** Использовать отдельный бюджет сэмплов, а не `Random` на каждый сэмпл
3. **Для Sin:** Рассмотреть lookup-таблицу (LUT) с интерполяцией
4. **Обёртка:** Заменить `System.Random` на более быстрый ГПСЧ типа `xorshift128+`

## Риски

- Изменение DSP влияет на звук — требуется тестирование на слух
- Предвычисление шума увеличивает потребление памяти
