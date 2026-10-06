# Edge-детектор клавиш: секстант мигает при удержании Z

> Пути к файлам в тексте указаны относительно корня репозитория.

## Действующее состояние (2026-10-06)

**Правка реализована; локальные gates и live-проверка Z пройдены. Причина
исходного мигания не объявляется доказанной.** Автор
подтвердил непрерывное физическое удержание Z, отсутствие потери фокуса игры и
воспроизведение мигания в live view. При отдельном удержании Z на карте снимок
лога до правки содержит одну запись `Z ignored outside live view (mode=Map)`
(строка 551 на момент проверки). Это не показывает повторов действия в том
режиме, но не
объясняет live-view эпизод. Unity описывает `GetKeyDown` как событие кадра начала
нажатия; лог игры не пишет сырые переходы клавиш, поэтому утверждение «Unity
повторяет `GetKeyDown` при удержании» не доказано.

Внесена защитная обработка фронта из непрерывного `Input.GetKey`-состояния. Для
edge-triggered actions она ограничивает одно непрерывное наблюдаемое удержание
одним действием, независимо от причины прежних импульсов; это mitigation, а не
установленный диагноз. Непрерывное управление стрелками по-прежнему использует
`GetKey` только в открытом оверлее и сохраняет прежнее hold-поведение.
Порог/дебаунс не добавлялся: данных о кратком ложном отпускании `GetKey` нет.
Первоначальные локальные прогоны не запускали игру. При повторной проверке
2026-10-06 автор запустил игру сам; presence `HeldKeyEdge` в загруженной сборке
подтверждено reflection, затем проверена Z (§14.1). Новых наблюдений и сохранений
не создавали; 23 файла `.save` не изменились. Тест с I на рабочем сейве запрещён:
I записывает наблюдения.

Дополнительный source-аудит по запросу автора (§14.2) нашёл открытые дефекты
modal ownership/lifetime: нет foreign-blockade gate и `OnDisable` cleanup,
общий `IsOpen` не защищён от второго владельца. Они есть и до правки, и после
`HeldKeyEdge`; их связь с исходным физическим удержанием Z **не доказана**.

| Пункт | Статус |
|---|---|
| Непрерывное удержание Z мигает в live view | **подтверждено автором**; лог не связывает физические переходы с переключениями |
| Повторяется ли `GetKeyDown` в исходном эпизоде | **неизвестно**; исходные keydown/key-up не логируются |
| Z на карте во время удержания | один `ignored outside live view`; повторов действия в логе нет |
| Edge-защита | **реализована** для Z/P, I/4, Ctrl+стрелок, Shift+Z и F7 |
| Unit tests | **1416 PASS / 0 FAIL** на 2026-10-06 |
| SourceCheck | **101 PASS / 0 FAIL**, включая `ToggleKeys_AreEdgeDetected`, на 2026-10-06 |
| Полные gates | **ALL GREEN** (build, analyze, tests, PVS) на 2026-10-06 |
| Graph | `graphify update .` выполнен; HTML пропущен штатно из-за 9688 узлов (>5000) |
| Mutation run | **не выполнялся**; wiring закреплён SourceCheck AST gate, поведение helper — unit-тестами |
| Live Z | **6/6 PASS**: FPP внутри лодки, симуляция на паузе; 3-секундные удержания и 100-мс нажатия, §14.1 |
| Остальная live-приёмка | P/4, Shift+Z, F7 и Ctrl+стрелки **не проверены**; I на рабочем сейве не тестировать |
| Безопасность live-проверки | 23/23 `.save` неизменны по SHA-256, длине и mtime; камера FPP, закрытый прибор, пауза и калибровка возвращены |

---

## 1. Проверенные наблюдения и ограничения диагностики

Данные на 2026-10-06:

- Автор подтвердил: Z удерживалась физически непрерывно, окно игры не теряло
  фокус, мигание в live view воспроизводилось.
- Отдельный контроль на карте: удержание Z около трёх секунд дало одну запись
  `Z ignored outside live view (mode=Map)` (снимок `output_log.txt` до правки, строка
  551 на момент проверки). Это не показывает, что именно происходит в live view.
- В том же логе строки 416–460 содержат чередование `help shown/hidden` за
  несколько секунд. Лог не записывает события P, поэтому число строк не
  устанавливает, была ли P удержана или нажималась отдельно.
- Тест удержания I не выполнять на рабочем сейве: он изменяет running fix,
  распределяя запись по слотам наблюдений. Нынешний runtime-пруф для I
  намеренно отсутствует.

Следствие: удержание Z и мигание — подтверждённый пользовательский симптом;
конкретный источник повторных срабатываний — нет. Проверка настроек повтора
Windows сама по себе не различает повторы Unity, события клавиатуры и сброс
состояния ввода, поэтому она не считается доказательством причины.

---

## 2. Симптом

Автор воспроизвёл многократное переключение секстанта при непрерывно удерживаемой
`Z`; потери фокуса окна не было. Логи показывают переключения прибора, но не
показывают границы физических нажатий, поэтому причина остаётся открытой.

---

## 3. Доказательства из лога игры

Источник: `C:\Users\Sergey Pimenov\AppData\LocalLow\Deep Water Studio\UBOAT\output_log.txt`.
Четыре эпизода: строки 176–196, 1901–1923, **2154–2313**, 3372–3569.

