# Фаза 2: Устранение хрупких зависимостей

**Приоритет:** Критично | **Сложность:** Средняя | **Шагов:** 18 (из 18)

---

### Шаг 2.1 — Кэширование hierarchy lookups (все MonoBehaviour)
- Все `transform.Find()` перенести в `Start()` / `Awake()`, сохранять в приватные поля
- Затронутые файлы:
  - `GroundHandler.cs` — `Find("Background")`, `Find("GroundCheckmark")`, `parent.Find("Toggle")`
  - `ToggleHandler.cs` — `Find("Background")`, `Find("on")`, `Find("off")`, `parent.Find("ciferbllat")`
  - `ZoneHandler.cs` — 4 уровня `Find()` в `CanSwitchZone()`
  - `Main.cs` — 8 вызовов `Find()` в `SceneEventsListener_OnSceneStart`

**❌ Не нужен (почти весь).** Большинство `Find()` уже в `Start()`/`Awake()` — однократные вызовы. `Main.cs:OnSceneStart` — тоже однократный. Единственный hot path: `ZoneHandler.CanSwitchZone()` — покрыт шагом 2.2.

---

### Шаг 2.2 — ZoneHandler: кэширование Toggle и Ground (`ZoneHandler.cs`)
- `CanSwitchZone()` делает 4 `Find()` + `GetComponent()` каждый клик
- Сохранить `powerToggle` и `groundBgImage` в `Start()`

**✅ Нужен.** `ZoneHandler.cs:93-117`: `CanSwitchZone()` вызывается **каждый клик**. 4 `Find()` + `GetComponent()` на каждый клик — GC pressure + лишняя работа.

---

### Шаг 2.3 — Guard для null parent (`GroundHandler.cs:77`)
- `transform.parent.Find("Toggle")` — добавить null-check на `transform.parent`

**✅ Нужен.** `GroundHandler.cs:77`: `transform.parent.Find("Toggle")` — если parent detached (destroy, hierarchy changed) → NPE.

---

### Шаг 2.4 — Guard для null parent (`ExpanderHandler.cs:19`, `ToggleHandler.cs:47`)
- `ExpanderHandler`: `transform.parent.GetComponent<RectTransform>()` — добавить null-check на `transform.parent`
- `ToggleHandler`: `transform.parent.Find("ciferbllat")` — добавить null-check на `transform.parent`
- Идентичная проблема шагу 2.3, но для других компонентов

**✅ Нужен.** `ExpanderHandler.cs:19`: `transform.parent.GetComponent<RectTransform>()` — NPE если parent null. `ToggleHandler.cs:47`: `transform.parent.Find("ciferbllat")` — то же.

---

### Шаг 2.5 — Unsubscribe от событий (`Main.cs:69-70`)
- `SceneEventsListener.OnSceneStart += ...` и `OnSceneAwake` — нет отписки (`-=`)
- При выгрузке/перезагрузке мода подписки накапливаются → дублирование вызовов
- Добавить отписку в `OnDestroy()` или аналогичный lifecycle hook

**✅ Нужен.** `Main.cs:69-70`: `+=` без `-=` → подписки накапливаются при reload мода → `SceneEventsListener_OnSceneStart` вызывается N раз.

---

### Шаг 2.6 — ZoneHandler: EventTrigger leak (`ZoneHandler.cs:38-70`)
- `SetupClickHandler()` добавляет Entry в `trigger.triggers` при каждом `Start()`
- Если объект reactivate (disable/enable), старые Entry остаются → дублирование
- В шаге 4.3 предлагается замена на `IPointerClickHandler`, но этот баг нужно исправить до этого (минимально: очищать `triggers` перед добавлением)

**❌ Не нужен.** `Start()` вызывается **1 раз за lifetime** Unity MonoBehaviour. Disable/enable **не** вызывает `Start()` повторно. Leak возможен только при remove/re-add компонента — не используется в текущем коде.

---

### Шаг 2.7 — ToggleHandler: избыточный `SetActive` + `img.enabled` (`ToggleHandler.cs:86-97`)
- `OnToggleChanged` вызывает `SetActive(isOn)` и `img.enabled = isOn`
- `SetActive(false)` уже деактивирует GameObject — `enabled` бесполезен
- Убрать `img.enabled = ...` (оставить только `SetActive`)

**✅ Нужен.** `ToggleHandler.cs:86-97`: `SetActive(false)` деактивирует entire GameObject → `img.enabled = false` **бесполезен**. Избыточный код = путь к багу.

