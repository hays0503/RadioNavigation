# PLAN Phase 6 — Защита загрузки + гигиена инфраструктуры (2026-09-25)

Статус (сверка 2026-10-04): **фазы 0–5 реализованы; live-рестарт и основные критерии
§9 пройдены; приёмка физического LMB и load-unblock на заранее залипшей сессии
остаётся открытой**. Это исторический план с результатами, не поручение повторить
save/load эксперименты. Источник исходного плана: 44 раунда
исследования + adversarial-верификация (см. §0). Все утверждения ниже помечены FACT / INFERENCE /
UNKNOWN и снабжены ссылками на первичные источники (игровой исходник `C:\code\projects\uboat`,
исходники модов, живой лог).

Текущие команды и счётчики — `README.md` / `DOCS.md`; 66/1135 ниже — результаты
этого этапа. Поздняя инструментальная приёмка McpBridge 0.10.0 и durable rollback
описана в `docs/live-diagnostics.md`; recovery lease не равен восстановлению
сломавшейся игровой загрузки. Новый документационный проход игру не перезапускал.
Сверка документации F241 выполнена 2026-10-04: `DOCS.md`, Phase5 и ledger §7;
историческое «массовая правка не выполнялась» в перечне находок больше не актуально.

---

## 0. Доказательная база (кратко)

**Корень проблемы «мир заклинило»** — FACT: ре-ентрантность `SavesManager.Load`
(`SavesManager.cs:431-440` не проверяет `isLoading`) привела к тому, что вторая
`LoadCoroutine` погибла с `NullReferenceException` в `RestoreSceneRoots`
(`output_log.txt:58-61`; `roots.Length` вне try/catch, `:1187-1204`), не выполнив хвост.
Наследие: **один «осиротевший» элемент** в статическом `List<object> LoadingUIController.blockades`
(`:88/117-124`; `Remove` снимает одно вхождение) ⇒ вечная блокировка ввода/экрана загрузки,
и `UnityEngine.Time.timeScale = 0` (`:460`, восстановление — только в ветке завершения `:317`).
Данные целы: 215 `MapPointUI` в сцене, значения `Autosave_2`, камера жива.

**FACT: минимум две корутины.** `{NPE@:572, SavesManager.IsLoading == false, blockades == 1}`
несовместимы с одной корутиной: единственное присваивание `isLoading = false` — на `:596`,
после `:572`; погибшая корутина туда не дойдёт.

**INFERENCE: погибла вторая (поздняя) корутина.** Хук `Loaded` сработал ровно один раз
(`output_log.txt:62`); погибшая до `:599` не доходит ⇒ хук от завершившейся ⇒ завершившаяся
прошла `:573` раньше, чем погибшая дошла до `:572` ⇒ погибшая стартовала позже.

**FACT: виноват харнесс, не мод.** Мод не вызывает `Load`/`Save`; его action выполняется внутри
try/catch дренажа очереди игры (`:604-611`); `SavingStarted` в игре имеет единственного
подписчика — `SavingNotificationUI` (`:24,54`), карту он не трогает, наш prune идёт до
сериализации (`:879` → `:882+`).

**FACT: `MapChartingDatabase : BackgroundTaskBase`** (`MapChartingDatabase.cs:9`), сцена не
сериализуется (ни мод, ни карта не регистрируют `SceneObject`) ⇒ персистентны только строки БД,
prune по строкам — достаточная мера.

**UNKNOWN:** покадровый интерливинг; объём glow-логов в 3D-виде; физический LMB-тест
(в RDP мышь недоступна MCP); recovery на заранее залипшей сессии. Длительность чистой загрузки
измерена: `Autosave_2` = 26 680 мс.

---

## 1. Что НЕ делать (опровергнуто — чтобы не внедрить заново)

