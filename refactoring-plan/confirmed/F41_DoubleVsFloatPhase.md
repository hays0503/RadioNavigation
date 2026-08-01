# F41: Double vs float — несоответствие типов фазы

**Приоритет:** P2 (Medium)
**Область:** Sound · DspPipeline
**Тип:** Precision · Code Quality

---

## Проблема

Во всех трёх DSP-классах фаза аккумулируется как `double`, но сравнивается с `sampleRate` (float). При накоплении больших значений фазы double → float conversion в сравнении теряет точность. Также все `Math.Sin` принимают double, а `sampleRate` — float, что требует неявного приведения.

---

## Evidence

CarrierGenerator.cs:28-32:
```csharp
phase += 1.0;                            // double
if (phase >= sampleRate)                 // sampleRate — float
    phase -= sampleRate;                 // float → double conversion
```

Atmosphere.Apply (DspPipeline.cs:63-65):
```csharp
qsbPhase += 1f;                          // float
if (qsbPhase >= sampleRate)              // float
    qsbPhase -= sampleRate;
```

ReceiverDsp.Process (DspPipeline.cs:127-128):
```csharp
whistlePhase += 1.0;                     // double
if (whistlePhase >= sampleRate)          // float
    whistlePhase -= sampleRate;
```

---

## Impact

- На длительных сессиях возможен дрейф фазы (в норме фаза сбрасывается, но float→double на границе может давать off-by-one)
- Код непоследователен: CarrierGenerator использует double, Atmosphere — float

## Fix

Привести все к единому типу (double). Comparator: если `phase >= sampleRate`, то `phase -= sampleRate` — для double накопление безопаснее.