> Эти номера и расчёты относятся к прежнему снимку лога, описанному во время
> расследования; файл уже обновлялся/обрезался. Для снимка 2026-10-06 текущая
> контрольная строка теста на карте — 551 (§1). Не переносить старую нумерацию на
> текущий файл.

Цикл из 13 строк, повторяется без пауз (строки 2154→2161):

```
closed
SUNDRIFT calibration kept: dt=-6.59 min (series active)
[SunDisc] texture rebuilt, spots=5
ATTACH water=True attached=2 leftEn=False rightEn=False fov=65.0 pipFov=10.8
BLOOMCUT right on (filters=0)
BLOOMCUT left on (filters=0)
opened (two-camera split ocular)
VECL open … / VECR open … / VECV open … / VECD open … / VECSUN open … / VECSP open …
SUNDRIFT sep=0.000 …
```

Измерения:

| Величина | Как получено | Значение |
|---|---|---|
| fps и период телеметрии | `MANUALRENDER` шагами ровно 59 кадров: 30799→30858→30917→30976→31036→31096→31155→31214→31273→31332→31391→31450→31509→31568→31627→31686→31745→31804→31863→31922 | **60 fps, 1 Гц** |
| Длина эпизода | 31922 (стр. 2153) → 32494 (стр. 2311) | **572 кадра** |
| Число переключений | строки 2154, 2161, 2169, 2175, 2185, 2191, 2199, 2205, 2213, 2219, 2227, 2233, 2241, 2247, 2254, 2260, 2267, 2273, 2282, 2288, 2297, 2303, 2313 | **23** |
| Кадров на переключение | 572 / 23 | **~25** |
| Доля закрытого состояния | Δ`skipClosed`=26 при 2 циклах на 190 кадрах; Δ`calls`=320 + 26 = 346 вызовов ≈ 2 клона × LateUpdate | **~6.5 кадра закрыт** |

Свойства цикла:

- **Данные не теряются.** `VECD open arc=16.026667` идентичен во всех 23 циклах;
  `SUNDRIFT calibration kept: dt=-6.59 min (series active)` повторяется каждый раз.
- **Цикл дорогой:** 23 пересоздания текстуры солнечного диска
  (`SextantController.cs:417` → `SunDiscRenderer.RandomizeDisc`), 46
  переключений PPv2-волюмов bloom-cut, 23 пересборки `AttachedCameras` главной
  камеры (`SextantPipFactory.AttachToMain`), 23 полных слива `VEC*` телеметрии.
- **Записей внешнего агента в этом фрагменте нет:** `[McpBridge]` встречается
  только на строках 16, 17, 37, до бурстов. Без журнала всех bridge requests
  это не исключает внешнее управление.
- **Повторов `I` в этом логе нет:** `OBS slot=1` (стр. 1707), `slot=2` (1949),
  `slot=3` (2151) — по одной записи на слот. Значит порча данных от `I` —
  **латентный риск, а не наблюдённый симптом**. (Раннее утверждение «`I` портит
  данные» было предсказанием, выданным за факт — неверно.)

---

## 4. Частота не совпадает со стандартным автоповтором

~25 кадров на переключение. При 60 fps это ≈2.4 Гц; fps во время эпизода
проседает из-за завалки лога (`MANUALRENDER` внутри эпизода даёт шаги 402 и 98
вместо 59), поэтому истинная частота лежит в диапазоне **~1.3–4 Гц**.
Сопоставление со старой оценкой Windows autorepeat (~30 Гц, задержка 500 мс)
не устанавливает причину: параметры ОС на машине здесь не измерялись,
а документированный `GetKeyDown` вообще не равен потоку WM_KEYDOWN autorepeat.
Частота toggle без сырых input transitions не доказывает ни ОС-повтор,
ни иной источник.

---

## 5. Подход разработчиков игры

Живая реализация — `C:\code\projects\uboat\UBOAT.Game.Core.Input\InputAxis.cs`.
Каталог `DWS.Common.Input` (`RawInputAxis`, `SmoothedInputAxis`, `SelectAxis`) —
**ни одного потребителя** во всём исходнике игры.

```csharp
// InputAxis.cs:166, :184 — СОСТОЯНИЕ, а не импульс
if (UnityEngine.Input.GetKey(keyCode))
    return currentValue = 1f;

// InputAxis.cs:125-130 — мемоизация кадра, previousValue обновляется один раз
if (lastUpdateFrame == frameCount)
    return currentValue;
previousValue = currentValue;
lastUpdateFrame = frameCount;

// InputAxis.cs:196-206 — фронт поверх собственного замера
public bool JustPressed
{
    get
    {
        if (Value >= 0.5f)
            return previousValue < 0.5f;
        return false;
    }
}
```

Свойства подхода:

- **Привязка — данные.** `InputAxis : ScriptableObject`, приватные
  `[SerializeField] positiveKeys[]/negativeKeys[]` (`:38-42`), опциональные
  `axisName`/`axisName2` для стиков, множители. Ассеты `Resources/Inputs/*`,
  тянутся `ResourceManager.RetrieveAsset<InputAxis>("Inputs/Open Journal")`
  (`KeyboardShortcuts.cs:181-208`).
- **Клавиатура и геймпад слиты в одном `Value`.** `JoystickButton0..19` +
  смещение `controllerIndex * 20` (`:104`, `:157-165`), статический
  `wasControllerRecentlyUsed` переключает подсказку в UI.