---

### Шаг 2.8 — ZoneHandler: неполное кэширование в `CanSwitchZone()` (`ZoneHandler.cs:93-117`)
- Шаг 2.2 кэширует `powerToggle` и `groundBgImage`, но `CanSwitchZone()` всё равно делает:
  - `transform.parent.Find("Ground")` (line 106) — **не кэшировано**
  - `groundT.Find("Background")` (line 109) — будет кэширован как `groundBgImage`
- **Итого**: если кэшировать только `groundBgImage`, `Find("Ground")` всё ещё вызывается каждый клик
- Решение: кэшировать `groundTransform` в `Start()` и использовать его в `CanSwitchZone()` для доступа к Background
- Альтернатива: кэшировать `groundBgImage` напрямую из `transform.Find("Ground/Background")` (one-liner)

**✅ Нужен.** `ZoneHandler.cs:106`: `parent.Find("Ground")` — не кэшировано. `Find("Ground/Background")` напрямую решает проблему одним вызовом.

---

### Шаг 2.9 — PanelDragHandler: утечка Texture2D при перезагрузке сцены (`PanelDragHandler.cs:23-64`)
- `GenerateMoveCursor()` создаёт новую `Texture2D` при каждом `Start()` (после unload/load сцены)
- Шаг 4.2 предлагает кэшировать в static поле — но **не упоминает** `DestroyImmediate()` старой текстуры
- При каждой перезагрузке сцены: новая текстура в памяти, старая — GC не может собрать (ссылка на ней была)
- ~~Решение: в static setter кэша — `if (cached != null) DestroyImmediate(cached); cached = new;`~~ **⚠️ ИСПРАВЛЕНИЕ v3**: `DestroyImmediate()` в runtime небезопасен (только Editor context). Правильный подход:
  - Кэшировать текстуру в **static поле**, создавать **один раз** (`if (cached != null) return;`)
  - Уничтожать текстуру только при **выгрузке мода** или **application quit**: `Application.quitting += () => { if (cached != null) Destroy(cached); };`
  - Не пересоздавать текстуру при каждом `Start()` — она одна на весь lifecycle мода
- **Приоритет**: важно — утечка памяти при reload сцены + неверный `DestroyImmediate` в runtime

**✅ Нужен.** `PanelDragHandler.cs:25`: `new Texture2D(32, 32)` каждый `Start()`. При reload сцены — старая текстура не уничтожается. Static cache решает, но ownership model текстуры должен быть определён: кто создаёт, кто уничтожает, когда.

---

### Шаг 2.10 — ZoneHandler: хардкод имён зон (`ZoneHandler.cs:13`)
- `ZoneNames = { "Zone1", "Zone2", "Zone3", "Zone4", "Zone5" }` — хардкод
- Если в prefab'е `Radio` зоны называются иначе (например `Zone_1`, `ZoneA`, `zone1`) — `transform.Find()` возвращает `null` → `zoneImages[i] = null` → зоны молча не работают
- **Действие**: либо верифицировать имена в prefab'е, либо добавить fallback `Find("Zone" + (i+1))` с предупреждением
- Кэшировать в `Start()` (уже сделано), но добавить лог для каждой не найденной зоны

**✅ Нужен.** Если имена не совпадают — `zoneImages[i] = null` → зоны молча не работают, нет ошибки. Нужен лог для каждой не найденной зоны.

---

### Шаг 2.11 — PanelDragHandler → ExpanderHandler: хрупкая coupling (`PanelDragHandler.cs:62-63`)
- `OnBeginDrag` проверяет `eventData.pointerPress.GetComponentInParent<ExpanderHandler>()`
- Если Expander перемещён в иерархии (вынесен из панели) или переименован — проверка ломается
- Дополнительно: `GetComponentInParent` ищет по типу компонента, а не по позиции — но если ExpanderHandler будет на другом объекте, проверка пропустит его
- **Решение**: либо кэшировать `ExpanderHandler` при `Start()`, либо добавить tag/layer для идентификации

**✅ Нужен.** `PanelDragHandler.cs:72`: `GetComponentInParent<ExpanderHandler>()` — хрупкая связь. Если ExpanderHandler переместится в иерархии → drag guard сломается.

---

