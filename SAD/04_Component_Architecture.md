# Component Architecture — RadioNavigation Mod

## Дополнение 2026-10-05: астронавигация в сейве

`SextantSavePersistence` связывает worksheet с `LoadingInitialization`,
`Loaded` и `SavingStarted`. `SextantSaveData : SceneObject` хранит bounded
`Payload` v1; `SextantSaveSnapshot` / `SextantSaveCodec` проверяют наблюдения,
frozen карточку/результат, калибровку/track/FIX/LOP/скрытие. Restore идёт через
хвост очереди без пересчёта; loading guard и source latch у `NavMapMarkerBase`
сохраняют native drag/ссылки фигур. Контракт и live reload/изоляция —
`docs/astro-save.md`.

## 1. Component Hierarchy

```
RadioNavigation Mod (Source/)
│
├─── Entry Point
│    └── Logic/Main.cs (Harmony patches, mod initialization)
│
├─── UI Components (15 files — flat in Source/UI/)
│    ├── RadioHandler.cs (Main radio panel)
│    ├── ToggleHandler.cs (Power switch)
│    ├── ZoneHandler.cs (Zone selector)
│    ├── GroundHandler.cs (Ground toggle)
│    ├── CursorHandler.cs (Frequency dial)
│    ├── PanelDragHandler.cs (Panel dragging)
│    ├── ExpanderHandler.cs (Panel expand/collapse)
│    ├── DebugAngleHandler.cs (Debug display)
│    ├── InformationPanelHandler.cs (Info display)
│    ├── SignalRichTextFormatter.cs (Signal formatting)
│    ├── SpinnerHandlerBase.cs (Base spinner class)
│    ├── VolumeSpinnerHandler.cs (Volume control)
│    ├── FeedbackSpinnerHandler.cs (Feedback control)
│    ├── RadioInstaller.cs (UI instantiation)
│    └── PrefabNames.cs (Prefab path constants)
│
├─── Logic Components (11 files — flat in Source/Logic/)
│    ├── SonneController.cs (Main controller — God Class)
│    ├── RadioStoryIntegration.cs (Game story/locale patches)
│    ├── StationScheduler.cs (Station cycle state machine)
│    ├── MorseKeyer.cs (Morse timing state machine)
│    ├── NavigationPattern.cs (Consol patterns)
│    ├── NavigationElementType.cs (Enum)
│    ├── SonneSignalGenerator.cs (Signal generation)
│    ├── MorseAlphabet.cs (Morse code data)
│    ├── MorseTiming.cs (Timing calculations)
│    ├── ComparisonExtensions.cs (Float comparison helpers)
│    └── Main.cs (Harmony entry point — listed above as Entry Point)
│
├─── Data Components (5 files — flat in Source/Data/)
│    ├── BeaconData.cs (Station database — 14 stations)
│    ├── DialConfig.cs (Dial calibration — 5 bands)
│    ├── ConsolCycleConfig.cs (Cycle timing)
│    ├── StationState.cs (Runtime state)
│    └── SignalTypes.cs (Signal enums)
│
└─── Sound Components (2 files, 5 classes — flat in Source/Sound/)
     ├── AudioSynthesisEngine.cs (Audio processing)
     └── DspPipeline.cs (DSP utilities — 3 classes: CarrierGenerator, Atmosphere, ReceiverDsp)
```

**NOTE**: Physical directory structure is flat — files are organized by layer in `Source/{UI,Logic,Data,Sound}/` with no subfolders. Subfolder names in the original SAD (RadioUI/, Spinners/, Installation/, Integration/, Core/, Navigation/, Morse/) were conceptual groupings, not physical directories. Removed non-existent classes: GameStrings, TickScheduler, StationManager, SignalDecoder, AudioPipeline, CarrierBank, AudioController (see F47).

**Class dossiers**: детальные справочники по 10 ключевым классам (назначение, API, жизненный цикл, связи, инварианты, тесты) — раздел 10 в конце этой главы.

## 2. Component Responsibilities

### 2.1 Core Components

| Component | Responsibility | Inputs | Outputs |
|-----------|----------------|--------|---------|
| **SonneController** | Orchestrate all subsystems | Unity lifecycle events | Audio data, UI updates |
| **StationScheduler** | Manage station cycle phases | Timer ticks | Phase transitions |
| **MorseKeyer** | Generate Morse timing | Element requests | Sound/silence ticks |

### 2.2 Navigation Components

| Component | Responsibility | Inputs | Outputs |
|-----------|----------------|--------|---------|
| **NavigationPattern** | Generate consol patterns | Station config | Pattern sequences |
| **SonneSignalGenerator** | Calculate bearing tables | Station positions | Dot counts, sectors |

### 2.3 Data Components

| Component | Responsibility | Inputs | Outputs |
|-----------|----------------|--------|---------|
| **BeaconData** | Store station definitions (14) | Static data | Station array |
| **DialConfig** | Map angles to frequencies (5 bands) | Angle value | Frequency value |
| **ConsolCycleConfig** | Define cycle timing | Cycle duration | Timing config |
| **StationState** | Track station runtime state | Update commands | State values |

### 2.4 Sound Components

| Component | Responsibility | Inputs | Outputs |
|-----------|----------------|--------|---------|
| **AudioSynthesisEngine** | Main audio processing loop | Audio buffer | Processed audio |
| **CarrierGenerator** | Generate sine waves | Frequency, phase | Audio samples |
| **Atmosphere** | Generate atmospheric noise | Random state | Noise samples |
| **ReceiverDsp** | Apply AGC and filtering | Raw signal | Filtered signal |

## 3. Component Interfaces

### 3.1 Current Interfaces (Implicit)

```csharp
// SonneController — no formal interface
public class SonneController : MonoBehaviour {
    // Called by Unity
    void Update();
    void OnAudioFilterRead(float[] data, int channels);
    
    // Called by UI
    public void SetFrequency(float freq);
    public void SetVolume(float vol);
    
    // Internal
    private void UpdateStationGains();
    private void UpdateStationNavigation();
    private void ProcessAudio(float[] data, int channels);
}
```

### 3.2 Proposed Interfaces (NOT IMPLEMENTED — see F45/ADR-004)

```csharp
// IStationDataSource — extract from BeaconData
public interface IStationDataSource {
    BeaconStation[] GetAll();
    BeaconStation? GetByName(string name);
}

// IAudioOutput — extract from AudioSynthesisEngine
public interface IAudioOutput {
    float OutputVolume { get; set; }
    void ProcessAudio(float[] data, int channels, StationState[] stations,
        StationScheduler[] schedulers, MorseKeyer[] keyers,
        CarrierGenerator[] carriers, bool radioOn,
        int currentZoneIndex, int sonneZoneIndex);
}

// IMorseDecoder — extract from MorseKeyer (and MorseAlphabet)
public interface IMorseDecoder {
    char[] DecodeElements(int[] elements);
    int[] EncodeText(string text);
}
```

## 4. Component Dependencies

### 4.1 Current Dependencies

```
Main.cs
  └──► RadioStoryIntegration (reflection)
  └──► RadioInstaller (instantiation)

SonneController
  ├──► StationScheduler[] (14 stations)
  ├──► MorseKeyer[] (14 keyers)
  ├──► CarrierGenerator[] (14 carriers)
  ├──► AudioSynthesisEngine (audio)
  ├──► BeaconData (station data)
  ├──► DialConfig (frequency mapping)
  └──► ConsolCycleConfig (timing)

InformationPanelHandler
  ├──► SonneController (data source)
  ├──► ZoneHandler (zone state)
  └──► GroundHandler (ground state)

RadioHandler
  ├──► SonneController (frequency control)
  └──► ToggleHandler (power state)
```

### 4.2 Dependency Issues

| Issue | Severity | Description |
|-------|----------|-------------|
| Circular dependency risk | Medium | UI ↔ Logic tight coupling |
| Static coupling | High | ToggleHandler.IsInteractable used everywhere |
| God object fan-out | High | SonneController depends on everything |
| No abstraction | High | Direct class references, no interfaces |

## 5. Component Metrics

### 5.1 Size Metrics

**NOTE**: Corrected from original SAD — LOC were wrong for multiple components (F50).

| Component | Lines | Methods | Fields | Complexity |
|-----------|-------|---------|--------|------------|
| SonneController | 508 | ~29 | ~20 | Very High |
| InformationPanelHandler | 474 | 20+ | 15+ | Very High |
| RadioStoryIntegration | 381 | 15+ | 8+ | High |
| BeaconData | 373 | 3 | 1 | Low |
| MorseKeyer | 240 | 10+ | 8 | Medium |
| DialConfig | 158 | 3 | 2 | Medium |
| SonneSignalGenerator | 146 | 6 | 0 | Medium |
| AudioSynthesisEngine | 150 | 5 | 8 | High |
| MorseAlphabet | 126 | 5 | 2 | Low |
| StationScheduler | 120 | 8 | 6 | Medium |

### 5.2 Coupling Metrics

| Component | Efferent Coupling | Afferent Coupling | Instability |
|-----------|-------------------|-------------------|-------------|
| SonneController | 12 | 8 | 0.60 |
| InformationPanelHandler | 8 | 2 | 0.80 |
| RadioStoryIntegration | 8 | 1 | 0.89 |
| ToggleHandler | 3 | 6 | 0.33 |
| BeaconData | 1 | 4 | 0.20 |
| DialConfig | 1 | 3 | 0.25 |

## 6. Component Communication Patterns

### 6.1 Event-Based Communication