- **Перепривязка игроком с проверкой конфликтов.** Сеттер `PositiveKeys`
  (`:246-256`), `ResetToDefaults()` (`:360-364`), `HasPositiveOverlap(InputAxis)`
  (`:366+`) — инфраструктура проверки пересечений привязок; наличие метода
  не является гарантией отсутствия конфликтов текущего пользовательского setup.
- **Гейт «когда можно» отделён от «что нажато».** `GameInput`:
  `AddBlockade` (`:106`), `RemoveBlockade` через `Tweens.FrameDelay(1)` (`:160-169`),
  `HasActiveBlockades` (`:131`), плюс защита от двойного потребления Escape в
  одном кадре `escapeConsumedFrame` (`:41`, `:182`, `:192`). Игра глохнет хоткеи
  при блокаде: `KeyboardShortcuts.cs:242`.
- **Тумблеры игры (тот же класс контрола, что наш Z):** журнал открыть/закрыть
  (`KeyboardShortcuts.cs:218-231`), пресет глубины гидрофона «вкл/восстановить»
  (`:246-256`), пауза (`:257`), смена постпроцессинга (`:290`),
  `uiScale` (`:232-241`), пресет FOV бинокля (`PeriscopeController.cs:897`),
  свободная камера (`MainCamera.cs:888`), показ интерфейса
  (`GenericCameraController.cs:396`, `FPPCameraController.cs:1224`).

**Примеры сырого `Input.GetKeyDown` в игре:** наличие этих мест не доказывает
повтор API при удержании и не является основанием запретить его всем тумблерам.

| Место | Клавиша | Контекст использования |
|---|---|---|
| `CutsceneController.cs:238`, `MovieScene.cs:93`, `CreditsUI.cs:60`, `DevConsoleCommands.cs:2230,2261` | Escape | закрытие, идемпотентно |
| `DialogueUI.cs:155`, `DialogueActionBase.cs:164` | цифры, Escape | выбор, идемпотентно |
| `MapChartingUI.cs:349` | Delete | удалить наведённое, идемпотентно |
| `SectionViewCameraController.cs:1313` | Backspace | идемпотентно |
| `PeriscopeUI.cs:779-807`, `PeriscopeController.cs:465-474`, `RouteRendererMinimap.cs:80-94` | E, Ctrl | **пара `GetKeyDown` + `GetKeyUp`** — непрерывное действие |
| `DevConsole.cs:133,150` | стрелки | шаг по истории команд |

Проверенные journal/map/binocular consumers используют `InputAxis.JustPressed`;
другие hard-coded paths используют raw Input и собственное состояние. Более
важное отличие от секстанта — permission gate и lifecycle/ownership, §14.2.

### Предупреждение: не копировать `RawInputAxis`

`DWS.Common.Input/RawInputAxis.cs:63` присваивает `lastUpdateFrame` **только на
пути возврата нуля**; на ранних `return currentValue = 1f` (`:42`, `:50`, `:58`)
мемоизация не ставится. При зажатой клавише `previousValue` перетирается на
каждом чтении внутри кадра, и `JustPressed` (`:70-80`) может быть истинно только
на первом чтении press-start кадра, не оставаться стабильным для всех consumers.
Для frame-stable snapshot сравнивать надлежит `InputAxis` — живую
реализацию, — а не соседа по папке. Даже мёртвый `SelectAxis.cs:11-38`
подтверждает ту же идею: поле `activated` как защёлка фронта.

---

## 6. Область: edge-managed места из аудита клавиш

Ниже — места, где повторный импульс способен менять состояние или нарушать
запланированный темп. Кроме мигания Z, строки — оценка риска, а не утверждение,
что этот симптом отдельно воспроизведён. Все указанные edge-managed места сейчас
переведены на `HeldKeyEdge`/sampled state.

| Место | Действие | Риск повторного импульса |
|---|---|---|
| `SextantController.Update` | Z открыть/закрыть | повторное переключение |
| `SextantController.Update` | P справка | повторное переключение |
| `ConstellationMesh.Update` | Shift+Z | повторное переключение |
| `SextantInputPoller` | 4 линза | повторное переключение |
| `ChronometerOverlayHandler.LateUpdate` | F7 fallback | повторное переключение |
| `SextantInputPoller.PollFix` | I записать наблюдение | повторная запись/смена слота; runtime на рабочем сейве запрещён |
| `SextantInputPoller` | Ctrl+стрелки | повторный press сбрасывает таймер тонкого барабана |

Идемпотентны и не трогаются: фильтры `1/2/3/0` (`SextantInputPoller.cs:108-111`),
зум `+/-` и `R` (`:128-138`), `C` (`:150`), `O` (`:226`),
`MapShapeEraser.cs:76` (Delete), `RadioHandler.cs:92` (Escape).

### Барабан вернье: чувствительность к повторным press-импульсам

`SextantArcInput.AdvanceFineHold` (`:30-44`) по замыслу: тик на нажатии, затем
`FineRepeatDelay` = 0.3 с и `FineRepeatInterval` = 0.15 с, как настоящий барабан.
Ветка `if (pressed)` (`:32-35`) намеренно перезапускает таймер для свежего
нажатия. **Если** источник ошибочно подаёт повторные press-импульсы во время
одного удержания, они будут перехватывать темп. При штатном одном press-start
это корректная repeat-policy; ошибка источника для стрелок не установлена.

