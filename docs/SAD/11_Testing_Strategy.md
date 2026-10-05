# Testing Strategy — RadioNavigation Mod

> **Актуально 2026-10-05:** xunit 2.9 + собственный раннер `Dev/Tests/Runner`,
> **1411 PASS / 0 FAIL**; анализаторы **68 PASS**, SourceCheck **100 PASS**.
> ADR-014 baseline — 182, не текущий счётчик. Количество PASS не равно coverage.
> Runtime-мод проверяется raw csc/PVS через `Tools/build.ps1`, а игра компилирует Source
> самостоятельно. Секции 3.x и 7.x ниже — **исторические иллюстрации NUnit/API,
> не исполняемые тесты текущего проекта**; реальный контракт читать в Tests.

## 1. Testing Overview

| Test Type | Coverage Target | Current | Priority |
|-----------|-----------------|---------|----------|
| Unit Tests | >60% — историческая цель | 1411 PASS, pure logic + DSP; текущий % не измерялся | High |
| Integration Tests | >80% — историческая цель | Offline DSP/контракты + отдельная живая приёмка Unity/игры | Medium |
| Manual Testing | сценарии с явными постусловиями | Partial; lamp/open-frame проверены, hatch/external PSF pending | Medium |
| Performance Tests | baseline сравнимых сценариев | отдельные измерения; общей текущей perf-приёмки нет | Low |

## 2. Test Architecture

Ниже исходная выборка 2026-08-02, не полный список. Сейчас также покрыты
астрономия/прецессия/топоцентрическая Луна, running/triangle fix, snapshot и
layout policy, aim-rate/probe/lune/rim, PIP parent-frame и выбор render route.
Save codec v1 добавил 32 behavioral cases; пять save-мутаций пойманы ожидаемыми
гейтами. Отдельно проверены реальные save/reload/изоляция/drag-hidden-линией
и возврат кампании (`docs/astro-save.md`); это не физический drag/ластик.
Тестовый csproj линкует выбранные Source-файлы со stubs; игровой runtime не
поднимается. Собственный раннер нужен из-за ограничений vstest/testhost на
этой машине, а не потому, что linking Source принципиально запрещает dotnet test.

```
Dev/Tests/  [xunit 2.9 + собственный раннер Dev/Tests/Runner; без Unity-рантайма]
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
└── Runner/Program.cs [самописный запускатель xunit, без vstest/testhost]
```

## 3. Исторические примеры unit-тестов (не копировать как текущий API)

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

## 7. Исторические примеры test data (не текущие fixtures)

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

```powershell
# Единая точка входа: build → SourceCheck/quick → tests → PVS
powershell -ExecutionPolicy Bypass -File Tools/gates.ps1 -Parallel
# Только behavioral tests; закрепляет DOTNET_TieredCompilation=0
powershell -ExecutionPolicy Bypass -File Tools/tests.ps1 -Configuration Release
# Снимок 2026-10-04: Passed: 1379, Failed: 0
dotnet run --project Dev/RadioNavigation.Analyzers.Tests -c Release
dotnet run --project Dev/RadioNavigation.SourceCheck -c Release
```

Этот документационный проход повторил tests **1379/0**, analyzers **68/0** и
полный SourceCheck **98/0** (включая семь гейтов документации). Full build,
Roslynator и PVS относятся к приёмке лампы (§16.25 `docs/plans/SEXTANT_PLAN.md`), не к новому
запуску здесь. Не выдавать прежний full-gate за новый запуск.

### 8.2 Run Specific Test Category

```bash
# Регрессионный прогон Dev/Tests/Runner = весь набор.
# SourceCheck принимает подстроку ИМЕНИ МЕТОДА, не класса:
dotnet run --project Dev/RadioNavigation.SourceCheck -c Release -- Docs_
```

### 8.3 Generate Coverage Report

```powershell
# Custom runner => no vstest => coverlet is out. Process-level coverage:
dotnet tool install -g dotnet-coverage   # once
powershell -ExecutionPolicy Bypass -File Tools/coverage.ps1
# -> ./artifacts/reports/coverage/coverage.cobertura.xml
```
Historical coverage snapshot 2026-09-04 (not measured again in this pass):
only the 70/138 Source files then linked into `Dev/Tests/*.csproj` were
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
4. Покрытие и live-сценарии проверять отдельно; PASS-count не заменяет coverage.

### 9.3 Граница гейтов и мутационный протокол

- SourceCheck clean-reference ловит неверные game API и C# 7.0; структурные
  гейты не доказывают pure-семантику, GPU submission или внешний вид.
- Behavioral tests держат pure-математику; live A/B с положительным стимулом —
  Unity/SRP и presented-картинку. `accepted=true` не доказывает результат ввода.
- Проверять **вызов и аргументы**, не присутствие имени. Мутация считается
  пойманной при падении ожидаемого гейта/теста; постороннее падение не зачёт.
  Чистые baseline до/после, восстановление в finally и чистая пересборка после
  отката обязательны; reused бинарь может ложно подтвердить прежнюю мутацию.
- Полная документационная гигиена — `DocumentationTests`: UTF-8/no BOM/newline,
  ADR uniqueness/table, DOCS paths, registry inventory, SAD class references.
  `Agents_DocStatesCurrentFacts` всё ещё ищет исторический литерал **1135**,
  а не вычисляет 1379: это известная граница теста, не основание оставить старый
  счётчик текущим. В этом проходе код гейтов не менялся. Фильтр с результатом
  Passed: 0 / Failed: 0 не является проверкой; все семь hygiene-гейтов входят
  в полный SourceCheck, а пример Docs_ запускает только три Docs_-метода.
- Живая приёмка и сохранность — `docs/live-diagnostics.md`; не менять защищённые
  сейвы и не объявлять pending-пункт закрытым по конфигурационному snapshot.

### 9.2 Test Debt

| Item | Priority | Effort | Status |
|------|----------|--------|--------|
| Базовая xunit-инфраструктура | High | — | ✅ F22, 2026-08-01 |
| Тесты UI-хендлеров (зависимы от Unity) | Medium | 3-4h | Открыто |
| Тесты циклов Consol 60/240s | Medium | — | Offline покрытие есть (AudioRenderPauseTests, MorseKeyerTests); слуховая live-приёмка отдельно |
| Покрытие (OpenCover/ReportGenerator) | Low | 2-3h | ✅ 2026-09-04: `Tools/coverage.ps1` (dotnet-coverage, без vstest), Source-only 97.0% |
| Manual-чеклисты | Low | — | актуализированы 2026-10-04, часть игровых сценариев остаётся pending |

---

*Document Version: 2.2*

*Last Updated: 2026-10-04 (1379 / 68 / 98; baseline-история в ADR-014).*