| Утверждение | Статус | Основание |
|---|---|---|
| «Аудио-фикс перепиннит золотые тесты» | **НЕВЕРНО** | тесты передают частоту явно: `AudioRenderPauseTests.cs:20,53,70`; `const SampleRate` остаётся офлайн-дефолтом |
| «`SonneController:248` — таймер на `deltaTime», чинить» | **НЕВЕРНО** | это симуляция; при сжатии ×32 её правка разъехала бы морзе с планировщиком. Заморозка сессии подтвердила решение |
| «Маркер-реконсиляция — заметная нагрузка» | **НЕВЕРНО** | живой замер: 32 fps, `msAvg = msP50 = msP95 = msMax = 31.22` (нулевая дисперсия) при 215 маркерах |
| «`MarkerDeclutter` — дефект» | **НЕВЕРНО** | `Item` = struct (`:32`), scratch-буферы и делегат кешированы (`:49-54`), размен задокументирован (`:20-21`) |
| «`SextantBloomCut` — 3 `GetComponent` в кадре» | **НЕВЕРНО** | `LogAppliedOnce` — одноразовый, latch `logged` (`:124-126`) |
| «`PollUiScale` читает PlayerPrefs в кадре» | **НЕВЕРНО** | ранний выход до перекладки (`:225-230`), чтение только при изменении |
| «`volatile` отсутствует (сводка, F28)» | **НЕВЕРНО** | закрыто F210; все DSP-поля, читаемые аудио-ниткой, `volatile` |
| «`CS8032` обнуляет гейты» | **НЕВЕРНО** | `RnAudit` собирает без `NoWarn` (`analyze.ps1:154-168`); CS8036 передаётся только в IdeAudit-сборку |
| «PlayerPrefs в кадре — опровергнуто» | **НЕВЕРНО (моя ошибка)** | `SextantStabilization.PrefsKey/LockPrefsKey` вне кеш-листа → 2 нативных вызова/кадр (см. §4.1) |
| «Гонка моста = джиттер на каждом маркере» | **ПРЕУВЕЛИЧЕНО** | реальный эффект — хитбокс, посчитанный по устаревшей `lossyScale` (на кадр); severity **LOW** |

---

## 2. Фаза 0 — Мод (≈30 строк кода + 4 гейта + 3 комментария)

### 2.1 `Source/Logic/RadioNavDiagnostics.cs:59-68` — два ключа в кеш
```csharp
cachedValue[SextantStabilization.PrefsKey]   = IsEnabled(SextantStabilization.PrefsKey, true);
cachedValue[SextantStabilization.LockPrefsKey] = IsEnabled(SextantStabilization.LockPrefsKey, true);
```
Причина: `IsEnabledCached` кеширует ровно 5 ключей, фолбэк `:72` читает `PlayerPrefs` напрямую.
`SextantPipPose.UpdateAimFreeze` (`:306-313`, вызывается из per-frame `Update` `:75→:109`) ⇒
2 нативных вызова реестра каждый кадр, пока открыт секстант.

### 2.2 `Source/UI/MapMarkerPointBridge.cs` — порядок кадра
Добавить `[DefaultExecutionOrder(1000)]` на класс. Причина: игра двигает точки в
`MapChartingUI.LateUpdate` → `MapPointUI.UpdatePoints` (`:321-327`), оба на порядке 0 ⇒ порядок
внутри фазы не определён. Атрибут гарантирует «после игры». Severity эффекта LOW (§1), правка
верна и дешёва. Переписать доктрины `:177-181` и `:190-193`: «после — за счёт атрибута, а не
порядка регистрации компонентов».

### 2.3 Комментарии/код, вводящие в заблуждение
* `Source/UI/MapShapeEraser.cs:18` — сейчас написано «runs BEFORE MapChartingUI.Update»;
  правильно: «раньше `MapChartingUI.LateUpdate`, потому что Unity выполняет всю фазу `Update`
  раньше `LateUpdate`; атрибут `-1000` на это не влияет».
* `Source/Logic/MapSaveSanitizer.cs:269-275` — комментарий, что `MapChartingDatabase` не
  `ScriptableObjectSingleton`, поэтому DI-ветка фактически мёртвая, рабочий путь — приватное
  статическое поле `MapChartingUI.mapChartingDatabase`.
* `Source/UI/SextantColumnsBuilder.cs:126` — лог `FIXLAYOUT` негейтнутый; перевести на
  «лог по изменению» по образцу `NavMapMarkerBase:250-254` (`if (ownStale != lastStaleLogged)`).

### 2.4 Идентифицируемость сборки + канарейка версии игры
* `ModBootstrapper.cs:40,127` печатают `GetName().Version` ⇒ в игре всегда `0.0.0.0`
  (живой лог `:13`), `Manifest.json` = `2.1`. Добавить в строку `[RadioNav][DIAG]`: версию из
  `Manifest.json` (IO-разрешение есть), `LeuchtfeuerRegistry.Profiles.Count`, число звёзд.
* `VerifiedGameVersion = "2026.1-patch21"` + сравнение с `Application.version`; при расхождении —
  `LogWarning` со списком 5 Harmony-целей ( сейчас патчи падают молча).

### 2.5 Гейт `LifecycleOrder_And_DiagnosticsCache_ArePinned` (реализован)
Один гейт на три инварианта, синтаксис/AST (не текст):
1. `MapMarkerPointBridge` имеет `[DefaultExecutionOrder]` с **положительным** аргументом;
2. `MapShapeEraser` объявляет `Update` и **не** объявляет `LateUpdate`/`FixedUpdate`;
3. каждый ключ, читаемый `RadioNavDiagnostics.IsEnabledCached` из per-frame-метода **или из
   вызываемого им хелпера (один прыжок)**, присутствует в списке `cachedValue[...] = IsEnabled(...)`.

**Мутационные проверки (выполнены, все три сработали):**

| Мутация | Результат |
|---|---|
| убрать два ключа F233 из списка обновления | FAIL: `SextantPipPose.UpdateAimFreeze reads diagnostics key SextantStabilization.PrefsKey per frame…` |
| снять `[DefaultExecutionOrder(1000)]` с моста | FAIL: `has no [DefaultExecutionOrder] — its LateUpdate can run before the game's MapChartingUI.LateUpdate` |
| перенести `Update` эрейзера в `LateUpdate` | FAIL (обе формулировки сразу: «no Update» + «must not move…into LateUpdate/FixedUpdate») |