Ветка Ctrl+стрелки **вообще не логируется** — `logVector` зовётся только в
ветке без Ctrl (`SextantInputPoller.cs:60-61`); данных о повторе press во время
такого удержания здесь нет.

---

## 7. Что проверено и границы исключения гипотез

Проверки ниже понижают вероятность некоторых причин, но не содержат телеметрии
физических keydown/keyup и потому не устанавливают источник каждого импульса.

| Гипотеза | Что показывают данные (не сильнее этого) |
|---|---|
| Внешний агент (мост/скрипт) | `[McpBridge]` только на строках 16–37 прежнего снимка, до бурстов |
| Второй экземпляр контроллера (двойная загрузка) | `HasComponentNamed` уменьшает риск повторного добавления, но не гарантирует exclusive ownership. Read-only enumeration 2026-10-06 нашёл один текущий controller; отсутствие дубля в старом эпизоде не доказано, §14.2 |
| `TryOpenForProbe` как источник | Повторный open идемпотентен, но явное чередование probe open/close могло бы переключать прибор; полного request trace нет |
| Шим нового Input System | `Unity.InputSystem.dll` в `UBOAT_Data\Managed` отсутствует; игра читает свои клавиши через `InputAxis` |
| Потеря фокуса окна | автор её не наблюдал в воспроизведении; это не исключает иные краткие изменения состояния ввода |
| Блокада `GameInput` | блокирует обработку игровых хоткеев; не доказывает, какое состояние возвращает Unity `Input.GetKey` |
| Коллизия привязок | дамп осей не содержит Z; это не полный аудит hard-coded handlers, см. `game-hotkeys.md` |
| Отключение компоненты/панели | Собственный код мода не деактивирует radio, но native Escape Menu отключает siblings под Viewport; отсутствие `OnDisable` cleanup — открытый дефект, §14.2 |
| Ручной `Camera.Render()` | по текущему коду `MANUALRENDER` — имя телеметрии (`SextantVectorLog.cs`), не доказательство ввода |

Первоначальный список гипотез не устанавливает источник: нет raw key telemetry
и нет read-only live trace связывающего физическую клавишу с каждым toggle.
Текущая реализация ограничивает действия rising edge из удерживаемого состояния,
но причина исходного поведения остаётся неизвестной.

---

## 8. Дизайн правки

### 8.1 Чистый хелпер (C# 7.0, без Unity) — `Source/Logic/HeldKeyEdge.cs`

```csharp
public sealed class HeldKeyEdge
{
    private bool wasDown;

    public bool Advance(bool nowDown)
    {
        bool edge = nowDown && !wasDown;
        wasDown = nowDown;
        return edge;
    }

    public void Synchronize(bool nowDown)
    {
        wasDown = nowDown;
    }
}
```

`Synchronize` вызывается при `OnEnable`, чтобы клавиша, уже зажатая на границе
жизненного цикла компонента, не выглядела новым нажатием.

### 8.2 Подключение

- `SextantController.Update`: один вызов `Advance` для Z/P **до ветвления
  `IsOpen` и до `Close(); return;`**; `OnEnable` синхронизирует текущее состояние.
- `SextantInputPoller.SampleEdges`: каждый кадр снимает `GetKey` для Alpha4,
  Keypad4, I и четырёх стрелок. `actionsEnabled` отбрасывает edge-triggered
  actions вне открытого секстанта, но не прекращает обновление защёлок.
  Непрерывное управление стрелками по `GetKey` в `PollArc`/`PollYawTrim`
  сохраняет существующее поведение удержания при открытии.
- `PollLens`/`PollFix` потребляют готовые фронты; стрелочные верньер и yaw-repeat
  получают edge только на старте удержания, а внутренний таймер `AdvanceFineHold`
  сохраняет задержку/интервал повторов.
- `ConstellationMesh.Update` edge-детектирует Z независимо от клавиши-модификатора
  Shift; `ChronometerOverlayHandler.LateUpdate` опрашивает F7 до поиска native
  bridge, чтобы удержание не превращалось в свежий fallback press.
- Все компоненты вызывают `Synchronize` в `OnEnable`; сбрасывать **edge-state в
  false** на `OnDisable` не требуется. Это не отменяет необходимый modal cleanup
  (`Close`, blockade/PIP/canvas) на отключении: сейчас он отсутствует, §14.2.

### 8.3 Frame snapshot и потребление события — разные контракты

Native `InputAxis` мемоизирует **и current, и previous** для стабильных reads
несколькими consumers. В моде `Advance` вызывается ровно один раз владельцем
перед action branches, поэтому отдельный frame cache для этой проводки не нужен.
Это не означает, что native cache ошибочен. При будущем общем input snapshot
его следует сохранять, а single-action dispatch/ownership обеспечивать отдельно.
`RawInputAxis` из §5 — другая реализация, не дефект живого `InputAxis`.

### 8.4 Гарантия и остаточный риск

- Пока `Input.GetKey` непрерывно остаётся `true`, один владелец edge-state
  выдаёт один фронт; повторное `GetKeyDown` само по себе больше не является
  источником действий этих обработчиков.
- При обычной последовательности `false → true` поведение одноразового
  нажатия сохраняется.

**Остаточный риск:** если Unity на кадр наблюдает `false` во время физического
удержания, последующий `true` станет новым фронтом. Пользователь потери фокуса
не наблюдал, но runtime sample/telemetry для этого сценария нет. Дебаунс на
предположении не добавлялся.

---

## 9. Тесты

