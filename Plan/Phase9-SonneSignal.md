# Phase 9: Sonne/Consol Navigation Signal

## Goal

Add realistic Sonne (Consol) navigation signal generation to the T3PLL38 radio panel.

## Architecture

```
SonneSignalGenerator → NavigationPattern → MorseKeyer (stream) →
CarrierGenerator → Atmosphere → ReceiverDSP → OnAudioFilterRead()
```

### Files

| New | Path | Lines |
|-----|------|-------|
| ✅ | `Source/SonneSignalGenerator.cs` | ~80 |
| ✅ | `Source/MorseKeyer.cs` | ~50 |
| ✅ | `Source/DspPipeline.cs` | ~200 |
| ✅ | `Source/SonneController.cs` | ~60 |
| ✅ | `prompts/sonne-algorithm.md` | — |
| ✅ | `Plan/Phase9-SonneSignal.md` | this |

| Changed | Path | What |
|---------|------|------|
| ✏️ | `Source/RadioInstaller.cs` | add SonneController |
| ✏️ | `Source/RadioHandler.cs` | hook station selector + play/stop |

### Key types

```csharp
struct Group { ulong Bits }             // bit 0 = first element
struct NavigationPattern { Group[] Groups; byte ElementsPerGroup; }
```

### Signal chain classes

**SonneSignalGenerator**
- `CalculateDashRatio(float delta, float beamHalfWidth)` → dashRatio
- `GeneratePatternBresenham(int groupCount, int elementsPerGroup, float dashRatio)` → NavigationPattern

**MorseKeyer** (streaming state machine, no intermediate array)
- Tracks current group index, element index within group, element timer
- Dot = 1 time unit, Dash = 3 units, inter-element gap = 1 unit

**CarrierGenerator** — CW carrier oscillator
- Frequency from DialConfig (Band II: 150–340 kHz)
- BFO offset adjustable: ±0–2 kHz
- Output: float sample (sine)

**Atmosphere**
- Thermal noise floor (low-level continuous)
- Impulse noise (occasional spikes)
- Random crackle
- QSB (slow fade 0.05–0.3 Hz)

**ReceiverDSP**
- AGC: fast attack, slow release
- BFO: heterodyne beat
- RC band-pass filter
- Soft saturation (valve-like limiting)

## Non-goals

This implementation does not attempt to emulate:

- transmitter hardware
- antenna phase network
- LF ground-wave propagation
- ionospheric sky-wave interference
- historical timing calibration

The objective is to reproduce the operator experience at the receiver while remaining computationally inexpensive.

## Steps

### 9.1 SonneSignalGenerator

- [ ] Run `prompts/sonne-algorithm.md` through LLM → C# implementation
- [ ] Integrate into `Source/SonneSignalGenerator.cs`
- [ ] Verify: dashRatio 0.0, 0.27, 0.5, 0.73, 1.0 produce correct counts and uniform distribution

### 9.2 NavigationPattern + Group

- [ ] Struct definitions
- [ ] Verify: no extra heap alloc, PopCount works, bit ordering is bit-0-first

### 9.3 MorseKeyer

- [ ] Streaming state machine (current group, element, timer)
- [ ] No KeyEvent[] allocation
- [ ] Timing: dot = 1, dash = 3, gap = 1 (arbitrary unit, scaled by sample rate)

### 9.4 DspPipeline

- [ ] `CarrierGenerator`: sine oscillator at tuned frequency + BFO offset
- [ ] `Atmosphere`: thermal noise (gaussian), impulse noise (poisson), crackle, QSB (sinusoidal gain)
- [ ] `ReceiverDSP`: AGC (peak follower, fast attack ~1 ms, slow release ~500 ms), RC filter (first-order, ~800 Hz cutoff), soft saturation (tanh)
- [ ] All running inside `OnAudioFilterRead()`

### 9.5 SonneController

- [ ] Select station from `BeaconData.Stations`
- [ ] Compute `delta = NormalizeAngle(bearing - beamHeading)`
- [ ] Outside beam (`|delta| > beamHalfWidth`): silence
- [ ] Inside beam: `dashRatio = (delta + beamHalfWidth) / (2 * beamHalfWidth)`
- [ ] Feed into pipeline start/stop

### 9.6 RadioHandler integration

- [ ] `RadioInstaller.cs` — add SonneController component
- [ ] Wire station selector (next/previous station on button or knob)

## Verification criteria

- [ ] Transition from dot-dominant to dash-dominant is continuous.
- [ ] Baseline (delta = 0) produces approximately equal dot/dash ratio.
- [ ] Pattern is deterministic: same player position → same groups.
- [ ] Outside beam: no navigation pattern generated.
- [ ] `OnAudioFilterRead()` produces no managed allocations after warmup.
- [ ] No dynamic allocation per audio callback.
- [ ] Runtime complexity: O(groupCount × elementsPerGroup).