**Две ошибки, найденные самими мутациями (важно для будущих гейтов):**
* Первая версия гейта проверяла только методы с именем `Update` — и **пропустила** мутацию F233,
  потому что чтение живёт в `UpdateAimFreeze` (тот самый однопрыжковый случай F237). Отсюда
  требование «один прыжок» в самом гейте.
* Поиск списка обновления сначала искал `SimpleAssignmentExpressionSyntax`/`IndexExpressionSyntax`
  и возвращал **пустое** множество — гейт превращался в грабли, срабатывающие на каждом
  легитимном ключе. Рабочая форма: `ElementAccessExpressionSyntax`, у которого родитель —
  простое присваивание, плюс сравнение по **leaf-имени** ключа (в списке константы без
  квалификатора, в вызовах — с `RadioNavDiagnostics.`).

**Внимание:** правило «нет `IsEnabledCached(key, true)`» **нельзя** применять ко всем ключам —
`SextantStabilization.*` это фича-гейты с default `true`; правило scoped на `RadioNavDiagnostics.*`.

---

## 3. Фаза 1 — Харнесс (McpBridge + скрипты)

### 3.1 Новый `McpBridge/Source/Logic/McpLoadGate.cs`
Предикат готовности = условия, которыми **сама игра** определяет завершение
(`LoadingUIController.cs:237` + `:293-317` + `SavesManager:636`):
```csharp
SavesManager.IsLoading == false
LoadingUIController.IsLoading == false
((ICollection)LoadingUIController.blockades).Count == 0      // поле СТАТИЧЕСКОЕ (:88)
Time.timeScale != 0
GameState.CurrentState == GameState.State.Gameplay
MainCamera.isActiveAndEnabled                                  // экземпляр, кешировать
CharacterPortraitsUI.portraits.Length != 0                     // ЧИТАТЬ ПОЛЕ, не вызывать метод
                                                               // только при LoadingUIController.IsLoading
```
`LoadingUIController.IsLoading` — свойство **экземпляра** (`:102`) ⇒ кешировать
`FindObjectOfType<LoadingUIController>()`; `blockades` — статическое.

**Live-коррекция 2026-09-25:** в полностью рабочей `Gameplay` сцене найден
`CharacterPortraitsUI` на неактивном HUD (`active=false`, `Portraits` ещё не заполнены).
Безусловная проверка portraits давала `loading.ready=false` при 60 fps и работающем мире.
Поэтому portraits проверяются только при `LoadingUIController.IsLoading=true`, а
`ResolvePortraits()` сначала ищет активный объект, затем `Resources.FindObjectsOfTypeAll`.
В залипшей сессии из §0 `IsLoading=true`, поэтому исходная защита сохраняется.