`Dev/Tests/HeldKeyEdgeTests.cs` (чистая логика, без Unity, 5 тестов):

- одно нажатие = ровно один фронт;
- удержание N кадров = **ровно один** фронт (ядро требования);
- отпустить → нажать = второй фронт;
- синхронизация при включении не создаёт ложного фронта;
- `nowDown=false` на первом кадре (чистый старт).

Пятый тест прогоняет фронт через `SextantArcInput.AdvanceFineHold`: немедленный
тик, тишина до `FineRepeatDelay`, затем тики по `FineRepeatInterval`. Отдельные
существующие `SextantArcInputTests` также закрепляют таймерные границы.

---

## 10. Гейт SourceCheck `ToggleKeys_AreEdgeDetected`

Реализован в `SourceCheckTests.ToggleKeys_AreEdgeDetected`. Проверяет **вызовы**,
а не наличие полей (правило проекта «объявлено ≠ используется»):

1. в `Update` контроллера — вызовы `Advance` для Z и P;
2. вызовы `Advance` для Alpha4/Keypad4/I в `SampleEdges`, потребление готовых
   `lensPressedThisFrame`/`fixPressedThisFrame` в `PollLens`/`PollFix`;
3. вызов для `Shift+Z` в `ConstellationMesh`, для `F7` в хронометре;
4. передачу edge-состояний `arcPressedThisFrame` и `yawPressedThisFrame` в
   `AdvanceFineHold`;
5. **отсутствие** raw `GetKeyDown` для Z/P/4/I/стрелок/F7 и Shift+Z по всему
   `Source/**` — по AST-вызовам, а не по прозе комментария;
6. Sternspreiz key-4 gate сверяет sampling Alpha4/Keypad4 в `SampleEdges` и
   потребление фронта в `PollLens`.

Мутационный прогон. Харнесс обязан делать чистую пересборку, восстанавливать
файл в `finally`, снимать копию один раз, а вердикт — требовать **ожидаемого**
имени падения («Passed: 0, Failed: 0» = ошибка харнесса, а не зелёный цвет;
инкрементальная сборка однажды уже «поймала» мутацию, которую не собирала).

| Мутация | Ожидаемый падающий гейт |
|---|---|
| вернуть `GetKeyDown` в `SextantController.cs:296` | гейт, правило 5 |
| заменить `Advance` на `return nowDown` | тесты `HeldKeyEdge` |
| удалить вызов для `I` | гейт, правило 2 |
| удалить вызов для `Shift+Z` | гейт, правило 3 |
| убрать передачу `pressed` в `AdvanceFineHold` | тесты барабана |
| переписать гейт на проверку объявления поля вместо вызова | гейт обязан **остаться красным** |

Прогон:

```
dotnet run --project Dev/RadioNavigation.SourceCheck -- ToggleKeys
powershell -ExecutionPolicy Bypass -File Tools/gates.ps1
```

---

## 11. Документация и границы утверждений

Выполнено:

1. `knowledge/inventory/game-input-semantics.md` фиксирует только подтверждённое
   поведение `InputAxis.Value`/`Pressed`/`JustPressed`/`JustReleased` из игрового
   исходника и отдельно оговаривает, что это не универсальная модель всех
   hard-coded handlers. Повтор `GetKeyDown` при удержании за факт не выдаётся.
2. `knowledge/inventory/game-hotkeys.md` ссылается на эту запись и явно
   разделяет binding dump и поведение отдельных обработчиков.
3. `docs/plans/SEXTANT_PLAN.md` больше не приводит F7 в моде как прецедент
   `GetKeyDown`; исправлены неподтверждённые F7/G в списке занятых и уточнён
   статус букв, отсутствующих в binding dump.
4. `AGENTS.md` Gotchas закрепляет текущую edge-политику, ограничение диагноза и
   запрет тестировать I на рабочем сейве.

Документация не утверждает, что найден корень мигания: для него нет raw-input
телеметрии. Она разделяет наблюдённый симптом, проверенный game-source контракт
и defensive mitigation.

---

## 12. Ловушки по конвенциям проекта

- **C# 7.0** (жёсткий контракт игрового компилятора): никаких `default;`,
  выражений-«выражений», `switch`-expressions. Иначе `BadImageFormatException`
  в игре.
- **`const bool` как фича-флаг запрещён** — сворачивается и даёт CS0162 в
  игровом компиляторе (использовать `static readonly bool`). Здесь флагов нет,
  но记住 при добавлении опции.
- **CS0169** (поле объявлено, но не используется) ломает
  `Mod_CompilesAgainstCleanGameApi` — гейт по вызовам обязателен ещё и поэтому:
  поймает «поле объявили, а не подключили».
- **Не проверять правилом текст вида `Input.GetKeyDown(KeyCode.Z)`** — мутация
  класса `Shader.PropertyToID` (строка как доказательство присутствия) уже
  проходила в этом проекте. Проверять: вызов `Advance` с указанием владельца +
  отсутствие идентификатора.
- **Порог в секундах, если появится:** объявлять `float`, печатать через
  `InvariantCulture` (хост живёт в культуре с запятой); литерал порога держать в
  тесте как `const float`, а не вычислять из мутируемой константы.
- **Сложность:** `MethodComplexity_NoSuperMethods` повторно пройден после
  подключения edge-поллинга — `SextantController.Update` остался в пороге.

---

## 13. Порядок работ

