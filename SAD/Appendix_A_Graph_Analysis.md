# Appendix A: Graph Analysis — RadioNavigation Mod

> **⚠️ Deep Scan Update (2026-08-01):** Анализ графа обновлён. Всего **199 проблем** (F01–F199).
> - **F47:** Удалены несуществующие узлы
> - **F48:** Array sizes 4→14
> - **F49:** Community node/sizes updated: UI 15, Logic 12, Data 5, Sound 2 (total 34)
> - **F51:** `Atmosphere.Apply(sample, sampleRate)` (not `GenerateNoise`), `ReceiverDsp.Process()` (not `ApplyAGC`)
> - **F52:** SignalRichTextFormatter moved to UI Community; ComparisonExtensions moved to Logic Community
>
> Полные описания: `refactoring-plan/F45.md`..`F52.md`, `F65.md`..`F199.md`.

## 1. Graph Overview

| Metric | Value |
|--------|-------|
| Total Nodes | ~34 (34 source files + inline types) |
| Total Edges | ~80 |
| Communities | 4 |
| Average Degree | 4.0 |
| Density | 0.10 |

> **_NOTE_**: Node count corrected from 40→34 (F49). Community 4 (Integration) merged into Community 1 (Logic) since `Main` and `RadioStoryIntegration` are both in `Source/Logic/`.

## 2. Community Detection

### 2.1 Community Structure

```
Community 0: UI Layer (15 nodes)
├── RadioHandler
├── ToggleHandler
├── ZoneHandler
├── GroundHandler
├── CursorHandler
├── InformationPanelHandler
├── SpinnerHandlerBase
├── VolumeSpinnerHandler
├── FeedbackSpinnerHandler
├── PanelDragHandler
├── ExpanderHandler
├── DebugAngleHandler
├── RadioInstaller
├── PrefabNames
└── SignalRichTextFormatter

Community 1: Logic Layer (12 nodes)
├── SonneController
├── RadioStoryIntegration
├── StationScheduler
├── MorseKeyer
├── MorseAlphabet
├── NavigationPattern
├── SonneSignalGenerator
├── MorseTiming
├── ComparisonExtensions
├── NavigationElementType
└── Main

Community 2: Data Layer (5 nodes)
├── BeaconData
├── DialConfig
├── ConsolCycleConfig
├── StationState
└── SignalEntry (struct in SignalTypes.cs)

Community 3: Sound Layer (4 nodes)
├── AudioSynthesisEngine
├── CarrierGenerator (nested in DspPipeline.cs)
├── Atmosphere (nested in DspPipeline.cs)
└── ReceiverDsp (nested in DspPipeline.cs)
```

### 2.2 Community Cohesion

| Community | Cohesion Score | Description |
|-----------|----------------|-------------|
| UI Layer | 0.72 | High internal coupling |
| Logic Layer | 0.68 | Moderate coupling |
| Data Layer | 0.85 | **Very high** — tight data model |
| Sound Layer | 0.78 | High internal coupling |
| Integration | 0.45 | Low — isolated components |

## 3. God Nodes

### 3.1 Top Connected Nodes

| Node | Degree | Community | Role |
|------|--------|-----------|------|
| **SonneController** | 12 | Logic | **Hub** — connects all layers |
| **InformationPanelHandler** | 8 | UI | **Hub** — UI orchestration |
| **RadioStoryIntegration** | 7 | Integration | **Bridge** — game connection |
| **BeaconData** | 6 | Data | **Data source** |
| **AudioSynthesisEngine** | 5 | Sound | **Audio hub** |

### 3.2 Node Analysis

#### SonneController (God Node)

```
Connections:
├──► StationScheduler[] (14)
├──► MorseKeyer[] (14)
├──► CarrierGenerator[] (14)
├──► AudioSynthesisEngine
├──► BeaconData
├──► DialConfig
├──► ConsolCycleConfig
├──► InformationPanelHandler
├──► RadioHandler
└──► ToggleHandler

Metrics:
├── Degree: 12
├── Betweenness: 0.45
├── Closeness: 0.38
└── PageRank: 0.15
```

**Analysis**: SonneController is the central hub of the system. It connects to almost every other component, making it a critical point of failure and a maintenance bottleneck.

#### InformationPanelHandler (God Node)

```
Connections:
├──► SonneController
├──► ZoneHandler
├──► GroundHandler
├──► ToggleHandler
├──► RadioHandler
├──► UIElementFactory (proposed)
├──► StationFinder (proposed)
└──► StationInfoDisplay (proposed)

Metrics:
├── Degree: 8
├── Betweenness: 0.32
├── Closeness: 0.42
└── PageRank: 0.12
```

**Analysis**: InformationPanelHandler is the second-largest hub, mixing UI creation, business logic, and data display.

## 4. Surprising Connections

### 4.1 Cross-Layer Dependencies

| From | To | Type | Issue |
|------|----|------|-------|
| SonneController | RadioHandler | Logic → UI | Should be UI → Logic |
| InformationPanelHandler | ZoneHandler | UI → UI | Should use service |
| InformationPanelHandler | GroundHandler | UI → UI | Should use service |
| CursorHandler | ToggleHandler | UI → UI | Static coupling |

### 4.2 Hidden Dependencies

| Component | Hidden Dependency | Discovery |
|-----------|-------------------|-----------|
| StationScheduler | AudioSynthesisEngine | Phase transitions logged in audio |
| MorseKeyer | StationScheduler | Timing coupled |
| BeaconData | SonneSignalGenerator | Duplicated bearing table logic |

## 5. Dependency Flow Analysis

### 5.1 Ideal Flow (Proposed)