**Критично (найдено adversarial-проходом):** `ValidatePortraits()` **не идемпотентен** — во
второй ветке он взводит твин-цепочку (`FrameDelay(2) → Delay(2f, useUnscaledTime=true) →
FrameDelay(1, UpdatePortraits)`), которая заполняет `portraits` (`CharacterPortraitsUI.cs:455-489,
821-826`). Вызов метода в предикате **мутирует систему**; читать поле. Побочный эффект полезен
только в пробнике (§3.3). Условие portraits **не печатается** сторожем ⇒ по логу его не видно.

### 3.2 `McpSaveLoad.Load` — guard + ожидание (без дедлока!)
`McpHttp.cs:1926-1932`: `pump.Enqueue(delegate { body = McpSaveLoad.Load(name); })` выполняется
**на главном потоке**; ожидание внутри main-thread job **заморозило бы игру** (загрузка не
продвигается без главного потока). Схема (как в `McpScenario.Run:42-55`):
1. main-thread job: pre-guard (`Ready()` иначе `{"ok":false,"error":"load-in-progress",…}`) →
   `saves.Load(name)` → возврат сразу;
2. **HTTP-нить** владеет циклом: `Pump(… StateJson())` + `Thread.Sleep(250)`, бюджет 280 с;
3. таймаут → `{"ok":false,"error":"load-slow","why":<условия>}` — **не** тихий `ok`;
4. `Thread.Sleep(500)` settle + повтор предиката (окно между `SavesManager:653` и `:660-662`,
   где предикат уже истин, а игра ещё пересоздаёт EventSystem).

`LoadTimeoutMs` (`:28`, 120 с) — потолок одного pump-job, **не** всего ожидания: менять не нужно.
Клиентский таймаут `mcp_server.py:350` 130 → **330 с** (сервер держит сокет дольше клиента —
прямой путь к таймауту у агента).

### 3.3 Пробник `load_unblock` (навсегда, по решению автора)
Порядок: (1) `LoadingUIController.RemoveBlockade(saves)` — **public static** (`:122`), рефлексия
не нужна; (2) если предикат не сошёлся — перевзвести portraits штатным механизмом (вызвать
`ValidatePortraits()`, он сам запустит починку); (3) если `Time.timeScale == 0` — форсировать
`1` с `"forced": true`; (4) дождаться предиката; (5) **честно вернуть список невыполненных
условий**, а не «ok». Всё в лог `[McpBridge] unblock …`. Маршрут `/loadunblock` + инструмент
`load_unblock` в `mcp_server.py` с описанием.

**Живая база на 25.09.2026 (сессия залипла, всё проверено пробами):**
`blockades = 1`, `LoadingUIController.isLoading = true`, `Time.timeScale = 0`,
`SavesManager.IsLoading = false`, `deserializer = null`, `roots = null`, `rootStates.Count = 43`,
`path = Autosave_2.save`, `GameState.CurrentState = 3 (Gameplay)`, `MainCamera.isActiveAndEnabled = true`,
`CharacterPortraitsUI.portraits.Count = 24`, все 11 static-полей `LoadingUIController` ненулевые,
`GameTime.TimeScale = "1"`. `ValidatePortraits()` сначала дал `false` (см. §3.1 про
неидемпотентность), после прогрева цепочки — `true`. Значит снятие blockade должно привести к
`Loaded game in: X ms` в логе, `timeScale → 1` и `blockades = 0`.

### 3.4 Прочие правки харнесса
* `McpSaveLoad.Save`: отказ при `IsLoading || IsActivelyDeserializing || SaveBlockades.Count > 0 || Time.timeScale == 0` → `save-unsafe` (`Save` в игре тоже без защиты, `:852-855`; живой `SaveBlockades.Count = 0` ⇒ битый `Autosave_3` записан нами).
* `McpSensors.BuildState`: добавить `"loading":{"ready":bool,"why":…}`; `McpWait.Evaluate`: новое
  условие `loading=ready`; в описании `wait_for` явно указать, что `pos=ok`
  (`McpWait.cs:108-112`) **не** означает готовность мира.