```csharp
// Current: Static events
public static event Action<float, int> OnFrequencyChanged;  // RadioHandler
public event Action<bool> OnRadioStateChanged;               // ToggleHandler
public event Action<int> OnZoneChanged;                      // ZoneHandler
```

### 6.2 Direct Method Calls

```csharp
// Current: Direct coupling
SonneController.SetFrequency(freq);
SonneController.UpdateStationGains();
SonneController.UpdateStationNavigation();
```

### 6.3 Shared Mutable State

```csharp
// Current: Shared state (problematic)
stations[s].gain = computedGain;          // Audio thread
stations[s].beatFrequency = beat;        // Main thread
stations[s].navigationStartTime = time;  // Both threads
```

## 7. Component Lifecycle

### 7.1 Initialization Order

```
1. Main.OnLoaded() [IUserMod interface]
   │
   2. RadioInstaller.Instantiate()
   │   ├── Instantiate prefab (Canvas + Panel from AssetBundle)
   │   └── Attach UI Handlers
   │
   3. SonneController.Awake()
   │   ├── Load BeaconData (14 stations)
   │   ├── Initialize Stations (14 schedulers, keyers, carriers)
   │   ├── Create Silent Audio Clip
   │   └── Start Navigation Loop
   │
   4. UI Handler.Start()
       ├── Subscribe to Events
       └── Initialize UI State
```

### 7.2 Update Order

```
Each Frame:
1. SonneController.Update()
   ├── UpdateStationGains()
   ├── UpdateStationNavigation()
   └── Update UI references
   
2. UI Handler.Update()
   ├── Process Input
   └── Update Visual State
   
Audio Thread:
1. OnAudioFilterRead()
   └── AudioSynthesisEngine.ProcessAudio()
       └── Single per-sample loop across all 14 stations
           ├── Generate Carrier
           ├── Apply Atmosphere
           └── Apply Receiver DSP
```

## 8. Component Testing

### 8.1 Testable Components (Pure C#)

| Component | Testability | Notes |
|-----------|-------------|-------|
| MorseAlphabet | ✅ High | Pure data, no deps |
| MorseTiming | ✅ High | Pure logic |
| NavigationPattern | ✅ High | Pure data |
| SonneSignalGenerator | ✅ High | Pure math |
| StationScheduler | ✅ High | Pure logic |
| DialConfig | ✅ High | Pure mapping |

### 8.2 Untestable Components (Unity-dependent)

| Component | Testability | Dependencies |
|-----------|-------------|--------------|
| SonneController | ❌ Low | MonoBehaviour, Time.time |
| AudioSynthesisEngine | ⚠️ Medium | Mathf.Clamp (minimal) |
| RadioStoryIntegration | ❌ Low | UnityEngine.Application |
| InformationPanelHandler | ❌ Low | UI, Transforms |

**Note**: 0% test coverage (no test project exists). See F22, F45 (ADR-007 — changed to Deferred).

## 9. Lighthouse Subsystem (добавлено 2026-08-22)

### 9.1 Компоненты

| Компонент | Файл | Ответственность |
|-----------|------|-----------------|
| **LighthousePlacer** | `Source/Logic/LighthousePlacer.cs` | Спавн prefab `lighthouse` как sandbox-сущности (группа публичная, сущность `DisplayInUI=false`/`IsPublic=false`), attach `LighthouseRotator`, связывание с `LighthouseBeamGlow`; статик `LighthouseEntity` для маркера |
| **LighthouseRotator** | `Source/Logic/LighthouseRotator.cs` | Вращение фонарной сборки (`LighthouseRotator` child) на `RotationDegreesPerSecond` |
| **LighthouseBeamGlow** | `Source/Logic/LighthouseBeamGlow.cs` | Свет маяка: HDRPLensFlare (без лимита дистанции в device-режиме, +25% размер), screen-space dot+streak fallback, Spot Light boost, `_RotationOffset` окон; позиция света = `Spot Light` (ADR-028) |
| **LighthouseMapMarker** | `Source/UI/LighthouseMapMarker.cs` | Собственный маркер на карте: world-space canvas под `EntityMarkersManager`, pixel-сайзинг 25→50 px, hover(имя)/click(имя+характеристика), зачистка стейловых игровых маркеров (ADR-027) |
| **StationMapOverlay** | `Source/UI/StationMapOverlay.cs` | Точки станций Consol/Sonne на карте (MapPointUI-дубликаты, без записи в charting DB) |
| **CameraResolver** | `Source/Logic/CameraResolver.cs` | Общий резолвер камеры для маяка и звезды (ADR-018) |

### 9.2 Зависимости

```
LighthousePlacer
  ├──► LighthouseProfiles (данные профилей)
  ├──► LighthouseRotator (attach)
  └──► LighthouseBeamGlow (Initialize)

LighthouseMapMarker
  ├──► LighthousePlacer.LighthouseEntity (позиция)
  ├──► LighthouseProfiles (имя + характеристика для тултипа)
  ├──► EntityMarkersManager (родитель-канвас, масштаб)
  └──► ResourceManager (спрайт IconLighthouse из бандла)

LighthouseBeamGlow
  ├──► CameraResolver (рендер-камера)
  ├──► LighthouseVisualModel (перцепционная модель света)
  └──► SkyVisibilityResolver (туман)
```

### 9.3 Тестируемость

| Компонент | Тестируемость | Примечание |
|-----------|---------------|------------|
| LighthouseVisualModel | ✅ High | Чистая математика (LighthouseVisualModelTests) |
| LighthouseProfile(s) | ✅ High | Данные (LighthouseProfileTests) |
| LighthousePlacer/BeamGlow/MapMarker | ❌ Low | MonoBehaviour + игра |

## 10. Class Dossiers — детальные справочники по классам

> Слито из `docs/dossiers/` 2026-08-23 без сокращений. Досье сгенерированы автоматически · проверять по исходнику.
> **Примечание:** досье созданы на ветке `experiment/menu-video-background`; упоминания `MenuVideoBackground` исторические — класс удалён вместе с веткой в тот же день.

### 10.1 Main
- Source: `Source/Logic/Main.cs`
- Generated: 2026-08-23 · проверять по исходнику

- Назначение: точка входа мода (`IUserMod`) — ставит все Harmony-патчи и запускает DI-инъекцию.
- Также собирает игровую сцену: инстанцирует UI-панель радио из AssetBundle и создаёт рендереры звёздного неба.
- Единственный класс, знающий полный порядок инициализации мода.
- Ключевые ответственности:
  - Pre-flight проверка 5 целевых методов игры и 4 патч-методов до применения любого патча.
  - Применение Harmony-патчей с полным откатом при частичном сбое.
  - Подписка на `SceneEventsListener.OnSceneAwake` / `OnSceneStart` с предварительной отпиской (защита от дублей).
  - Валидация иерархии префаба «Radio» против путей из `PrefabNames`.
  - Создание `StarsMesh`, `ConstellationMesh`, `PlanetRenderer`, `LighthousePlacer` в игровой сцене.