1. Проверить существующие данные, не записывая новые наблюдения на рабочий сейв.
2. `HeldKeyEdge` + pure unit tests — **выполнено**.
3. Подключить все edge-managed sites и fine-hold — **выполнено**.
4. Добавить SourceCheck wiring gate — **выполнено**; целевой прогон PASS.
5. Обновить input reference, Sextant plan и журнал — **выполнено**.
6. `Tools/tests.ps1`: **1416 PASS / 0 FAIL**; полный SourceCheck:
   **101 PASS / 0 FAIL**; `Tools/gates.ps1`: **ALL GREEN**; `graphify update .`
   выполнен. Повторный полный gate также **ALL GREEN**. Live Z пройдена (§14.1);
   остальные live bindings остаются ожидающими; тест I на рабочем сейве не выполнять.
7. Коммит не создавался.

---

## 14. Приёмка

- [x] Исходный код и логи исследованы; подтверждено, что raw key edges в лог не
      попадают. Причина мигания остаётся гипотезой.
- [x] Удержание Z в live view → ровно одна строка `opened`, ни одной `closed`
      в тестовом интервале на загруженной новой сборке (FPP внутри лодки, §14.1).
- [x] Повторное удержание Z при открытом приборе → одна `closed`, без повторного открытия.
- [x] Короткое нажатие по-прежнему открывает и закрывает (100 мс, §14.1).
- [ ] Удержание `4` → линза не дёргается.
- [ ] Удержание `P` → панель справки не мигает.
- [ ] Удержание `Shift+Z` → созвездия переключаются один раз.
- [ ] Удержание `F7` → хронометр переключается один раз.
- [x] Удержание `I` runtime не проверять на текущем рабочем сейве: действие
      записывает running fix. Безопасная замена — отдельная disposable-копия
      кампании, если live-проверка позже будет отдельно разрешена.
- [ ] Удержание `Ctrl`+стрелка 2 с → темп тиков соответствует
      `FineRepeatDelay`/`FineRepeatInterval`, а не титровке ОС.
- [x] `dotnet run --project Dev/RadioNavigation.SourceCheck` — **101 PASS / 0 FAIL**.
- [x] `powershell -ExecutionPolicy Bypass -File Tools/tests.ps1` — **1416 PASS / 0 FAIL**.
- [x] `Tools/gates.ps1` — ALL GREEN (build → SourceCheck+quick → tests → PVS).
- [x] `graphify update .` — code graph обновлён; HTML не сгенерирован по лимиту 5000 узлов.