* `McpPerf`: при незакрытом окне помечать `"stale": true` — сейчас `perf_sample` возвращает
  `complete:false` и **идентичный** payload при повторе, `gcHeapDeltaKb` отрицателен (живой замер).
* `McpSensors`: переименовать поле `radio[].frequency` → `tunedKHz` (0 означает «не настроено»,
  это не частота станции).
* `mcp_server.py`: описание инструмента `load` — «откажется, если загрузка идёт; дождётся
  завершения; при таймауте вернёт load-slow».
* `run-game.ps1`: готовность по `"loading":{"ready":true}`, а не по `*"position":{"ok":true*`
  (строка 94 — именно этот сигнал спровоцировал кирпич); проверять `modlist.txt` = ровно
  `RadioNavigation`,`McpBridge` (`ModManager.cs:171-172`; флага `enabled` у игры нет).
* Удалить 7 тестовых сейвов: `mcp-test`, `regression-tmp`, `rdoc-capture`, `rdc_capture_test`,
  `lh-verify-1`, `pre-lh-verify`, `funkmod-rus-fix-test` (цель бота = самый свежий по времени
  файл, `SavesManager.GetSaves:395-398` → `IntroScene:239` берёт `saves[0]`). **Выполнено
  2026-09-25:** все 7 файлов удалены; `Autosave_1/2/3` не тронуты.

### 3.5 Реализация Фазы 1 (выполнена 2026-09-25)

* `McpBridge/Source/Logic/McpLoadGate.cs`: предикат из §3.1, кеширование Unity-ссылок,
  reflection только для приватного статического `LoadingUIController.blockades`; в предикате
  читается `CharacterPortraitsUI.Portraits`, **не** вызывается `ValidatePortraits()`.
  Единственный мутирующий вызов остался в `UnblockOnce()`.
* `McpSaveLoad.Load`: pre-guard на main-thread job; `Save` отказывает при `IsLoading`,
  `IsActivelyDeserializing`, `SaveBlockades.Count > 0` или `Time.timeScale == 0`.
* `McpHttp.HandleLoad`: короткий main-thread job + ожидание до 280 с на HTTP-нити; settle
  500 мс и повторная проверка; ошибки `load-in-progress` / `load-slow` возвращают условия
  `why`, а не тихий `ok`. Ожидание не выполняется внутри pump-job — дедлока нет.
* `/loadunblock` → `McpLoadGate.UnblockOnce()`: снимает ровно один blockade, при необходимости
  перевзводит portraits, возвращает `timeScale=1`, затем HTTP-нить ждёт gate.
* `/state` получил `loading.ready`; `wait_for` получил `loading=ready`; `run-game.ps1` больше
  не использует `position.ok` как признак готовности и проверяет точный `modlist.txt`.
* `perf_sample` помечает неполное окно `"stale":true`; радио-поле переименовано
  `frequency` → `tunedKHz`; клиентский таймаут `load` увеличен 130 → 330 с.
* Статические проверки: `compile-mcpbridge.ps1` OK (42 sources), `analyze-bridge.ps1` 0
  diagnostics, `python mcp_server.py --selftest` PASS (включая `load`, `load_unblock`,
  `loading=ready`).

---

## 4. Фаза 2 — Аудио: частота устройства

FACT: живой `AudioSettings.outputSampleRate = 48000`, движок использует
`AudioSynthesisEngine.SampleRate = 44100` (`:12`) **только как делитель фазы**
(`DspPipeline.cs:43,107,109-110,209,211,227`; `AudioSynthesisEngine.cs:71,244,245`).
Ошибка частот = 48000/44100 = 1.0884 ⇒ **+146.7 цента ≈ 1.47 полутона** на все
тоны/свист/QSB/RC-cutoff. Тайминги морзе **не** затронуты: планировщик идёт по
`Time.deltaTime`/`Time.time` (`SonneController:248`), `MorseKeyer` частоту не упоминает.

Фикс: `const int SampleRate = 44100` остаётся офлайн-дефолтом; добавить
`static int DeviceSampleRate` (ленивое чтение `AudioSettings.outputSampleRate` в try/catch) и
использовать его в 3 местах подстановки. Изменений тестов: 0.
Гейт: `AudioSynthesisEngine_UsesDeviceSampleRate` (движок не передаёт константу в аудио-путь,
офлайн-фолбэк покрыт юнит-тестом).