### Шаг 2.12 — InjectT3PLL38Decision: тихая ошибка при инжекции (`RadioStoryIntegration.cs:70-138`)
- Весь метод обёрнут в `catch (Exception ex) { Debug.LogError(...) }`
- Если инжекция сломалась **на полпути** (decision создан, но `InsertDecision` не вызван) — story остаётся в неконсистентном состоянии
- `_onUpdatedMethod?.Invoke(...)` (строка 132) может не вызваться из-за предыдущего исключения
- **Решение**: добавить `bool success` флаг и возвращать его, или логировать что `OnUpdated` не вызван

**✅ Нужен.** `RadioStoryIntegration.cs:70-138`: catch ловит всё. Если decision создан, но `InsertDecision` не вызван — story в неконсистентном состоянии, нет лога об этом.

---

### Шаг 2.13 — ZoneHandler: кэширование `transform.parent` (`ZoneHandler.cs`)
- `CanSwitchZone()` делает `transform.parent` трижды (строки 94, 100, 106)
- Если parent уничтожен между обращениями — NPE на втором/третьем обращении (первое уже проверено null)
- **Решение**: кэшировать `Transform parentTransform` в `Start()`, использовать в `CanSwitchZone()`

**✅ Нужен.** `ZoneHandler.cs:95,99,106`: `transform.parent` 3× — potential NPE если parent destroyed между обращениями. Кэширование решает эту проблему.

---

### Шаг 2.14 — ExpanderHandler: null-check на transform.parent (`ExpanderHandler.cs:19`)
- `panelRect = transform.parent.GetComponent<RectTransform>()` — нет null-check на `transform.parent`
- Если Expander вынесен из иерархии Radio (refactoring, обновление) → NPE в `Start()`
- Аналогичная проблема шагам 2.3 и 2.4, но для ExpanderHandler
- **Решение**: добавить `if (transform.parent == null) { Debug.LogError("[TestUI] ExpanderHandler: parent is null"); return; }`
- **Приоритет**: критично — масштабирование панели неработоспособно при отсутствии parent

**✅ Нужен.** `ExpanderHandler.cs:19`: `transform.parent.GetComponent<RectTransform>()` — если parent null (объект detached, hierarchy changed) → NPE в `Start()`. ExpanderHandler не может масштабировать панель.

---

### Шаг 2.15 — Race condition: Toggle value change vs Ground state (`ToggleHandler.cs:84-110`, `GroundHandler.cs:87-114`)
- `ToggleHandler.OnToggleChanged` (строка 84) выполняется при каждом `isOn` изменении
- `GroundHandler.OnPointerClick` (строка 87) меняет `radioToggle.isOn` (строка 93) **и потом** `radioToggle.interactable` (строка 112)
- Между этими двумя операциями `OnToggleChanged` может выполниться (если `isOn` изменился), видя промежуточное состояние
- **Сценарий**: `radioToggle.isOn = false` → `OnToggleChanged(false)` срабатывает → `onImage.SetActive(false)` → `GroundHandler` продолжает: `radioToggle.interactable = false`
- **Хуже**: если `GroundHandler.OnPointerClick` вызывается извне (программно), `OnToggleChanged` может выполниться **после** `interactable = false`, когда `isOn` уже изменён
- **Решение**: в `GroundHandler.OnPointerClick` блокировать `ToggleHandler` через флаг или отписываться от `onValueChanged` на время изменения
- **Альтернатива**: использовать `Toggle.SetIsOnWithoutNotify()` (Unity 2021+) для изменения `isOn` без вызова listener'ов
- **Приоритет**: важно — race condition при программном изменении Toggle

**✅ Нужен.** `GroundHandler.cs:93`: `radioToggle.isOn = false` вызывает `OnToggleChanged(false)` → `SetActive(false)` выполняется **до** `radioToggle.interactable = false` (строка 112). Промежуточное состояние visible для ToggleHandler.

---

### Шаг 2.16 — Структурная валидация prefab до установки компонентов (`Main.cs:100-159`)
- После `InstantiatePrefab("Radio")` код добавляет компоненты условно: `if (component == null) AddComponent<>()`
- Но **не проверяет** наличие обязательных детей и компонентов до начала установки
- **Сценарий**: `Cursor` отсутствует в prefab → `Find("CursorSpinner")` = null → `CursorHandler` не добавлен → drag-вращение не работает. Код продолжает → `GroundHandler` добавлен → в `Start()` ищет `transform.parent.Find("Toggle")` → null → NPE
- **Решение**: добавить pre-flight проверку после `InstantiatePrefab`:
  ```csharp
  string[] requiredChildren = { "CursorSpinner", "Toggle", "Ground", "Ground/Background", "Ground/Background/GroundCheckmark", "Expander", "RadioZone", "VolumeSpinner", "FeedbackSpinner" };
  var missing = new List<string>();
  foreach (var path in requiredChildren)
  {
      Transform t = radio.transform.Find(path);
      if (t == null) missing.Add(path);
  }
  if (missing.Count > 0)
  {
      Debug.LogError("[TestUI] Prefab structure validation FAILED. Missing: " + string.Join(", ", missing));
      Destroy(radio);
      return;
  }
  ```
