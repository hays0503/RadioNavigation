# Testing Strategy — RadioNavigation Mod

> **⚠️ Baseline (2026-08-19, ADR-014) → актуализировано 2026-08-24:** Инфраструктура тестов РЕАЛИЗОВАНА (F22): xunit 2.9 + собственный раннер (`Tests/Runner`), **263 теста PASS** (`powershell -ExecutionPolicy Bypass -File tests.ps1`; история роста: baseline-182 `8b3021fa` → 232 → 253 → 255 → 257 → 260 → 263, детали в AGENTS.md). `dotnet build -c Release -c Debug` 0 errors. ADR-007 «Deferred» (F45) — полный охват интеграционных/производительных тестов целевой (см. `docs/adr/ADR-014-baseline.md`). Секции 3.x — целевые примеры (могут не совпадать с `Tests/`).

## 1. Testing Overview

| Test Type | Coverage Target | Current | Priority |
|-----------|-----------------|---------|----------|
| Unit Tests | >60% | 263 PASS (baseline ADR-014 → рост 2026-08-22/23; core logic + DSP без Unity) | High |
| Integration Tests | >80% | Частично (DspPipeline, StationScheduler) | Medium |
| Manual Testing | 100% | Partial (см. Фазу live-валидации) | Medium |
| Performance Tests | Baseline + 10% | None | Low |

## 2. Test Architecture (реализовано, 2026-08-02)

```
Tests/  [xunit 2.9 + собственный раннер Tests/Runner; без Unity-рантайма]
├── AudioRenderPauseTests.cs       ├── MorseKeyerTests.cs
├── AudioSynthesisEngineTests.cs   ├── MorseTimingTests.cs
├── AstroMathTests.cs              ├── NavigationPatternTests.cs
├── BeaconDataTests.cs             ├── PanelStateStoreTests.cs
├── ChronometerCursorPolicyTests.cs├── PlayerPositionProviderTests.cs
├── ChronometerMapDragMathTests.cs ├── RadioNavDiagnosticsTests.cs
├── ComparisonExtensionsTests.cs   ├── ShieldTests.cs
├── ConsolCycleConfigTests.cs      ├── SignalRichTextFormatterTests.cs
├── DialConfigTests.cs             ├── SignalTypesTests.cs
├── DspPipelineTests.cs            ├── SonneBeamTests.cs
├── GameContractTests.cs           ├── SonneSignalGeneratorTests.cs
├── GeoMathTests.cs                ├── StarCatalogTests.cs
├── LighthouseMarkerSizingTests.cs ├── StationSchedulerTests.cs
├── LighthouseProfileTests.cs      ├── StoryDecisionGatewayTests.cs
├── LighthouseVisualModelTests.cs  └── TransformHelpersTests.cs
├── MorseAlphabetTests.cs
├── Helpers/    [WavWriter, KeyingEnvelope — golden-audio рендер]
├── Stubs/      [UnityEngineMathf/Debug/Mathf/Transform/Vector2/PlayerPrefs, GameStubs]
└── Runner/Program.cs [самописный запускатель xunit (dotnet test неприменим:
                      тесты подключают исходники мода напрямую)]
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

### 4.1 DspPipelineTests.cs ✅ реализовано

Покрывает `CarrierGenerator`/`Atmosphere`/`ReceiverDsp` (вложенные классы DspPipeline): фаза несущей [0,1), AGC, границы шума.

### 4.2 Статус интеграционных классов (F47)

| Test File | Status | Notes |
|-----------|--------|-------|
| AudioPipelineTests.cs | ✘ отклонено (F47) | AudioPipeline не существует |
| StationManagerTests.cs | ✘ отклонено (F47) | StationManager не существует |
| SignalDecoderTests.cs | ✘ отклонено (F47) | SignalDecoder не существует |

Реальные цели интеграции: `SonneController.UpdateStationGains()`, `StationScheduler.Tick()`, `AudioSynthesisEngine.Tick()` — покрыты соответствующими тестовыми файлами (насколько возможно без Unity-рантайма).

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

### 8.1 Test Infrastructure (F22 ✅ реализовано)

```bash
# xunit + самописный раннер (dotnet test неприменим: тестовый проект линкует
# исходники мода напрямую, без сборки игровой DLL)
powershell -ExecutionPolicy Bypass -File tests.ps1
# ожидаемый результат: "Passed: 263, Failed: 0" (текущий; baseline-история — ADR-014)
```

### 8.2 Run Specific Test Category

```bash
# Фильтр по имени теста — через раннер не поддержан; запускать через
# TestDriven/dotnet test нельзя (см. выше). Регрессионный прогон = весь набор.
```

### 8.3 Generate Coverage Report

```powershell
# Custom runner => no vstest => coverlet is out. Process-level coverage:
dotnet tool install -g dotnet-coverage   # once
powershell -ExecutionPolicy Bypass -File coverage.ps1
# -> ./coverage.cobertura.xml
```
Ceiling: only the 70/138 Source files linked into `Tests/*.csproj` are
reachable (2026-09-04 inventory: Data 12/14, Logic 48/72, UI 8/50,
Sound 2/2). The rest is MonoBehaviour/scene code — live-gate territory.
Measured 2026-09-04: Source-only 7213/7435 lines (97.0%), branch 84%,
65 files instrumented, 20 below 100%, none at zero. Worst:
RadioNavDiagnostics (2/14), StoryDecisionGateway (32/129),
ComparisonExtensions (16/22). No blocking threshold: a global % lies
on a Unity codebase (report only).

## 9. Test Maintenance

### 9.1 Test Review Process

1. All new code must have tests
2. All bug fixes must have regression tests
3. Tests must be reviewed with code
4. Test coverage must meet targets

### 9.2 Test Debt

| Item | Priority | Effort | Status |
|------|----------|--------|--------|
| Базовая xunit-инфраструктура | High | — | ✅ F22, 2026-08-01 |
| Тесты UI-хендлеров (зависимы от Unity) | Medium | 3-4h | Открыто |
| Тесты циклов Consol 60/240s | Medium | 1-2h | Открыто (Фаза 4) |
| Покрытие (OpenCover/ReportGenerator) | Low | 2-3h | Скрипт `coverage.ps1` готов (dotnet-coverage, без vstest); первый прогон не выполнен — нужен shell |
| Обновление manual-чеклистов под live-игру | Low | 1-2h | Открыто |

---

*Document Version: 2.1*  
*Last Updated: 2026-08-24 (263 PASS; baseline-история в ADR-014, tag baseline-182-20260819)*  
*Updated: 263 PASS; статусы F01–F207 в `refactoring-plan/hypothesis/INDEX.md`, закрытие — `refactoring-plan/RESOLVED_LEDGER.md`.*  