**Выполнено 2026-09-25:** добавлен кешируемый `DeviceSampleRate`; `AudioSynthesisEngine`
сохраняет resolved rate в поле и передаёт её в `ReceiverDsp`/`Atmosphere` на каждом sample,
`StationFleet` и `SonneController` используют тот же resolved rate. `SampleRate` не удалён:
это deliberate offline fallback для 1135 offline-тестов. Проверки: новый
`AudioSynthesisEngine_UsesDeviceSampleRate` PASS; `DeviceSampleRate_FallsBackToOfflineDefaultWhenUnavailable`
PASS; золотой `AudioRenderPauseTests` не изменился.

---

## 5. Фаза 3 — `LighthouseDotUi`: горячие геттеры и боксинг

FACT: `CoreRect/HaloRect/StreakRect` (`:52-63`) — геттеры с `GetComponent<RectTransform>()` на
каждое обращение; вызываются покадрово из `LighthouseBeamGlow.cs:304,305,314` ⇒ ~11 500
`GetComponent`/с при 64 маяках. `SetImageColor` (`:140-152`) боксует `Color` на вызов
(`PropertyInfo` уже кешируется) ⇒ ~11 500 боксов/с.
Фикс: кешировать три rect при build; не писать цвет, если alpha не изменилась.
Гейт: структурная проверка, что rects берутся из полей, а не `GetComponent` в геттере.

**Выполнено 2026-09-25:** `LighthouseDotUi` кеширует `coreRect`/`haloRect`/`streakRect` при
`EnsureBuilt`; `SetImageColor` latch-ит alpha каждого слоя и вызывает reflection `SetValue`
только при изменении, поэтому больше не боксует `Color` на неизменившемся кадре. Гейт
`LighthouseDotUi_CachesRectsAndSuppressesUnchangedColor` PASS; PVS clean.

---

## 6. Фаза 4 — Логи и инструментарика лога

* `LighthouseGlowVerbose` (`:164-165`) и `LighthouseMapMarkerPosition` (`LighthouseMapMarker:151`)
  — default **OFF** ⇒ в лог уходит ~20 строк/мин в режиме карты (живой замер окна 540→630 с:
  29 модных строк из 34). Цена не «флуд», а «хоронит реальные события» — NPE в `:58` я искал
  грепом. Фикс: default **off**; `FIXLAYOUT` — по изменению.
* Префикс `[t=…]`/`[frame=…]` в троттнутых эмиттерах (4-5 мест) либо heartbeat раз в 60 с:
  в логе игры нет ни времени, ни номера кадра, из-за чего весь анализ стоил десятки замеров.
* Существующий `ReleaseLogs_AreGated` (`SourceCheckTests.cs:537-565`) — whitelist из 5 файлов;
  инвариант «дефолтные verbose-флаги выключены» он не проверяет ⇒ расширить до общего правила
  (см. §2.5).

**Выполнено 2026-09-25:** `LighthouseGlowVerbose` и `LighthouseMapMarkerPosition` переведены
на default OFF; feature-gates `SextantStabilization.*` оставлены default ON. Добавлен
`RadioNavDiagnostics.Stamp()` (`Time.time` + `Time.frameCount`) к marker/ lighthouse diagnostics
и `FIXLAYOUT`; `FIXLAYOUT` по-прежнему пишется только при изменении. `ReleaseLogs_AreGated`
расширен проверками default-off, stamp и change-gate. Проверки: 1135 PASS, SourceCheck 66 PASS.

---

## 7. Фаза 5 — Мелочи (низкий приоритет, измерено)

* Таймеры на `deltaTime` → `unscaledDeltaTime`: `InformationPanelHandler:124,131`,
  `SonneController:213,232,239` (5 мест). На решение: `SpinnerHandlerBase:126`,
  `LighthouseRotator:17`. **Не трогать:** `SonneController:248` (симуляция).
* Реконсиляция 0.25 → 1 с (`NavMapMarkerBase:53`): 12 полных сканов сцены/с → 3. Гигиена, не
  срочность (нулевая дисперсия кадров, §1).
