# F40: CarrierGenerator.frequency не ограничен сверху

**Приоритет:** P2 (Medium)
**Область:** Sound · DspPipeline
**Тип:** Robustness

---

## Проблема

Сеттер `CarrierGenerator.Frequency` (DspPipeline.cs:13-15) обрезает только снизу (`Math.Max(0f, value)`), но не ограничивает сверху. Частота может быть установлена в ультразвуковой диапазон или выше Nyquist (sampleRate/2). При превышении Nyquist возникают алиасинг-артефакты.

---

## Evidence

DspPipeline.cs:12-15:
```csharp
public float Frequency
{
    get => frequency;
    set => frequency = Math.Max(0f, value);   // ← нет верхней границы
}
```

DspPipeline.cs:25-35:
```csharp
public float NextSample()
{
    float sample = (float)Math.Sin(2.0 * Math.PI * frequency * phase / sampleRate);
    // если frequency > sampleRate/2 — алиасинг
}
```

---

## Impact

- При `frequency > sampleRate/2` (22050@44100) — алиасинг
- При `frequency = 0` — тишина (безопасно)
- BfoBase = 1000 Hz — в норме, но offset может увести выше Nyquist

## Fix

Добавить `Math.Min(sampleRate / 2f, value)`.