- Публичный API / ключевые члены:
  - `OnLoaded()` — вызывается игрой при загрузке мода; вся работа с Harmony здесь.
  - `[Inject] static ResourceManager resourceManager` — источник префабов (`InstantiatePrefab("Radio")`).
  - Поле `harmony` — экземпляр с id `"UBOAT.Mods.RadioNavigation"`.
  - Статическое поле `loaded` — флаг повторной загрузки мода.
  - Константы: `MainSceneViewportPath = "/GUI/Viewport"`, `RadioRootName = "RadioNavigationPanel"` (F176 — единый источник истины lookup'а GUI).
  - `DumpHierarchy(Transform, string)` — только под `#if DEBUG` (в рантайме игры вырезается).
- Жизненный цикл:
  - Шаг 1, `OnLoaded`: лог версий мода/игры; если это перезагрузка (`loaded == true`) — `UnpatchAll(harmony.Id)` и сброс статики (`RadioStoryIntegration.ResetRuntimeState`, `LighthousePlacer.Reset`, `LighthouseBeamGlow.ResetStatics` (ADR-017), `GameTimeResolver.Reset`).
  - Шаг 2: резолв целей — `RadioStory.ContinueStory` (public instance), `Locale.RetrieveString` (оба оверлоада), `RadioStory.OnDecisionSelected` и `RadioStory.OnStopped` (non-public), плюс статические `InjectT3PLL38Decision` / `OverrideLocaleKey` / `HandleCustomDecisions` / `HideRadioOnStop` из `RadioStoryIntegration`.
  - Шаг 3: цель или патч не найден → `LogError` «Pre-flight failed» без патчинга; иначе циклический патчинг, при исключении — `UnpatchAll(свой id)` (откат только своих патчей, F121/F139).
  - Шаг 4: подписка на события сцен, `loaded = true`.
  - Шаг 5, `OnSceneAwake` (любая сцена): при живом `InjectionFramework.Instance` — `InjectIntoAssembly(Assembly.GetExecutingAssembly())`; иначе `LogError` про permission в Manifest.json.
  - Шаг 6, `OnSceneStart`: меню → `MenuVideoBackground.EnsureRunning(scene)`; не «Main Scene» → выход.
  - Шаг 7: поиск `/GUI/Viewport`, проверка инъекции `resourceManager`, переиспользование существующей панели (`RadioHandler.Instance` или узел `RadioNavigationPanel`) при повторном входе.
  - Шаг 8: `InstantiatePrefab("Radio")`, переименование в `RadioNavigationPanel`, валидация 9 обязательных детей по путям `PrefabNames`.
  - Провал валидации → destroy префаба и выход; либо деградированный запуск при `PlayerPrefs RadioNav.SkipValidation=1`.
  - Затем `RadioInstaller.Instantiate(radio, viewport)`, дозапись `StationMapOverlay` / `LighthouseMapMarker` при отсутствии.
  - Создание `StarSkyRoot`(StarsMesh), `ConstellationRoot`(ConstellationMesh), `PlanetRoot`(PlanetRenderer) — все через `DontDestroyOnLoad`; `LighthousePlacer.EnsureRunning()`; `StarSkyPhase0Probe.EnsureRunning()` только при `PlayerPrefs RadioNav.Phase0Probe=1` (ADR-020).
- Связи:
  - Игра: `UBOAT.Game.Scene.Stories.RadioStory`, `Locale`, `ResourceManager` (DI), `DWS.Common.InjectionFramework`, `SceneEventsListener`.
  - Мод: `RadioStoryIntegration`, `MenuVideoBackground`, `PrefabNames`, `RadioInstaller`, `StationMapOverlay`, `LighthouseMapMarker`, `LighthousePlacer`, `LighthouseBeamGlow`, `GameTimeResolver`, `StarsMesh`, `ConstellationMesh`, `PlanetRenderer`, `StarSkyPhase0Probe`.
- Инварианты и грабли:
  - Порядок жёсткий: Harmony-патчи ставятся в `OnLoaded`, DI происходит позже — на Awake первой сцены.
  - `OnSceneStart` падает в `LogError`, если `resourceManager` ещё null — панель просто не создастся.
  - DI требует permission `Reflection` (или `Injection`) в Manifest.json; сборка инъецируется явно, т.к. имя `radionavigation` не матчится с авто-инъекцией фреймворка.
  - Fallback-и молчаливо понижают функциональность: нет viewport/resourceManager/префаба → warning/error и выход без панели.
  - Повторный вход в сцену → переиспользование существующей панели, а не пересоздание.
  - `UnpatchAll` только со своим id: голый `UnpatchAll()` снёс бы патчи чужих модов.
  - Игра перекомпилирует `Source/*.cs` сама без define `DEBUG` — `#if DEBUG` блоки в рантайме недоступны независимо от конфигурации build.ps1.
- Тесты: прямых юнит-тестов класса нет; контракт игры (иерархия `RadioStory`/`Story`, сигнатуры для pre-flight) пинают `Tests/GameContractTests.cs`. Полный набор — `powershell -ExecutionPolicy Bypass -File tests.ps1`.

### 10.2 SonneController
- Source: `Source/Logic/SonneController.cs`
- Generated: 2026-08-23 · проверять по исходнику

- Назначение: центральный MonoBehaviour панели Sonne/Consol — владеет массивами станций (`stations`, `schedulers`, `keyers`, `carriers` по `BeaconData.Stations`) и связывает главный поток (планирование, навигация, диагностика) с аудио-потоком через `AudioSynthesisEngine`. Также декодирует морзе-позывной и копит буфер сигнала для UI.
- Ключевые ответственности:
  - Создание движка и всех станций в `Awake`: конфиг цикла (`ConsolCycleConfig.GetForCycleDuration`), начальный паттерн `SonneBeam.BuildPattern`, подписка событий кеyеров.
  - Прокачка симуляции в `Update()`: опрос позиции игрока через `IPlayerPositionProvider` (по умолчанию `GamePlayerPositionProvider`, порог сдвига 0.0001°), пересчёт усилителей каждые 16 мс, навигации каждые 200 мс (`NavUpdateInterval`), затем `audioEngine.UpdateStations(...)`.
  - Расчёт усиления по резонансной кривой: `gain = 1/sqrt(1 + QF²·ratio²)`, `ratio = f/f0 − f0/f`; выбор доминантной станции (порог 0.01); очистка буфера при смене доминанты.
  - Навигационная логика: пеленги через `GeoMath`, `SonneSignalGenerator.CalculateInBeam/CalculateSectorNumber`, при смене пеленга >0.2° обновляет `CurrentConsolPattern` планировщика из `SonneBeam.TryGetNavigationPattern`.
  - Декодер позывного: буфер точек/тире, сравнение длины с ожидаемой (`MorseAlphabet.GetMorse`), несовпадение даёт `?`; хранится только последний позывной.
  - Буфер сигнала: `signalEntries` из `SignalEntry`, обрезка через `SignalRichTextFormatter.TrimEntries/TrimDecodedText` (лимит `MaxSignalEntries`); разделитель-пробел на конец последовательности.
- Публичный API:
  - `UpdateTuning(frequencyKHz, zoneIndex)` — частота/зона + пересчёт усилителей.
  - `UpdatePosition(lat, lon)` — позиция игрока, триггерит навигацию; NaN/Infinity отклоняются.
  - `GetSignalRichText()` / `GetDecodedText()` / `ClearSignalBuffer()` — выдача и сброс буфера для UI.
  - `GetReceivedNavigationDotCount()` / `GetReceivedNavigationDashCount()` — точки/тире в окне ≤20 символов (не CAP-счётчики).
  - `GetStationDiagnostics(index)` → struct `StationDiagnostics` (фаза, сектор, DotCount/EqSignIndex, пеленги, NavigationElapsed).
  - Свойства `StationCount`, `DominantStationIndex`, `IsInSonneZone`; статические `CalculateBearing`/`CalculateMapBearing` (делегируют `GeoMath`).
- Жизненный цикл Unity / аудио-поток:
  - `[RequireComponent(typeof(AudioSource))]`; в `Awake` на источник ставится зацикленный тихий клип (`silentClip`, 2 сек) и `Play()` — так драйвится `OnAudioFilterRead`.
  - `OnAudioFilterRead` выполняется на аудио-потоке и лишь транслирует в `audioEngine.ProcessAudio(...)`; флаг `suppressAudioForTimeCompression` — `volatile bool` (ускорение времени >1.01 глушит звук; пауза — нет, тон продолжает звучать).
  - `OnDestroy`: сначала `cachedAudioSource.Stop()` (F89 — рвёт колбэк до разбора объекта), затем `audioEngine.Reset()` (F115), потом отписка всех событий (RadioHandler, спиннеры, кеyеры).
- Связи:
  - Владеет и двигает: `AudioSynthesisEngine`, `StationScheduler[]`, `MorseKeyer[]`, `CarrierGenerator[]`, `StationState[]`.
  - Вызывает: `SonneBeam` (Initialize/BuildPattern/TryGetNavigationPattern), `SonneSignalGenerator`, `BeaconData`, `ConsolCycleConfig`, `MorseAlphabet`, `SignalRichTextFormatter`, `GeoMath`, `ToggleHandler.IsRadioOn`, `RadioHandler.OnFrequencyChanged`.
  - Его ставят: инстанцируется модом как префаб панели; спиннеры `VolumeSpinnerHandler`/`FeedbackSpinnerHandler` (дети в иерархии) дергают `OnVolumeChanged`/`OnFeedbackChanged` (последний также `audioEngine.ApplyFeedback`).
- Паттерны и идиомы:
  - Кешированные делегаты обработчиков (`keyerElementHandlers`/`keyerSequenceHandlers`) для корректной отписки; захват индекса станции замыканием.
  - Кеш `cachedAudioSource` (F178) — `OnDestroy` не ходит повторно по графу компонентов.
  - Предзаданные ёмкости списков/билдеров (F120) — без аллокаций роста на горячем пути; `StringBuilder` для декодера.
  - Guard-хелперы: `IsInvalidStationIndex`, `IsProcessingDominantStation`, `IsStationNavigating`, `TryGetCallsignFromHistory` (F87/F66 — фейковый позывной не выдумывается).
- Инварианты и грабли:
  - Всё мутирующее состояние станций трогается только в `Update`/обработчиках главного потока; аудио-поток читает готовые поля (volatile у ключей внутри `MorseKeyer`).
  - Нулевая/NaN частота (F09/F58) обнуляет все усиления вместо деления на ноль; вне зоны Sonne (`currentZoneIndex != 1`) усиления тоже нулевые.
  - `Time.timeScale == 0` (пауза) останавливает `Update`, но `OnAudioFilterRead` продолжает звать `ProcessAudio` — текущий тон договаривает; это осознанно (комментарий у поля suppress).
  - Лог диагностики пеленгов пишется однократно и только при смещении позиции (антифлуд); сверяет map- и true-пеленги, взаимно-обратные дельты и дистанцию `GeoMath.HaversineKm`.
  - `NavigationElapsed` в диагностике считается только когда фаза Navigation и `NavigationStartTime > 0`; `EqSignIndex` — фактическая позиция эквизонала после коррекции потерянных знаков.
- Тесты: прямого `SonneControllerTests` нет. Формула усиления закреплена в `ShieldTests` (дублирует SonneController.cs:507); поведение движка под ним — `AudioSynthesisEngineTests`, `AudioRenderPauseTests`; компоненты — `MorseKeyerTests`, `StationSchedulerTests`, `SonneBeamTests`, `SonneSignalGeneratorTests`.

### 10.3 AudioSynthesisEngine
- Source: `Source/Sound/AudioSynthesisEngine.cs`
- Generated: 2026-08-23 · проверять по исходнику

- Назначение: не-Unity движок синтеза радиосигнала Sonne — сводит несущие всех станций, атмосферный шум и DSP-тракт приёмника в выходной буфер, а на главном потоке продвигает планировщики и кеyеры 50-мс логическими тиками. Является точкой соблюдения контракта main-thread/audio-thread (F01/F29).
- Ключевые ответственности:
  - `UpdateStations(...)`: главный поток; аккумулятор `tickAccumulator` (double, F94 — float уплывал бы за часы) копит время и срабатывает каждые `TickIntervalSeconds` = 0.05 с; в тике по каждой станции с ненулевым Gain: неактивному кеyеру грузится consol-паттерн либо последовательность + ровно один `sched.Tick()` (F98), активному — `Advance()`.
  - Штамп `NavigationStartTime = currentTime` на переходе фазы PreNavPause → Navigation (F13 — время берётся с главного потока, никогда `Time.time` из аудио).
  - Pitch-split (`UpdateCarrierPitch`, static): точка выше бита (`+DotPitchOffsetHz`), тире ниже (`−DashPitchOffsetHz`), эквизонал/тон — на базовом бите; станции без `BeatFrequency` сохраняют ручную частоту несущей.
  - `ProcessAudio(...)`: аудио-поток; посэмплово суммирует `Gain · (dash ? DashGainBoost : 1) · carrier.NextSample()` (фаза несущей крутится даже при молчащем кеyере), затем `atmosphere.Apply` → `dsp.Process` → размножение по каналам × `OutputVolume`.
  - `ApplyFeedback(angle)`: Feedback = angle/140 (clamp01); из него QualityFactor (base + Feedback²·perUnit), пол шума (lerp 0.004→0.0005), вероятность импульсов (0.002→0.0002), уровень свиста выше Feedback 0.75.
- Публичный API:
  - `UpdateStations(deltaSeconds, currentTime, stations, schedulers, keyers, carriers, radioOn)` — тик симуляции.
  - `ProcessAudio(data, channels, stations, keyers, carriers, radioOn, currentZoneIndex, sonneZoneIndex)` — заполнение буфера.
  - `ApplyFeedback(angle)` / свойство `OutputVolume` (clamp 0..1) / `QualityFactor`.
  - `Reset()` — чистит Atmosphere, ReceiverDsp и аккумулятор (F115); доступ `Atmosphere`/`Dsp` только для чтения.
  - Константы: `SampleRate = 44100`; свойства `Feedback { get; private set; }`.
- Жизненный цикл Unity / аудио-поток:
  - Создаётся в `SonneController.Awake` как обычный объект; сам колбэков Unity не имеет.
  - Main thread: `UpdateStations`, `ApplyFeedback`, `OutputVolume` set, `Reset` (последний — из `OnDestroy` контроллера).
  - Audio thread (из `SonneController.OnAudioFilterRead`): `ProcessAudio` строго read-only — читает volatile-флаги `MorseKeyer.KeyOn/CurrentElementIsDash`, поля `StationState.Gain/Frequency`, никаких мутаций планировщиков/кеyеров и никакого UnityEngine API (комментарий над методом).
  - Гейты выхода с очисткой буфера `Array.Clear`: неверная зона или радио выключено.
  - Буст тиры: множитель `Constants.DashGainBoost` применяется только к слышимому семплу, не к фазе несущей.
- Связи:
  - Его ставят: создаёт и дергает `SonneController` (`Update` → `UpdateStations`, `OnAudioFilterRead` → `ProcessAudio`, спиннеры → `ApplyFeedback`/`OutputVolume`).
  - Двигает: `MorseKeyer.Load/LoadConsol/Advance`, `StationScheduler.GetCurrentSequence/GetCurrentConsolPattern/Tick/OnSequenceFinished`, `CarrierGenerator.Frequency` (pitch-split) и `NextSample()`.
  - Обёртывает: `Atmosphere` (шум/импульсы, сид `Environment.TickCount`) и `ReceiverDsp(SampleRate)` (QSB, AGC, фильтры); конфиг — `Constants.*`.
- Паттерны и идиомы:
  - Fixed-timestep аккумулятор поверх переменного `deltaSeconds` — детерминизм тиков вне кадровой частоты.
  - Data-oriented проход: параллельные массивы станций вместо списков объектов, ранний skip по `Gain.IsNegligible(0.001f)`.
  - Extension-guard'ы (`IsNotNull`/`IsNull`/`IsZero`/`IsPositive`/`IsNegligible`) и `Mathf.Clamp*` на входах.
- Инварианты и грабли:
  - Разделение потоков держится дисциплиной, а не локами: всё мутируемое — только main thread; аудио читает volatile-флаги кеyера. Добавление записи в `ProcessAudio` — регрессия F01/F29.
  - Ровно один `sched.Tick()` за логический тик и только когда кеyер неактивен (F98) — иначе фазы уезжают относительно паттерна.
  - Pitch-свитч выровнен по границам элементов (тики), свитч не попадает в середину тона.
  - `radioOn=false` останавливает и симуляцию (F132), и вывод; но `Time.timeScale=0` (пауза) глушит лишь через флаг контроллера, тон договаривает.
  - `Feedback` пересчитывает сразу четыре параметра — менять их поодиночке извне нельзя, они согласованы этой функцией.
  - Станция с нулевым гейном пропускается целиком — и в тике симуляции, и в посэмпловом суммировании; фаза несущей при молчащем кеyере всё равно крутится (`NextSample()` без сложения), чтобы тон начинался не с нуля фазы.
- Тесты: `AudioSynthesisEngineTests` (8): штамп NavigationStartTime, полная длительность PreNavPause, вход в Callsign за один тик, пауза планировщика при выключенном радио, очистка буфера (радио off / чужая зона), сигнал при KeyOn, pitch-split dot/dash. Сквозной golden-рендер конверта с паузой — `AudioRenderPauseTests`.

### 10.4 MorseKeyer
- Source: `Source/Logic/MorseKeyer.cs`
- Generated: 2026-08-23 · проверять по исходнику

- Назначение: конечный автомат морзе-ключа станции — прогоняет последовательность элементов (точка/тире/эквизонал) или consol-паттерн, выставляя звуковые состояния для аудио-потока. Единственный мост между планировщиком фаз (`StationScheduler`) и синтезом звука.
- Ключевые ответственности:
  - Состояния `Idle → ElementOn → ElementSilence` (+ `Tone` для эквизонала в consol-паттерне); тикание через `Advance()` по 50 мс логическим тикам движка.
  - Два взаимоисключающих источника элементов: `IKeySequence sequence` (позывной/навигация) и массив `consolPattern`; загрузка через `Load(...)` / `LoadConsol(...)`.
  - Публикация в аудио-поток только volatile-флагов: `keyOn`, `isActive`, `currentIsDash`.
  - Тайминги берутся из статического `MorseTiming` с учётом фазы Callsign (укороченный профиль позывного) и `ConsolCycleConfig`.
- Публичный API:
  - `Load(IKeySequence, scheduler, config)` / перегрузка `Load(NavigationPattern, ...)` — начать новую последовательность; F65: затирает устаревший consol-паттерн.
  - `LoadConsol(NavigationElementType[], scheduler, config)` — консольный цикл; `sequence = null` (F175).
  - `Reset()` — Idle, сброс обоих источников (F165), `isActive=false`.
  - `Advance()` — один логический тик автомата; при `!isActive` сразу выход (планировщик тикает вызывающий код, F01/F98).
  - Свойства чтения: `KeyOn`, `IsActive`, `IsTone`, `CurrentElementIsDash`, `IsCallsignElement`.
  - События: `OnElementCompleted(NavigationElementType, bool isCallsign)`, `OnSequenceFinished`.
- Жизненный цикл Unity / аудио-поток:
  - Класс не-Unity (чистый C#), живёт в массиве `MorseKeyer[]` у `SonneController`.
  - Main thread: `Load*`/`Reset`/`Advance` зовёт только `AudioSynthesisEngine.UpdateStations` (когда кеyер активен) и `SonneController` при инициализации.
  - Audio thread: читает исключительно volatile `keyOn`/`currentIsDash`/`isActive` — без локов, torn reads исключены волатильностью (F01/F29); запись этих флагов идёт из `SetState`/`StartElement` на главном потоке.
- Связи:
  - Вызывается: `AudioSynthesisEngine.UpdateStations` (`Load`/`LoadConsol`/`Advance`), `SonneController.Awake` (создание, подписка событий).
  - Вызывает: `StationScheduler.GetCurrentSequence/GetCurrentConsolPattern/OnSequenceFinished` (подбор следующего паттерна), `MorseTiming.Get*Ticks(config)`, события слушает `SonneController.OnStationElementCompleted/OnStationSequenceFinished`.
  - Данные: `NavigationPattern`/`IKeySequence`, `NavigationElementType`, `ConsolCycleConfig`, `StationScheduler.Phase.Callsign`.
- Паттерны и идиомы:
  - Явный enum-автомат с счётчиком `ticksInState` вместо таймеров — детерминизм и тестируемость вне Unity.
  - События как шина завершения элемента/последовательности наверх; кеyер сам не решает, что играть дальше, а спрашивает планировщик.
  - Extension-guard'ы `IsNotNull`/`IsPositive`/`IsNull` по всему файлу.
  - Перегрузка `Load(NavigationPattern, ...)` — тонкая обёртка над основной версией через явное приведение к `IKeySequence`.
- Инварианты и грабли:
  - Ровно один источник активен: вход в consol чистит `sequence` и наоборот (F175); `Load` чистит stale-паттерн (F65), `Reset` чистит оба (F165) — иначе старый паттерн «побеждал» новый.
  - Без планировщика конец паттерна ведёт в Idle (F07): зациклить нечего — следующий паттерн неоткуда взять; ветка с планировщиком либо получает следующий, либо тоже уходит в Idle.
  - `GetCurrentElementType` при выходе индекса за диапазон пишет `LogError` (F114) и возвращает Dot как безопасный fallback — раньше тихий fallback маскировал баги индексации.
  - Эквизонал в `ElementOn` переходит к следующему элементу напрямую, без тишины (сплошной тон); обычная точка/тире — через `ElementSilence`.
  - `SetState` — единственное место записи `keyOn`: звук есть в `ElementOn` и `Tone`; `isActive` пишется вручную рядом (Load/LoadConsol/Reset/ветки Idle).
  - `IsCallsignTiming` требует ненулевой `sequence` И фазу Callsign — consol-паттерн никогда не тикает позывным профилем.
  - `StartElement` выставляет `currentIsDash` до переключения состояния; `Tone` возможен только из эквизонала consol-паттерна, длительность — `MorseTiming.GetToneTicks(config)`.
- Тесты: `MorseKeyerTests` (6): stale-паттерн после Load/Reset (3 теста F65/F165), метка callsign, порядок dash-equisignal-dot в dash-секторе, `CurrentElementIsDash` следует паттерну. Тайминги тиков — `MorseTimingTests`; сквозной прогон ключа через движок — `AudioSynthesisEngineTests` и golden-рендер `AudioRenderPauseTests`.

### 10.5 InformationPanelHandler
- Source: `Source/UI/InformationPanelHandler.cs`
- Generated: 2026-08-23 · проверять по исходнику

- Назначение: диагностическая панель внутри радио-UI — динамически строит список uGUI-лейблов со статусом тумблера, ground, зоны, громкости, feedback, частоты и ближайшей станции, а также rich-text сигналом, декодированным морзе и постанционными паттернами Сонне. Единственный потребитель состояния почти всех хендлеров панели.
- Ключевые ответственности:
  - Построение UI в `Start`: путь `InformationPanel/ScrollView/Viewport/Content`; при отсутствии — `enabled = false`.
  - `EnsureLayout` ставит `VerticalLayoutGroup` + `ContentSizeFitter`; далее 7 статус-лейблов (toggle/ground/zone/volume/feedback/frequency/station), два разделителя, signal (cyan, richText), decoded, заголовок паттернов и массив `patternTexts` длиной `BeaconData.Stations.Length` (высота 60, overflow).
  - Шрифты: Arial из OS создаётся один раз (кэш), фолбэк на builtin `LegacyRuntime.ttf`.
  - Тикеры `Update`: `UpdatePatterns` каждые 0.5 c, `UpdateSignalDisplay` каждые 0.15 c.
  - Отображение частоты: `DialConfig.FormatFrequency` + id диапазона из `DialConfig.Bands`.
  - Поиск ближайшей станции только для зоны 1: перебор `BeaconData.Stations`, ближайшая по `history[0].FrequencyKHz` с порогом `Constants.StationMatchDeltaKHz`; в подписи — гауссово «усиление» от дельты частоты.
  - Паттерны: вне зоны Сонне всё чистится; доминантная станция получает суффикс `[фаза]` и время навигации.
  - Каждая слышимая строка — callsign, IN BEAM/OUTSIDE, gain %, BFO, фаза, ToBoat°; вторая строка — Zone:S(DASH/DOT), EQ-индекс, dots/dashes, Bearing°.
  - Доминанте добавляются счётчики принятых •/▬; зелёный цвет в луче, серый вне; неиспользованные строки чистятся.
  - Сигнал: `GetSignalRichText` в signal-лейбл (серый при пустом/вне зоны), `GetDecodedText` в decoded; смена декода логируется один раз.
- Публичный API:
  - Внешнего API нет: все методы приватные, класс целиком внутренний.
  - Подписки на события хендлеров и таймеры обновляют только собственные Text-поля панели.
- Жизненный цикл Unity:
  - `Start`: построение → `FindHandlers` → `Subscribe` → `RestorePanelState` → `RefreshAll` (начальное состояние прогоняется через те же обработчики).
  - `RestorePanelState`: `PanelStateStore.Load` → `volumeSpinner.SetAngle`, `feedbackSpinner.SetAngle`, `zoneHandler.SetZone`, `radioHandler.CursorAngle = cursor` — восстановление через обычный пайплайн (F15).
  - `Update`: два независимых таймера на `Time.deltaTime`.
  - Подписки: `toggleHandler.OnValueChanged`, `groundHandler.OnValueChanged`, `zoneHandler.OnZoneChanged`, `volumeSpinner.OnValueChanged`, `feedbackSpinner.OnValueChanged`, статическое `RadioHandler.OnFrequencyChanged` — симметричные `Subscribe`/`Unsubscribe`.
  - `OnDestroy`: сначала `PanelStateStore.Save(vol, fb, zone, cursorAngle)`, затем `Unsubscribe()` — утечки подписок нет даже при пропуске Save.
- Связи:
  - Инсталлируется `RadioInstaller.EnsureComponent<InformationPanelHandler>(radio)`, живёт на корневом GO радио.
  - Читает: `ToggleHandler` (+ кэшированный `UnityEngine.UI.Toggle`), `GroundHandler.IsGroundEffectivelyActive()`, `ZoneHandler.CurrentIndex`, `VolumeSpinnerHandler.Angle`, `FeedbackSpinnerHandler.Angle`, `RadioHandler.CursorAngle`/`OnFrequencyChanged`.
  - Диагностика: `SonneController` — `IsInSonneZone`, `DominantStationIndex`, `GetStationDiagnostics`, `GetSignalRichText`, `GetDecodedText`, счётчики принятых навигационных точек.
  - Данные: `BeaconData.Stations`, `DialConfig`, `Constants`, `PanelStateStore`.
- Паттерны и идиомы:
  - Push-модель: каждое событие хендлера сразу перерисовывает один лейбл; pull-модель для быстрых данных (паттерны/сигнал) — по таймеру.
  - Кэш вместо повторных GetComponent (Toggle, шрифт) — пометки F178/F103.
  - Единая точка входа значений: `RefreshAll` переиспользует те же On*-обработчики, что и события.
- Инварианты и грабли:
  - Отсутствие любого хендлера — только LogError, панель продолжает работать с тем, что нашла (все обращения null-guarded).
  - Доминантная станция −1 не должна показывать `[Pause]` несуществующей станции — ветка F101.
  - DEBUG-лог диагностики живёт внутри else-ветки доминанта (фикс F88: раньше ломал DEBUG-сборки).
  - `FindClosestStation` осмыслен только для зоны 1 — в остальных зонах имя станции пустое.
- Тесты: `PanelStateStoreTests` покрывает сохранение/загрузку операторского состояния (используется в `RestorePanelState`/`OnDestroy`); сам класс напрямую юнит-тестами не покрыт.

### 10.6 ChronometerOverlayHandler
- Source: `Source/UI/ChronometerOverlayHandler.cs`
- Generated: 2026-08-23 · проверять по исходнику

- Назначение: удерживает нативный хронометр игры (F7, `ShipControlUI`) видимым поверх радио-панели на общем screen-space canvas — при открытии радио поднимает контейнер хронометра над панелью, при закрытии возвращает как было. Добавляет масштабирование ×2.5 по клику и перетаскивание развёрнутого циферблата по карте (M) с записью позиции в нативный оверрайд. Управляет курсором над зонами хронометра через чистую политику.
- Ключевые ответственности:
  - Резолв нативного бриджа: `ShipControlUI` из `InjectionFramework` либо fallback `FindObjectsOfTypeAll` с фильтром сцены/hideFlags; ленивый кэш всех ссылок.
  - Рефлексия приватных членов игры: поля `chronometerToolDragContainer`, `chronometerButton`, `chronometerToolDefaultPos`, `chronometerToolDragState`, статический `uiDialsDataHolder`, свойство интерфейса `IUserInterfaceDialsDataHolder.ChronometerPosOverride`, метод `ApplyToolPositionLimits`, поле `CursorManager.normalCursorTex`.
  - Поднятие/возврат слоя: добавляет на контейнер `Canvas` + `GraphicRaycaster` + `ChronometerDragShield` (с created-флагами), `sortingOrder = parentCanvas.sortingOrder + 100`, репарентит контейнер под себя.
  - Захват-восстановление canvas-state, парента, sibling, default position, масштаба; при null parentCanvas — fallback `MoveRadioBehindNativeUi`.
  - Два драга: локальный поднятого лица (`BeginLocalDrag`/`DragLocal`/`EndLocalDrag`) и мап-драг развёрнутого (`OnMapDragBegin/Delta/End`) с блокадой `GameInput`.
  - Позиция развёрнутого лица пишется в нативный оверрайд раз в 30 кадров и в конце жеста: `ChronometerMapDragMath.ToNormalizedOverride` + клэмп нативным `ApplyToolPositionLimits`.
  - Процедурные курсоры-стрелки 32×32 (линии Брезенхэма), hotspot (16,16).
- Публичный API:
  - `bool IsChronometerElement(Transform)` — трансформ внутри контейнера хронометра.
  - `internal SetScaleHover/SetMoveHover(bool)` — вход от сенсоров; игнор при активном локальном драге.
  - `internal BeginLocalDrag(PointerEventData)/DragLocal/EndLocalDrag` — цикл драга поднятого лица.
  - `internal OnMapDragBegin()/OnMapDragDelta(Vector2)/OnMapDragEnd()` — мап-драг; только при `chronometerExpanded && !chronometerRaised`.
  - `internal OnScaleClicked()`/`ToggleChronometerScale()` — переключение ×2.5, защита по кадру (`lastScaleToggleFrame`).
  - `internal GetDragAreaRect()`, `IsChronometerExpanded`, `IsChronometerRaised`, `IsLocalDragActive` — запросы для спутников.
- Жизненный цикл Unity:
  - Awake/Start нет — всё лениво в `LateUpdate` (`TryResolveChronometer`); scale-control создаётся сразу, т.к. контейнер — нативный сериализуемый ребёнок.
  - `Update`: пока `localDragActive`, каждый кадр давит нативный драг (`CancelNativeDrag`).
  - `LateUpdate`: детект смены `IsChronometerOpen`; F7-fallback тогглит `chronometerButton.isOn`, если игра не переключила сама в этом кадре; `SyncChronometerLayer(IsRadioVisible && isOpen)`.
  - Подписок на события C# нет; DI — статический `[Inject] CursorManager`; `GameInput` лениво из `InjectionFramework`.
  - `OnDestroy`: `EndLocalDrag`; если поднят — полный `RestoreChronometerLayer`; уничтожает только созданные им компоненты и текстуры курсоров.
- Связи:
  - Инсталлируется `RadioInstaller.EnsureComponent<ChronometerOverlayHandler>(radio)` — живёт на GO радио рядом с `RadioHandler`/`PanelDragHandler`.
  - Вызывает: `InjectionFramework`, `ShipControlUI` (рефлексия), `GameInput.AddBlockade/RemoveBlockade`, `CursorManager.SetCustomCursor/SetDefaultCursor`, `PanelDragHandler.SetChronometerPointerInside/SetCursorOverride/ClearCursorOverride`, чистые `ChronometerCursorPolicy.Evaluate` и `ChronometerMapDragMath.ToNormalizedOverride`.
  - Владеет и настраивает `ChronometerDragShield` и `ChronometerScaleControl` через `SetOwner` — они шлют клики/ховеры обратно сюда.
- Паттерны и идиомы:
  - «Чистая политика + переводчик команд»: `RefreshCursor` лишь транслирует `ChronometerCursorCommand` в доступный бэкенд (cursorManager → PanelDragHandler override → сырой `Cursor.SetCursor`).
  - Захват-восстановление: мутируемые поля снимаются один раз (`canvasStateCaptured`, `defaultPositionCaptured`, `scaleCaptured`) и восстанавливаются симметрично.
  - Ленивая резолюция с одноразовыми предупреждениями (`resolveWarningLogged`) — без спама лога.
- Инварианты и грабли:
  - Drag state: нативное `chronometerToolDragState` принудительно сбрасывается в enum-0 из Update, каждого шага и конца драга — иначе `HandleDialDragging` игры борется за тот же жест; попутно снимается блокада `shipControlUI`.
  - Pointer gating: `BeginLocalDrag` отвергает нажатия над `Selectable` и требует `chronometerRaised && IsRadioVisible`; мап-драг требует `chronometerExpanded && !chronometerRaised` — режимы взаимоисключающие.
  - GameDefault обязан идти через `ApplyGameDefaultCursor`: чужие `UnsetCustomCursor` оставляют режим Default при живой кастомной текстуре, guarded `SetDefaultCursor` выйдет рано — текстуру приходится форсить напрямую.
  - Ховер не сбрасывается покадрово (на карте убил бы подсказку); восстановление точечное через `ResetChronometerScale`.
  - Рефлексия молча вернёт false при переименовании полей игры — искать префикс `[RadioNav][Chronometer]` в логе.
- Тесты: `ChronometerCursorPolicyTests` (двухзонная политика, emit-only-on-change) и `ChronometerMapDragMathTests` (нормализация PosOverride, ADR-034); сам обработчик напрямую не покрыт — тонкая связка с Unity/игрой.

### 10.7 StationMapOverlay
- Source: `Source/UI/StationMapOverlay.cs`
- Generated: 2026-08-23 · проверять по исходнику

- Назначение: отладочный оверлей для карты, открываемой по M, — рисует транзиентные маркеры радиостанций из `BeaconData.Stations`, используя родной префаб точки игры, но без регистрации фиксированных станций в сохраняемой базе картографии. Маяк здесь не рисуется — у него отдельный `LighthouseMapMarker`.
- Ключевые ответственности:
  - Резолв нативных ресурсов картографии рефлексией: непубличные поля `MapChartingUI.pointPrefab` (тип `MapPointUI`) и `MapChartingUI.linesContainer`; до готовности — Warning и ретрай по таймеру.
  - Создание маркеров: `Coordinates.ToWorldPosition(lat, lon)` → `MapPointData(position){Name}` → `pointPrefab.Duplicate(data)`; затем переименование в `RadioNavStationMarker_<i>`, `Interactive = false`, `SetParent(linesContainer)`.
  - Пакетное позиционирование: после цикла один вызов `MapPointUI.UpdatePoints(created)`.
  - Самообслуживание в `Update`: если маркеры живы — ежекадровое `MapPointUI.UpdatePoints(markers)`; если уничтожены (выгрузка сцены/пересборка карты) — очистка списка и ретрай создания раз в 0.5 c (`unscaledDeltaTime`).
  - `AreMarkersAlive`: количество равно числу станций и ни один элемент не null — дешёвая проверка вместо отслеживания пересоздания `MapChartingUI`.
  - Одноразовая миграция: `RemoveLegacyMarkers` уничтожает сохранённые в сейв-базе старые маркеры мода — совпадение `point.Name` с именем станции и позиция в пределах 0.05 мировых единиц; собственные префиксованные маркеры пропускаются.
  - Откат при ошибке: исключение в цикле создания уничтожает уже созданные точки и пишет LogError — полусозданный набор наружу не выходит.
- Публичный API:
  - Внешнего API нет: MonoBehaviour с приватным жизненным циклом.
  - Управляется только Unity-событиями (Start/Update/OnDestroy) и событиями `SavesManager`.
- Жизненный цикл Unity:
  - `Start`: `ScriptableObjectSingleton.LoadSingleton<SavesManager>()`; подписка `savesManager.LoadingStarted += OnLoadingStarted`, `savesManager.Loaded += OnLoaded`; первый `TryCreateMarkers()`.
  - `Update`: цикл живости/ретраяев, описанный выше; таймер ретрая копится по unscaled-времени.
  - Сохранения: `OnLoadingStarted` сбрасывает `markersCreated` и отменяет pending-очистку легаси.
  - `OnLoaded(Queue<Action>)` кладёт `OnSaveLoadCompleted` в очередь загрузки игры (или зовёт сразу при null-очереди).
  - После загрузки: `markersCreated = false`, `legacyCleanupPending = true` — свежий сейв мог содержать протухшие маркеры, легаси-чистка выполняется заново перед созданием.
  - `OnDestroy`: отписка обоих событий `SavesManager` + `ClearOwnedMarkers()` — утечки подписок и висячих маркеров нет.
- Связи:
  - Инсталлируется `Main.cs` (`radio.AddComponent<StationMapOverlay>()` при отсутствии компонента).
  - Вызывает: `MapChartingUI` (рефлексия полей + `GetComponentsInChildren<MapPointUI>`), `MapPointUI.Duplicate/UpdatePoints`, `Coordinates.ToWorldPosition`.
  - Подписки: `SavesManager.LoadingStarted`, `SavesManager.Loaded` (через `ScriptableObjectSingleton.LoadSingleton<SavesManager>` в `Start`).
  - Данные: `BeaconData.Stations` (имя, широта, долгота каждой станции).
  - Не трогает маяк — за него отвечает `LighthousePlacer`/`LighthouseRotator`.
- Паттерны и идиомы:
  - Transient-маркеры: ничего не пишется в сохраняемую базу картографии — маркеры пересоздаются и чистятся самим оверлеем.
  - Self-healing polling вместо подписок на жизнь карты: проверка живости дешевле и надёжнее попытки отследить пересоздание `MapChartingUI`.
  - Одноразовые флаги-этапы (`legacyCleanupPending`, `markersCreated`) с переустановкой на границах загрузки сейва.
  - Атомарное создание: сборка в отдельный список `created` с откатом (Destroy) при исключении.
- Инварианты и грабли:
  - Маркер считается «своим» только по префиксу имени `RadioNavStationMarker_` — легаси-детекция опирается на совпадение имени И позиции, чтобы не снести пользовательские точки с тем же названием.
  - Если `MapChartingUI` ещё не готов (префаб/контейнер null) — только Warning и ретрай по таймеру; создание невозможно до инициализации карты игрой.
  - После загрузки сейва маркеры всегда пересоздаются: `OnSaveLoadCompleted` сбрасывает состояние до того, как `Update` заметит расхождение.
  - Рефлексия к `pointPrefab`/`linesContainer` сломается молча при переименовании в игре — диагностикой служат строки `[RadioNav][Map]` в логе.
- Тесты: прямых юнит-тестов нет — класс плотно связан с Unity/игровой картографией; чистой извлечённой логики (как `ChronometerMapDragMath`) в нём нет.

### 10.8 LighthousePlacer
- Source: `Source/Logic/LighthousePlacer.cs`
- Generated: 2026-08-23 · проверять по исходнику

- Назначение: расставляет 3D-префаб маяка для каждого активного `LighthouseProfile` как декоративную sandbox-сущность игры; собственной карты-метки у сущности нет — точку на карте с подписью рисует `StationMapOverlay`. При ненулевой `RotationDegreesPerSecond` в профиле вешает `LighthouseRotator` на дочерний ротатор префаба и инициализирует `LighthouseBeamGlow`. Паттерн «группа+сущность» позаимствован у воркшоп-мода Expanded Ice: `SandboxGroup.Create` + `SandboxEntity.Create` поверх синтетического `EntityType`, собранного из рантайм-`GameDataRow` (префаб резолвится из AssetBundle мода через `ResourceManager`); парентинг в `SceneSpawnContainer` группы держит модель приклеенной к карте при сдвиге плавающего origin сцены.
- Ключевые ответственности:
  - Двухфазный поллинг на каждый профиль: фаза 1 (`TryResolveGroup`) — найти существующую группу или создать новую; фаза 2 (`TryAttachRotator`) — дождаться ротатора под `SceneSpawnContainer` и навесить компоненты.
  - Синтез `EntityType` из 18 колонок листа «Entities/Types» (категория «Structure» сохраняет родной Y префаба), путь префаба под ключом «Prefab Path»; 18-е значение страхует индексные чтения `EntityBlueprint` от null.
  - Позиционирование: `Coordinates.ToWorldPosition(lat, lon)`; после спавна лог с обратной конвертацией в Lat/Lon, высотой огня и скоростью вращения.
  - Самолечение (`HealGroup`): пустая «группа-оболочка» после загрузки сейва либо так и не заспавнившаяся сущность уничтожаются через `group.DestroyGroup()` и пересоздаются под тем же именем.
  - Реаттач при каждом старте Main Scene: игра респавнит префаб без рантайм-компонентов — ротатор и glow навесаются заново поверх переиспользованной группы; у восстановленной группы сбрасываются DisplayInUI/IsPublic (старые сейвы могли включить их → дубль маркера игры).
  - Публикация `LighthouseEntity`; скрытие сущности от игрового `EntityMarkersManager` (DisplayInUI/IsPublic=false) и запрет случайного смещения (`RandomizeSpawnPosition=false`), чтобы не было дубликата «Unknown» и дрейфа позиции.
  - Glow-контроллер живёт вне префаба и перепривязывается к текущему container/rotator при каждом (ре)аттаче — переживает LOD-деактивацию модели и респаун после save/load.
- Публичный API:
  - `EnsureRunning()` — идемпотентно создаёт холдер «Lighthouse Placer», добавляет компонент, наполняет размещения из `LighthouseProfiles.Active()`; список профилей фиксируется в момент создания.
  - `Reset()` — обнуляет статический `instance`; зовёт `Main` при выгрузке сцены.
  - `static SandboxEntity LighthouseEntity` — последняя размещённая сущность; читается `LighthouseMapMarker` для позиционирования метки; при переиспользовании группы указывает на `Entities[0]`.
- Жизненный цикл Unity:
  - Awake/Start/OnDestroy отсутствуют; вся работа в `Update`, целиком обёрнутом в try/catch — исключение одного кадра не убивает цикл.
  - `Update`: ждёт `gameState.CurrentState == Gameplay` (до перехода статики `SandboxGroup` не инжектированы — `Create` кидает NRE); каждому незавершённому размещению лимит `MaxAttemptsPerProfile`=3600 кадров (~минута при 60 fps), иначе Fatal+лог про prefab/bundle.
  - Готовые размещения (Done/Fatal) пропускаются сразу; спавн помечает группу публичной (`IsGroupPublic=true` + `AddPublicGroup`) с флагом `DontDestroyIfEmpty`.
  - Когда все размещения Done/Fatal — лог «placement finished», `Destroy(gameObject)` холдера и обнуление `instance`.
- Связи:
  - Вызывает игру: `Sandbox.Instance/FindGroup/AddGroup/AddPublicGroup/GetCountryByCode`, `SandboxGroup.Create<SandboxStaticGroup>` (+флаг `DontDestroyIfEmpty`), `SandboxEntity.Create`, `EntityType.GetCountryBlueprint`, `Coordinates.ToWorldPosition`.
  - Вызывает мод: `TransformHelpers.FindChildRecursive`, `LighthouseRotator` (AddComponent + `SpeedDegreesPerSecond`), `LighthouseBeamGlow.Initialize` через приватный `EnsureGlowController` (scene-root GO «RadioNavLighthouseGlow_<GroupName>»).
  - Его запускают: `Main` (`EnsureRunning` при загрузке игровой сцены, `Reset()` в OnLoaded вместе с `ResetStatics` glow); потребляет: `LighthouseMapMarker.LighthouseEntity`.
- Паттерны и идиомы:
  - Поллинговый конечный автомат на вложенном классе `Placement` (Done/Fatal/Attempts/RotatorWaitAttempts) вместо корутин; повторный вход идемпотентен.
  - Класс помечен `[NonSerializedInGameState]` — плейссер не сериализуется игрой в сейв.
  - Переиспользуемые статические буферы `entityTypeValues/Keys/Hashes` (P5.1) — ретраи каждый кадр не плодят List/string[]/int[] мусор; строки-значения транзиентны, реюз безопасен.
  - Троттлинг логов каждые 120 попыток (`LogThrottled`); рефлексивный дамп статиков `Sandbox*`/`EntityType` только один раз при первой ошибке (`DumpStatics`) — сразу видно, что ещё null.
- Инварианты и грабли:
  - Вызов до состояния Gameplay = NRE внутри `SandboxGroup.Create` — отсюда ожидание в `IsGameplayReady` с троттленным логом.
  - Синтетический `EntityType` невосстановим из сейва: восстановленная группа может остаться без сущностей; без хила placement блокируется навсегда (grace `RotatorGracePeriodFrames`=600).
  - Страны грузятся несколькими кадрами позже восстановления сейва — «waiting for countries» это ожидание, не fatal.
  - Имя ассета префаба в бандле чувствительно к регистру (`ResolvePrefabPath` пробует `RetrieveAsset<GameObject>` и сразу `Free()`); не найдено — Fatal.
  - Имя glow-контроллера обязано быть стабильным между респаунами: `EnsureGlowController` ищет его через `GameObject.Find`.
  - Ожидание ротатора логируется каждые 120 кадров с описанием контейнера (`DescribeContainer`, до 8 детей) — помогает понять, что префаб заспавнился, но имя ротатора не совпадает.
  - Потокобезопасность не требуется: всё выполняется в главном потоке Unity.
- Тесты: прямых нет (MonoBehaviour с рантайм-зависимостями игры); косвенно покрывают `TransformHelpersTests` (рекурсия `FindChildRecursive`, общая с glow) и `LighthouseProfileTests` (профили, питающие размещения).

### 10.9 LighthouseBeamGlow
- Source: `Source/Logic/LighthouseBeamGlow.cs`
- Generated: 2026-08-23 · проверять по исходнику

- Назначение: делает лампу маяка видимой с 10+ км. Живёт на собственном scene-root GameObject («RadioNavLighthouseGlow_<GroupName>», создаёт `LighthousePlacer`), а не на префабе — поэтому продолжает работать при LOD-деактивации модели и переживает save/load (плейсер перепривязывает его к новому контейнеру). Зеркалит механики игрового `LighthouseLight`, но яркость, затухание и пиксельные размеры считает через единую перцептивную модель `LighthouseVisualModel`.
- Ключевые ответственности — 4 канала рендера:
  - `HDRPLensFlare` префаба: ночью активируется, `Intensity = FlareIntensityScale(2) × I_visual`; основной дальний сигнал, виден в любом режиме камеры, включая приборные (рендерится в их RenderTexture).
  - Screen-space точка на overlay-Canvas (sortingOrder 500): core + halo + streak; фолбэк при отсутствии флэра; uGUI `Image` резолвится рефлексией по всем сборкам, т.к. компилятор модов игры без ссылок на UnityEngine.UI.
  - Spot Light «Spot Light» ротатора: ночная подсветка воды/террейна по физическому A(r)·T(r) (без гаммы), range ≥2500 м, intensity ≥2.5; днём принудительно 0.
  - `_RotationOffset` через `MaterialPropertyBlock` на рендерерах с шейдером «Standard (DWS, Lighthouse)» — светящиеся окна синхронно крутятся с лучом.
  - Гейтинг день/ночь: `Weather.IsDarkWeather` ([Inject]); при null-погоде фолбэк — эвристика по солнцу (forward.y > −0.05 или intensity < 0.6).
  - Ключевые константы модели: BeamVisibleAngleDeg=60° (loom-окно B_wide), FlareMaxDistanceM=1200 м, StreakFadeNear/FarM=70/200 м, HaloMaxAlpha=0.12 (Mie-рассеяние — физически нулевое в чистом воздухе), DotSizeScale=1.1.
- Публичный API:
  - `Initialize(container, rotatorLocalPosition, rotator, lighthouseName)` — привязка к заспавненному маяку; повторный вызов после save/load респауна просто заменяет ссылки (перепривязка контейнера/ротатора).
  - `static ResetStatics()` (ADR-017) — уничтожает статические текстуры точки/стрика, сбрасывает `renderingCamera`, зовёт `CameraResolver.ResetForReload()`; вызывается из `Main.OnLoaded` при hot-reload, только из main thread когда glow-GO уже уничтожены.
- Жизненный цикл Unity:
  - Awake/Start нет; инициализация в `Initialize`: поиск солнца (Directional), спота (+буст range/intensity, автоповорот на 180° если смотрит назад от ротатора), флэра по имени типа, правка NearFadeStart/EndDistance=70/200 м, размеры элементов Flares[0..2], сбор оконных рендереров.
  - `Update`: `ResolveCamera` → `EnsureDeviceCameraFarClip` (подъём far clip приборной камеры до 30000 м; игра жёстко ставит 11000, Periscope.cs:1249 — каждый кадр, т.к. игра сбрасывает) → `UpdateDayNight` → `UpdateWindowRotation` → `UpdateSpot` → `UpdateFlare` → `KeepFlareInView` → `UpdateDot` → verbose-диагностика раз в 2 с (`Time.unscaledTime`).
  - `OnDestroy`: уничтожает только `dotCanvas.gameObject` обычным null-check (кастомный IsNotNull врёт при выгрузке сцены); статические текстуры чистит только `ResetStatics`.
- Связи:
  - Создаётся и настраивается `LighthousePlacer.EnsureGlowController` + повторный `glow.Initialize(...)` при каждом реаттаче.
  - Использует мод: `LighthouseVisualModel` (DistanceAttenuation/Transmittance/BeamIntensityFactor/VisualIntensity/DotSizePx/StreakLengthCapPx/WorldLightIntensity), `SkyVisibilityResolver.GetFogginess()`, `CameraResolver` (ADR-018: TryResolve, IsDeviceCamera, ResetForReload), `TransformHelpers.FindChildRecursive`, гейт `RadioNavDiagnostics.IsEnabledCached(LighthouseGlowVerboseKey)`.
  - Использует игру: `MainCamera.WorldToScreenPointNonJitteredExternalView` (не плывёт с TAA, знает про перископ/бинокль), `PeriscopeController`/`Periscope` для диагностики приборного вида, `Weather` [Inject].
- Паттерны и идиомы:
  - Рефлексия ко внешним типам: поля флэра (NearFade дистанции, массив `Flares` → элементы glow/streak/star, свойство Intensity), тип `UnityEngine.UI.Image`; PropertyInfo кэшируется для горячего пути `SetImageColor`.
  - Логирование только на переходах состояния (`SetDotEnabled`/`SetFlareActive` + гейты `DotWillLog`/`FlareWillLog`) — дорогие строки не строятся каждый кадр.
  - Детерминированное зеркало решения `FlareActiveByLogic` вместо мигающего `renderer.isVisible` (isVisible дёргается из-за раздутых bounds).
  - Цепочка решений `UpdateDot`: приборная камера → скрыть; флэр активен и в клипе → скрыть; не ночь/нет камеры → скрыть; за камерой (z≤0) → скрыть; beam-factor ≤0.01 → скрыть; иначе позиционирование и покраска по I_visual.
  - Раздача флэр↔точка: во FPP дальше FlareMaxDistanceM=1200 м флэр выключен, рисуется точка с точными капами модели (≤10px core / ≤13px streak); в приборном виде точку нельзя спозиционировать — флэр живёт на любой дистанции (как у игры).
  - `KeepFlareInView`: bounds меша флэра раздуваются до ~1e9, чтобы frustum culling не убивал дальний флэр после пересоздания bounds компонентом.
  - Ленивое построение dot-UI (`EnsureDotUi` один раз), статические кэш-текстуры (точка 64×64 радиальный квадратичный фоллофф; стрик 128×16 двойная гауссиана), переapply размеров (+25% FlareSizeScale) на каждом включении флэра.
- Инварианты и грабли:
  - Статика (dotTexture/streakTexture/renderingCamera/imageColorProperty) общая между инстансами нескольких маяков; без `ResetStatics` при выгрузке домена — утечка GPU-текстур (HideFlags.DontSave).
  - Точка прячется, только когда флэр активен по логике И внутри far clip ×0.9; за клипом точка берёт видимость на себя — иначе свет исчезает вовсе.
  - Non-jittered проекция бывает протухшей в бинокле/UZO — фолбэк на обычную `WorldToScreenPoint` реально рендерящей камеры; screen.z≤0 прячет точку.
  - `GetWorldPosition` якорится на «Spot Light» ротатора (единый источник истины для спота, флэра и точки); фолбэки: rotator → container.TransformPoint → transform.
  - Update тяжеловат: несколько `WorldToScreenPoint` за кадр (угол горизонта стрика — ещё две проекции), поэтому рефлексия закэширована, логи троттлятся и гейтятся PlayerPrefs.
  - Стрик поворачивается по экранному углу горизонта (`GetHorizonAngleDeg`) — остаётся параллельным воде при крене лодки.
  - Потокобезопасность не требуется: весь код в главном потоке Unity; `ResetStatics` зовётся только когда все инстансы уничтожены.
- Тесты: прямых нет (Unity MonoBehaviour); косвенно покрывают `LighthouseVisualModelTests` (математика модели), `TransformHelpersTests` (FindChildRecursive), `RadioNavDiagnosticsTests` (гейт verbose-логов).

### 10.10 StarCatalog
- Source: `Source/Data/StarCatalog.cs`
- Generated: 2026-08-23 · проверять по исходнику

- Назначение: статический каталог из 5046 звёзд для звёздного неба и астронавигации — единственные данные о положениях/яркости звёзд в моде.
- Сгенерирован автоматически (см. заголовок файла) из каталожного файла Stellarium `hip_gaia3` (`stars_0_0v0_21.cat`).
- Координаты приведены с учётом собственного движения и прецессии на эпоху J1942.0 (STAR_SKY_PLAN §7.2).
- Ключевые ответственности:
  - Хранение неизменяемого массива `StarEntry[5046]` с координатами, яркостью и флагом навигационной звезды.
  - Никакой логики: чистые данные, без методов; единственное состояние — сам массив.
- Публичный API / ключевые члены:
  - `StarCatalog.Stars` — `static readonly StarEntry[]`, 5046 элементов.
  - Массив отсортирован по убыванию яркости: Sirius (`Hip=32349`, Vmag −1.46) — элемент 0; хвост — звёзды Vmag ≈ 5.99–6.00.
  - `struct StarEntry.Hip` — `int`, номер Hipparcos.
  - `struct StarEntry.RaDeg` / `DecDeg` — `float`, RA/Dec в градусах, эпоха J1942.0.
  - `struct StarEntry.Vmag` — `float`, звёздная величина.
  - `struct StarEntry.Bv` — `float`, показатель цвета B−V.
  - `struct StarEntry.IsNav` — `bool`, признак навигационной звезды.
- Жизненный цикл:
  - Нет собственного жизненного цикла: статические данные инициализируются при первом обращении к типу.
  - Мутирующего API нет — каталог фактически иммутабелен.
- Связи (кто читает каталог):
  - `StarsMesh` (Source/Logic/StarsMesh.cs) — рендер звёзд билбордами, обходит весь массив (STAR_SKY_PLAN §9).
  - `ConstellationMesh` (Source/Logic/ConstellationMesh.cs) — линии созвездий по индексам из `ConstellationLines.cs`.
  - `ConstellationLines.cs` (Source/Data/) — 215 сегментов, индексы указывают прямо в `StarCatalog.Stars`.
  - `StarSkyRenderer` - **прототип, удалён 2026-08-31** (dead sweep #2), заменён на `StarsMesh`; в дереве исходников его нет.
- Инварианты и грабли:
  - Объём: файл ~554 КБ / 5067 строк — один литерал массива.
  - Правка только через регенерацию из Stellarium: руками данные не менять.
  - Эпоха координат J1942.0 (военный период игры), а не J2000 — потребители не должны смешивать с современными координатами.
  - `IsNav=true` ровно у 48 ярчайших звёзд (Vmag ≤ ~1.97); остальные 4998 — фон неба.
  - Индексы `ConstellationLines.cs` жёстко привязаны к порядку массива: перестановка или укорачивание `Stars` ломает созвездия.
  - Защита в `ConstellationMesh` — только bounds-check `starIdx >= Stars.Length`: перестановки он не ловит.
  - Память: static readonly живёт всегда, ~5046 × 24 байта ≈ 120 КБ — приемлемо; дублировать копии не нужно, читайте напрямую.
  - Имён звёзд и привязок к созвездиям в файле нет — `IsNav` единственная семантическая разметка.
- Тесты: `Tests/StarCatalogTests.cs`, 5 тестов:
  - Массив непуст (`StarCatalog_HasStars`).
  - Есть навигационные звёзды (`StarCatalog_NavigationStars_Exist`).
  - Координаты в диапазонах RA 0–360, Dec −90..+90 (`StarCatalog_CoordinatesInRange`).
  - Sirius самый яркий и первый (`StarCatalog_Sirius_IsBrightest`).
  - Canopus (южная звезда) невидим на широте +50° (`StarCatalog_Canopus_NotVisibleAt50N`).

### 10.11 StoryDecisionGateway (добавлено 2026-08-24)

- Generated: 2026-08-24 · проверять по исходнику
- Файл: `Source/Logic/StoryDecisionGateway.cs` (~263 ln, static class)
- Назначение: вся reflection-работа с игровыми `RadioStory`/`Story` в одном месте — замена god-object `RadioStoryIntegration` (F04, цепочка G1–G4). Введён 2026-08-23 (commit 28f4a62).
- Ключевые ответственности:
  - Поиск методов/полей по иерархии типов (`FindMethodDeep` — трассирует базовые классы; G2/G3).
  - Инжекция кастомных решений (`InsertDecision`), stage-гейт с enum-безопасным чтением (`Convert.ToInt32`, G1), локализация через readonly-поле `LocalizationKey` (G4).
  - Кэширование всех `Type`/`MethodInfo`/`FieldInfo` при инициализации (F68/F107/F124).
- Связи: фасад `RadioStoryIntegration` → gateway; pre-flight проверка таргетов в `Main`.
- Тесты: `Tests/StoryDecisionGatewayTests.cs` + контракт игры в `Tests/GameContractTests.cs`.

----

*Document Version: 1.5*  
*Last Updated: 2026-08-24*  
*Corrections: Fixed component hierarchy (flat structure, added ComparisonExtensions), fixed array sizes 4→14 (F48), file counts (F49), added missing classes (F52). See F01–F207 (total 207 issues) — статусы в `hypothesis/INDEX.md`, закрытие в `RESOLVED_LEDGER.md`. v1.4: merged class dossiers from docs/dossiers/ into §10 (lossless). v1.5: added StoryDecisionGateway (§10.11).*