* Бокс enum→string: `SextantController:677`, `SextantStarId:428` — по одной строке.

**Выполнено 2026-09-25:** пять таймеров переведены на `unscaledDeltaTime`; симуляционный
`audioEngine.UpdateStations(Time.deltaTime, ...)` и `SonneController:248` оставлены без изменений.
`NavMapMarkerBase.ReconcileIntervalSeconds` = 1 с. Пункт «enum→string» не менялся: повторная
проверка исходников показала, что отмеченные места — reflection `int`/`bool` и стабильный
внутренний ключ `(int)body.Kind`, а не доказанный boxing enum; менять их без профилирования
значило бы рискнуть стабильностью BodyKey.

---

## 8. Поздние строки реестра (F231+ в `hypothesis/INDEX.md`)

Отдельные confirmed-досье этих номеров не создавались; 194 файла в `confirmed/`
относятся к исходному аудиту F01–F207.

| ID | P | Суть | Тест/гейт |
|---|---|---|---|
| F231 | P0 | `SavesManager.Load` без guard + `Save` без guard ⇒ двойная загрузка убивает корутину, осиротевший blockade + `timeScale=0` навсегда | живой критерий §9 |
| F232 | P1 | `MapMarkerPointBridge.LateUpdate` без `DefaultExecutionOrder` — неопределённый порядок с `MapChartingUI.LateUpdate` | `LifecycleOrder_And_DiagnosticsCache_ArePinned` |
| F233 | P1 | `IsEnabledCached` кеширует 5 ключей; 2 ключа секстанта мимо ⇒ `PlayerPrefs` 2×/кадр | `LifecycleOrder_And_DiagnosticsCache_ArePinned` |
| F234 | P1 | аудио: 44100 против живых 48000 ⇒ +146.7 цента во всех частотных констанных DSP | `AudioSynthesisEngine_UsesDeviceSampleRate` |
| F235 | P2 | `LighthouseDotUi` — `GetComponent` в геттерах (11.5k/с) + бокс `Color` (11.5k/с) | `LighthouseDotUi_CachesRectsAndSuppressesUnchangedColor` |
| F236 | P2 | дефолтные verbose-флаги (glow/позиция) + `ReleaseLogs_AreGated` как whitelist | `ReleaseLogs_AreGated` |
| F237 | P2 | RN0006/RN0007 слепы к однопрыжковому выносу в хелпер (12 сканов/с в `RunReconcile` прячутся так) | анализаторный тест |
| F238 | P2 | RN0007 смотрит только тело `OnAudioFilterRead`; чистоту аудио-пути доказывает огибающая-тест, не анализатор | документ + тест |
| F239 | P2 | `perf_sample` отдаёт неполное окно без признака stale | selftest моста |
| F240 | P2 | сборка мода не опознаётся в логе (`0.0.0.0`); нет канарейки версии игры | `BootstrapDiagnostics_IdentifyBuildAndGameVersion` |
| F241 | — | `SEXTANT_PLAN.md` §7 (12 пунктов) устарел; 24 пункта в 4 планах открыты при выполненной работе | проход по планам (§10) |
| F242 | — | сводка 50 агентов (P2/P3) наполовину устарела — сверять с кодом/реестром | 7 записей «ложная тревога» по образцу F226 |
| F243 | P2 | `loading.ready` требовал portraits даже при `LoadingUIController.IsLoading=false`; inactive HUD давал ложный not-ready | live: 60 fps + `ready=true` после conditional check; bridge compile/analyze/selftest |

---

## 9. Порядок проверки и критерии успеха

Исторический проверочный проход 2026-09-25: `compile-mcpbridge.ps1` → `analyze-bridge.ps1` → `python mcp_server.py --selftest` →
`build.ps1` (+PVS) → `dotnet run --project RadioNavigation.SourceCheck` (**66 PASS**) → `tests.ps1`
(**1135 PASS**) → `analyze.ps1 -Profile quick` → `graphify update .`.

Живые критерии (после рестарта):
1. `load_unblock` на залипшей сессии: в логе появляется `Loaded game in: X ms`; `timeScale` → 1;
   часы идут; `blockades = 0`; `LoadingUIController.isLoading = false`.
