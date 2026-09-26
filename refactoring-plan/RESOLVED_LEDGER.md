# RESOLVED_LEDGER — закрытие гипотез аудита F01–F207

> Создан 2026-08-24. Полная сверка всех 194 confirmed-досье против текущего `Source/` (6 срезов, каждый файл прочитан, якоря проверены grep'ом).
> Легенда доказательств: **TAG** = инлайн-маркер `// F##:` рядом с цитируемым местом · **GONE** = цель устранена (метод/класс/ветка удалены или переписаны) · **DEFERRED** = закрыт без правки, см. `DEFERRED.md` · **COMMIT** = коммит с фиксом · **LIVE** = live-валидация, правка не требовалась.
> Семантика: `hypothesis/INDEX.md` отвечает «была ли проблема реальна» (✅/✘); этот файл — «что с ней стало».

## 1. Закрыто с инлайн-тегом в коде (TAG)

| ID | Доказательство |
|----|----------------|
| F61 | ToggleHandler.cs:167 reset tag |
| F65 | MorseKeyer.cs:46 consolPattern=null |
| F66 | SonneController.cs:708 cites F66 |
| F72 | SpinnerHandlerBase.cs:113 deltaTime-scaled |
| F74 | ComparisonExtensions.cs:31 thresholds documented |
| F75 | MorseAlphabet.cs:66 DecodeMorse/ToTicks removed |
| F77 | SonneSignalGenerator.cs:92 pattern packing |
| F78 | build.ps1:76-78 /define:DEBUG mapping |
| F79 | SonneSignalGenerator.cs:76 InBeam from sector |
| F80 | GeoMath.cs:16 clamp rounding overshoot |
| F84 | ExpanderHandler.cs:23 null-parent guard |
| F85 | GroundHandler.cs:82 raycastTarget=false |
| F87 | SonneController.cs:706 no fake callsign |
| F88 | InformationPanelHandler.cs:351 scoped diagnostics |
| F89 | SonneController.cs:146 audio stop in OnDestroy |
| F90 | RadioHandler.cs:10 dead event removed |
| F92 | ConsolCycleConfig.cs:21 unused fields removed |
| F94 | AudioSynthesisEngine.cs:17 double accumulator |
| F95 | CursorHandler.cs:40 ScrollRect dropped |
| F97 | CursorHandler.cs:108 Instance guard |
| F98 | AudioSynthesisEngine.cs:110 one tick per logical tick |
| F101 | InformationPanelHandler.cs:335 domIdx<0 branch |
| F102 | CursorHandler.cs:43 cachedScrollRect |
| F103 | InformationPanelHandler.cs:164 cachedArialFont |
| F106 | AudioSynthesisEngine: Debug.Log removed |
| F107 | RadioStoryIntegration.cs:152 cached delegates |
| F109 | SignalRichTextFormatter.cs:72 collapse (cites F120) |
| F111 | MorseKeyer.cs:24 volatile isActive |
| F115 | SonneController.cs:151 engine.Reset() |
| F116 | MorseKeyer.cs:46 Load clears consolPattern |
| F117 | SonneController.cs:161-171 unsubscribe loop |
| F118 | ZoneHandler.cs:66 single PointerClick entry |
| F119 | RadioHandler.cs:73 OnZoneChanged -= |
| F121 | Main.cs:101 UnpatchAll(harmony.Id) |
| F122 | StationScheduler.cs:120 Reset→Phase.Pause |
| F126 | ConsolCycleConfig.cs:78 IsValid() |
| F129 | MorseAlphabet.cs:81-89 word pauses |
| F130 | DialConfig.cs:9 class→struct BandConfig |
| F132 | AudioSynthesisEngine.cs:68 radioOn gate |
| F133 | RadioHandler.cs:98 IsChildOf check |
| F135 | StationScheduler.cs:112 default case |
| F136 | MorseTiming.cs:57 ToSilenceTicks warns |
| F141 | DspPipeline.cs:28 one-pole smoothing |
| F142 | DspPipeline.cs:185 Clamp(-1,1) |
| F143 | DspPipeline.cs:101 Atmosphere clamped |
| F144 | NavigationPattern.cs:41 zero→8 default |
| F147 | RadioHandler.cs:28 Mathf.Approximately |
| F148 | GroundHandler.cs:111 left-button filter |
| F149 | SpinnerHandlerBase.cs:87 OnDisable resets drag |
| F151 | DialConfig.cs:118 interp from AngleStart |
| F152 | Main.cs:275 LogException added |
| F153 | DspPipeline.cs:104 cached cosine half |
| F154 | DspPipeline.cs:148 sampleRate stored+used |
| F155 | DspPipeline.cs:134 cachedAlpha in setter |
| F156 | DspPipeline.cs:56 named LoudCrackle constants |
| F159 | DebugAngleHandler.cs:28 null-guarded fallback |
| F160 | IPH.cs:261 delegates to handlers |
| F161 | DialConfig.cs:156 kHz formatting |
| F164 | ConsolCycleConfig.cs:76 real 40/60/120 paths |
| F165 | MorseKeyer.cs:88 Reset clears sequence |
| F166 | MorseAlphabet.cs:70 parameter removed |
| F169 | DspPipeline.cs:30 phase normalized [0,1) |
| F171 | GroundHandler.cs:98,144 #if DEBUG wrap |
| F172 | SignalRichTextFormatter.cs:72 Trim collapsed |
| F173 | IPH.cs:164 LegacyRuntime.ttf fallback |
| F174 | NavigationPattern.cs:48 negative index guard |
| F175 | MorseKeyer.cs:181,210 cross-type transitions reworked |
| F177 | AudioSynthesisEngine.cs:45 TickCount seed |
| F183 | MorseAlphabet.cs:59 c<0 removed |
| F184 | MorseKeyer.cs:252 IsCurrentElementDash shared |
| F185 | IPH.cs:73 Text[Stations.Length] |
| F187 | PanelDragHandler.cs:175 isDragging guard |
| F188 | RadioInstaller.cs:88 localPosition=zero |
| F192 | RadioHandler.cs:11 event deleted (comment cites F192) |
| F196 | build.ps1:80-94 per-config debug/optimize split |
| F197 | ExpanderHandler.cs:41 IsInteractable check |
| F198 | PanelDragHandler.cs:110-113 IsInteractable check |
| F199 | ExpanderHandler.cs:65-71 OnDisable resets flags |

## 2. Закрыто удалением цели (GONE)

| ID | Что исчезло |
|----|-------------|
| F13 | Debug.Log/Time.time в Source/Sound — ноль совпадений |
| F16, F18, F64, F73, F99, F128, F131, F134, F137, F146, F168, F204 | PositionFixCalculator.cs удалён целиком (позиционная цепочка снята 2026-08-08) |
| F19 | NavigationPattern StringBuilder L65,L89 — переписан |
| F25 | ProcessAudio raw arrays — параметр schedulers убран |
| F26 | PrefabNames.* приняты (5 файлов, вкл. Main.cs) |
| F27* | частично: методы остаются, но anti-pattern признан style-only (см. DEFERRED) |
| F34, F35, F36 | BearingTable/GenerateBearingTable/GetTransitionCount — pipeline удалён |
| F37, F38, F39 | BeaconData переписан под CAP-59: callsigns non-null, Plougourvest/332.5 кластеры заменены исторически корректным набором |
| F40 | Frequency clamp 0..20000 в DspPipeline.cs:19 |
| F42 | PanelDragHandler.cs:223 OnDestroy уничтожает текстуру (cites F42) |
| F53 | MorseAlphabet underscore bug — таблица переписана |
| F54 | UpdateStationBeats удалён |
| F57 | RadioInstaller.cs:40-52 логи обёрнуты #if DEBUG |
| F58 | NaN-guard: вызывающий валидирует до DialConfig |
| F59 | PrefabNames.DialFace принят в ToggleHandler.cs:97 |
| F67 | IPH.cs:245 cachedToggle null-checked (через F178) |
| F68 | localeType/loadSingleton static-кэш (теперь в gateway) |
| F69 | Main.cs:272-275 Debug.LogException добавлен |
| F76 | GetElementCount(string) удалён |
| F81 | MorseTiming Math.Round везде + golden-конфиг (морзе-канон 2026-08-23) |
| F86 | ConsolCycleConfig 40/60/120 dispatch + IsValid (дубль F164) |
| F91 | NormalizeAngle удалён |
| F93 | RadioInstaller логи #if DEBUG |
| F100 | IPH.cs:454-488 переписан, bounds guard :468 |
| F108 | signalEntries Add+Trim остался, но main-thread — гонка мертва |
| F110 | dominantStationIndex int, обе стороны main-thread после redesign |
| F127 | GenerateBearingTable удалён; GetForCycleDuration реальный :88 |
| F145 | callsignLength поле исчезло (API keyer переписан) |
| F162 | BeaconData.GetDotCountToBearingTable удалён |
| F166* | см. также TAG-таблицу |
| F170* | открыт — перенесён в §5 |

## 3. Закрыто без правки кода (DEFERRED / LIVE)

| ID | Основание |
|----|-----------|
| F11, F14, F24, F30, F33, F41, F55, F56, F67, F74, F76, F82*, F120, F167 | DEFERRED.md «Verified fixed without a new edit» |
| F12, F28 | Constants.cs / GeoMath.cs существуют (ADR-005 направление) |
| F15 | PanelStateStore.cs (PlayerPrefs) реализован |
| F22 | Тестовая инфраструктура: 263 PASS, baseline ADR-014 |
| F52 | Appendix_B дополнен тремя классами (до архивации приложений) |
| F190, F191 | COMMIT 3d4f958 (clamp Asin/Acos); класс позже удалён f1e204b — moot |
| F200 | UpdatePosition теперь вызывается из position feed (SonneController.cs:435) |
| F202 | LIVE-валидация: угол ручки корректен, правка не нужна (DEFERRED) |
| F203 | LIVE-валидация: кросс-талков нет, QF не трогаем (DEFERRED) |
| F207 | TAG PanelDragHandler.cs:115-129 — guard от child-hijack |
| F182 | build.ps1:9-15 Test-Path GamePath validation |
| F189 | Гонка устранена иначе: keyer Advance на main thread, ProcessAudio read-only (AudioSynthesisEngine.cs:118) |

## 4. Вероятно закрыто (проверить при следующем касании)

| ID | Причина предположения |
|----|----------------------|
| F201 | Scheduler/keyer переписаны в golden-audio эпоху; семантику фазы Callsign сверить при ревизии StationScheduler.cs:64 |
| F205 | Формат с кириллической «В°» в UI-коде не находится; вероятно исчез при переписывании FormatFrequency/FormatCompact |
| F206 | tickAccumulator теперь double += deltaSeconds с TickIntervalSeconds (AudioSynthesisEngine.cs:71-74) — стерео-удвоение устранено; подтвердить тестом |

## 5. Всё ещё ОТКРЫТО (22) — снимок на 2026-08-24

> **Пересчёт 2026-09-26 (частичный).** Список ниже — снимок на 2026-08-24, в нём
> ровно 22 находки. За прошедшие месяцы часть из них закрыта. Перепроверено по
> текущему коду:
>
> | Находка | Прежнее состояние | Факт на 2026-09-26 | Доказательство |
> |---|---|---|---|
> | F70 | открыта (`FormatCompact` вводит в заблуждение) | **закрыта** — метода `Format` в `NavigationPattern` больше нет | `Source/Logic/NavigationPattern.cs`: 0 совпадений (dead sweep #2) |
> | F105 | открыта (аллокация `StringBuilder` в аудио-пути) | **закрыта** — `StringBuilder` в `SonneController` не используется; строки 204-206 заняты null-guard'ом `Sandbox.Instance` | `Source/Logic/SonneController.cs`: 0 совпадений |
> | F112 | открыта (`DspPipeline.Frequency` не `volatile`) | **закрыта** — `targetFrequency` объявлен `private volatile float` | `Source/Sound/DspPipeline.cs:12` |
> | F114 | открыта (тихий fallback на `Dot` прятал out-of-range) | **закрыта** — fallback сохранён как безопасное значение, но теперь пишет `LogError` с диагностикой | `Source/Logic/MorseKeyer.cs:266-272` |
> | F125 | открыта (молчаливый `return ""`) | **частично** — баг unsigned-char устранён (это отдельная F183), но `""` на неотображённый символ и на `c >= table.Length` остаётся | `Source/Logic/MorseAlphabet.cs:61-63` |
> | F83 | открыта (zombie-решения при hot-reload) | **не перепроверено** — класс `RadioStoryIntegration` в дереве есть; ядро G1-G4 закрыто через `StoryDecisionGateway.cs` (28f4a62), но сам класс не удалён | `Source/Logic/RadioStoryIntegration.cs:13` |
>
> **Остальные 16 находок этого раздела (F02, F03, F05, F17, F21, F27, F31, F32,
> F62, F63, F71, F82, F163, F170, F179, F181) в этом проходе не перепроверялись.**
> Число 22 — размер снимка, а не актуальный статус; полный пересчёт требует
> отдельного прохода по коду. Не переписывайте это число на глаз.
>
> Поздние раунды (F208-F229, F231-F243) закрытия не добавили: их состояние
> указано прямо в `hypothesis\INDEX.md`.

God-object/static/logging хвост = территория DEFERRED + PLAN_Phase5:

`F02` SonneController monolith · `F03` IPH monolith · `F05` ToggleHandler statics · `F17` StationState mutable struct · `F21` Main.cs multi-responsibility · `F27` ComparisonExtensions anti-pattern · `F31` 210 Debug.Log (14 guarded) · `F32` IPH 5×GetComponentInChildren · `F62` NavigationPattern Group[] в struct · `F63` DebugAngleHandler TMP dependency · `F70` FormatCompact transition misrepresentation · `F71` Random per sample в audio thread · `F82` Main catch swallow (deliberate, но LogException уже есть — переоценить) · `F83` RSI hot-reload zombie decisions · `F105` StringBuilder allocs SonneController.cs:204-206 · `F112` DspPipeline.Frequency non-volatile · `F114` MorseKeyer Dot fallback masks OOB · `F125` MorseAlphabet silent "" return · `F163` StationState struct aliasing · `F170` table[128] unguarded · `F179` Main bootstrap multi-duty · `F181` DIAG logs persist

Частичные (MIXED, ~10): F01 (volatile есть, locks нет) · F04 (цепочка мертва, god-object фасада остался) · F06-F10 (флагманские пункты закрыты, хвост не весь) · F20 (Band I MHz остался) · F23 (fragility снижена, RSI.cs жив) · F29 (барьеров нет) · F123 (guard добавлен, поле не инициализируется) · F138 (IPH защищено, дубликат в SonneController.cs:498 нет) · F176 (константа централизована, Find остался) · F178 (кэширование широкое, одиночки остались) · F180 (PrefabNames константы, строковые пути остались)

## 6. Отвергнуто (13)

F43–F51 (already-fixed в SAD до верификации), F60, F96, F104 (never-true), F186 (subclaim-only: отклонён лишь param-bounds механизм; ядро G1-G4 реально и закрыто gateway'ем — см. баннер в DEFERRED.md).

---
*Источник данных: полная сверка 2026-08-24 (6 параллельных срезов + личная верификация F200–F207). Следующее обновление — после выполнения слайсов PLAN_Phase5.*
