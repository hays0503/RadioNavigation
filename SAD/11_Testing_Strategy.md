# Testing Strategy — RadioNavigation Mod

> **⚠️ Deep Scan Update (2026-08-01):** ADR-007 имеет статус «Deferred» (был «Accepted» — F45, исправлено). **0 тестов существует** (F10). Проект использует `csc.exe` (не `dotnet build`), поэтому команда `dotnet test` не работает — нужна тестовая инфраструктура (F22). `StationManagerTests`/`SignalDecoderTests`/`PositionTrackerTests`/`AudioPipelineTests` относятся к несуществующим классам (F47) — annotated `⚠ proposed`. Всего **199 проблем** (F01–F199). Полные описания: `refactoring-plan/F45.md`..`F52.md`, `F65.md`..`F199.md`.

## 1. Testing Overview

| Test Type | Coverage Target | Current | Priority |
|-----------|-----------------|---------|----------|
| Unit Tests | >60% | 0% | High |
| Integration Tests | >80% | 0% | High |
| Manual Testing | 100% | Partial | Medium |
| Performance Tests | Baseline + 10% | None | Medium |

## 2. Test Architecture

```
Tests/  [⚠ proposed — F22: no test project exists yet]
├── Unit/
│   ├── Data/
│   │   ├── BeaconDataTests.cs
│   │   ├── DialConfigTests.cs
│   │   └── ConsolCycleConfigTests.cs
│   │
│   ├── Logic/
│   │   ├── PositionFixCalculatorTests.cs
│   │   ├── MorseAlphabetTests.cs
│   │   ├── MorseTimingTests.cs
│   │   ├── NavigationPatternTests.cs
│   │   ├── SonneSignalGeneratorTests.cs
│   │   └── StationSchedulerTests.cs
│   │
│   └── Extensions/
│       └── ComparisonExtensionsTests.cs
│
├── Integration/
│   ├── ⚠ DspPipelineTests.cs  [⚠ proposed — tests CarrierGenerator/Atmosphere/ReceiverDsp nested classes]
│   └── ⚠ AudioPipelineTests.cs [⚠ proposed — AudioPipeline doesn't exist, F47]
│
└── Manual/
    ├── AudioQualityTests.md  [manual checklist, not .cs]
    ├── UITests.md           [manual checklist, not .cs]
    └── GameIntegrationTests.md [manual checklist, not .cs]
```

## 3. Unit Tests

### 3.1 Data Layer Tests

#### BeaconDataTests.cs

```csharp
[TestFixture]
public class BeaconDataTests {
    [Test]
    public void Stations_NotNull_ReturnsArray() {
        var stations = BeaconData.Stations;
        Assert.That(stations, Is.Not.Null);
        Assert.That(stations.Length, Is.GreaterThan(0));
    }
    
    [Test]
    public void Stations_EachHasValidCoordinates() {
        foreach (var station in BeaconData.Stations) {
            Assert.That(station.Latitude, Is.InRange(-90, 90));
            Assert.That(station.Longitude, Is.InRange(-180, 180));
        }
    }
    
    [Test]
    public void Stations_EachHasValidBearingTable() {
        foreach (var station in BeaconData.Stations) {
            var table = station.GetDotCountToBearingTable();
            Assert.That(table, Is.Not.Null);
            Assert.That(table.Length, Is.EqualTo(61));
        }
    }
}
```

#### DialConfigTests.cs

```csharp
[TestFixture]
public class DialConfigTests {
    [Test]
    public void AngleToFrequency_MinAngle_ReturnsMinFreq() {
        float freq = DialConfig.AngleToFrequency(-90f);
        Assert.That(freq, Is.EqualTo(DialConfig.MinFrequency));
    }
    
    [Test]
    public void AngleToFrequency_MaxAngle_ReturnsMaxFreq() {
        float freq = DialConfig.AngleToFrequency(90f);
        Assert.That(freq, Is.EqualTo(DialConfig.MaxFrequency));
    }
    
    [Test]
    public void AngleToFrequency_MidAngle_ReturnsMidFreq() {
        float freq = DialConfig.AngleToFrequency(0f);
        Assert.That(freq, Is.EqualTo(DialConfig.MidFrequency));
    }
    
    [Test]
    public void AngleToFrequency_InterpolatesCorrectly() {
        float freq = DialConfig.AngleToFrequency(-45f);
        Assert.That(freq, Is.InRange(DialConfig.MinFrequency, DialConfig.MidFrequency));
    }
}
```

