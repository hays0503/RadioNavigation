# Testing Strategy — RadioNavigation Mod

## 1. Testing Overview

| Test Type | Coverage Target | Current | Priority |
|-----------|-----------------|---------|----------|
| Unit Tests | >60% | 0% | High |
| Integration Tests | >80% | 0% | High |
| Manual Testing | 100% | Partial | Medium |
| Performance Tests | Baseline + 10% | None | Medium |

## 2. Test Architecture

```
Tests/
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
│       └── MathExtensionsTests.cs
│
├── Integration/
│   ├── AudioPipelineTests.cs
│   ├── StationManagerTests.cs
│   ├── SignalDecoderTests.cs
│   └── PositionTrackerTests.cs
│
└── Manual/
    ├── AudioQualityTests.cs
    ├── UITests.cs
    └── GameIntegrationTests.cs
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
            Assert.That(station.BearingTable, Is.Not.Null);
            Assert.That(station.BearingTable.Length, Is.EqualTo(61));
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

### 4.1 AudioPipelineTests.cs

```csharp
[TestFixture]
public class AudioPipelineTests {
    [Test]
    public void ProcessAudio_WithStations_ProducesOutput() {
        var pipeline = new AudioPipeline();
        var stations = CreateTestStations();
        float[] data = new float[1024 * 2]; // stereo
        
        pipeline.ProcessAudio(data, 2, stations);
        
        Assert.That(data.Any(d => d != 0), Is.True);
    }
    
    [Test]
    public void ProcessAudio_VolumeControl_RespectsVolume() {
        var pipeline = new AudioPipeline();
        var stations = CreateTestStations();
        float[] data1 = new float[1024 * 2];
        float[] data2 = new float[1024 * 2];
        
        pipeline.OutputVolume = 0.5f;
        pipeline.ProcessAudio(data1, 2, stations);
        
        pipeline.OutputVolume = 1.0f;
        pipeline.ProcessAudio(data2, 2, stations);
        
        float avg1 = data1.Average(Math.Abs);
        float avg2 = data2.Average(Math.Abs);
        
        Assert.That(avg2, Is.GreaterThan(avg1));
    }
}
```

### 4.2 StationManagerTests.cs

```csharp
[TestFixture]
public class StationManagerTests {
    [Test]
    public void SetActiveStation_ValidIndex_SetsStation() {
        var manager = new StationManager();
        
        manager.SetActiveStation(2);
        
        Assert.That(manager.ActiveStationIndex, Is.EqualTo(2));
    }
    
    [Test]
    public void UpdateStationGains_AllStations_ComputesGains() {
        var manager = new StationManager();
        
        manager.UpdateStationGains();
        
        foreach (var station in manager.Stations) {
            Assert.That(station.Gain, Is.InRange(0f, 1f));
        }
    }
}
```

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

### 8.1 Run All Tests

```bash
dotnet test
```

### 8.2 Run Specific Test Category

```bash
dotnet test --filter "Category=Unit"
dotnet test --filter "Category=Integration"
dotnet test --filter "Category=Performance"
```

### 8.3 Generate Coverage Report

```bash
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura
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
