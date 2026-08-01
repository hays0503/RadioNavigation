# F01: Thread-Safety — Race Condition в Audio Callback

**Приоритет:** P0 (Critical)
**Область:** Sound · AudioSynthesisEngine + SonneController
**Блокирует:** Любые изменения в звуковой подсистеме, стабильность на non-x86 платформах

---

## Проблема

`OnAudioFilterRead` вызывается Unity в выделенном audio thread-е. `Update()` бежит в main thread. Оба одновременно читают и пишут одни и те же массивы без какой-либо синхронизации.

**Ни одного примитива синхронизации во всём проекте:** нет `lock`, `Monitor`, `volatile`, `Interlocked`, `[MethodImpl(MethodImplOptions.Synchronized)]`. Полный grep дал 0 совпадений.

---

## Evidence

### Два потока, общие массивы

```
MAIN THREAD                          AUDIO THREAD
Update() [SonneController.cs:320]    OnAudioFilterRead() [443]
  └─ UpdateStationGains() [337]        └─ ProcessAudio() [445]
       ├─ stations[i].Gain = gain           ├─ if(stations[s].Gain...)    ← RACE
       │  [366]                             │  [AudioSynthesisEngine.cs:80,134]
       ├─ stations[i].BeatFrequency          ├─ schedulers[s].Tick()
       │  [369]                             │  [92,114]
       └─ carriers[i].Frequency              └─ keyers[s].Advance()
          [370]                                [123]
```

### Таблица гонок

| Shared Variable | Main Thread (writer) | Audio Thread (reader/writer) | Race Type |
|---|---|---|---|
| `stations[].Gain` (float) | L366 `stations[i].Gain = gain` | L80,134 `stations[s].Gain.IsNegligible(...)` | write/read — torn float |
| `stations[].BeatFrequency` (float) | L369 `stations[i].BeatFrequency = ...` | L60 `stations[i].BeatFrequency - BfoBase` | write/read — torn float |
| `carriers[].Frequency` (float) | L370 `carriers[i].Frequency = ...` | CarrierGenerator.NextSample() читает frequency | write/read — фаза несущей сломана |
| `schedulers[].CurrentConsolPattern` (reference) | L423 `schedulers[i].CurrentConsolPattern = ...` | L102 `sched.GetCurrentConsolPattern()` | write/read — torn reference на 32-bit |
| `schedulers[].CurrentPhase` (enum/int) | Через `Tick()` на audio thread | L87-89 читает, L92 пишет | write/write на audio thread + main thread не пишет, но |
| `keyers[].IsActive` (bool) | `Load`/`LoadConsol`/`Reset` (main) | L83 `keyers[s].IsActive` (audio) | write/read — torn bool |
| `keyers[].state` (enum) | `Load`/`LoadConsol`/`Reset` (main) | `Advance`, `StartElement` (audio) | write/write, write/read |
| `atmosphere.NoiseFloor`, `ImpulseProbability` | `ApplyFeedback` [AE.cs:46-47] | `atmosphere.Apply()` [AE.cs:142] | write/read — stale noise params |
| `dsp.WhistleLevel` | `ApplyFeedback` [AE.cs:49] | `dsp.Process()` [AE.cs:143] | write/read — stale DSP state |

### `schedulers[s].Tick()` и `keyers[s].Advance()` бегут внутри аудио-коллбэка

AudioSynthesisEngine.cs:92, 114:
```csharp
sched.Tick();
```

AudioSynthesisEngine.cs:123:
```csharp
keyers[s].Advance();
```

При этом `Advance()` (MorseKeyer.cs:78) может вызвать `scheduler.Tick()` повторно, и `scheduler.OnSequenceFinished()` (MorseKeyer.cs:148-151), которые мутируют `CurrentPhase`.

### Полный путь вызовов

`OnAudioFilterRead` [SonneController.cs:443-446] передаёт в `ProcessAudio` все 4 разделяемых массива + глобальное статическое поле `ToggleHandler.IsRadioOn` — без volatile/барьера памяти:

```csharp
audioEngine.ProcessAudio(data, channels, stations, schedulers, keyers, carriers,
    ToggleHandler.IsRadioOn, currentZoneIndex, SonneZoneIndex);
```

---

## Предлагаемое решение

1. **Double-buffer pattern:** audio thread читает из `readBuffer`, main thread пишет в `writeBuffer`; swap на frame boundary.
2. **Вынос `StationScheduler.Tick()` и `MorseKeyer.Advance()`** из `ProcessAudio` в `Update()` — в аудио-коллбэке остаётся только чтение предвычисленных значений.
3. **`volatile`** на флагах, которые читаются из audio thread (`ToggleHandler.IsRadioOn`).

---

## Acceptance Criteria

- [ ] Аудио-коллбэк не мутирует состояние: только читает предвычисленные сэмплы.
- [ ] Все разделяемые данные защищены double-buffer или volatile.
- [ ] Звук не содержит артефактов при длительной работе (>1 час).