### 3.2 Logic Layer Tests

#### PositionFixCalculatorTests.cs

```csharp
[TestFixture]
public class PositionFixCalculatorTests {
    [Test]
    public void Calculate_TwoStations_FindsIntersection() {
        var measurements = new List<Measurement> {
            new Measurement { StationLat = 54.79f, StationLon = 9.43f, Bearing = 45f },
            new Measurement { StationLat = 54.09f, StationLon = 8.89f, Bearing = 135f }
        };
        
        var result = PositionFixCalculator.Calculate(measurements);
        
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Latitude, Is.InRange(53f, 56f));
        Assert.That(result.Longitude, Is.InRange(8f, 11f));
    }
    
    [Test]
    public void Calculate_PerpendicularBearings_MinimalError() {
        var measurements = new List<Measurement> {
            new Measurement { StationLat = 54.79f, StationLon = 9.43f, Bearing = 0f },
            new Measurement { StationLat = 54.09f, StationLon = 8.89f, Bearing = 90f }
        };
        
        var result = PositionFixCalculator.Calculate(measurements);
        
        Assert.That(result.ErrorKm, Is.LessThan(5f));
    }
    
    [Test]
    public void Calculate_SameBearing_DegenerateCase() {
        var measurements = new List<Measurement> {
            new Measurement { StationLat = 54.79f, StationLon = 9.43f, Bearing = 45f },
            new Measurement { StationLat = 54.09f, StationLon = 8.89f, Bearing = 45f }
        };
        
        var result = PositionFixCalculator.Calculate(measurements);
        
        Assert.That(result, Is.Not.Null);
        Assert.That(result.ErrorKm, Is.GreaterThan(10f));
    }
    
    [Test]
    public void GetDistanceKm_SamePoint_ReturnsZero() {
        float distance = PositionFixCalculator.GetDistanceKm(
            54.79f, 9.43f, 54.79f, 9.43f);
        Assert.That(distance, Is.EqualTo(0f));
    }
    
    [Test]
    public void GetDistanceKm_KnownDistance_ReturnsCorrect() {
        // Approximate distance between Flensburg and Kiel
        float distance = PositionFixCalculator.GetDistanceKm(
            54.79f, 9.43f, 54.32f, 10.14f);
        Assert.That(distance, Is.InRange(40f, 60f));
    }
}
```

#### MorseAlphabetTests.cs

```csharp
[TestFixture]
public class MorseAlphabetTests {
    [Test]
    public void GetMorse_A_ReturnsCorrect() {
        var result = MorseAlphabet.GetMorse('A');
        Assert.That(result, Is.EqualTo(".-"));
    }
    
    [Test]
    public void GetMorse_Z_ReturnsCorrect() {
        var result = MorseAlphabet.GetMorse('Z');
        Assert.That(result, Is.EqualTo("--.."));
    }
    
    [Test]
    public void DecodeMorse_Roundtrip() {
        string original = "SOS";
        var encoded = MorseAlphabet.EncodeText(original);
        var decoded = MorseAlphabet.DecodeElements(encoded);
        Assert.That(decoded, Is.EqualTo(original));
    }
    
    [Test]
    public void TextToPattern_SOS_CorrectSequence() {
        var pattern = NavigationPattern.TextToPattern("SOS");
        Assert.That(pattern, Is.Not.Null);
        Assert.That(pattern.Elements.Count, Is.GreaterThan(0));
    }
}
```

#### StationSchedulerTests.cs