Живой деплой — **только** через `Tools/run-game.ps1` (touch исходников + рестарт
игры; скрипт убивает игру и quick-load'ит последний сейв — требует согласия
автора). DLL из `Tools/build.ps1` в `Temp\` не копировать: на 2026.1-patch21 этот
обход ломает загрузку мода.

### 14.1 Повторная проверка Z в игре (2026-10-06)

Автор выбрал самостоятельный запуск игры. До ввода проверены: `loading.ready=true`,
`HeldKeyEdge` найден в `radionavigation` с `Advance`/`Synchronize`, ровно один
`SextantController` (`/GUI/Viewport/RadioNavigationPanel`, Main Scene), камера
FPP внутри лодки, прибор закрыт, наблюдения/фикс отсутствуют. Симуляция временно
поставлена на паузу; аппаратный polling остаётся кадровым.

Первые две попытки `SendInput` были отклонены `not-foreground` (браузер на
переднем плане); действий Z не произошло, эти попытки **не засчитаны**. Далее
один hold проведён через PostMessage с readback held-state. Остальные пять
сценариев — через `SendInput` с `foreground=true`. Доставка `accepted=true`
сама по себе не считалась PASS: сверены игровые постусловия, состояние `zEdge`
и число переключений **между уникальными BEGIN/END-маркерами** в логе.

| Маркер `KEYEDGE_Z_20261006_…` | Ввод | `opened` | `closed` | После отпускания |
|---|---|---:|---:|---|
| `POST_OPEN_HOLD` | PostMessage down → ≥3 с → up | 1 | 0 | open=True, wasDown=False |
| `SEND_CLOSE_HOLD` | SendInput, удержание 3000 мс | 0 | 1 | open=False, wasDown=False |
| `SEND_OPEN_HOLD_2` | SendInput, удержание 3000 мс | 1 | 0 | open=True, wasDown=False |
| `SEND_CLOSE_HOLD_2` | SendInput, удержание 3000 мс | 0 | 1 | open=False, wasDown=False |
| `SEND_TAP_OPEN` | SendInput, 100 мс | 1 | 0 | open=True, wasDown=False |
| `SEND_TAP_CLOSE` | SendInput, 100 мс | 0 | 1 | open=False, wasDown=False |

Для PostMessage `zEdge.wasDown=True` проверен в начале и в конце удержания;
`log_assert_absent` подтвердил отсутствие `closed` за 3062 мс. В SendInput
hold-вызовах прошло 3034/3010/3010 мс. Во всех шести ограниченных интервалах —
ноль `OBS slot=` и исключений. Это **PASS в перечисленных условиях**, не
доказательство причины старого мигания и не покадровая визуальная приёмка PIP.
Сценарии с внешним видом, переходами камеры/фокуса и другими bindings отдельно
не проверялись.

Восстановлено: прибор closed, arc=0, filters=0/0, камера FPP, пауза False,
`SunEphemerisCalibration.DeltaMinutes=-6.619999885559082` (исходное значение),
PlayerPrefs `RadioNav.SunCalibrationMinutes=-6.62` не изменился. `I`, `O` и save
не вызывались; сравнение всех `.save` до/после: **23/23**, SHA-256/длина/mtime
совпадают, добавленных/удалённых файлов нет.

Read-only receipts: `%LOCALAPPDATA%/Temp/opencode/key-edge-z-before-20261006.json`,
`key-edge-z-after-20261006.json`, `key-edge-z-log-check-20261006.json`.
Номера строк в receipt относятся только к снимку свежего лога этой приёмки;
дальнейший запуск игры может заменить `output_log.txt`.

### 14.2 Исходный код и native-практики (2026-10-06)

Проверен исходный `SextantController` из коммита
`bfa2c211bfe68b4f2ade438843891983a0e19952` (до edge-правки) и текущая рабочая
версия. В исходном `Update:264-300` ветки `IsOpen` / `else if` взаимоисключающие,
после `Close` стоит `return`. Одно выполнение этого метода не может сначала
открыть, затем закрыть прибор по тому же нажатию. В исходниках мода не найден
второй вызывающий путь `Update`, таймер автопереключения Z или `ResetInputAxes`.
Программные open/close доступны также через `SextantProbe`, а destruction
пишет `IsOpen=false`; это отдельные причины изменения состояния, не keyboard hold.

**Подтверждённые проблемы конструкции, всё ещё не исправленные:**

1. **Нет очистки при отключении контроллера.** В текущем
   `Source/UI/SextantController.cs:348-378` cleanup существует только в
   `OnDestroy`; `OnDisable` отсутствует. Canvas создаётся отдельным корнем
   (`SextantOverlay.cs:111-147`), PIP-ресурсы тоже живут отдельно.
   Отключение панели останавливает Update/LateUpdate, но само по себе не
   снимает блокаду, не прячет overlay и не выключает PIP. Это конкретный путь
   к несогласованному состоянию. Native `EscapeMenu.OnEnable:144-152` отключает
   активных siblings; read-only reflection подтвердил общий parent
   `/GUI/Viewport` у Escape Menu и RadioNavigationPanel и отсутствие parent
   у sextant canvas. Сам сценарий Escape с открытым прибором **не выполнялся**.
2. **Нет проверки, кому принадлежит ввод.** `CanOpen:404-423` проверяет только
   camera mode, resolve камеры и наличие `GameInput`. Он не проверяет чужие
   блокировки или focused text field; открытая ветка `Update:288-316` также
   продолжает принимать действия при появлении чужого modal owner.
   Это позволяет секстанту реагировать на клавиши, предназначенные другому UI.
   Native `KeyboardShortcuts.DoUpdate:216-245` и chronometer shortcut
   `ShipControlUI.Update:950-952` проверяют permission до действия.
3. **Общее состояние без исключительного владельца.** `IsOpen:52` — static,
   `Awake:164` безусловно заменяет `Instance`; нет guard на второй контроллер.
   Второй экземпляр в тот же press-frame видит `IsOpen=true` от первого и
   исполняет свой `Close`: глобальный флаг становится false, но canvas и
   blockade первого остаются. Более того, `OnDestroy:358` любого экземпляра
   сбрасывает общий флаг и pose/radius даже если он не владелец открытого прибора.
   `HasComponentNamed` и bootstrap уменьшают вероятность появления дублей, но
   не делают такой lifecycle безопасным. В текущей игре read-only enumeration
   нашёл **один** SextantController и один RadioHandler; дубль в старом эпизоде
   **не установлен**.

Дополнительный пробел относительно native map/binocular consumers: gate
`IsLiveViewMode:879-886` не учитывает `IsInTransition` и состояние загрузки/UI.
Наличие этих проверок нужно выбирать по контракту устройства; простое
копирование всех native-условий может изменить разрешённое поведение FPP.

**Что именно делают разработчики игры** (пути относительно
`C:\code\projects\uboat`):

- `UBOAT.Game.Core.Input/InputAxis.cs:120-218`: held-state sampling, кэш по
  `Time.frameCount`, `JustPressed` из previous/current. Но это **не потребление**
  события: несколько reads в одном press-frame возвращают true.
- `UBOAT.Game.Core.Input/GameInput.cs:106-169`: owner-based blockade;
  removal отложен на кадр и не сбрасывает Unity Input. `HasActiveBlockades`
  дополнительно проверяет focused TMP field; `Except(this)` — только список
  владельцев. Нельзя заменить gate одним `HasActiveBlockades()` при открытом
  секстанте: его собственная блокада тогда запретит даже закрытие.
- `GameInput.cs:180-197`: Cancel имеет отдельный consumed-frame guard.
- `UBOAT.Game.UI/JournalUI.cs:119-126`, `EscapeMenu.cs:156-186`: cleanup на
  `OnDisable`, а не только explicit Close / destroy.
- `UBOAT.Game.Scene.Camera.ExternalControllers/PeriscopeController.cs:465-480`
  использует обычный `GetKeyDown`/`GetKeyUp` для Ctrl-зума с state guard.
  Поэтому сам вызов `GetKeyDown` нельзя объявлять ошибкой вопреки native-коду.

**Офлайн-проверка исходных методов, не новый game acceptance.** В временном
Roslyn-harness методы `Update`, `CanOpen`, `Close`, `OnDestroy`, camera/shift
gates копировались из обеих версий без изменения тела. Для текущей версии
также скопированы edge-синхронизация и настоящий `HeldKeyEdge`. Рендер,
worksheet и калибровка заменены инертными объектами; `TryOpen` моделирует только
`CanOpen` + публикацию blockade/canvas/IsOpen, а native one-frame tween removal
не моделируется. `DisableOwner` — проверка отсутствия callback и остановки
диспетчеризации, не фактический вызов Unity engine.

| Сценарий модели | До edge-правки | После HeldKeyEdge |
|---|---|---|
| Один owner, press-start + 120 held-frames | Один `opened` | Один `opened` |
| Один owner, Update дважды в press-frame | `opened` → `closed` | Один `opened` |
| Чужой input owner уже блокирует ввод | Прибор открывается | Прибор открывается |
| Отключить owner без destruction | Open/canvas/blockade остаются | То же |
| Два owner, одно нажатие | Open=False, canvas A и blockade A остаются | То же |
| Destroy другого экземпляра при открытом A | Open=False, canvas A и blockade A остаются | То же |

Также выполнены неизменённые `InputAxis.Value`/`JustPressed`: первый read=true,
повторный read в том же кадре=true, следующий held-frame=false. Это показывает,
почему перенос на `InputAxis` сам по себе не решает двух владельцев.
Повторный вызов Update одной компоненты в реальной игре **не обнаружен**;
соответствующая строка таблицы проверяет чувствительность к такой предпосылке,
а не доказывает её наличие.

Результат с hashes и ограничениями:
`artifacts/reports/key-edge-source-audit-20261006.json`.
Harness: `%LOCALAPPDATA%/Temp/opencode/key-edge-source-audit/Audit.csproj`,
запуск `dotnet run --project Audit.csproj -c Release` из его каталога.
Это **12 model-сценариев** (6 × 2 версии) + native cache check, не прибавка к
1416 product-unit tests. Существующий `Blockade_AcquireReleaseBalanced`
проверяет `ChronometerDragProtocol`, не lifecycle секстанта; edge wiring gate
не утверждает exclusive ownership или modal cleanup.

**Рекомендуемая следующая правка:** единственный owner с owner-scoped
open-state и teardown, идемпотентный `OnDisable`→Close, затем разрешение действия
через native blockade/text-focus policy (sampling остаётся непрерывным даже
когда action запрещён). Frame guard полезен для повторной диспетчеризации,
но не заменяет ownership. Debounce не добавлять без измеренных false-release
samples. На этом этапе production-код **не менялся**, игрового ввода,
паузы/камеры/prefs/save actions не было; использовалась только read-only reflection.

---

## 15. Журнал приёмки

_(заполняется по мере выполнения; дата — результат)_

```
2026-10-06  Исполнение начато. Безопасный read-only разбор: Z на карте за ~3 с
            дал одну запись ignored outside live view; P-события нельзя
            восстановить по help shown/hidden. Автор подтвердил непрерывное
            удержание Z и отсутствие потери фокуса; причина повтора не доказана.