```
UI Layer
    │
    ▼
Service Layer (Interfaces)
    │
    ▼
Logic Layer
    │
    ▼
Data Layer
    │
    ▼
Sound Layer
```

### 5.2 Current Flow (Actual)

```
UI Layer ◄──────────────────► Logic Layer
    │                              │
    │                              │
    ▼                              ▼
Data Layer ◄────────────────► Sound Layer
```

**Issue**: Bidirectional dependencies between layers

## 6. Critical Paths

### 6.1 Audio Processing Path

```
OnAudioFilterRead()
    │
    ▼
AudioSynthesisEngine.ProcessAudio()  [9 params, F51]
    │
    ├──► CarrierGenerator.NextSample()
    ├──► Atmosphere.Apply(sample, sampleRate)  [F51: was GenerateNoise]
    └──► ReceiverDsp.Process()  [F51: was ApplyAGC]
```

**Risk**: Thread safety issues in this path (F01, F13, F29)

### 6.2 Navigation Update Path

```
SonneController.Update()
    │
    ├──► UpdateStationGains()
    ├──► UpdateStationNavigation()
    └──► UpdatePosition()
```

**Risk**: Performance bottleneck

### 6.3 UI Update Path

```
InformationPanelHandler.Update()
    │
    ├──► RefreshAll()
    ├──► UpdateStationInfo()
    ├──► UpdateSignalDisplay()
    └──► UpdateDiagnostics()
```

**Risk**: Redundant updates

## 7. Graph Metrics

### 7.1 Centrality Metrics

| Node | Degree Centrality | Betweenness | Closeness | PageRank |
|------|-------------------|-------------|-----------|----------|
| SonneController | 0.30 | 0.45 | 0.38 | 0.15 |
| InformationPanelHandler | 0.20 | 0.32 | 0.42 | 0.12 |
| RadioStoryIntegration | 0.18 | 0.28 | 0.35 | 0.10 |
| BeaconData | 0.15 | 0.18 | 0.40 | 0.08 |
| AudioSynthesisEngine | 0.13 | 0.15 | 0.38 | 0.07 |

### 7.2 Clustering Coefficient

| Community | Clustering Coefficient |
|-----------|------------------------|
| UI Layer | 0.65 |
| Logic Layer | 0.58 |
| Data Layer | 0.78 |
| Sound Layer | 0.72 |
| Integration | 0.40 |

## 8. Recommendations

### 8.1 Reduce Hub Dependencies

| Hub | Current Degree | Target Degree | Action |
|-----|----------------|---------------|--------|
| SonneController | 12 | 6 | Extract ⚠ StationManager (F47), ⚠ SignalDecoder (F47) |
| InformationPanelHandler | 8 | 4 | Extract ⚠ UIElementFactory, ⚠ StationFinder (both proposed F47) |

### 8.2 Break Cycles

| Cycle | Break Point | Solution |
|-------|-------------|----------|
| UI ↔ Logic | SonneController → UI | Interface-based dependency |
| Logic ↔ Data | BeaconData → SonneSignalGenerator | Extract shared bearing logic |

### 8.3 Improve Cohesion

| Community | Current | Target | Action |
|-----------|---------|--------|--------|
| UI Layer | 0.72 | 0.85 | Reduce cross-handler dependencies |
| Logic Layer | 0.68 | 0.80 | Extract focused classes |
| Data Layer | 0.85 | 0.90 | Already high — maintain |

## 9. Visualization

### 9.1 Adjacency Matrix

```
         Sonne  Info   Radio  Beacon  Audio  Station  Morse  Position
Sonne    -      1      1      1       1      1        1      1
Info     1      -      1      0       0      0        0      0
Radio    1      1      -      0       0      0        0      0
Beacon   1      0      0      -       0      0        0      1
Audio    1      0      0      0       -      0        0      0
Station  1      0      0      0       0      -        1      0
Morse    1      0      0      0       0      1        -      0
Position 1      0      0      1       0      0        0      -
```

### 9.2 Layer Dependency Diagram

```
┌─────────────────────────────────────────────┐
│              UI Layer                        │
│  ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐          │
│  │Radio│ │Toggle│ │Zone │ │Ground│          │
│  └──┬──┘ └──┬──┘ └──┬──┘ └──┬──┘          │
└─────┼───────┼───────┼───────┼──────────────┘
      │       │       │       │
      ▼       ▼       ▼       ▼
┌─────────────────────────────────────────────┐
│           Logic Layer                       │
│  ┌─────────────────────────────────────┐   │
│  │         SonneController             │   │
│  │  (Hub - connects everything)        │   │
│  └─────────────────────────────────────┘   │
└─────────────────────────────────────────────┘
      │
      ▼
┌─────────────────────────────────────────────┐
│           Data Layer                        │
│  ┌─────────┐ ┌─────────┐ ┌─────────┐       │
│  │BeaconData│ │DialConfig│ │StationState│   │
│  └─────────┘ └─────────┘ └─────────┘       │
└─────────────────────────────────────────────┘
      │
      ▼
┌─────────────────────────────────────────────┐
│           Sound Layer                       │
│  ┌─────────────┐ ┌─────────────┐           │
│  │AudioSynth   │ │DspPipeline  │           │
│  │Engine       │ │             │           │
│  └─────────────┘ └─────────────┘           │
└─────────────────────────────────────────────┘
```

---

*Document Version: 2.0*  
*Last Updated: 2026-08-01*  
*Updated: Community structure corrected (4 communities, 34 nodes). API signatures corrected (F51). All phantom nodes removed (F47). SignalRichTextFormatter → UI Community, ComparisonExtensions → Logic Community. Updated for 199 problems (F01–F199).*