```csharp
[TestFixture]
public class StationSchedulerTests {
    [Test]
    public void Tick_PausePhase_AdvancesTime() {
        var scheduler = new StationScheduler();
        scheduler.Configure(new ConsolCycleConfig());
        
        scheduler.Tick();
        
        Assert.That(scheduler.ElapsedTicks, Is.EqualTo(1));
    }
    
    [Test]
    public void Tick_CallsignPhase_PlaysCallsign() {
        var scheduler = new StationScheduler();
        scheduler.Configure(new ConsolCycleConfig());
        
        // Advance to callsign phase
        for (int i = 0; i < scheduler.PauseDuration; i++) {
            scheduler.Tick();
        }
        
        Assert.That(scheduler.CurrentPhase, Is.EqualTo(Phase.Callsign));
    }
    
    [Test]
    public void Tick_CompleteCycle_ReturnsToPause() {
        var scheduler = new StationScheduler();
        scheduler.Configure(new ConsolCycleConfig());
        
        // Complete full cycle
        for (int i = 0; i < scheduler.TotalDuration; i++) {
            scheduler.Tick();
        }
        
        Assert.That(scheduler.CurrentPhase, Is.EqualTo(Phase.Pause));
    }
}
```

### 3.3 Sound Layer Tests

#### DspPipelineTests.cs

```csharp
[TestFixture]
public class DspPipelineTests {
    [Test]
    public void GenerateCarrier_SineWave_CorrectFrequency() {
        var carrier = new CarrierGenerator(440f, 44100);
        float sample = carrier.NextSample();
        Assert.That(sample, Is.InRange(-1f, 1f));
    }
    
    [Test]
    public void ApplyAGC_LoudSignal_ReduceGain() {
        var dsp = new ReceiverDsp();
        float[] data = new float[1024];
        for (int i = 0; i < data.Length; i++) {
            data[i] = 0.9f;
        }
        
        dsp.ApplyAGC(data);
        
        Assert.That(data[0], Is.LessThan(0.9f));
    }
    
    [Test]
    public void Atmosphere_Noise_BoundedOutput() {
        var atmosphere = new Atmosphere();
        float[] data = new float[1024];
        
        atmosphere.GenerateNoise(data);
        
        foreach (var sample in data) {
            Assert.That(sample, Is.InRange(-1f, 1f));
        }
    }
}
```

## 4. Integration Tests

### 4.1 DspPipelineTests.cs ⚠ proposed (F22)

```csharp
// ⚠ proposed — CarrierGenerator, Atmosphere, ReceiverDsp are nested classes in DspPipeline.cs
[TestFixture]
public class DspPipelineTests {
    [Test]
    public void GenerateCarrier_SineWave_CorrectFrequency() {
        var carrier = new CarrierGenerator(440f, 44100);
        float sample = carrier.NextSample();
        Assert.That(sample, Is.InRange(-1f, 1f));
    }
    
    [Test]
    public void ApplyAGC_LoudSignal_ReduceGain() {
        var dsp = new ReceiverDsp();
        float[] data = new float[1024];
        for (int i = 0; i < data.Length; i++) {
            data[i] = 0.9f;
        }
        
        // ⚠ Actual API: ReceiverDsp has 1 Process method, not ApplyAGC (F51)
        dsp.Process(data);  // Corrected — actual ReceiverDsp.Process() (F47)
        
        Assert.That(data[0], Is.LessThan(0.9f));
    }
    
    [Test]
    public void Atmosphere_Noise_BoundedOutput() {
        var atmosphere = new Atmosphere();
        float[] data = new float[1024];
        
        // ⚠ Actual API: Apply(sample, sampleRate), not GenerateNoise(data) (F47)
        atmosphere.Apply(data, 44100);  // Corrected signature
        
        foreach (var sample in data) {
            Assert.That(sample, Is.InRange(-1f, 1f));
        }
    }
}
```

### 4.2 StationManagerTests.cs ⚠ proposed (F47)

```csharp
// ⚠ proposed — StationManager doesn't exist (F47). Tests apply to SonneController instead.
// Real test target: SonneController.UpdateStationGains(), StationScheduler.Tick()
```

### 4.3 Integration Test Gap

| Test File | Status | Notes |
|-----------|--------|-------|
| AudioPipelineTests.cs | ⚠ proposed (F47) | AudioPipeline doesn't exist |
| StationManagerTests.cs | ⚠ proposed (F47) | StationManager doesn't exist |
| SignalDecoderTests.cs | ⚠ proposed (F47) | SignalDecoder doesn't exist |
| PositionTrackerTests.cs | ⚠ proposed (F47) | PositionTracker doesn't exist |

## 5. Manual Testing

### 5.1 Audio Quality Tests

| Test | Steps | Expected Result |
|------|-------|-----------------|
| Sonne signal clarity | Tune to beacon, listen | Clear navigation signals |
| Morse callsign | Wait for callsign phase | Audible callsign |
| Volume control | Adjust volume | Smooth volume change |
| Noise atmosphere | Listen to background | Realistic noise |

