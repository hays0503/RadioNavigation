# Отчёт о верификации гипотез F01–F207

**Дата:** 2026-08-01 (фаза 5 — 2026-08-01)
**Метод:** статический анализ исходного кода (33 файла, `Source\**`, ~4 600 строк), grep-проверки callers/деклараций, чтение SAD-документов. Фаза 5: сверка с исходниками игры (`C:\code\projects\uboat`, полный C#-проект сборки `com.uboat.game`, Unity 2021.3.45f2, Harmony 2.2.2). Запуск игры не выполнялся — runtime-поведение (Unity API, AssetBundle-арт, сцена) вне рамок проверки.
**Файлы:** `confirmed\` (194), `rejected\` (13), `INDEX.md` — колонка «Статус» для всех 207.

---

## Итоги

| Метрика | Значение |
|---------|----------|
| Всего гипотез | 207 |
| Подтверждено (✔) | **194** |
| Опровергнуто (✘) | **13** |
| Дубли/связки (внутри confirmed) | ~15 (перечислены ниже) |
| Новые находки фазы 5 (G1–G4) | 4 (см. секцию «Game source verification»)

---

## Опровергнутые гипотезы (rejected)

| ID | Гипотеза | Причина опровержения |
|----|----------|----------------------|
| **F43** | AudioSource не добавлен при готовом SonneController | `RadioInstaller.cs:69-73` — `if (GetComponent<SonneController>().IsNull())` → внутри ветки `EnsureComponent<AudioSource>` (:71) И `AddComponent<SonneController>` (:72). AudioSource обеспечивается на основном пути |
| **F44** | SAD class diagram 10+ расхождений | SAD уже исправлен/аннотирован до нас: Appendix B содержит F51/F47/F160-заметки, ProcessAudio 9 params, EmitFrequency исправлен. Остаточное: App B:97 `MorseKeyer.Configure` (реально Load/LoadConsol) |
| **F45** | ADRs Accepted, но не реализованы | `SAD/12:16-21` — ADR-002/003/004/005/007 переведены в **Deferred** (аннотация «F45 исправлено») |
| **F46** | Harmony-таргеты в SAD неверны | `SAD/02:66-71` — исправлено; реальные таргеты Main.cs:39-43 совпадают с гипотезой |
| **F47** | 14+ несуществующих классов | SAD аннотировал их как «? proposed» (SAD/12:100,157,161-163,171-172; App B:4,299,551) |
| **F48** | Массив станций «4» в ADR-003 | `SAD/03:248`, `SAD/04:155-157` — уже «14 stations» |
| **F49** | Неверные счётчики слоёв | `SAD/03:95-101` — уже 15/12/5/2/34 (верно) |
| **F50** | Неверные LOC | `SAD/03:184-252` — уже 508/240/224/126/146/34/381/158 (верно). **Остаточное:** LOC UI-секции SAD/03 всё ещё грубо неверны (см. ниже) |
| **F51** | Неверные сигнатуры в SAD | App B аннотирован (Atmosphere.Apply, ReceiverDsp.Process, ProcessAudio 9 params, EmitFrequency). Более того, сама гипотеза неточна: `ApplyFeedback` реально `(float angle)` (AES:40), `GetCurrentConsolPattern` — метод, не свойство |
| **F60** | CursorHandler спамит SetSelectedGameObject каждый фрейм | `CursorHandler.cs:127` — уже есть guard `currentSelectedGameObject != gameObject`; вызов :128 происходит только при скролле и наведении, не каждый фрейм |
| **F96** | IPH GetComponent вместо GetComponentInParent | По дизайну RadioInstaller:20/72/75 RadioHandler, SonneController и InformationPanelHandler ставятся на ОДИН GameObject — `GetComponent` корректен |
| **F104** | AmbiguousMatchException из-за совмещения NonPublic+Public BindingFlags | Перегрузок `InsertDecision` в игре нет (единственная сигнатура `Story.cs:377`) → AmbiguousMatch невозможен. Реальная проблема — G2 (GetMethod на BaseType=GenericStory → null), см. фазу 5 |
| **F186** | `GetParameters()[1]` → IndexOutOfRange | `InsertDecision(int, Decision)` — 2 параметра, `[1]` корректен; к тому же guard `insertMethod.IsNotNull()` не срабатывает (insertMethod и так null, G2) → обращение недостижимо |

Примечание: F44–F51 — «исторические неточности документации»: проблемы существовали в оригинальном SAD, но рабочие копии SAD уже содержат исправления и аннотации (вне нашей работы). Отдельные остаточные расхождения SAD задокументированы ниже. F104/F186 опровергнуты исходниками игры (фаза 5).

---

## Фаза 5 — Game source verification (исходники игры, C:\code\projects\uboat)

Полный исходник игры доступен: сборка `com.uboat.game` (net40), Unity **2021.3.45f2** (UnityPlayer.dll), 0Harmony **2.2.2** в `Managed\`. Установленная `com.uboat.game.dll` (mtime 2026-06-21) на 2 дня старше исходников (csproj 2026-06-23) — предположительно тот же контент; `Application.version` статически не определяется.

### 5.1 Подтверждено совпадение мод ↔ игра (было runtime-only, стало статически)

| Что проверено | Результат |
|---------------|-----------|
| Harmony-таргеты ×5 | Все существуют: `RadioStory.ContinueStory` (public override, RadioStory.cs:262), `Locale.RetrieveString(string)` (Locale.cs:588) и `(string,string)` (:613) — **instance**-методы, `RadioStory.OnDecisionSelected(Decision)` (protected override, :402), `OnStopped()` (protected override, :126) |
| AssetBundle | Мод собран Unity **2021.3.33f1** (заголовок UnityFS), игра 2021.3.45f2 — тот же мажор → совместимы |
| Имя сборки | `com.uboat.game` (csproj) → первая ветка `Type.GetType("..., com.uboat.game")` в RSI:297-320 работает |
| `Decisions` | `public ReadOnlyCollection<Decision>` (Story.cs:186) — обёртка над тем же листом, что и `InsertDecision` (Story.cs:377/379) → IList-каст мода (RSI:126) корректен |
| `Decision`-конструкторы | 3 public (Story.cs:44/57/70); мод выбирает ctor с `pars[3] == typeof(Action)` → Story.cs:57 ✓; аргументы CreateDecision (RSI:201-229) совместимы |
| `SelectDecision` | `ApplyCost → OnDecisionSelected → CallCallback` (Story.cs:321-325) — точка патча мода верна |
| `Locale.Language` | Свойство существует (Locale.cs:181) ✓; `ScriptableObjectSingleton.LoadSingleton(Type)` (ScriptableObjectSingleton.cs:28) ✓ |
| Manifest.json | Поля мода совпадают с `ModManifest` (ModManager.cs:17-31); `dependencies` игнорируется; `supportedGameVersions` в игре **не валидируется** |
| Путь загрузки | Игра грузит моды из `persistentDataPath\Mods\<id>\` → совпадает с размещением (AppData\LocalLow\Deep Water Studio\UBOAT\Mods) |
| Компиляция мода | Игра **компилирует `Source\*.cs` сама** (Mod.cs `LoadAssembly`/`Compile`: Roslyn, defines `UBOAT`, `UBOAT_2024_1`, `UNITY_STANDALONE_WIN`, **без DEBUG**; устаревание — по mtime vs `com.uboat.game.dll`). Собранный build.ps1 dll в рантайме не используется |
| Harmony в игре | Игра сама **не патчит** (0 вызовов Create/PatchAll) — 0Harmony.dll возится только для модов |

### 5.2 Новые находки — инжекция решения T3PLL38 мертва (4 независимые поломки)

| ID | Поломка | Детали |
|----|---------|--------|
| **G1** | `stageField` = null | `GetField("stage")` на RadioStory — поля `stage` в RadioStory/GenericStory/Story **нет** (grep всей игры: только HelpBehaviour.cs). RSI:113 → «Init failed, cannot inject T3PLL38» → решение не инжектится → панель не открывается |
| **G2** | `insertMethod` = null | RSI:56 `typeof(RadioStory).BaseType` = `GenericStory`, а `InsertDecision` объявлен в `Story` (Story.cs:377); `GetMethod` **не трассирует базы** → null → `decisionType` null → `Init()` выходит на RSI:69-71 |
| **G3** | `onUpdatedMethod` = null | `OnUpdated()` объявлен в `Story` (Story.cs:438, protected), ищется на GenericStory → null → NotifyUpdated только логирует warning |
| **G4** | `localizationKeyProp` = null | `LocalizationKey` — **readonly поле** (Story.cs:24), а мод делает `GetProperty` (RSI:74) → null → `DecisionAlreadyExists` не дедуплицирует, `HandleCustomDecisions` всегда fail-open (AllowOriginal) |

Итог G1–G4: в текущей версии игры весь RadioStoryIntegration мёртв (панель не открывается через радио-историю, переключение не перехватывается, локализация фолбэк «english»). Это усиленное подтверждение F23 (Reflection Fragility) и причина для отдельного плана фикса (по договорённости — только документировано).

Полные файлы находок: `game-source-findings\G1_stageField_dead.md`, `G2_insertMethod_null.md`, `G3_onUpdatedMethod_null.md`, `G4_localizationKeyProp_null.md`, сводка — `game-source-findings\INDEX.md`, справочник API мод↔игра — `game-source-findings\GAME_SOURCE_REFERENCE.md`.

### 5.3 Дельты вердиктов гипотез

- **F104 → ✘** (см. таблицу rejected): перегрузок нет, AmbiguousMatch невозможен.
- **F186 → ✘** (см. таблицу rejected): 2 параметра, `[1]` корректен; guard недостижим.
- **F31 / F57 → дельта**: 12 `#if DEBUG`-блоков в рантайме **не компилируются** (игра не передаёт DEBUG-символ) → «81 Debug.Log в Release» становится «81 всегда + 12 никогда». F57 (блок без guard) остаётся подтверждённым.
- **F78 → дельта**: `/define:Debug` в build.ps1 — реальный баг локальной сборки (Debug-конфиг не даёт `#if DEBUG`), но на рантайм не влияет: игра перекомпилирует из Source без DEBUG.
- **F83 → уточнение**: механизм теперь точен — `ClearDecisions()` (GenericStory.cs:577) сбрасывает решения каждый `ContinueStory`; инжекция (постфикс) переживает до следующего ContinueStory; после `OnStopped` UI закрыт → риск «зомби» низкий. Впрочем, из-за G1-G4 инжекция сейчас вообще не работает.
- **F121/F139 → дельта**: мод создаёт собственный инстанс `new Harmony("UBOAT.Mods.RadioNavigation")` (Main.cs:35) → `UnpatchAll()` (Main.cs:30/78) снимает только свои патчи; игра не патчит → риск ограничен перезагрузкой мода.
- **F68/F107 → дельта**: имя сборки подтверждено, первая ветка работает; uncached Type.GetType per call остаётся.
- **F01/F13/F206 → контекст**: в игре **нет** `OnAudioFilterRead` (0 вхождений) → аудио-поток целиком мод-сторона; F206 (mono silentClip SC:14) остаётся latent-подтверждённым.
- **F176/F180 → подтверждены**: `GameObject.Find("/GUI/Viewport")` (Main.cs:138) в коде игры не создаётся (сцена, непроверяемо статически) — тот же класс хрупкости.
- **F89/F112/F117 → пере-подтверждение не требуется** — мод-внутренние, от игры не зависят.

---

## Остаточные расхождения SAD (после правок «исправлено v2.0»)

1. **SAD/03 UI-секция (3.1)** — LOC 6 файлов грубо занижены: ToggleHandler «~48» (реально 158), ZoneHandler «~25» (147), CursorHandler «~37» (135), SpinnerHandlerBase «~15» (109), PanelDragHandler «~20» (147), GroundHandler «~83» (143). Logic/Data-секции корректны.
2. **Appendix B:97** — `MorseKeyer.Configure(pattern, scheduler)` не существует; реально `Load` + `LoadConsol`.
3. **Appendix B 1.1** — RadioStoryIntegration и SonneSignalGenerator отсутствуют в диаграмме (F52 — единственная подтверждённая гипотеза SAD-группы).

---

## Подтверждённые P0 (11 из 13; F45/F46 опровергнуты)

F01 (audio race), F05 (static mutable state), F13 (Unity API в audio thread), F23 (reflection fragility), F29 (0 примитивов синхронизации), **F53** (MorseAlphabet 'A' = `._` — underscore вместо dash, MorseAlphabet.cs:12), **F65** (stale consolPattern в MorseKeyer.Load, MorseKeyer.cs:31-44/203-212), **F77** (stations[i].Pattern никогда не обновляется, SC:423), **F78** (build.ps1:35 `/define:Debug` vs `#if DEBUG` — case-sensitive), **F79** (SC:409 `InBeam = true` хардкод), **F200** (UpdatePosition — 0 callers, фикс всегда 0°N 0°E).

## Заметные confirmed (неполный перечень)

- F31: 93 Debug.Log, 12 под `#if DEBUG` → 81 в Release; F57/F93: RadioInstaller:40-49 блок Debug.Log без `#if DEBUG`.
- F37/F38/F39: BeaconData — 6× null callsign, 1× null frequency, 3 станции на 332.5 kHz.
- F66/F87: SC:477 fallback `Name.Substring(0,2)` без null-чека → фейковые позывные.
- F70: FormatCompact рисует transition-группу сплошными точками; F81: MorseTiming truncation — элементы на 50 мс короче; F98: до 3 scheduler-тиков за аудиоцикл (AES:92/:114 + MK:82-83); F132: тики идут при выключенном радио (AES:128 после тик-цикла).
- F80/F128/F204: три независимых дефекта долготной математики PFC (NaN при a>1.0; не-нормализация deltaLam; линейное усреднение долгот).
- F97: CursorHandler:109 разыменование Instance до null-чека :114.
- F99: stale position fix при <2 измерений; F100: stale callsign в FindClosestStation.
- F202: 90°-рассогласование значение/визуал спиннеров (код подтверждён; проявление зависит от арта ручки в AssetBundle); F205: кириллическая «В°» в IPH:342/382-383; F206: тики по кадрам (latent, mono silentClip SC:14); F207: hijack drag с Zone/Toggle/Ground (PanelDragHandler:94-96 исключает только Expander).
- F53, F55 (и дубль F86), F59, F62, F63, F68 (и дубль F107), F69/F82, F71, F72, F74, F75, F76, F83, F84, F85, F88, F89, F90, F91, F92, F94, F95, F101, F102, F103, F128, F132, F204, F206 — подтверждены по коду.

## Дубли и связки (внутри confirmed)

F14≙F119; F16≙F168; F17≙F163; F24≙F172; F30≙F119; F55≙F86; F57≙F93; F68≙F107; F69≙F82; F135≙F167; F121+F139 (UnpatchAll); F134+F146 (мутация Weight/measurements); F137/F190/F191 — три отдельных NaN-вектора (Acos:179, Asin:149, Asin:187); F80 — четвёртый, отдельный (Atan2/Sqrt(1−a) :214); F193 уточняет F125 (GetElementCount мёртв, 0 callers).

## Оговорки

- **F202**: подтверждён структурно (offset 90° в значении vs `-value` без компенсации в визуале, контраст с CursorHandler); финальное проявление зависит от ориентации графики ручки в бинарном AssetBundle — требует проверки на живой панели.
- **F87**: механизм фейковых позывных подтверждён; список 6 станций с `History[0].Callsign == null` статически не верифицируем (runtime-данные).
- **F83**: механизм уточнён по исходникам игры (см. 5.3): ClearDecisions на каждом ContinueStory, UI закрыт после OnStopped → риск «зомби» низкий; инжекция сейчас не работает из-за G1-G4.
- **F71**: частота вызовов `rng.NextDouble()` подтверждена по коду (DspPipeline:69/72/80), реальное влияние на CPU не измерялось.
- SAD/03:179 «AudioSource not ensured (F43)» — SAD-заметка неточна (см. F43 в rejected).

---

## Итог

Из 207 задокументированных проблем **194 реальны** (13 опровергнуты: 11 — SAD/UI-ложные, 2 — F104/F186 по исходникам игры). Критический приоритет: F53, F65, F77, F78, F79, F200 — однострочные/локальные фиксы с высоким влиянием. Дубли (~15) стоит схлопнуть перед планированием рефакторинга.

**Фаза 5 (исходники игры):** совместимость мод ↔ игра подтверждена по 10 пунктам (все 5 Harmony-таргетов, AssetBundle, имена, Decision-конструкторы, DI, манифест, компиляция). Вскрыты 4 новые поломки (G1–G4), совместно убивающие интеграцию с RadioStory: панель в текущей версии игры не открывается. Правка кода — вне рамок (договорённость: верификация + документация); готов отдельный план фикса.