2. `load Autosave_2` возвращает `ok` **после** ожидания; 215 маркеров; ни одного NPE; в логе
   две строки `Loading save file` за сессию, а не больше.
3. Замер длительности чистой загрузки → подстановка бюджета ожидания.
4. Ручной тест: линия/транспортир/круг между двумя маяками → ластик по маяку снимает фигуры,
   маркер остаётся; Delete — то же.
5. `load` во время загрузки → `load-in-progress`, а не вторая загрузка.

**Live gate 2026-09-25:** обычный безопасный рестарт без `-quicklatestgamestate` выполнен;
новый bridge загрузился, поэтому `/state` содержит `loading`, а радио использует `tunedKHz`.
Факты после рестарта:
* `loading.ready=true`, `why=[]`; `wait?loading=ready` вернул `ok=true` за 6 мс.
* `/loadunblock` на здоровой сессии: `ok=true`, `ready=true`, 612 мс (залипший NRE-сценарий
  не воспроизводился искусственно).
* `load Autosave_2`: `ok=true`, ожидание 27.442 с; `Loaded game in: 26680 ms`; 215 точек,
  `lineCount=0`, NPE=0.
* `load Autosave_1` параллельно с повторным запросом: первый `ok=true`, второй
  `load-in-progress` с причинами `savesManager.isLoading`, `loadingUI.isLoading`,
  `blockades=1`, `timeScale=0`, `mainCamera.inactive`, `portraits.empty`; после завершения
  снова 215 точек, 0 NPE.
* Статический фигурный probe также пройден: создана линия `RadioNav LH B0894` ↔ `RadioNav LH B0849`
  (`lineCount=1`), вызов `MapShapeEraser.RemoveShapesForPointName` вернул `1`, после чего
  `lineCount=0`, оба статичных маркера остались (`pointCount=215`, `interactive=true`).
* Реальный Delete-input path также пройден: после `MapProbe.Hover(B0894)` отправлен
  `input/key Delete` через bridge; `lineCount` стал `0`, появился
  `[RadioNav][Map] eraser removed 1 shape(s) ... (marker kept)`, оба маркера остались.
  Физический mouse/LMB через RDP всё ещё не подтверждён; Delete-ветка и cleanup-логика live подтверждены.

Осталось live-only: §9.1 на заранее залипшей сессии и физический mouse/LMB §9.4.

---

## 10. Проход по планам и реестру (актуализация 2026-10-04)

* `SEXTANT_PLAN.md`: действующие правила вынесены в шапку и §§16.23–16.25;
  исходный §7 — история. Наличие артефакта не закрывает его live-постусловие.
* `PLAN_Phase5.md`: отмечены реализованные и отсутствующие слайсы. F71 и event
  leaks не закрываются тем, что симуляционный `deltaTime` оставлен правильно;
  требуется соответствующий live-прогон. Hot-reload заменён restart.
* `PLAN_Phase4_GodObjects.md`: preflight подтверждён отдельно (5/5); исходные
  планы и остальные live-критерии не переписаны как выполненные.
* `STAR_SKY_PLAN.md` / ADR-020: исходный набор дат остаётся незакрытым Proposed;
  наличие production-каталога само по себе не переводит этот ADR в Accepted.
* `MAP_MARKERS_PLAN.md`: актуальная политика фигуры/ластика, реконсиляция 1 с и
  сохранённые поздние результаты добавлены; физический LMB остаётся pending.
* Сводка 50 агентов / F242 не переаудировалась. Новое количество закрытых
  находок не выводится из одного документационного прохода.

---

## 11. Открытые пункты (UNKNOWN — не заполнять догадками)

1. Покадровый интерливинг двух корутин (нужен instrumented-прогон).
2. Длительность чистой загрузки **измерена**: 26 680 мс для Autosave_2 (§9);
   это не гарантия времени любой загрузки/конфигурации.
3. Объём glow-логов в 3D-виде (оценка «2.5 МБ/мин» — из сводки, **не измерена**).
4. Живое подтверждение перехвата ластика (мышь).
5. Сработает ли одно действие `RemoveBlockade` в будущих залипающих сессиях, или потребуется
   перевзвод portraits — зависит от того, успеет ли цепочка заполнения портретов завершиться до
   снятия blockade; порядок в §3.3 это учитывает.