2026-10-06  Реализован HeldKeyEdge и внедрён в Z/P, I/4, стрелки, Shift+Z, F7;
            синхронизация при OnEnable; действия вне активного режима отбрасываются.
            Добавлены 5 unit-тестов и SourceCheck ToggleKeys_AreEdgeDetected.
            Unit tests: 1416 PASS / 0 FAIL. Full SourceCheck: 101 PASS / 0 FAIL.
            Первый SourceCheck обнаружил устаревшее ожидание старого key-polling
            пути в Sternspreiz gate; gate обновлён на разделённую sample/consume
            проводку. Также исправлен отсутствовавший final newline в плане.
2026-10-06  Первый полный gate нашёл PVS V3125 на условной null-проверке
            inputPoller перед позднейшим обязательным вызовом. Удалена лишняя
            проверка: Awake создаёт poller до Update. Повторный полный gate:
            build/analyze/tests/PVS — ALL GREEN (1416 unit, 101 SourceCheck).
2026-10-06  `graphify update .`: обновлено 9688 узлов / 17619 рёбер /
            680 сообществ; HTML пропущен штатно (лимит 5000). Graphify сообщил,
            что 687 data/config files дают ноль AST-узлов; semantic/doc graph
            этим code-only update не обновлялся.
            Mutation run и live acceptance не выполнялись. Игра, деплой и рабочий
            сейв не запускались/не менялись.
2026-10-06  Повторный полный gate: build/analyze/tests/PVS ALL GREEN,
            1416 unit / 101 SourceCheck, 329.3 с. Автор запустил игру сам.
            Новый HeldKeyEdge проверен reflection; live Z — 6/6 PASS (§14.1),
            один переход на каждое удержание/нажатие. I/O/save не вызывались;
            все 23 .save не изменились (SHA-256/длина/mtime). Прибор закрыт,
            камера FPP, пауза и калибровка возвращены. Остальные bindings,
            mutation run и покадровая визуальная приёмка здесь не закрываются.
2026-10-06  Source-аудит исходного bfa2c211 и текущей версии (§14.2): найдены
            открытые modal gate / OnDisable / exclusive-owner дефекты.
            12 source-slice model-сценариев + native InputAxis cache check
            выполнены; это не Unity/live reproduction физического удержания.
            Исходный штатный hold тоже даёт один opened. Native source и
            read-only runtime hierarchy проверены; дубликат сейчас не найден.
            Production-код и живое состояние этой проверкой не менялись.
```