- **Приоритет**: критично — partial setup создаёт объект с неработающими subsystems, нет ошибки до NPE в рантайме

**✅ Нужен.** `Main.cs:100-159`: компоненты добавляются условно, но структура prefab не проверяется. Нет обязательных детей → частичный setup → NPE позже.

---

### Шаг 2.17 — Null-check обязательных Unity-компонентов во всех handlers (все MonoBehaviour)
- При динамическом `AddComponent` Unity-компоненты не гарантированы. Каждый handler должен проверять зависимости в `Start()`/`Awake()` и отключаться при failure:
  - `ToggleHandler.cs:37`: `GetComponent<Toggle>()` → если null → `enabled = false; return;` + `LogError`
  - `ToggleHandler.cs:38`: `GetComponent<RectTransform>()` → аналогично
  - `CursorHandler.cs:18`: `GetComponent<RectTransform>()` → аналогично
  - `CursorHandler.cs:21`: `GetComponent<Image>()` → уже проверяется в шаге 1.14, но нет graceful degradation
  - `SpinnerHandlerBase.cs:35`: `GetComponent<RectTransform>()` → аналогично
  - `PanelDragHandler.cs:18-19`: `GetComponent<RectTransform>()`, `GetComponentInParent<Canvas>()` → аналогично
  - `ExpanderHandler.cs:19`: `transform.parent.GetComponent<RectTransform>()` → аналогично (покрыто шагом 2.14, но нет `enabled = false`)
  - `GroundHandler.cs:52`: `transform.Find("Background")` → `bgImage` может быть null, `OnPointerClick` проверяет, но `Start()` — нет
- **Общий паттерн**: каждый handler в `Start()` проверяет обязательные зависимости. При failure: `Debug.LogError("[TestUI] {HandlerName}: {missing component} not found, disabling"); enabled = false; return;`
- **Приоритет**: критично — NPE в рантайме вместо контролируемого отключения

**✅ Нужен.** При `AddComponent` нет гарантии что родительские/собственные компоненты существуют. Каждый handler может крашнуться в `Start()` или позже без fallback.

---

### Шаг 2.18 — Touch/multi-pointer drag guard (`CursorHandler.cs`, `SpinnerHandlerBase.cs`, `PanelDragHandler.cs`)
- Текущий drag guard: `isDragging = true/false` — один флаг на весь компонент
- **Проблема**: на touch-устройствах несколько pointer'ов. Pointer A начинает drag → `isDragging = true`. Pointer B移动 → `OnDrag` вызывается с pointer B, но `isDragging` всё ещё `true` от pointer A → drag-вращение/spinner движется от неправильного pointer
- `PanelDragHandler.cs:76-82`: `OnDrag` перемещает панель по `eventData.delta` — некорректно для multi-touch
- `CursorHandler.cs:31-44`: `OnDrag` вычисляет угол от `Input.mousePosition` → уже исправлено в шаге 4.4 на `eventData.position`, но нет проверки pointer ID
- `SpinnerHandlerBase.cs:56-75`: аналогично
- **Решение**: хранить `activePointerId` в `OnBeginDrag`, проверять в `OnDrag`/`OnEndDrag`:
  ```csharp
  private int activePointerId = -1;
  
  public void OnBeginDrag(PointerEventData eventData)
  {
      if (eventData.button != PointerEventData.InputButton.Left) return;
      isDragging = true;
      activePointerId = eventData.pointerId;
  }
  
  public void OnDrag(PointerEventData eventData)
  {
      if (!isDragging || eventData.pointerId != activePointerId) return;
      // ...
  }
  
  public void OnEndDrag(PointerEventData eventData)
  {
      if (eventData.pointerId != activePointerId) return;
      isDragging = false;
      activePointerId = -1;
  }
  ```
- **Приоритет**: важно — drag handlers работают некорректно на touch, но desktop-only можно считать acceptable scope (документировать limitation)

**✅ Нужен.** `isDragging` один на компонент. Multi-touch: pointer A начинает drag, pointer B移动 обрабатывается как drag A. Некорректное поведение на touch.
