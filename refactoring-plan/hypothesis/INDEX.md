# Сводный индекс проблем рефакторинга

## Легенда

| Приоритет | Смысл |
|-----------|-------|
| P0 (Critical) | Падения, data race, данные повреждены на production |
| P1 (High) | NRE в релизе, логические ошибки, утечки памяти |
| P2 (Medium) | Техдолг, замедление разработки, копипаста |
| P3 (Low) | Косметика, code style |

| Статус | Смысл |
|--------|-------|
| ✔ | Подтверждено статической проверкой против кода — проблема реальна |
| ✘ | Опровергнуто — проблема НЕ подтвердилась (см. `rejected\`) |
| ⏸ | Подтверждено, исправление отложено (причины/список — `..\DEFERRED.md`) |
| 🔎 | Требует live-проверки в игре (`..\LIVE_VALIDATION_CHECKLIST.md`) |

> **Решённость (что стало с проблемой):** см. `..\RESOLVED_LEDGER.md` — построчные доказательства закрытия ~160 гипотез; открытыми на 2026-08-24 остаются 22.

> Верификация всех 207 гипотез: 2026-08-01, статический анализ исходного кода (33 файла, ~4 600 строк) + сверка с исходниками игры (`C:\code\projects\uboat`). Итог: **194 confirmed** (→ `confirmed\`), **13 rejected** (→ `rejected\`).

---

## Список проблем

| ID | Название | Приоритет | Область | Строк | Статус |
|----|----------|-----------|---------|-------|-------|
| **F01** | Audio Race Condition | **P0** | Sound · Threading | 84 | ✅ |
| **F02** | God Object: SonneController | **P1** | Architecture | 100+ | ⏸ |
| **F03** | God Object: InformationPanelHandler | **P1** | Architecture | 80+ | ⏸ |
| **F04** | God Object: RadioStoryIntegration | **P1** | Architecture | 70+ | ⏸ |
| **F05** | Static Mutable State | **P0** | Threading · Architecture | 60+ | ✅ |
| **F06** | Dead Code & Mistakes | **P2** | Hygiene | 50+ | ✅ |
| **F07** | Null Reference Risks | **P1** | Correctness | 65+ | ✅ |
| **F08** | GC Pressure | **P2** | Performance | ~170 | ⏸ |
| **F09** | Logic Bugs | **P1** | Correctness | 55+ | ✅ |
| **F10** | Build Config Issues | **P2** | Build | ~160 | ✅ |
| **F11** | Memory Leaks | **P1** | Memory | ~160 | 🔎 |
| **F12** | Copy-Paste Duplication | **P2** | Hygiene | 200 | ✅ |
| **F13** | Unity API in Audio Thread | **P0** | Sound · Threading | ~40 | ✅ |
| **F14** | Event Subscription Leaks | **P1** | UI · Events | ~50 | 🔎 |
| **F15** | No State Persistence | **P2** | Save/Load | ~50 | ✅ |
| **F16** | PositionFixCalculator.ErrorKm = 0 | **P1** | Logic · PF Calculator | ~10 | ✅ |
| **F17** | StationState mutable struct | **P2** | Data · StationState | ~25 | ✅ |
| **F18** | PositionFixCalculator God Object | **P2** | Architecture | ~60 | ⏸ |
| **F19** | NavigationPattern string concat | **P3** | Performance | ~20 | ✅ |
| **F20** | DialConfig band inconsistencies | **P2** | Data · DialConfig | ~30 | ⏸ |
| **F21** | Main.cs multiple responsibilities | **P2** | Architecture | ~60 | ⏸ |
| **F22** | No test coverage (0%) | **P2** | Project-wide | ~60 | ✅ |
| **F23** | Reflection Fragility — RadioStoryIntegration | **P0** | Logic · Integration | ~80 | ✅ |
| **F24** | TrimEntries O(n²) RemoveAt(0) | **P1** | UI · Signal Buffer | ~15 | 🔎 |
| **F25** | AudioSynthesisEngine Array Coupling | **P1** | Sound · API Design | ~40 | ⏸ |
| **F26** | PrefabNames.cs Not Used | **P2** | UI · Constants | ~20 | ✅ |
| **F27** | ComparisonExtensions Anti-Pattern | **P2** | Hygiene | ~34 | ⏸ |
| **F28** | Magic Numbers Scattered (~40 literals) | **P2** | Project-wide | ~40 | ✅ |
| **F29** | Zero Synchronization Primitives | **P0** | Sound · Threading | ~30 | ✅ |
| **F30** | RadioHandler missing OnDestroy unsubscribe | **P1** | UI · RadioHandler | ~10 | 🔎 |
| **F31** | 93 Debug.Log — 81 active in Release | **P2** | Project-wide | ~80 (call-sites) | ⏸ |
| **F32** | IPH: 5 GetComponentInChildren подряд | **P3** | UI · Performance | ~5 | ⏸ |
| **F33** | VolumeSpinner / FeedbackSpinner дубликаты | **P3** | UI · Duplication | ~34 | ✅ |
| **F34** | BearingTable / LookupBearing — мёртвый код | **P1** | Logic · Bearing Table | ~40 | ✅ |
| **F35** | Два разных алгоритма GenerateBearingTable | **P2** | Logic · Data · Duplication | ~30 | ✅ |
| **F36** | GetTransitionCount считает только Dots | **P2** | Logic · Naming | ~12 | ✅ |
| **F37** | 6 станций с null callsign | **P2** | Data · BeaconData | ~30 | ⏸ |
| **F38** | Plougourvest имеет null Frequency | **P2** | Data · BeaconData | ~25 | ⏸ |
| **F39** | 3 станции на одной частоте 332.5 kHz | **P2** | Data · BeaconData | ~15 | ⏸ |
| **F40** | CarrierGenerator frequency без верхней границы | **P2** | Sound · DSP | ~5 | ✅ |
| **F41** | Double vs float — фаза в DSP | **P2** | Sound · DSP | ~15 | ✅ |
| **F42** | Static Texture2D курсора не уничтожен | **P2** | UI · PanelDragHandler | ~10 | ✅ |
| **F43** | AudioSource не добавлен при готовом SonneController | **P1** | UI · RadioInstaller | ~10 | ✘ |
| **F44** | SAD Appendix B — 10+ расхождений с кодом | **P2** | Documentation · SAD | ~100 | ✘ |
| **F45** | SAD ADRs «Accepted» but 0% implemented (ADR-002/003/004/005/007) | **P0** | Documentation · SAD | ~35 | ✘ |
| **F46** | SAD Harmony patch targets completely wrong | **P0** | Documentation · SAD | ~58 | ✘ |
| **F47** | 14+ non-existent classes presented as real in SAD | **P1** | Documentation · SAD | ~61 | ✘ |
| **F48** | SAD station array sizing wrong (4 vs 14) | **P1** | Documentation · SAD | ~58 | ✘ |
| **F49** | SAD layer file counts wrong (40→34) | **P2** | Documentation · SAD | ~45 | ✘ |
| **F50** | SAD line counts wrong (10+ files) | **P2** | Documentation · SAD | ~51 | ✘ |
| **F51** | SAD method signatures wrong | **P2** | Documentation · SAD | ~43 | ✘ |
| **F52** | SAD missing 3 real classes from diagrams | **P1** | Documentation · SAD | ~57 | ⏸ |
| **F53** | MorseAlphabet 'A' — `._` вместо `.-` (dot-underscore) | **P0** | Logic · MorseAlphabet | 1 | ✅ |
| **F54** | AudioSynthesisEngine.UpdateStationBeats() — мёртвый код | **P1** | Sound · Dead Code | ~12 | ✅ |
| **F55** | ConsolCycleConfig.GetForCycleDuration() игнорирует параметр | **P1** | Data · ConsolCycleConfig | ~4 | ✅ |
| **F56** | RadioHandler — zoneHandler.OnZoneChanged без отписки | **P1** | UI · RadioHandler · Events | ~3 | ✅ |
| **F57** | RadioInstaller.Instantiate() — Debug.Log без #if DEBUG (×10) | **P1** | Build · Debug Logging | ~10 | ✅ |
| **F58** | DialConfig.AngleToFrequency() — float.NaN не проверяется | **P1** | Data · DialConfig · Correctness | ~5 | ✅ |
| **F59** | PrefabNames.DialFace определён, но не используется | **P2** | Data · PrefabNames | ~1 | ✅ |
| **F60** | CursorHandler.Update() форсирует SetSelectedGameObject каждый фрейм | **P2** | UI · CursorHandler · EventSystem | ~1 | ✘ |
| **F61** | ToggleHandler статика не сбрасывается при перезагрузке сцены | **P2** | UI · ToggleHandler · Static | ~2 | ✅ |
| **F62** | StationState.Pattern — ссылочный массив внутри struct → алиасинг | **P2** | Logic · StationState · Memory | ~1 | ⏸ |
| **F63** | DebugAngleHandler — жёсткая зависимость от TMPro | **P2** | UI · DebugAngleHandler | ~3 | ⏸ |
| **F64** | PositionFixCalculator — struct copy mutation через for (fragile) | **P2** | Logic · PositionFixCalculator | ~6 | ✅ |
| **F65** | MorseKeyer.Load() — stale consolPattern не очищен | **P0** | Logic · MorseKeyer | ~2 | ✅ |
| **F66** | TryGetCallsignFromHistory — NRE при station.Name == null | **P1** | Logic · SonneController | ~1 | ✅ |
| **F67** | InformationPanelHandler — Toggle component без null-чека | **P1** | UI · InformationPanelHandler | ~1 | ✅ |
| **F68** | RadioStoryIntegration — рефлексия Type.GetType не кешируется | **P1** | Logic · Integration · Performance | ~1 | ✅ |
| **F69** | Main.cs — catch блок логгирует только e.Message | **P1** | Logic · Main | ~1 | 🔎 |
| **F70** | NavigationPattern.FormatCompact() — неверное отображение transition | **P1** | Logic · NavigationPattern | ~1 | ⏸ |
| **F71** | DspPipeline — 2-3 Random.NextDouble() на сэмпл в audio thread | **P2** | Sound · DSP · Performance | ~1 | ⏸ |
| **F72** | SpinnerHandlerBase — Mathf.Lerp без Time.deltaTime (FPS-зависим) | **P2** | UI · SpinnerHandler | ~1 | ✅ |
| **F73** | PositionFixCalculator — Haversine формула дублирована | **P2** | Logic · PositionFixCalculator · Duplication | ~1 | ✅ |
| **F74** | ComparisonExtensions — несимметричные пороги IsSignificant/IsNegligible | **P2** | Logic · ComparisonExtensions | ~1 | 🔎 |
| **F75** | Мёртвый код: DecodeMorse(), StationScheduler.ToTicks() | **P2** | Logic · Hygiene | ~2 | ✅ |
| **F76** | MorseAlphabet.GetElementCount() — теоретическое int overflow | **P3** | Logic · MorseAlphabet | ~1 | ✅ |
| **F77** | StationState.Pattern never updated after init | **P0** | Logic · SonneController | ~5 | ✅ |
| **F78** | build.ps1 preprocessor case mismatch (`Debug` vs `DEBUG`) | **P0** | Build · Configuration | ~5 | ✅ |
| **F79** | InBeam = true hardcoded (always "IN BEAM") | **P0** | Logic · SonneController · Navigation | ~3 | ✅ |
| **F80** | PositionFixCalculator Haversine NaN | **P1** | Logic · PositionFixCalculator | ~3 | ✅ |
| **F81** | MorseTiming integer truncation (5% speedup) | **P1** | Logic · MorseTiming | ~5 | ✅ |
| **F82** | Main.cs swallows exceptions | **P1** | Logic · Main | ~5 | ✅ |
| **F83** | RadioStoryIntegration zombie decisions on hot-reload | **P1** | Logic · RadioStoryIntegration | ~10 | ⏸ |
| **F84** | ExpanderHandler.Start doesn't disable on null parent | **P2** | UI · ExpanderHandler | ~3 | ✅ |
| **F85** | GroundHandler raycastTarget blocks clicks | **P2** | UI · GroundHandler | ~1 | ✅ |
| **F86** | ConsolCycleConfig.GetForCycleDuration stub | **P2** | Data · ConsolCycleConfig | ~4 | ✅ |
| **F87** | TryGetCallsignFromHistory fake callsigns | **P2** | Logic · SonneController | ~5 | ✅ |
| **F88** | csproj suppresses all nullable warnings | **P2** | Build · Configuration | ~1 | ✅ |
| **F89** | OnDestroy doesn't stop AudioSource | **P3** | Logic · SonneController · Cleanup | ~3 | ✅ |
| **F90** | OnVisibilityChanged event never subscribed | **P3** | UI · RadioHandler · Events | ~1 | ✅ |
| **F91** | NormalizeAngle dead code | **P3** | Logic · SonneController | ~7 | ✅ |
| **F92** | ConsolCycleConfig unused fields | **P3** | Data · ConsolCycleConfig | ~3 | ✅ |
| **F93** | RadioInstaller Debug.Log without #if DEBUG | **P3** | Build · Debug Logging | ~10 | ✅ |
| **F94** | AudioSynthesisEngine tickAccumulator float drift | **P3** | Sound · AudioSynthesisEngine | ~2 | ✅ |
| **F95** | CursorHandler unnecessary ScrollRect | **P3** | UI · CursorHandler | ~7 | ✅ |
| **F96** | InformationPanelHandler GetComponent vs GetComponentInParent | **P3** | UI · InformationPanelHandler | ~2 | ✘ |
| **F97** | CursorHandler.OnScroll — RadioHandler.Instance NRE before null check | **P1** | UI · CursorHandler · NRE | ~5 | ✅ |
| **F98** | Scheduler ticked 2-3x per audio cycle (pauses halved/thirded) | **P1** | Sound · Logic · Timing | ~20 | ✅ |
| **F99** | Stale position fix retained when measurements < 2 | **P1** | Logic · SonneController · Position Fix | ~5 | ✅ |
| **F100** | FindClosestStation stale callsign from previously-closer station | **P2** | UI · InformationPanelHandler · Logic | ~5 | ✅ |
| **F101** | InformationPanelHandler shows "[Pause]" when no dominant station | **P2** | UI · InformationPanelHandler · Logic | ~5 | ✅ |
| **F102** | CursorHandler.Update per-frame GetComponentInParent<ScrollRect> | **P2** | UI · CursorHandler · Performance | ~3 | ✅ |
| **F103** | Font.CreateDynamicFontFromOSFont called per label (~15x) | **P2** | UI · InformationPanelHandler · GC Pressure | ~2 | ✅ |
| **F104** | RadioStoryIntegration AmbiguousMatchException from combined BindingFlags | **P2** | Logic · RadioStoryIntegration · Reflection | ~10 | ✘ |
| **F105** | StringBuilder allocs on audio thread (DecodeCallsignBuffer) | **P2** | Sound · Threading · GC Pressure | ~15 | ✅ |
| **F106** | Debug.Log + Time.time in audio thread, no #if DEBUG | **P2** | Sound · Audio Thread · Debug Logging | ~3 | ✅ |
| **F107** | Uncached Type.GetType on every locale string lookup | **P2** | Logic · RadioStoryIntegration · Performance | ~10 | ✅ |
| **F108** | List.Add on audio thread can resize + allocate | **P2** | Sound · Threading · Thread Safety | ~5 | ✅ |
| **F109** | RemoveAt(0) on audio thread while main thread reads (data corruption) | **P1** | Sound · Threading · Data Corruption | ~10 | ✅ |
| **F110** | dominantStationIndex cross-thread read/write, no volatile | **P2** | Sound · Threading · Memory Visibility | ~2 | ✅ |
| **F111** | MorseKeyer.IsActive cross-thread, no volatile | **P2** | Sound · Threading · Memory Visibility | ~2 | ✅ |
| **F112** | BeatFrequency write/read without synchronization | **P3** | Sound · Threading · Thread Safety | ~2 | ✅ |
| **F113** | MorseKeyer.AdvanceElement resets callsignLength=0 on pattern transition | **P3** | Logic · MorseKeyer · Correctness | ~3 | ✅ |
| **F114** | GetCurrentElementType silent Dot fallback masks potential OOB | **P3** | Logic · MorseKeyer · Defensive Coding | ~2 | ✅ |
| **F115** | OnDestroy doesn't reset DSP/Atmosphere state | **P3** | Logic · SonneController · Resource Mgmt | ~3 | ✅ |
| **F116** | Asymmetric null-clearing between Load/LoadConsol | **P3** | Logic · MorseKeyer · API Design | ~3 | ✅ |
| **F117** | SonneController keyer event subscriptions leak in OnDestroy | **P1** | Logic · SonneController · Events · Memory | ~10 | ✅ |
| **F118** | ZoneHandler PointerClick entries не фильтруют по кнопке | **P2** | UI · ZoneHandler · EventTrigger | ~5 | ✅ |
| **F119** | RadioHandler.OnDestroy не отписывается от zoneHandler.OnZoneChanged | **P1** | UI · RadioHandler · Events · Memory | ~5 | ✅ |
| **F120** | SonneController signalEntries + measurements GC pressure в hot path | **P2** | Logic · SonneController · Performance · GC | ~15 | ✅ |
| **F121** | Harmony.UnpatchAll() глобально откатывает патчи всех модов | **P1** | Logic · Main · Harmony | ~5 | ✅ |
| **F122** | StationScheduler.Reset() устанавливает неправильную фазу | **P1** | Logic · StationScheduler · State Machine | ~5 | ✅ |
| **F123** | StationState.NavigationStartTime не инициализируется в Awake | **P1** | Logic · SonneController · Station State | ~5 | ✅ |
| **F124** | GetCurrentLanguage() повторно резолвит Type.GetType на каждый вызов | **P2** | Logic · RadioStoryIntegration · Performance | ~25 | ✅ |
| **F125** | MorseAlphabet.GetMorse() молча возвращает "" для неизвестных символов | **P2** | Logic · MorseAlphabet · Correctness | ~5 | ✅ |
| **F126** | ConsolCycleConfig — нет валидации отрицательных timing values | **P2** | Data · ConsolCycleConfig · Validation | ~15 | ✅ |
| **F127** | SonneSignalGenerator.GenerateBearingTable хардкодит Cycle120s | **P2** | Logic · SonneSignalGenerator · Configuration | ~3 | ✅ |
| **F128** | PositionFixCalculator.FindIntersection не обрабатывает анти meridian | **P2** | Logic · PositionFixCalculator · Geography | ~10 | ✅ |
| **F129** | MorseAlphabet.GetElementCount() не учитывает границы слов | **P3** | Logic · MorseAlphabet · Morse Standard | ~10 | ✅ |
| **F130** | BandConfig — mutable class в static readonly массиве | **P3** | Data · DialConfig · Encapsulation | ~15 | ✅ |
| **F131** | Stations w/o frequency in position fix | **P2** | Logic · SonneController · Position Fix | ~15 | ✅ |
| **F132** | Keyer/scheduler advance while radio off | **P1** | Sound · AudioSynthesisEngine · RadioOff | ~20 | ✅ |
| **F133** | Escape key blocked by child selection | **P2** | UI · RadioHandler · Escape Key | ~10 | ✅ |
| **F134** | Calculate mutates input measurements | **P3** | Logic · PositionFixCalculator · API Design | ~5 | ✅ |
| **F135** | OnSequenceFinished no default case | **P3** | Logic · StationScheduler · Defensive Coding | ~5 | ✅ |
| **F136** | MorseTiming silence wrong for bad config | **P3** | Logic · MorseTiming · Validation | ~10 | ✅ |
| **F137** | PositionFixCalculator Math.Acos NaN | **P1** | Logic · PositionFixCalculator · Numerical | ~5 | ✅ |
| **F138** | IPH History[0] bounds crash | **P1** | UI · InformationPanelHandler · Bounds | ~3 | ✅ |
| **F139** | Main UnpatchAll without harmony ID | **P1** | Logic · Main · Harmony | ~5 | ✅ |
| **F140** | Main patch method null check missing | **P2** | Logic · Main · Reflection | ~5 | ✅ |
| **F141** | CarrierGenerator frequency clicks | **P2** | Sound · DSP · CarrierGenerator | ~10 | ✅ |
| **F142** | ReceiverDsp output unclamped (whistle) | **P2** | Sound · DSP · ReceiverDsp | ~3 | ✅ |
| **F143** | Atmosphere output unclamped (noise) | **P2** | Sound · DSP · Atmosphere | ~3 | ✅ |
| **F144** | NavigationPattern zero divide | **P2** | Logic · NavigationPattern · Validation | ~5 | ✅ |
| **F145** | First callsign cycle no callsignLength | **P2** | Logic · SonneController · Timing | ~10 | ✅ |
| **F146** | PositionFix mutates on failure path | **P2** | Logic · PositionFixCalculator · API | ~5 | ✅ |
| **F147** | RadioHandler float equality == | **P2** | UI · RadioHandler · Floating-Point | ~3 | ✅ |
| **F148** | GroundHandler no button filter | **P2** | UI · GroundHandler · Input | ~3 | ✅ |
| **F149** | SpinnerHandler multitouch state leak | **P2** | UI · SpinnerHandlerBase · State | ~5 | ✅ |
| **F150** | DialConfig overlapping Band III/IV | **P2** | Data · DialConfig · Band Ranges | ~5 | ✅ |
| **F151** | DialConfig missing interpolation segment | **P2** | Data · DialConfig · Interpolation | ~5 | ✅ |
| **F152** | Main third catch loses stack trace | **P2** | Logic · Main · Error Handling | ~3 | ✅ |
| **F153** | Atmosphere Box-Muller wastes 50% RNG | **P3** | Sound · DSP · Atmosphere · RNG | ~5 | ✅ |
| **F154** | ReceiverDsp constructor ignores sampleRate | **P3** | Sound · DSP · ReceiverDsp · API | ~3 | ✅ |
| **F155** | ReceiverDsp filter alpha recomputes | **P3** | Sound · DSP · ReceiverDsp · Perf | ~3 | ✅ |
| **F156** | Atmosphere undocumented impulse path | **P3** | Sound · DSP · Atmosphere · Maintainability | ~3 | ✅ |
| **F157** | RadioStoryIntegration duplicate BindingFlags | **P3** | Logic · RadioStoryIntegration · Hygiene | ~1 | ✅ |
| **F158** | RadioHandler dead sonneController field | **P3** | UI · RadioHandler · Dead Code | ~2 | ✅ |
| **F159** | DebugAngleHandler SerializeField overwrite | **P3** | UI · DebugAngleHandler · Unity Lifecycle | ~3 | ✅ |
| **F160** | IPH duplicated state queries | **P3** | UI · InformationPanelHandler · Code Smell | ~5 | ✅ |
| **F161** | DialConfig inconsistent rounding | **P3** | Data · DialConfig · Formatting | ~3 | ✅ |
| **F162** | BeaconData DotTable ignores parameter | **P3** | Data · BeaconData · API | ~3 | 🔎 |
| **F163** | StationState mutable struct copy-divergence | **P3** | Data · StationState · Value Type | ~5 | ✅ |
| **F164** | ConsolCycleConfig GetForCycleDuration stub | **P3** | Data · ConsolCycleConfig · Stub | ~3 | ✅ |
| **F165** | MorseKeyer.Reset doesn't clear sequence | **P2** | Logic · MorseKeyer · State Management | ~10 | ✅ |
| **F166** | MorseAlphabet.TextToPattern ignores parameter | **P3** | Logic · MorseAlphabet · API | ~1 | ✅ |
| **F167** | StationScheduler.OnSequenceFinished missing phases | **P2** | Logic · StationScheduler · State Machine | ~12 | 🔎 |
| **F168** | PositionFixCalculator.Calculate returns ErrorKm = 0 | **P2** | Logic · PositionFixCalculator · Algorithm | ~5 | ✅ |
| **F169** | CarrierGenerator phase drift from float precision | **P2** | Sound · DspPipeline · Numerical | ~10 | ✅ |
| **F170** | MorseAlphabet fixed table overflow | **P2** | Logic · MorseAlphabet · Data Structure | ~10 | ✅ |
| **F171** | GroundHandler.StateLog аллокация в DEBUG каждый кадр | **P3** | UI · GroundHandler · Performance | ~26 | ✅ |
| **F172** | SignalRichTextFormatter.TrimEntries O(n²) RemoveAt(0) | **P3** | UI · SignalRichTextFormatter · Performance | ~8 | ✅ |
| **F173** | InformationPanelHandler.CreateLabel fragile font loading | **P2** | UI · InformationPanelHandler · Portability | ~10 | ✅ |
| **F174** | NavigationPattern.GetElement negative index OOB | **P2** | Logic · NavigationPattern · Correctness | ~8 | ✅ |
| **F175** | MorseKeyer.AdvanceElement cross-type transition | **P2** | Logic · MorseKeyer · State Machine | ~40 | ✅ |
| **F176** | Main.cs GameObject.Find hardcoded hierarchy path | **P2** | Logic · Main · Performance | ~15 | ✅ |
| **F177** | DspPipeline deterministic radio noise generation | **P3** | Sound · DspPipeline · Architecture | ~5 | ✅ |
| **F178** | Repeated GetComponent lookups не кешируются | **P3** | UI · Hygiene | ~20 | ✅ |
| **F179** | Main.cs bootstrap объединяет несколько обязанностей | **P2** | Logic · Main · Architecture | ~35 | ⏸ |
| **F180** | UI validation основана на строковых путях | **P2** | Logic · Main · Maintainability | ~20 | ⏸ |
| **F181** | Main.cs избыточное диагностическое логирование | **P3** | Logic · Main · Logging | ~10 | ⏸ |
| **F182** | `build.ps1` doesn't validate `$GamePath` existence before invoking `csc` | **P3** | Build · Scripting | ~10 | ✅ |
| **F183** | `MorseAlphabet.GetMorse` performs redundant `c < 0` check on unsigned `char` | **P3** | Logic · MorseAlphabet · Code Hygiene | ~5 | ✅ |
| **F184** | `MorseKeyer` duplicates pattern/sequence branching in multiple internal getters | **P3** | Logic · MorseKeyer · Duplication | ~15 | ✅ |
| **F185** | `InformationPanelHandler` hardcodes 6 pattern labels for 14 available stations | **P2** | UI · InformationPanelHandler · Data Representation | ~10 | ✅ |
| **F186** | `RadioStoryIntegration.Init` accesses `GetParameters()[1]` without length check | **P2** | Logic · RadioStoryIntegration · Reflection Fragility | ~5 | ✘ |
| **F187** | `PanelDragHandler.OnPointerExit` resets custom cursor while dragging outside panel bounds | **P3** | UI · PanelDragHandler · User Experience | ~5 | ✅ |
| **F188** | `RadioInstaller` hardcodes fixed initial local position `(300, 100, 0)` without resolution scaling | **P3** | UI · RadioInstaller · Layout | ~5 | ✅ |
| **F189** | Cross-thread data race on `signalEntries` / `morseDecoderBuffer` between Audio Thread and UI Thread | **P1** | Sound · UI · Thread Safety | ~15 | ✅ |
| **F190** | PositionFixCalculator: `Math.Asin` без clamp на строке 149 — F137 ошибочно ссылается на F80 как на покрывающий | **P1** | Logic · PositionFixCalculator · Numerical Stability | ~5 | ✅ |
| **F191** | PositionFixCalculator: `Math.Asin` без clamp на строке 187 (`phi3`) | **P1** | Logic · PositionFixCalculator · Numerical Stability | ~5 | ✅ |
| **F192** | RadioHandler: `OnCursorAngleChanged` событие объявлено и вызывается, но никогда не подписано | **P3** | UI · RadioHandler · Events · Dead Code | ~2 | ✅ |
| **F193** | `MorseAlphabet.GetElementCount()` — мёртвый код, баги в F76/F129/F170 бесполезны | **P2** | Logic · MorseAlphabet · Dead Code | ~5 | ✅ |
| **F194** | `DebugAngleHandler.OnAngleChanged` — событие объявлено и вызывается, но никогда не подписано | **P3** | UI · DebugAngleHandler · Events · Dead Code | ~2 | ✅ |
| **F195** | `RadioHandler.NotifyCursorChanged()` — метод определён, но никогда не вызывается | **P3** | UI · RadioHandler · Dead Code | ~1 | ✅ |
| **F196** | `build.ps1` — `/debug:full` с `/optimize+` для всех конфигураций | **P3** | Build · Configuration | ~5 | ✅ |
| **F197** | `ExpanderHandler.OnBeginDrag` — отсутствует проверка `ToggleHandler.IsInteractable` | **P3** | UI · ExpanderHandler · Input | ~2 | ✅ |
| **F198** | `PanelDragHandler.OnBeginDrag` — отсутствует проверка `ToggleHandler.IsInteractable` | **P3** | UI · PanelDragHandler · Input | ~2 | ✅ |
| **F199** | `ExpanderHandler` — отсутствует очистка состояния drag при `OnDisable`/`OnDestroy` | **P3** | UI · ExpanderHandler · State Management | ~3 | ✅ |
| **F200** | `UpdatePosition()` — ноль вызывающих: position fix всегда ~0°N 0°E, dot-счёт статичен | **P0** | Logic · SonneController · Position Fix | ~20 | ✅ |
| **F201** | Полный Consol-паттерн транслируется дважды за цикл; фаза «только позывной» не играет | **P1** | Logic · StationScheduler · State Machine | ~20 | ✅ |
| **F202** | SpinnerHandlerBase — сдвиг 90° между значением и визуалом: прыжок при захвате, мёртвая зона | **P1** | UI · SpinnerHandlerBase · Input Mapping | ~15 | ✅ |
| **F203** | Приёмник: дефолтный QF=20 — кросс-талки соседних станций на 30-99% громкости | **P2** | Sound · Receiver Selectivity | ~15 | ✅ |
| **F204** | PositionFixCalculator — арифметическое среднее долгот через антимеридиан (latent) | **P2** | Logic · PositionFixCalculator · Geography | ~10 | ✅ |
| **F205** | IPH — кириллическая «В°» вместо «°» (U+0412+U+00B0) в 3 строках | **P3** | UI · InformationPanelHandler · Text | ~3 | ✅ |
| **F206** | AudioSynthesisEngine — тики по кадрам, не по сэмплам: стерео замедлит морзе вдвое (latent) | **P3** | Sound · AudioSynthesisEngine · Timing | ~5 | ✅ |
| **F207** | PanelDragHandler — перехват drag, начатого на Zone-кнопках / Toggle / Ground | **P2** | UI · PanelDragHandler · Input | ~5 | ✅ |

---

## Граф зависимостей

```
F10 (Build)      ──── независим, quick win
F06 (Dead)       ──── независим, quick win
F12 (Dupes)      ──── независим, quick win
F15 (Persist)    ──── независим, quick win
F07 (NRE)        ──── зависит от F05 (static state сбрасывает ссылки в null)
F05 (Static)     ──── блокирует F02, F04 (SonneController/RadioStory пишут статику)
F01 (Race)       ──── блокирует F08 (GC pressure в audio thread), F09 (логика в audio thread)
F13 (UnityAPI)   ──── часть F01 (audio thread violations)
F02 (God SC)     ──── зависит от F01, F05
F03 (God IP)     ──── независим от других god objects
F04 (God RS)     ──── зависит от F05, частично от F02
F08 (GC)         ──── частично фиксится F01 (вынос Tick из audio thread)
F09 (Bugs)       ──── частично фиксится F01 (div by zero в audio callback)
F11 (Leaks)      ──── частично пересекается с F14 (RadioHandler leak)
F14 (SubLeaks)   ──── независим, но связан с F11
F16 (ErrorKm)    ──── независим, quick win
F17 (Struct)     ──── часть F02 (SonneController)
F18 (PF God Obj) ──── зависит от F22 (тесты)
F19 (StringCcat) ──── независим, quick win
F20 (Bands)      ──── независим, требует проверки исторических данных
F21 (Main)       ──── частично пересекается с F14 (подписки в Main.cs)
F22 (Tests)      ──── блокирует безопасный рефакторинг всех Fxx
F23 (Refl)       ──── часть F04 (RadioStory), зависит от F05
F24 (RemoveAt)   ──── независим, quick win
F25 (Coupling)   ──── зависит от F22 (тесты для API change)
F26 (PrefabNm)   ──── независим, quick win
F27 (CompExt)    ──── независим
F28 (MagicNums)  ──── независим, пересекается с F12
F29 (Sync)       ──── шире F01 (все потоки, не только race на StationState)
F30 (RadioUnsub) ──── часть F11/F14 (RadioHandler leak)
F31 (DebugLog)   ──── независим, quick win
F32 (GetCompChild)─── независим, quick win
F33 (DupSpinner) ──── независим
F34 (BearingDead) ──── независим, quick win
F35 (DupBearing)  ──── часть F34
F36 (TransCount)  ──── независим
F37 (NullCall)    ──── независим
F38 (NullFreq)    ──── независим
F39 (DupFreq)     ──── независим
F40 (CarrierClip) ──── независим, quick win
F41 (PhasePrec)   ──── независим
F42 (TexLeak)     ──── независим
F43 (AudioSrc)    ──── независим, часть F10 (build/instantiation)
F44 (SADDoc)      ──── независим
F53 (MorseA)      ──── независим, quick win
F54 (DeadBeat)    ──── независим, quick win
F55 (CycleParam)  ──── независим
F56 (ZoneUnsub)   ──── независим, quick win
F57 (ReleaseLog)  ──── независим, quick win
F58 (NaNProp)     ──── независим
F59 (DialConst)   ──── независим, quick win
F60 (SetSelected) ──── независим, quick win
F61 (ToggleStat)  ──── F05 (static state)
F62 (StructAlias) ──── независим
F63 (TMPDep)      ──── независим, quick win
F64 (FragileCopy) ──── независим
F65 (StaleConsol) ──── независим, quick win
F66 (NameNull)    ──── независим, quick win
F67 (ToggleNRE)   ──── независим, quick win
F68 (ReflUncached) ──── независим
F69 (CatchLog)    ──── независим, quick win
F70 (FormatCompact) ──── независим
F71 (HeavyRandom) ──── F01 (audio thread)
F72 (LerpFPS)     ──── независим, quick win
F73 (DupHaversine) ──── F22 (тесты)
F74 (Threshold)   ──── независим, quick win
F75 (DeadCode)    ──── независим, quick win
F76 (IntOverflow) ──── независим, quick win
F77 (PatternNeverUpdated) ──── независим, quick win
F78 (BuildCaseMismatch) ──── независим, quick win
F79 (InBeamHardcoded) ──── независим, quick win
F80 (HaversineNaN) ──── независим, quick win
F81 (MorseTimingTruncation) ──── независим, quick win
F82 (ExceptionSwallow) ──── независим
F83 (ZombieDecisions) ──── зависит от F23
F84 (ExpanderNoDisable) ──── независим, quick win
F85 (GroundRaycast) ──── независим, quick win
F86 (CycleConfigStub) ──── независим
F87 (FakeCallsigns) ──── независим
F88 (NullableSuppressed) ──── независим
F89 (NoAudioStop) ──── независим, quick win
F90 (DeadEvent)   ──── независим, quick win
F91 (DeadNormalizeAngle) ──── независим, quick win
F92 (UnusedFields) ──── независим, quick win
F93 (ReleaseDebugLog) ──── независим, quick win
F94 (FloatDrift)  ──── независим
F95 (UnnecessaryScrollRect) ──── независим, quick win
F96 (GetComponentInParent) ──── независим, quick win
F14 ──► (none)
F30 ──► (none)
F07 ──► F05
F05 ──► F02, F04, F23
F01 ──► F08(partial), F09(partial)
F13 ──► часть F01
F29 ──► F01 + F13 + F05
F02 ──► F01, F05; ──► F17(part)
F04 ──► F05; ──► F23(part)
F03 ──► (none)
F11 ──► (none)
F08 ──► F01
F09 ──► F01
F17 ──► F02
F18 ──► F22
F21 ──► F14
F22 ──► blocks all refactoring
F23 ──► F05, F04
F25 ──► F22
F45 ──► F44 (ADR-002/003/004/005/007)
F46 ──► F44 (Harmony targets)
F47 ──► F44 (non-existent classes)
F48 ──► F44 (array sizes)
F49 ──► F44 (layer counts)
F50 ──► F44 (LOC)
F51 ──► F44 (signatures)
F52 ──► F44 (missing classes)
F83 ──► F23 (reflection)
F97 (CursorNRE)     ──── независим, quick win
F98 (DoubleTick)    ──── связан с F01 (audio thread)
F99 (StaleFix)      ──── независим, quick win
F100 (StaleCall)    ──── независим, quick win
F101 (PauseDisplay) ──── независим, quick win
F102 (ScrollRect)   ──── независим, quick win
F103 (FontAlloc)    ──── независим, quick win
F104 (Ambiguous)    ──── связан с F23, F68
F105 (StrBuildAlloc)─── связан с F01, F08, F13
F106 (DebugInAudio) ──── часть F13, F31
F107 (TypeGetType)  ──── связан с F23, F68
F108 (ListAddAudio) ──── связан с F01, F29, F109
F109 (RemoveAtAudio)─── связан с F24, F01, F29
F110 (DomIdxVolatile)──── часть F01, F29
F111 (IsActiveVolatile)── часть F01, F29
F112 (BeatFreqSync) ──── часть F01, F29
F113 (CallsignReset)─── связан с F65
F114 (DotFallback)  ──── независим
F115 (NoDspReset)   ──── независим
F116 (Asymmetric)   ──── связан с F65
F117 (KeyerLeak)    ────► F14, F11
F118 (PtrClick)     ────► (none)
F119 (ZoneUnsub)    ────► F14, F30
F120 (SignalGC)     ────► F08, F24, F108, F109
F121 (UnpatchAll)   ──── независим, quick win
F122 (ResetPhase)   ──── независим, quick win
F123 (NavStartTime)  ──── связан с F77, F01
F124 (LangRedundant) ──── связан с F68, F107, F23
F125 (MorseSilent)   ──── связан с F53, F37
F126 (CycleNoValid)  ──── связан с F55/F86, F92
F127 (GenHardcoded)  ──── связан с F55/F86, F35
F128 (Antimeridian)  ──── связан с F80, F73, F16
F129 (WordBoundary)  ──── связан с F53
F130 (BandConfigMut) ──── связан с F20, F05
F165 (ResetClear)  ──── связан с F65, F116, F113
F166 (TextToPattern) ──── связан с F70
F167 (SeqFinished)  ──── связан с F122, F135
F168 (ErrorKm)     ──── связан с F16, F134, F80
F169 (PhaseDrift)  ──── связан с F41, F94
F170 (TableOverflow)─── связан с F53, F76, F129
F171 (StateLog)    ──── связан с F31
F172 (TrimEntries) ──── связан с F24, F108
F173 (FontLoad)    ──── связан с F103
F174 (NegIndex)    ──── связан с F114, F70
F175 (CrossTrans)  ──── связан с F65, F113, F116, F165
F176 (FindPath)    ──── независим
F177 (DetRandom)   ──── независим
F178 (GetComponent) ──── независим
F179 (Bootstrap)   ──── независим
F180 (StringPaths) ──── независим
F181 (DiagNoise)   ──── независим
```

### Визуально (блокирующие → зависимые)

```
F10 ──► (none)
F06 ──► (none)
F12 ──► (none)
F15 ──► (none)
F16 ──► (none)
F19 ──► (none)
F20 ──► (none)
F24 ──► (none)
F26 ──► (none)
F27 ──► (none)
F28 ──► (none)
F31 ──► (none)
F32 ──► (none)
F33 ──► (none)
F34 ──► (none)
F35 ──► F34
F36 ──► (none)
F37 ──► (none)
F38 ──► (none)
F39 ──► (none)
F40 ──► (none)
F41 ──► (none)
F42 ──► (none)
F43 ──► (none)
F44 ──► (none)
F45 ──► (none) — documentation
F46 ──► (none) — documentation
F47 ──► (none) — documentation
F48 ──► (none) — documentation
F49 ──► (none) — documentation
F50 ──► (none) — documentation
F51 ──► (none) — documentation
F52 ──► (none) — documentation
F53 ──► (none) — quick win
F54 ──► (none) — quick win
F55 ──► (none)
F56 ──► (none) — quick win
F57 ──► (none) — quick win
F58 ──► (none)
F59 ──► (none) — quick win
F60 ──► (none) — quick win
F61 ──► F05 (static state)
F62 ──► (none)
F63 ──► (none) — quick win
F64 ──► (none)
F65 ──► (none) — quick win
F66 ──► (none) — quick win
F67 ──► (none) — quick win
F68 ──► (none)
F69 ──► (none) — quick win
F70 ──► (none)
F71 ──► F01 (audio thread)
F72 ──► (none) — quick win
F73 ──► F22 (тесты)
F74 ──► (none) — quick win
F75 ──► (none) — quick win
F76 ──► (none) — quick win
F77 ──► (none) — quick win
F78 ──► (none) — quick win
F79 ──► (none) — quick win
F80 ──► (none) — quick win
F81 ──► (none) — quick win
F82 ──► (none)
F83 ──► F23 (reflection)
F84 ──► (none) — quick win
F85 ──► (none) — quick win
F86 ──► (none)
F87 ──► (none)
F88 ──► (none)
F89 ──► (none) — quick win
F90 ──► (none) — quick win
F91 ──► (none) — quick win
F92 ──► (none) — quick win
F93 ──► (none) — quick win
F94 ──► (none)
F95 ──► (none) — quick win
F96 ──► (none) — quick win
F97 ──► (none) — quick win
F98 ──► F01 (audio thread)
F99 ──► (none) — quick win
F100 ──► (none) — quick win
F101 ──► (none) — quick win
F102 ──► (none) — quick win
F103 ──► (none) — quick win
F104 ──► F23, F68
F105 ──► F01, F08, F13
F106 ──► F13, F31
F107 ──► F23, F68
F108 ──► F01, F29
F109 ──► F24, F01, F29
F110 ──► F01, F29
F111 ──► F01, F29
F112 ──► F01, F29
F113 ──► F65
F114 ──► (none)
F115 ──► (none)
F116 ──► F65
F117 ──► F14, F11
F118 ──► (none)
F119 ──► F14, F30
F120 ──► F08, F24
F121 ──► (none) — quick win
F122 ──► (none) — quick win
F123 ──► F77, F01
F124 ──► F68, F107, F23
F125 ──► F53, F37
F126 ──► F55, F92
F127 ──► F55, F35
F128 ──► F80, F73, F16
F129 ──► F53
F130 ──► F20, F05
F131 ──► F38, F79, F99
F132 ──► F98, F89, F01
F133 ──► F60, F97
F134 ──► F64, F73, F120
F135 ──► F122, F98
F136 ──► F126, F81
F137 ──► F190, F191, F73, F16
F138 ──► F67, F07, F37
F139 ──► F121, F83, F23
F140 ──► F23, F68, F82
F141 ──► F40, F41, F29
F142 ──► F41, F13
F143 ──► F142, F71
F144 ──► F126, F129
F145 ──► F113, F77, F65
F146 ──► F134, F64, F99
F147 ──► F72, F74
F148 ──► F118, F85
F149 ──► F118, F133
F150 ──► F20, F130
F151 ──► F20, F58, F150
F152 ──► F69, F82, F106
F153 ──► F71, F143
F154 ──► F41, F25
F155 ──► F102, F103
F156 ──► F28, F71
F157 ──► F23, F68, F104
F158 ──► F06, F54
F159 ──► F63
F160 ──► F96, F32
F161 ──► F20, F28
F162 ──► F55, F86, F35
F163 ──► F17, F130
F164 ──► F55, F86, F92, F126
F165 ──► F65, F116, F113
F166 ──► F70
F167 ──► F122, F135
F168 ──► F16, F134, F80
F169 ──► F41, F94
F170 ──► F53, F76, F129
F171 ──► F31
F172 ──► F24, F108
F173 ──► F103
F174 ──► F114, F70
F175 ──► F65, F113, F116, F165
F176 ──► (none)
F177 ──► (none)
F178 ──► (none)
F179 ──► (none)
F180 ──► (none)
F181 ──► (none)
F193 ──► F06, F75, F76, F125, F129, F170 (MorseAlphabet dead code → пересмотр связанных планов)
F194 ──► F63, F90, F192 (DebugAngleHandler dead event)
F195 ──► F06, F158, F192 (RadioHandler dead code)
F196 ──► F10, F78 (build.ps1 debug flags)
F197 ──► F149, F199 (ExpanderHandler IsInteractable + state cleanup)
F198 ──► F197 (PanelDragHandler IsInteractable consistency)
F199 ──► F149 (ExpanderHandler drag state cleanup)
```

---

## Рекомендованный порядок исправления

### Фаза 0: Тестовая инфраструктура (подготовка)
1. **F22** — Test project (xUnit), 1 baseline test per core class. 8-12 часов.  
   *Без тестов каждое изменение — регрессия вслепую.*

### Фаза 1: Quick Wins (безопасные, нет блокировок)
2. **F10** — Build Config: починить HintPath, версию, build.ps1 валидацию. 1-2 часа.
3. **F06** — Dead Code: удалить 4 мёртвых метода. 30 мин.
4. **F12** — Copy-Paste: вынести ToRadians, BfoBase, убрать дубликаты. 2-3 часа.
5. **F15** — State Persistence: PlayerPrefs для Volume/Feedback/Zone. 1-2 часа.
6. **F16** — ErrorKm: вычислить радиус ошибки из weighted фиксов. 30 мин.
7. **F19** — StringBuilder в FormatPattern/FormatCompact. 30 мин.
8. **F24** — Queue вместо RemoveAt(0) в TrimEntries. 30 мин.
9. **F26** — PrefabNames: подключить константы во всех Find() вызовах. 1 час.
10. **F27** — ComparisonExtensions: удалить IsNull/IsNotNull. 1 час.
11. **F28** — MagicNumbers: создать RadioConstants.cs. 1 час.
12. **F31** — Debug.Log: обернуть 81 вызов в #if DEBUG. 1-2 часа.
13. **F32** — IPH: один GetComponentsInChildren вместо 5. 30 мин.
14. **F33** — Spinner: удалить дубликаты Volume/Feedback. 30 мин.
15. **F34** — BearingTable: удалить dead code (GenerateBearingTable, LookupBearing, BearingTable поле). 1 час.
16. **F40** — CarrierGenerator: clamp frequency сверху (sampleRate/2). 15 мин.
17. **F43** — RadioInstaller: EnsureComponent<AudioSource> вне зависимости от SonneController. 15 мин.
18. **F45** — SAD ADRs: сменить статус 5 ADR с «Accepted» на «Deferred». 30 мин.
19. **F46** — SAD Harmony: переписать Sec 2 и ADR-001 с корректными target-ами. 1 час.
20. **F47** — SAD классы: удалить 14+ несуществующих классов из документов. 2 часа.
21. **F48** — SAD array size: исправить 4→14 во всех документах. 30 мин.
22. **F49** — SAD layer counts: исправить 40→34. 30 мин.
23. **F50** — SAD LOC: исправить для 10+ файлов. 1 час.
24. **F51** — SAD signatures: исправить сигнатуры методов. 1 час.
25. **F52** — SAD missing: добавить 3 реальных класса в диаграммы. 1 час.
26. **F53** — MorseAlphabet 'A': исправить `"._"` на `".-"`. 15 мин.
27. **F54** — AudioSynthesisEngine: удалить мёртвый метод UpdateStationBeats(). 5 мин.
28. **F56** — RadioHandler: отписать zoneHandler.OnZoneChanged в OnDestroy. 10 мин.
29. **F57** — RadioInstaller: обернуть 10 Debug.Log в #if DEBUG. 2 мин.
30. **F59** — PrefabNames: использовать константу вместо литерала. 5 мин.
31. **F60** — CursorHandler: проверять currentSelectedGameObject перед SetSelected. 10 мин.
32. **F63** — DebugAngleHandler: заменить TMP_Text на Text или #if TEXTMESH_PRO. 15 мин.
33. **F65** — MorseKeyer: очищать consolPattern в Load(). 2 мин.
34. **F66** — SonneController: защита station.Name от null. 5 мин.
35. **F67** — InformationPanelHandler: защита GetComponent<Toggle> от null. 5 мин.
36. **F69** — Main.cs: заменить Debug.LogError на Debug.LogException. 2 мин.
37. **F72** — SpinnerHandlerBase: добавить Time.deltaTime в Lerp. 5 мин.
38. **F74** — ComparisonExtensions: сделать пороги симметричными. 5 мин.
39. **F75** — Dead Code: удалить DecodeMorse(), StationScheduler.ToTicks(). 5 мин.
40. **F76** — MorseAlphabet: добавить overflow check. 2 мин.
41. **F78** — build.ps1: исправить preprocessor case (`/define:DEBUG`). 5 мин.
42. **F79** — SonneController: вычислять InBeam по углу. 15 мин.
43. **F84** — ExpanderHandler: добавить `enabled = false` при null parent. 2 мин.
44. **F85** — GroundHandler: `raycastTarget = false`. 2 мин.
45. **F89** — SonneController: `audio.Stop()` в OnDestroy. 2 мин.
46. **F90** — RadioHandler: удалить неиспользуемый OnVisibilityChanged. 2 мин.
47. **F91** — SonneController: удалить мёртвый NormalizeAngle. 2 мин.
48. **F92** — ConsolCycleConfig: удалить неиспользуемые поля. 2 мин.
49. **F93** — RadioInstaller: обернуть Debug.Log в #if DEBUG. 2 мин.
50. **F95** — CursorHandler: убрать лишний ScrollRect. 5 мин.
51. **F96** — InformationPanelHandler: GetComponent → GetComponentInParent. 2 мин.
52. **F97** — CursorHandler: добавить `if (RadioHandler.Instance.IsNull()) return;` в OnScroll. 2 мин.
53. **F99** — SonneController: добавить `fixValid = false;` в else-ветку HasEnoughMeasurementsForFix. 2 мин.
54. **F100** — InformationPanelHandler: сбрасывать bestCallsign при смене ближайшей станции. 5 мин.
55. **F101** — InformationPanelHandler: добавить проверку domIdx >= 0 перед суффиксом. 5 мин.
56. **F102** — CursorHandler: кешировать ScrollRect в Start(). 5 мин.
57. **F103** — InformationPanelHandler: кешировать Font.CreateDynamicFontFromOSFont. 5 мин.
58. **F106** — AudioSynthesisEngine: обернуть Debug.Log в #if DEBUG. 2 мин.
59. **F121** — Main.cs: заменить `harmony.UnpatchAll()` на `harmony.UnpatchSelf()`. 5 мин.
60. **F122** — StationScheduler: в `Reset()` заменить `PostNavSilence` на `Pause`. 5 мин.

### Фаза 2: Безопасность + корректность данных
52. **F07** — Null Reference Risks: 4 confirmed NRE. 1-2 часа.
53. **F11** — Memory Leaks: отписки, Texture2D.Destroy, кэш шрифтов. 2-3 часа.
54. **F14** — Event Subscription Leaks: RadioHandler, ZoneHandler. 1 час.
55. **F30** — RadioHandler OnDestroy: добавить unsubscribe. 15 мин.
56. **F20** — Band I: Unit kHz, устранить перекрытие III/IV. 1-2 часа.
57. **F42** — Static Texture2D: cleanup в OnDestroy, не только Application.quitting. 30 мин.
58. **F55** — ConsolCycleConfig: добавить switch на cycleDurationSeconds. 30 мин.
59. **F58** — DialConfig: добавить проверку float.IsNaN в каллерах. 15 мин.
60. **F61** — ToggleHandler: сбросить статику в OnDestroy. 15 мин.
61. **F68** — RadioStoryIntegration: кешировать Type.GetType. 30 мин.
62. **F70** — NavigationPattern: исправить FormatCompact для transition. 30 мин.
63. **F71** — DspPipeline: оптимизировать Random в audio thread. 1-2 часа.
64. **F77** — SonneController: обновлять stations[i].Pattern в UpdateStationNavigation. 15 мин.
65. **F80** — PositionFixCalculator: clamp `a` перед sqrt. 5 мин.
66. **F81** — MorseTiming: `(int)Math.Round(ms / TickMs)`. 15 мин.
67. **F82** — Main.cs: rethrow или cleanup при panel construction failure. 30 мин.
68. **F83** — RadioStoryIntegration: cleanup zombie decisions при hot-reload. 1 час.
69. **F86** — ConsolCycleConfig: switch по cycleDurationSeconds. 30 мин.
70. **F87** — SonneController: дополнить callsigns для 6 станций. 1 час.
71. **F88** — csproj: убрать nullable warning suppressions. 30 мин.
72. **F94** — AudioSynthesisEngine: tickAccumulator double. 15 мин.
73. **F118** — ZoneHandler: объединить два PointerClick в один с проверкой button. 10 мин.
74. **F117** — SonneController: сохранить делегаты keyer events, отписать в OnDestroy. 15 мин.
75. **F119** — RadioHandler: добавить `zoneHandler.OnZoneChanged -= OnZoneChanged` в OnDestroy. 5 мин.
76. **F120** — SonneController: задать capacity для signalEntries и measurements. 15 мин.
77. **F123** — SonneController: установить `NavigationStartTime` в main thread при входе в фазу Navigation. 15 мин.
78. **F124** — RadioStoryIntegration: кешировать `singletonType`, `loadSingleton`, `localeType` в статических полях. 15 мин.
79. **F125** — MorseAlphabet: добавить `Debug.LogWarning` для неизвестных символов в `GetMorse()`. 10 мин.
80. **F126** — ConsolCycleConfig: добавить метод `IsValid()`, вызывать в `GetForCycleDuration()`. 30 мин.
81. **F127** — SonneSignalGenerator: использовать `ConsolCycleConfig.GetForCycleDuration(station.CycleDurationSeconds)` вместо хардкода. 10 мин.
82. **F128** — PositionFixCalculator: нормализовать `deltaLam` к [-PI, PI] перед вычислениями. 15 мин.
83. **F132** — AudioSynthesisEngine: вынести `radioOn` проверку перед циклом тиков. 30 мин.

### Фаза 3: Архитектурные блокеры
73. **F05** — Static Mutable State: сделать инстансными. 3-4 часа.
74. **F01** — Audio Race Condition: double-buffer или вынос Tick из audio thread. 4-6 часов.
75. **F29** — No Synchronization: lock/volatile/MemoryBarrier. 2-3 часа (пересекается с F01).
76. **F13** — Unity API in Audio Thread: убрать Debug.Log + Time.time из DSP. 30 мин.
77. **F23** — Reflection Fragility: GameApiCompatibilityChecker, fail-fast. 2-3 часа.

### Фаза 4: God Objects + выделение ответственности (требуют F01, F05, F29)
78. **F03** — InformationPanelHandler: отделить UI от логики. 3-4 часа.
79. **F02** — SonneController: разделить по паттернам. 5-8 часов.
80. **F17** — StationState: struct → class или readonly (часть F02). 1-2 часа.
81. **F04** — RadioStoryIntegration: вынести в отдельные патчи. 3-5 часов.
82. **F23** — (остаток) интеграция с F04. 1-2 часа.
83. **F18** — PositionFixCalculator: IPositionCalculator, GeoHelper. 2-3 часа.
84. **F21** — Main.cs: HarmonyPatchManager, PrefabValidator. 2-3 часа.

### Фаза 5: API Design + Data Quality + Прочее
85. **F25** — AudioSynthesisEngine: StationAudio record, убрать positional arrays. 2-3 часа.
86. **F08** — GC Pressure (частично фиксится F01, F05, F29). 2-3 часа.
87. **F09** — Logic Bugs (частично фиксится F01, F05, F29). 1-2 часа.
88. **F35** — Duplicate bearing algo: удалить BeaconStation.GetDotCountToBearingTable. 30 мин.
89. **F36** — GetTransitionCount: переименовать в GetDotCount. 15 мин.
90. **F37** — 6 null callsign: дополнить историческими данными. 1-2 часа.
91. **F38** — Plougourvest freq: добавить частоту или удалить станцию. 30 мин.
92. **F39** — 332.5 конфликт: time-division или разные частоты. 1-2 часа.
93. **F41** — Phase double vs float: привести к double. 1 час.
94. **F44** — SAD docs: обновить Appendix B. 2-3 часа.
95. **F62** — StationState aliasing: сделать Groups IReadOnlyList или ImmutableArray. 30 мин.
96. **F64** — PositionFixCalculator: переписать цикл с for с комментарием или with. 30 мин.
97. **F73** — PositionFixCalculator: вынести Haversine в общий метод. 30 мин.
98. **F129** — MorseAlphabet: различать inter-character (3) и inter-word (7) паузы в `GetElementCount()` и `TextToPattern()`. 30 мин.
99. **F130** — DialConfig: сделать `BandConfig` readonly struct с конструктором. 30 мин.
100. **F131** — SonneController: добавить `if (freq.IsNull()) continue;` перед добавлением измерения. 5 мин.
101. **F133** — RadioHandler: добавить `!selected.transform.IsChildOf(transform)` в Escape check. 5 мин.
102. **F134** — PositionFixCalculator: работать с копией measurements в Calculate(). 5 мин.
103. **F135** — StationScheduler: добавить default case в OnSequenceFinished(). 2 мин.
104. **F136** — MorseTiming: добавить проверку `period < sound` и `Debug.LogWarning`. 5 мин.
105. **F137** — PositionFixCalculator: clamp аргумент `Math.Acos` перед вызовом. 5 мин.
106. **F138** — InformationPanelHandler: добавить `History?.Length > 0` перед `[0]`. 2 мин.
107. **F139** — Main.cs: заменить `UnpatchAll()` на `UnpatchSelf()`. 2 мин.
108. **F140** — Main.cs: добавить null-check для patch methods перед `new HarmonyMethod()`. 5 мин.
109. **F141** — CarrierGenerator: добавить frequency smoothing (one-pole interpolator). 15 мин.
110. **F142** — ReceiverDsp: добавить `Mathf.Clamp(saturated, -1f, 1f)` перед return. 2 мин.
111. **F143** — Atmosphere: добавить clamp на выход Apply(). 2 мин.
112. **F144** — NavigationPattern: добавить `if (elementsPerGroup == 0)` guard в конструктор. 2 мин.
113. **F145** — SonneController: pre-compute CurrentConsolPattern в Awake(). 10 мин.
114. **F146** — PositionFixCalculator: восстанавливать weights при failure path. 5 мин.
115. **F147** — RadioHandler: заменить `==` на `Mathf.Approximately()` в CursorAngle setter. 2 мин.
116. **F148** — GroundHandler: добавить `eventData.button != Left` check. 2 мин.
117. **F149** — SpinnerHandlerBase: добавить `OnEnable` reset для isDragging. 2 мин.
118. **F150** — DialConfig: убрать перекрытие Band III/IV (340-720, 720-1600). 10 мин.
119. **F151** — DialConfig.AngleToFrequency: инициализировать `prevFreq=RangeMin, prevAngle=AngleStart`. 5 мин.
120. **F152** — Main.cs: добавить `Debug.LogException(e)` в третий catch block. 2 мин.

### Фаза 2b: Sound/Atmosphere fixes
121. **F153** — Atmosphere: кэшировать второй Gaussian сэмпл (Box-Muller). 10 мин.
122. **F154** — ReceiverDsp: убрать неиспользуемый sampleRate из конструктора. 2 мин.
123. **F155** — ReceiverDsp: кэшировать filter alpha, пересчитывать при смене cutoff. 5 мин.
124. **F156** — Atmosphere: вынести hardcoded impulse в именованные константы. 2 мин.
125. **F157** — RadioStoryIntegration: удалить дублированный `BindingFlags.Instance`. 1 мин.
126. **F158** — RadioHandler: удалить мёртвое поле sonneController. 1 мин.
127. **F159** — DebugAngleHandler: использовать fallback вместо перезаписи SerializeField. 2 мин.
128. **F160** — InformationPanelHandler: заменить ручной обход иерархии на handler references. 5 мин.
129. **F161** — DialConfig.FormatFrequency: использовать `"F1"` для kHz bands. 2 мин.
130. **F162** — BeaconData.GetDotCountToBearingTable: документировать ограничение или исправить F55. 5 мин.
131. **F163** — StationState: сделать readonly struct или class. 15 мин.
132. **F164** — ConsolCycleConfig: документировать что только 120s supported. 2 мин.

### Фаза 3b: F165-F181 (дополнительные quick wins и архитектурные правки)
133. **F165** — MorseKeyer.Reset: добавить `sequence = null`. 2 мин.
134. **F167** — StationScheduler: добавить default case в OnSequenceFinished. 5 мин.
135. **F169** — CarrierGenerator: нормализовать phase в [0, 1) для предотвращения дрифта. 15 мин.
136. **F172** — SignalRichTextFormatter.TrimEntries: использовать RemoveRange вместо цикла RemoveAt(0). 15 мин.
137. **F174** — NavigationPattern.GetElement: добавить проверку `index < 0` перед делением. 5 мин.
138. **F168** — PositionFixCalculator: вычислить ErrorKm из разброса пересечений. 30 мин.
139. **F170** — MorseAlphabet: добавить bounds check в Define() или перейти на Dictionary. 30 мин.
140. **F173** — InformationPanelHandler: кешировать Font.CreateDynamicFontFromOSFont. 10 мин.
141. **F175** — MorseKeyer.AdvanceElement: очищать consolPattern/sequence при cross-type переходе. 30 мин.
142. **F166** — MorseAlphabet.TextToPattern: удалить или реализовать elementDurationTicks. 5 мин.
143. **F171** — GroundHandler.StateLog: заменить string concatenation на StringBuilder. 10 мин.
144. **F177** — DspPipeline: разделить детерминированный/тестовый/игровой RNG. 30 мин.
145. **F178** — UI: кешировать GetComponent вызовы в Awake/Start. 15 мин.
146. **F181** — Main.cs: добавить уровни логирования, подавлять в release. 15 мин.
147. **F176** — Main.cs: кешировать GameObject.Find("/GUI/Viewport"). 30 мин.
148. **F179** — Main.cs: разделить OnLoaded на PatchInstaller/EventRegistrar/BootstrapDiagnostics. 2-3 часа.
149. **F180** — Main.cs: заменить строковые пути UI на типизированные ссылки. 30 мин.

150. **F193** — MorseAlphabet: удалить мёртвый `GetElementCount()`, пересмотреть F76/F129/F170. 5 мин.
151. **F194** — DebugAngleHandler: удалить dead event `OnAngleChanged` или добавить subscriber. 2 мин.
152. **F195** — RadioHandler: удалить мёртвый метод `NotifyCursorChanged()`. 1 мин.
153. **F196** — build.ps1: разделить `/debug`/`/optimize` по конфигурациям. 5 мин.
154. **F197** — ExpanderHandler: добавить `ToggleHandler.IsInteractable` check в `OnBeginDrag`. 2 мин.
155. **F198** — PanelDragHandler: добавить `IsInteractable` check или документировать. 2 мин.
156. **F199** — ExpanderHandler: добавить `OnDisable` для сброса `isDragging`. 3 мин.

### Фаза 5b: F200-F207 (новые находки аудита 2026-08-01)
157. **F200** — SonneController: подключить фид позиции лодки (патч/интерфейс `IPositionProvider`); до этого — скрывать фикс без данных. 1-2 часа.
158. **F201** — StationScheduler: `GetCurrentConsolPattern()` возвращать null в фазе `Callsign`. 30 мин.
159. **F202** — SpinnerHandlerBase: убрать `GetAngleOffset()` из value или компенсировать в визуале; проверить на живой панели. 30-60 мин.
160. **F203** — AudioSynthesisEngine/SonneController: поднять дефолтный QF (~100-200) или маскировать недоминантные станции в миксере. 1 час.
161. **F204** — PositionFixCalculator: векторное усреднение долгот (atan2-mean) в двух местах. 30 мин.
162. **F205** — InformationPanelHandler: заменить `В°` на `\u00B0` в 3 литералах. 2 мин.
163. **F206** — AudioSynthesisEngine: `tickAccumulator += ticksPerSample * channels`. 5 мин.
164. **F207** — PanelDragHandler: исключить потомков ZoneHandler/ToggleHandler/GroundHandler из drag. 15 мин.

**Итого (снимок на 2026-08-01, исторический):** ~95-130 часа + Фаза 0 (F22: 8-12 часов) + Фазы 1-2 + F97-F207 диапазоны — см. исходную оценку ниже.

> **⚠️ Актуализация 2026-08-24:** приведённый выше перечневый итог описывал планы «что бы сделать» и **устарел как план**: подавляющая часть пунктов уже выполнена. Построчное закрытие с доказательствами (теги `// F##:` в коде, коммиты, удаления) — в `..\RESOLVED_LEDGER.md`. Открытыми остаются 22 гипотезы (см. §5 ledger). Историческая оценка сохранена без правок:

<details><summary>Историческая оценка трудозатрат (2026-08-01)</summary>

~95-130 часа + Фаза 0 (F22: 8-12 часов) + Фаза 1-2 (F77-F96: ~5-8 часов) + F97-F120 (~4-5 часов) + F121-F130 (~3-4 часа) + F131-F136 (~20 мин) + F137-F164 (~2-3 часа) + F165-F181 (~6-8 часов) + F182-F189 (~1-2 часа) + F190-F192 (~15 мин) + F193-F199 (~15 мин): F190/F191 clamp Asin в PositionFixCalculator, F192 удалить dead event OnCursorAngleChanged, F193 удалить мёртвый GetElementCount, F194-195 dead code/events в DebugAngleHandler/RadioHandler, F196 debug-флаги build.ps1, F197-199 ExpanderHandler consistency + state cleanup. + F200-F207 (~4-6 часов): F200 подключить позицию/скрыть фикс, F201 фаза Callsign, F202 spinner offset, F203 селективность приёмника, F204 антимеридиан-среднее, F205 кириллический градус, F206 каналы в тиках, F207 drag с дочерних контролов.

</details>

---

## Cross-reference matrix

_Maтрица охватывает F01-F52. F53-F76 не имеют перекрёстных зависимостей с предыдущими (кроме F61→F05, F71→F01). F77-F96 — перекрёстные ссылки в отдельных файлах. F97-F116 — перекрёстные ссылки: F104→F23/F68, F105→F01/F08/F13, F106→F13/F31, F107→F23/F68, F108→F01/F29, F109→F24/F01/F29, F110-F112→F01/F29, F113/F116→F65. F117-F120: F117→F14/F11, F118→(none), F119→F14/F30, F120→F08/F24. F121-F130: F121→(none), F122→(none), F123→F77/F01, F124→F68/F107/F23, F125→F53/F37, F126→F55/F92, F127→F55/F35, F128→F80/F73/F16, F129→F53, F130→F20/F05. F131-F136: F131→F38/F79/F99, F132→F98/F89/F01, F133→F60/F97, F134→F64/F73/F120, F135→F122/F98, F136→F126/F81. F137-F164: F137→F190/F191/F73/F16, F138→F67/F07/F37, F139→F121/F83/F23, F140→F23/F68/F82, F141→F40/F41/F29, F142→F41/F13, F143→F142/F71, F144→F126/F129, F145→F113/F77/F65, F146→F134/F64/F99, F147→F72/F74, F148→F118/F85, F149→F118/F133, F150→F20/F130, F151→F20/F58/F150, F152→F69/F82/F106, F153→F71/F143, F154→F41/F25, F155→F102/F103, F156→F28/F71, F157→F23/F68/F104, F158→F06/F54, F159→F63, F160→F96/F32, F161→F20/F28, F162→F55/F86/F35, F163→F17/F130, F164→F55/F86/F92/F126. F165-F181: F165→F65/F116/F113, F166→F70, F167→F122/F135, F168→F16/F134/F80, F169→F41/F94, F170→F53/F76/F129, F171→F31, F172→F24/F108, F173→F103, F174→F114/F70, F175→F65/F113/F116/F165, F176→(none), F177→(none), F178→(none), F179→(none), F180→(none), F181→(none). F182-F189: F182→(none), F183→(none), F184→(none), F185→F32, F186→F23, F187→(none), F188→(none), F189→F01/F120. F190-F192: F190→F137/F73/F16/F128 (FindIntersection NaN clamp), F191→F137/F190/F73/F16/F128 (FindIntersection phi3 NaN), F192→F90/F06/F158 (dead event analog). F193-F199: F193→F06/F75/F76/F125/F129/F170, F194→F63/F90/F192, F195→F06/F158/F192, F196→F10/F78, F197→F149/F199, F198→F197, F199→F149. F200-F207: F200→F204/F99/F16/F131 (мёртвый фид позиции блокирует фикс-цепочку), F201→F145/F98/F65/F113/F175, F202→F72/F149/F33, F203→F39/F131/F142/F143, F204→F200/F128/F73, F205→F200/F161, F206→F94/F98, F207→F197/F198/F118/F148.

---

## Метрики

| Метрика | Значение |
|---------|----------|
| Всего проблем | 207 (F01-F207) |
| P0 (Critical) | 13 (F01, F05, F13, F23, F29, F45, F46, F53, F65, F77, F78, F79, **F200**) |
| P1 (High) | 48 (F02, F03, F04, F07, F09, F11, F14, F16, F24, F25, F30, F34, F43, F47, F48, F52, F54, F55, F56, F57, F58, F66, F67, F68, F69, F70, F80, F81, F82, F83, F97, F98, F99, F109, F117, F119, F121, F122, F123, F132, F137, F138, F139, **F189**, **F190**, **F191**, **F201**, **F202**) |
| P2 (Medium) | 92 (F06, F08, F10, F12, F15, F17, F18, F20, F21, F22, F26, F27, F28, F31, F35, F36, F37, F38, F39, F40, F41, F42, F44, F49, F50, F51, F59, F60, F61, F62, F63, F64, F71, F72, F73, F74, F75, F84, F85, F86, F87, F88, F100, F101, F102, F103, F104, F105, F106, F107, F108, F110, F111, F118, F120, F124, F125, F126, F127, F128, F131, F133, F140, F141, F142, F143, F144, F145, F146, F147, F148, F149, F150, F151, F152, F165, F167, F168, F169, F170, F173, F174, F175, F176, F179, F180, **F185**, **F186**, **F193**, **F203**, **F204**, **F207**) |
| P3 (Low) | 54 (F19, F32, F33, F76, F89, F90, F91, F92, F93, F94, F95, F96, F112, F113, F114, F115, F116, F129, F130, F134, F135, F136, F153, F154, F155, F156, F157, F158, F159, F160, F161, F162, F163, F164, F166, F171, F172, F177, F178, F181, **F182**, **F183**, **F184**, **F187**, **F188**, **F192**, **F194**, **F195**, **F196**, **F197**, **F198**, **F199**, **F205**, **F206**) |
| Quick wins (Фаза 1) | 65 (25 code + 8 documentation + 32 new F97-F164) |
| Оценочное время | ~95-125 часа + Фаза 0 (F22: 8-12 часов) + Фаза 1-2 (F77-F96: ~5-8 часов) + F97-F120 (~4-5 часов) + F121-F130 (~3-4 часа) + F131-F136 (~20 мин) + F137-F164 (~2-3 часа) + F165-F181 (~6-8 часов) + F182-F189 (~1-2 часа) + F200-F207 (~4-6 часов) |

## История изменений

| Дата | Изменение |
|------|-----------|
| 2026-07-30 | Первоначальный набор: F01-F12 |
| 2026-07-30 | Добавлены F13-F15 (Unity API в audio, утечки подписок, отсутствие сохранения). Обновлены F08 (foreach boxing), F10 (GameObject.Find + nullable warnings), F11 (EventTrigger lambdas) |
| 2026-07-30 | Добавлены F16-F22 (ErrorKm=0, StationState struct, PF God Object, string concat, band inconsistencies, Main.cs ответственности, нет тестов). Полный рескан всех 35 source-файлов + SAD + git истории |
| 2026-07-30 | Добавлены F23-F28 (Reflection Fragility, TrimEntries RemoveAt(0), Array Coupling, PrefabNames, ComparisonExtensions, Magic Numbers). Слияние со старым аудитом (547d8a6). Итого 28 проблем. |
| 2026-07-30 | Добавлены F29-F33 (No Synchronization, RadioHandler unsubscribe, Debug.Log Release, GetComponentInChildren chain, duplicate spinners). Глубокий рескан: примитивы синхронизации, event lifecycle, debug logging, Unity API patterns. Итого 33 проблемы. |
| 2026-07-30 | Добавлены F34-F44 (BearingTable dead code, duplicate bearing algo, GetTransitionCount, null callsigns, null frequency, freq conflict, carrier clamp, phase types, texture leak, AudioSource ensure, SAD drift). Рескан 6 узких зон: SonneSignalGenerator, BeaconData, DspPipeline, PanelDragHandler, RadioInstaller, SAD Appendix B. Итого 44 проблемы. |
| 2026-07-30 | Добавлены F45-F52 (SAD deep scan: ADRs not implemented, Harmony targets wrong, 14+ non-existent classes, array size 4→14, layer counts, LOC, method signatures, missing classes). Тотальный перекрёстный аудит SAD vs code, git, build/config, cross-file. Итого 52 проблемы. SAD документы 01-12 + Appendix A/B/C обновлены. |
| 2026-07-30 | Добавлены F53-F64 (внешний аудит — проблемы, не задокументированные в предыдущих 52). F53: MorseAlphabet 'A' underscore (P0), F54-F58 (5 P1: dead code, ignored parameter, unsubscribed event, Release Debug.Log, NaN propagation), F59-F64 (6 P2: unused constant, SetSelectedGameObject spam, static not reset, struct aliasing, TMPro dependency, fragile struct copy). Итого 64 проблемы. |
| 2026-07-30 | Добавлены F65-F76 (аудит исходного кода — проблемы, не задокументированные в F01-F64). F65: MorseKeyer stale consolPattern (P0), F66-F70 (5 P1: NRE tryGetCallsign, NRE Toggle, reflection uncached, catch stack trace, FormatCompact misrepresentation), F71-F75 (5 P2: heavy Random in DSP, FPS-dependent lerp, duplicated Haversine, threshold mismatch, dead code), F76 (P3: int overflow). Итого 76 проблем. |
| 2026-07-31 | Добавлены F77-F96 (20 незадокументированных проблем, перекрёстная проверка: сканирование кода vs SAD + F01-F76). F77-F79 (3 P0: Pattern never updated, build case mismatch, InBeam hardcoded), F80-F83 (4 P1: Haversine NaN, MorseTiming truncation, exception swallow, zombie decisions), F84-F88 (5 P2: ExpanderHandler, GroundHandler raycast, CycleConfig stub, fake callsigns, nullable warnings), F89-F96 (8 P3: cleanup gaps, dead code, float drift, unnecessary ScrollRect, GetComponentInParent). Итого 96 проблем (F01-F96). |
| 2026-07-31 | Добавлены F97-F116 (20 незадокументированных проблем, полный рескан всех 34 source-файлов vs F01-F96). F97-F99 (3 P1: CursorHandler NRE, scheduler double-tick, stale position fix), F100-F108+110-F111 (11 P2: stale callsign, Pause display, per-frame GetComponent, Font alloc, AmbiguousMatch, StringBuilder audio thread, Debug.Log audio thread, uncached Type.GetType, List.Add audio thread, volatile fields), F112-F116 (5 P3: BeatFrequency sync, callsignLength reset, Dot fallback, no DSP reset, asymmetric null-clearing). Итого 116 проблем (F01-F116). |
| 2026-07-31 | Добавлены F117-F120 (4 незадокументированные проблемы, аудит event subscriptions, EventTrigger, hot path allocations). F117 (P1: SonneController keyer event leak), F118 (P2: ZoneHandler PointerClick no button filter), F119 (P1: RadioHandler OnDestroy missing zone unsubscribe), F120 (P2: SonneController signalEntries+measurements GC pressure). Итого 120 проблем (F01-F120). |
| 2026-07-31 | Добавлены F121-F130 (10 незадокументированных проблем, аудит Harmony патчинга, state machine, reflection performance, Morse standard, DSP config, geography). F121-F123 (3 P1: Harmony.UnpatchAll global, StationScheduler.Reset wrong phase, NavigationStartTime not initialized), F124-F128 (5 P2: redundant Type.GetType, silent Morse failure, no timing validation, hardcoded cycle config, antimeridian handling), F129-F130 (2 P3: Morse word boundary, BandConfig mutable). Итого 130 проблем (F01-F130). |
| 2026-07-31 | Добавлены F131-F136 (6 незадокументированных проблем, аудит position fix, audio engine, UI escape, API design, scheduler, Morse timing). F131 (P2: stations w/o frequency in position fix), F132 (P1: keyer/scheduler advance while radio off), F133 (P2: escape key blocked by child selection), F134-F136 (3 P3: Calculate mutates input, OnSequenceFinished no default, MorseTiming silence wrong). Итого 136 проблем (F01-F136). |
| 2026-07-31 | Добавлены F137-F164 (28 незадокументированных проблем, полный аудит Sound/Logic/UI/Data/Main vs F01-F136). F137-F139 (3 P1: Acos NaN, History bounds crash, UnpatchAll global), F140-F152 (13 P2: patch method null, frequency clicks, DSP unclamped, Atmosphere unclamped, zero divide, first callsign cycle, mutate on failure, float equality, no button filter, multitouch leak, overlapping bands, missing interpolation, catch loses stacktrace), F153-F164 (12 P3: Box-Muller waste, constructor ignores param, filter alpha recompute, undocumented impulse, duplicate BindingFlags, dead field, SerializeField overwrite, duplicated state queries, inconsistent rounding, DotTable ignores param, mutable struct copy, stub GetForCycleDuration). Итого 164 проблемы (F01-F164). |
| 2026-07-31 | Добавлены F165-F181 (17 незадокументированных проблем, финальный рескан: сканирование всех 34 source-файлов vs F01-F164 + проверка граничных случаев в state machine, DSP, UI validation, bootstrap). F165-F175: аудит MorseKeyer/StationScheduler/PositionFixCalculator/DspPipeline/MorseAlphabet/InformationPanelHandler/NavigationPattern + кросс-зависимости с F01-F164. F165 (P2: Reset не очищает sequence), F166 (P3: TextToPattern игнорирует param), F167 (P2: OnSequenceFinished missing phases), F168 (P2: ErrorKm всегда 0), F169 (P2: CarrierGenerator phase drift), F170 (P2: MorseAlphabet table overflow), F171 (P3: StateLog debug alloc), F172 (P3: TrimEntries O(n²)), F173 (P2: fragile font loading), F174 (P2: negative index OOB), F175 (P2: cross-type transition asymmetry). F176-F181: архитектурные проблемы Main.cs bootstrap (GameObject.Find, GetComponent кэш, ответственности, string paths, логирование) + DspPipeline RNG. F176 (P2: GameObject.Find), F177 (P3: deterministic noise), F178 (P3: repeated GetComponent), F179 (P2: bootstrap responsibilities), F180 (P2: string UI paths), F181 (P3: diagnostic noise). Итого 181 проблема (F01-F181). |
| 2026-07-31 | Добавлены F182-F189 (8 незадокументированных проблем, аудит build.ps1, MorseAlphabet, MorseKeyer, InformationPanelHandler, RadioStoryIntegration, PanelDragHandler, RadioInstaller, SonneController audio thread race). F190-F192: аудит PositionFixCalculator NaN-векторов и dead events в RadioHandler. F190 (P1): `Math.Asin` line 149 без clamp — F137 ошибочно ссылается на F80. F191 (P1): `Math.Asin` line 187 (`phi3`) без clamp. F192 (P3): `OnCursorAngleChanged` dead event. Итого 192 проблемы (F01-F192). |
| 2026-08-01 | Добавлены F193-F199 (7 незадокументированных проблем, финальный аудит: dead code в MorseAlphabet.GetElementCount, dead event в DebugAngleHandler.OnAngleChanged, dead method в RadioHandler.NotifyCursorChanged, build.ps1 debug/optimization flags, ExpanderHandler/PanelDragHandler missing IsInteractable checks, ExpanderHandler drag state cleanup). Итого 199 проблем (F01-F199). |
| 2026-08-01 | Добавлены F200-F207 (8 незадокументированных проблем, аудит «что не покрыто F01-F199»: кросс-проверка кода vs INDEX, математика drag-маппинга, state machine фаз, фид позиции, гео-агрегирование, символьные данные). F200 (P0): UpdatePosition без вызывающих — фикс всегда ~0°N 0°E. F201-F202 (2 P1): двойная передача Consol-паттерна за цикл (фаза Callsign мертва), сдвиг 90° значение/визуал у спиннеров. F203-F204, F207 (3 P2): кросс-талки при QF=20, антимеридиан-среднее долгот (latent), drag с дочерних контролов панели. F205-F206 (2 P3): кириллическая «В°», тики по кадрам вместо сэмплов (latent). Итого 207 проблем (F01-F207). |
| 2026-08-01 | **Верификация всех 207 гипотез против исходного кода.** Итог: 196 confirmed, 11 rejected (F43, F44, F45, F46, F47, F48, F49, F50, F51, F60, F96). Файлы перемещены: `confirmed\` (196 шт), `rejected\` (11 шт). SAD\*.md уже содержали исправления/аннотации (F45-F52), поэтому гипотезы F44-F51 о неактуальной документации опровергнуты. |
| 2026-08-01 | **Фаза 5: сверка с исходниками игры** (`C:\code\projects\uboat`). Подтверждены 5 Harmony-таргетов, AssetBundle (2021.3.33f1 vs 2021.3.45f2), имена типов, Decision-конструкторы, DI, манифест; игра компилирует мод из Source без `DEBUG`-символа. Вскрыты 4 поломки инжекции T3PLL38 (G1 stageField, G2 insertMethod, G3 OnUpdated, G4 LocalizationKey). F104, F186 → ✘ (перемещены в `rejected\` с переименованием). Итог: **194 confirmed / 13 rejected**. |
| 2026-08-24 | **Полная сверка решённости всех 194 confirmed-досье против текущего `Source/`** (6 срезов). Итог: ~160 закрыто (инлайн-теги `// F##:`, коммиты 3d4f958/28f4a62/f1e204b, удаление PositionFixCalculator), 22 открыто, ~10 частично. Построчные доказательства — новый `..\RESOLVED_LEDGER.md`; легенда дополнена ⏸/🔎. |
| 2026-08-01 | **Документация фазы 5** — создана папка `game-source-findings\`: G1-G4 (полные файлы-находки с доказательствами и фиксами), `INDEX.md` (сводка, порядок починки G2→G1→G4→G3), `GAME_SOURCE_REFERENCE.md` (справочник API мод↔игра, file:line). VERIFICATION_REPORT.md дополнен ссылками. |

---

## Пост-аудит 2026-09-15 (F208+)

Новый код после аудита F01–F207 (секстант, хронометр-слайсы, маяки, маркеры) + дыры в гейтах. Верификация — чтение исходников; критерий приёмки — тесты.

### Исправлено (тесты приложены)

| ID | P | Область | Суть | Тест |
|----|---|---------|------|------|
| **F208** | P1 | Chronometer · Input | `ChronometerDragProtocol` добавлял `GameInput.AddBlockade(this)`, но `RemoveBlockade` вызывался только для чужого `shipControlUI` → blockade мода утекал, маршрут/клики карты мертвы на сессию. Фикс: хранить blocker, `ReleaseBlockade()` в `EndMapDrag` + `OnDestroy` | SourceCheck `Blockade_AcquireReleaseBalanced` |
| **F209** | P1 | Data · StarSky | `IsNav` сгенерирован как V<2 (48), курируемый `NavStarNames` = 57 → 20 названных звёзд (вкл. Полярную, HIP 11767) выпадали из списка тел и подсветки. Фикс: `IsNav` = курируемый набор (57) | `StarCatalog_IsNav_MatchesCuratedNavStarNames`, `StarCatalog_Polaris_IsNavigational` |
| **F210** | P2 | Sound · Threading | `Atmosphere.NoiseFloor/ImpulseProbability`, `ReceiverDsp.WhistleLevel` писались main-thread, читались audio-thread без volatile. Фикс: volatile-поля + свойства | `Atmosphere_TuningFields_AreVolatile` (рефлексия `IsVolatile`) |
| **F211** | P2 | UI · Persistence | `cachedCursorAngle = -1f` как sentinel, но курсор −90..90 → отрицательный угол терялся на teardown. Фикс: `hasCachedCursor` | — (Unity-bound; live) |
| **F212** | P2 | UI · Markers | `BuildAll` без иконки не ставил `built` → пересборка+warning каждый кадр; `markersManager.transform.parent.name` NRE на root; `GetTooltipFont` без fallback. Фикс: latch + guard + `LegacyRuntime.ttf` | — (Unity-bound; live) |
| **F213** | P2 | UI · Markers | `LighthouseMapMarker`: `FindObjectsOfType` каждую 1 с всю сессию, warning об иконке без latch, `StartsWith` без `StringComparison`. Фикс: `staleCleanupDone` + `iconWarned` + Ordinal | — (Unity-bound; live) |
| **F214** | P1 | Gate · Analyzer | `HarmonyPatchAnalyzer`: при metadata `Found` (обфусцированные цели = все реальные патчи RadioStory/Locale) `continue` до `ValidateHandler` → RN0009/RN0003/RN0005 не проверялись. Фикс: RN0009 при metadata Found | `MetadataFound_BadReturnPrefix_StillReportsRN0009` |
| **F215** | P1 | Gate · Analyzer | RN0007 не видел `+=` строки, `new float[n]`, явный boxing-cast, lambda-аллокации. Фикс: +4 вида узлов | 4 теста RN0007 |
| **F216** | P1 | Gate · Analyzer | RN0008: namespace строго `==UnityEngine` → `UnityEngine.UI.*`/`TMPro.*`/`UBOAT.*` не считались main-thread. Фикс: prefix-match | `UnityUIInTaskRun_ReportsRN0008` |
| **F217** | P2 | Gate · Analyzer | RN0006 только Update/LateUpdate/FixedUpdate; нет `OnGUI` и корутин с `yield return null`. Фикс: +OnGUI, +per-frame корутины | `LookupInOnGUI_ReportsRN0006`, `LookupInCoroutineLoop_ReportsRN0006`, негативный `LookupInCoroutineWithWait_NoRN0006` |
| **F218** | P2 | Gate · Analyzer | RN0010 не ловил `var fi = GetField(...); fi.SetValue(...)` без guard. Фикс: следование за локальной + распознавание `IsNull()/IsNotNull()` | `AssignedThenUsedUnguarded_ReportsRN0010`, `AssignedThenGuardedWithIsNotNull_NoRN0010` |
| **F219** | P1 | Bootstrap · Reload | `OnLoaded` reset-блок под мёртвым `if (loaded)` (игра создаёт новый `Main`); старый assembly не выгружается, его `SceneEventsListener`-хендлеры рутят старую инстань. Фикс: reset безусловный + `SceneEventHandlerPurge` (рефлексия по backing-field события) | `SceneEventHandlerPurgeTests` (4) |
| **F224** | P2 | Sextant · Leak | `CreateEyecupTexture()` не сохранялся → `result.EyecupTexture` null → текстура (DontSave) и 3 `Sprite` не уничтожались; `SextantBloomCut` профили не уничтожались. Фикс: `Result.OwnedSprites` + Destroy в `OnDestroy`; профили в `SextantBloomCut.Destroy` | SourceCheck `SextantOwnedResources_AreDestroyed` |
| **F220** | P2 | Perf · Frame-hot | Per-frame reflection/alloc: `SextantPipPose` (GetComponent+GetProperty/GetField/SetValue каждый кадр), `SkyVisibilityResolver`/`GameTypeCache` конкат строковых ключей, `CameraResolver` `FindObjectOfType` + `Camera.allCameras`, `SextantController` делегаты. Фикс: `TypeMemberCache` (ключ = Type), per-camera кэши + ранний выход по маске, frame-memo + `GetAllCameras(buffer)`, кэш enum-значений, кэш делегатов | `TypeMemberCacheTests` (8), SourceCheck `FrameHot_ReflectionAndCameraLookupsCached` |
| **F221** | P2 | Logging | Release-логи без гейта: `BearingDiagnostics` (~5 Гц), `RadioStoryIntegration` (decision count), `StoryDecisionGateway` (stage), `RadioHandler` (visible), `LighthouseFlareDriver` (over-range). Фикс: единый `RadioNavDiagnostics.IsVerboseLogEnabled()` (PlayerPrefs `RadioNav.VerboseLog`, off by default, кэш) + `&& frame.verbose` | `VerboseLog_DefaultOff_AndToggleOn`, SourceCheck `ReleaseLogs_AreGated` |
| **F222** | P2 | Lighthouse · Budget | `LighthousePlacer.MaxConcurrent3D` недосчитывал `WantsSpawn && !Spawned` (pending-грант не занимал слот) → cap мог превышаться между тиками; ветка `if (placement.WantsSpawn) continue` недостижима. Фикс: `LighthouseSpawnBudget.Consumed/CanGrant`, мёртвая ветка удалена | `LighthouseSpawnBudgetTests` (6) |
| **F223** | P2 | Lighthouse · Perf | `LighthouseDotUi.ResolveImageType` при неудаче пересканировал все сборки и логировал warning **каждый кадр**. Фикс: latch `imageTypeResolveFailed` | SourceCheck `LighthouseDotUi_ImageTypeFailureLatched` |
| **F226** | P2 | Data · StarCatalog | «49 дублей HIP» — **ложная тревога**: точных дублей 0; общий HIP = кратные системы с разной Vmag, строки удалять нельзя (`ConstellationLines` индексирует массив). Реальный эффект — дубли имён в bodies-readout. Фикс: дедуп по HIP в `SextantBodiesReadout.BuildStarEntries` (кроме `Hip=0`) | `StarCatalog_NoExactDuplicateRows` |
| **F228** | P2 | Gate · Build | `build.ps1` определял `DEBUG/RELEASE`, игра — `UBOAT;UBOAT_2024_1;UNITY_STANDALONE_WIN` без DEBUG/RELEASE → локальный билд компилировал другие `#if`-ветки. Фикс: игровой набор символов (`Debug` добавляет `DEBUG` локально) | SourceCheck `BuildScript_GameDefineParity` |
| **F227** | P1 | Gate · Tooling | **Закрыто:** (a) SourceCheck `ModRoot` от `Assembly.Location` (walk-up до Source+Manifest.json), `GameRoot`/`ManagedDir` — env `UBOAT_GAME_SOURCE`/`UBOAT_GAME_PATH`; (b) гейт `TestProject_LinksAllSourceFiles` (пропущенный `ConstellationLines.cs` слинкован + тест); (c) **V3080/V3095 включены обратно**: 18 хитов устранены в исходниках — extension-гарды `IsNull()/IsNotNull()` на этих путях заменены явными `== null`/`!= null` (все цели — Unity-типы с overloaded-оператором либо обычные ссылки), + реальное усиление `LighthouseBeamGlow.ApplyDotLayout` (guard dot/halo rect). `[SuppressMessage]` недоступен (sandbox запрещает `System.Diagnostics.*`), per-line/per-file suppression у PVS для C# нет | SourceCheck `TestProject_LinksAllSourceFiles`; PVS clean с V3080/V3095 |
| **F229** | P1 | Live · AssetBundle | **Закрыто (live-подтверждено 2026-09-15):** в игре 2026.1-patch21 игра не регистрирует AssetBundle мода в `ResourceManager` (`assetToBundleMap` пуст → `InstantiatePrefab("Radio")` = SimpleResource → null), панель и маркеры отсутствовали. Mod-side fallback: если lookup вернул null — грузим `Bundles/testui` сами (рефлексия, т.к. `AssetBundle` в `UnityEngine.AssetBundleModule` не в referenced-наборе) и инстанцируем `Radio`; при уже загруженном бандле переиспользуем. Live: `radionav.loaded=true`, станции декодируются, маркеры 1+3. Попутно: игра 2026.1 грузит **`Temp/radionavigation.dll`** и не перекомпилирует Source при старте — держать DLL в синхроне | live `radionav`/`mod_state`; `build.ps1` PVS clean |

### Открыто

Открытых пунктов нет. F208–F229 закрыты (F229 подтверждён live).

**Гейты на 2026-09-15 (исторический снимок):** `tests.ps1` 786 PASS / 0 fail (было 761); `RadioNavigation.Analyzers.Tests` 68 PASS (было 55); `RadioNavigation.SourceCheck` 51 PASS (было 44); `build.ps1` Build OK + PVS clean; `analyze.ps1 -Profile quick` baseline clean; `analyze.ps1 -RnAudit` clean (0 genuine).
> Актуальные числа — в разделе «Пост-аудит 2026-09-25» ниже и в `AGENTS.md` § Gotchas.

---

## Пост-аудит 2026-09-25 (F231–F243)

Проверка выполнена по плану `..\PLAN_Phase6_LoadGate_Hygiene.md`; live-рестарт и основные
критерии §9 пройдены, остаются физический LMB-тест и recovery на заранее залипшей сессии.

| ID | P | Суть | Статус / доказательство |
|----|---|------|--------------------------|
| **F231** | P0 | Повторный `SavesManager.Load` без re-entrancy guard → NPE, orphaned `LoadingUIController.blockades`, `timeScale=0` | ✅ `McpLoadGate` + guards; live: `Autosave_1/2` 215 точек, 0 NPE, concurrent load отклонён |
| **F232** | P1 | Неопределённый порядок `MapMarkerPointBridge.LateUpdate` и игры | ✅ `[DefaultExecutionOrder(1000)]`; `LifecycleOrder_And_DiagnosticsCache_ArePinned` |
| **F233** | P1 | Два секстантных feature-ключа не кешировались → 2 `PlayerPrefs`/кадр | ✅ `RadioNavDiagnostics` cache; мутация гейта ловит удаление ключей |
| **F234** | P1 | DSP использовал 44100 при живом device 48000 → +146.7 цента | ✅ `DeviceSampleRate`; `AudioSynthesisEngine_UsesDeviceSampleRate`, offline fallback test |
| **F235** | P2 | `LighthouseDotUi` и горячие rect-getters + boxing `Color` | ✅ cached rects + alpha latch; `LighthouseDotUi_CachesRectsAndSuppressesUnchangedColor` |
| **F236** | P2 | Verbose defaults on иFIXLAYOUT log без change-gate | ✅ defaults off, `RadioNavDiagnostics.Stamp`, расширенный `ReleaseLogs_AreGated` |
| **F237** | P2 | RN0006/RN0007 однопрыжково не видят вынос в helper | 🔎 подтверждено; полноценный analyzer fix отложен, diagnostics gate уже следует one-hop |
| **F238** | P2 | RN0007 проверяет только тело `OnAudioFilterRead`; чистота audio path доказывается тестом | 📌 задокументировано в Phase 6; analyzer scope не расширен |
| **F239** | P2 | `perf_sample` отдаёт неполное окно как обычный результат | ✅ `"stale": true` при `complete:false`; bridge selftest |
| **F240** | P2 | `0.0.0.0` в логе не идентифицирует build; нет canary версии игры | ✅ manifest + fingerprint + `VerifiedGameVersion` warning |
| **F241** | — | Старые пункты `SEXTANT_PLAN`/Phase 4/5 и 24 открытых пункта | 📌 план сверки зафиксирован; массовая правка документации не выполнялась |
| **F242** | — | Сводка 50 агентов наполовину устарела | ✅ повторная сверка P2/P3; неподтверждённые пункты не внедрялись |
| **F243** | P2 | `loading.ready` требовал portraits при `LoadingUIController.IsLoading=false`; inactive HUD давал ложный not-ready | ✅ условная проверка + `Resources.FindObjectsOfTypeAll`; live 60 fps, `ready=true` |

**Гейты после Phase 0–5:** `tests.ps1` **1135 PASS**; `RadioNavigation.SourceCheck`
**66 PASS**; bridge compile/analyze/selftest PASS; `build.ps1` Build OK + PVS clean;
`analyze.ps1 -Profile quick` — 189 baseline diagnostics, `baseline gate clean`; graphify
обновлён (8187 nodes / 14591 edges).