### 5.2 UI Tests

| Test | Steps | Expected Result |
|------|-------|-----------------|
| Frequency dial | Rotate dial | Frequency changes smoothly |
| Zone selector | Click zone buttons | Zones switch correctly |
| Ground toggle | Click ground button | Ground effect toggles |
| Information panel | Open panel | Station info displayed |

### 5.3 Game Integration Tests

| Test | Steps | Expected Result |
|------|-------|-----------------|
| Mod loads | Start game | Mod initializes without errors |
| Radio equipment | Open submarine | Radio panel appears |
| Save/Load | Save and load game | Radio state preserved |
| Story integration | Trigger story event | Locale overrides work |

## 6. Performance Tests

### 6.1 Audio Performance

| Metric | Target | Measurement |
|--------|--------|-------------|
| CPU usage (audio thread) | < 5% | Profiler |
| Memory allocation (per frame) | < 1KB | Profiler |
| Latency (audio buffer) | < 50ms | Audio latency test |

### 6.2 UI Performance

| Metric | Target | Measurement |
|--------|--------|-------------|
| Frame rate | > 60 FPS | Unity Profiler |
| UI update time | < 1ms | Profiler |
| Memory usage | < 10MB | Memory profiler |

## 7. Test Data

### 7.1 Test Stations

```csharp
public static class TestData {
    public static BeaconStation[] CreateTestStations() {
        return new[] {
            new BeaconStation {
                Name = "Flensburg",
                Latitude = 54.79f,
                Longitude = 9.43f,
                BearingTable = new int[61]
            },
            new BeaconStation {
                Name = "Kiel",
                Latitude = 54.32f,
                Longitude = 10.14f,
                BearingTable = new int[61]
            }
        };
    }
}
```

### 7.2 Test Measurements

```csharp
public static class TestMeasurements {
    public static List<Measurement> CreateTestMeasurements() {
        return new List<Measurement> {
            new Measurement {
                StationLat = 54.79f,
                StationLon = 9.43f,
                Bearing = 45f
            },
            new Measurement {
                StationLat = 54.32f,
                StationLon = 10.14f,
                Bearing = 135f
            }
        };
    }
}
```

## 8. Test Execution

### 8.1 Test Infrastructure Setup (F22)

```bash
# ⚠ No test infrastructure exists yet (F22, F10)
# Project uses csc.exe (not dotnet build)
# Proposed: NUnit + .NET Framework test project with CSC-compiled references

# Example build+test command (proposed):
# csc.exe /target:library /out:Tests/bin/RadioNavigation.Tests.dll \
#   Tests/*.cs Source/Logic/*.cs Source/Data/*.cs \
#   -r:"path/to/Assembly-CSharp.dll" -r:"path/to/UnityEngine.dll" \
#   -r:"path/to/Harmony.dll" -r:"path/to/nunit.framework.dll"
```

### 8.2 Run Specific Test Category

```bash
# ⚠ Not applicable until test infrastructure is set up (F22)
# Run specific category: nunit-console /where="TestCategory=Unit" Tests/bin/RadioNavigation.Tests.dll
# Generate coverage: OpenCover + ReportGenerator
```

### 8.3 Generate Coverage Report

```bash
# ⚠ Not applicable until test infrastructure is set up (F22)
```

## 9. Test Maintenance

### 9.1 Test Review Process

1. All new code must have tests
2. All bug fixes must have regression tests
3. Tests must be reviewed with code
4. Test coverage must meet targets

### 9.2 Test Debt

| Item | Priority | Effort |
|------|----------|--------|
| Add missing unit tests | High | 4-6h |
| Add integration tests | Medium | 3-4h |
| Add performance tests | Medium | 2-3h |
| Update manual test cases | Low | 1-2h |

---

*Document Version: 2.0*  
*Last Updated: 2026-08-01*  
*Updated: 199 problems cataloged (F01–F199). Test files referencing non-existent classes annotated `⚠ proposed` (F47). Build/run commands updated for `csc.exe` workflow. Actual nested class APIs documented (CarrierGenerator/Atmosphere/ReceiverDsp in DspPipeline.cs).*
