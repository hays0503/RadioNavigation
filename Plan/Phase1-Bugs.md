# Фаза 1: Исправление критических багов

**Приоритет:** Критично | **Сложность:** Низкая | **Шагов:** 25 (из 25)

---

### Шаг 1.1 — Singleton lifecycle (`RadioHandler.cs`)
- Добавить `OnDestroy()`: если `Instance == this`, обнулить `Instance`
- Без этого — dangling reference после перезагрузки сцены

**✅ Нужен.** `RadioHandler.cs:39-44`: `Awake()` не обнуляет `Instance` при destroy. При reload сцены (DontDestroyOnLoad = false) `Instance` указывает на destroyed объект → `Instance == null` на ложном null → dangling reference. `OnDestroy()` решает это.

---

### Шаг 1.2 — PanelDragHandler drag guard (`PanelDragHandler.cs`)
- `OnBeginDrag` делает early return без установки флага — `OnDrag` всё равно выполняется
- Добавить поле `isDragging`, выставлять `true` только в `OnBeginDrag` после проверок
- Проверять `isDragging` в `OnDrag`

**✅ Нужен.** `PanelDragHandler.cs:66-82`: `OnBeginDrag` делает early return для non-left-button и ExpanderHandler, но `OnDrag` вызывается Unity **всегда** после `OnBeginDrag` (EventSystem не проверяет return). Без флага `isDragging` — панель двигается правой кнопкой мыши.

---

### Шаг 1.3 — Fix unicode артефакт (`RadioStoryIntegration.cs:205,213`)
- `"T3PLLä38"` — содержит латинскую `ä`. Проверить совпадение с `LocaleKey = "Radio/OpenT3PLL38"`
- Если `ä` ≠ `3` — это баг в логах (строки 205, 213 используют `ä`, а `LocaleKey` — `3`)

**✅ Нужен.** `RadioStoryIntegration.cs:205`: `"[TestUI] LeaveStation - hiding T3PLLä38 panel"`, строка 213: `"[TestUI] T3PLLä38 toggled"` — латинская `ä` вместо цифры `3`. Бесполезно дезориентирует при отладке.

---

### Шаг 1.4 — Singleton lifecycle guard (`RadioHandler.cs:41-44`)
- Текущий паттерн: `if (Instance == null) Instance = this; else Destroy(gameObject);`
- **Баг**: при перезагрузке сцены (DontDestroyOnLoad = false), `Instance` может указывать на destroyed объект → `Instance == null` → новый экземпляр записывается → старый уже уничтожен
- Если объект **с** DontDestroyOnLoad: новый Awake получает `Instance != null` → убивает себя, хотя `Instance == this` (тот же объект)
- Исправление: `if (Instance != null && Instance != this) Destroy(gameObject); else Instance = this;`
- Добавить `LogWarning` при уничтожении дубликата для диагностики

**✅ Нужен.** Тот же баг что 1.1, но исправление конкретное: `if (Instance != null && Instance != this)` вместо `if (Instance == null)`. Старый паттерн убивает **новый** экземпляр при DontDestroyOnLoad, даже если `Instance` указывает на тот же объект.

---

### Шаг 1.5 — Prefab hierarchy name mismatch (`Main.cs:108`)
- `radio.transform.Find("CursorSpinner")` — если имя в prefab'е `"Cursor"`, Find вернёт `null` → `CursorHandler` никогда не добавится
- Проверить фактическую иерархию prefab'а `Radio` в AssetBundle:
  - `Radio` root → должен содержать children: `CursorSpinner` (или `Cursor`), `Toggle`, `Ground`, `Expander`, `RadioZone`, `VolumeSpinner`, `FeedbackSpinner`
- Если имя не совпадает — переименовать в Find или в prefab'е
- **Приоритет**: критично — без этого drag-вращение стрелки не работает

**✅ Нужен.** AGENTS.md указывает что в prefab'е child называется `Cursor` (или first child), а код ищет `CursorSpinner`. `Find("CursorSpinner")` вернёт `null` → `CursorHandler` не добавится → drag-вращение стрелки не работает.

---

### Шаг 1.6 — Prefab hierarchy: все имена children (`Main.cs:108-150`)
- Шаг 1.5 проверяет только `CursorSpinner`/`Cursor`
- Но **все** имена children в `Main.cs` могут не совпадать с иерархией в AssetBundle:
  - `ToggleHandler.cs:45` — `Find("on")`, `Find("off")` (внутри `Background`)
  - `ToggleHandler.cs:47` — `parent.Find("ciferbllat")` (двойной кириллический: циферблат)
  - `GroundHandler.cs:68` — `Find("Background")`, `Find("GroundCheckmark")`
  - `ZoneHandler.cs:13` — `ZoneNames = { "Zone1"..."Zone5" }` — если в prefab'е другие названия, зоны молча не работают
  - `ExpanderHandler.cs:19` — `transform.parent.GetComponent<RectTransform>()`
- **Действие**: вывести в лог полную иерархию children (recursively) в `Main.cs:OnSceneStart` под `#if DEBUG` для верификации

**✅ Нужен.** Верификация критична: если хотя бы одно имя не совпадает — компонент молча не добавляется, функция не работает, нет ошибки в логе.

---

### Шаг 1.7 — ResourceManager null-check (`Main.cs:99`)
- `resourceManager` — `[Inject] private static ResourceManager resourceManager`
- Если `InjectionFramework.Instance.InjectIntoAssembly(...)` не сработал (тип не зарегистрирован, DI недоступен) — `resourceManager` остаётся `null`
- `Main.cs:99`: `resourceManager.InstantiatePrefab("Radio")` → **NPE**
- План 5.7 проверяет `InjectionFramework.Instance` (сам инжектор), но **не проверяет результат инъекции** — `resourceManager` может быть null даже если DI работает
- Добавить: `if (resourceManager == null) { LogError("ResourceManager not injected"); return; }`

**✅ Нужен.** `[Inject]` — DI атрибут, результат не гарантирован. Если `InjectionFramework` недоступен или тип не зарегистрирован — `resourceManager` = null. `InstantiatePrefab` без null-check → NPE.

---

### Шаг 1.8 — Harmony lifecycle: unpatch на выгрузке мода (`Main.cs:26`)
- `OnLoaded()` создаёт `new Harmony("UBOAT.Mods.TestUI")` как **локальную переменную**
- При выгрузке мода: Harmony патчи остаются активными (patched методы не откатываются)
- Если мод перезагружается: повторный `OnLoaded()` пытается патчить уже запатченные методы → непредсказуемое поведение
- **Решение**:
  - Сохранить `Harmony` в приватное поле: `private Harmony _harmony;`
  - Добавить `IUserMod.OnModUnloading()` (если доступен) или `OnDestroy()` с `harmony?.UnpatchSelf()`
  - Если `IUserMod` не предоставляет unload hook — добавить `[NonSerializedInGameState]` на поле и проверять в `OnLoaded()`

**✅ Нужен.** `Main.cs:25`: `var harmony = new Harmony(...)` — локальная переменная. При выгрузке мода патчи остаются. При повторном `OnLoaded()` — дублирование патчей → непредсказуемое поведение.

---

### Шаг 1.9 — BindingFlags дублирование: все строки (`RadioStoryIntegration.cs:30,63,88`)
- План 6.5 упоминает строки 30 и 63 — `BindingFlags.Instance` указан дважды
- **Пропущена строка 88**: `_onUpdatedMethod = storyType?.GetMethod("OnUpdated", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null)` — та же ошибка
- Итого: 3 места с дублированием `BindingFlags.Instance`, исправить все три

**❌ Не нужен.** `BindingFlags` — битовый enum. `X | X` через OR = `X`. Дублирование `BindingFlags.Instance` **безвредно**, не влияет на результат `GetMethod()`. Это косметика, не баг.

---

### Шаг 1.10 — Lifecycle hooks: мод-уровень (`Main.cs`)
- `Main` класс не имплементирует `OnModUnloading()` / `OnDestroy()`
- При выгрузке/перезагрузке мода без перезапуска:
  - Harmony патчи остаются (Шаг 1.8)
  - `SceneEventsListener.OnSceneStart` подписки остаются (Шаг 2.5 добавляет `-=` но нет где его вызвать)
  - Следующий `OnLoaded()` добавит **ещё одну** подписку → дублирование
- **Решение**: реализовать mod-level lifecycle hook (если API позволяет), либо добавить guard `if (_loaded) return;` с `_loaded` флагом

**✅ Нужен.** `Main.cs:69-70`: подписки на `OnSceneStart` и `OnSceneAwake` без отписки. При.reload мода — `OnLoaded()` вызывается повторно → ещё одна подписка → дублирование.

---

### Шаг 1.11 — InstantiatePrefab: тихий null без лога (`Main.cs:98-99`)
- `resourceManager.InstantiatePrefab("Radio")` может вернуть `null` (AssetBundle не загружен, имя не найдено, bundle повреждён)
- Текущий код: `if (radio == null) return;` — **без логирования**
- Причины: `Bundles/testui` не загружен, `InstantiatePrefab("Radio")` — имя не найдено, AssetBundle повреждён
- Мод молча не работает, нет диагностики
- **Решение**: добавить `Debug.LogError("[TestUI] Prefab 'Radio' not found in AssetBundle or InstantiatePrefab returned null")` перед `return`

**✅ Нужен.** `Main.cs:98-99`: `if (radio == null) return;` — тихий failure. Причины: AssetBundle не загружен, имя `"Radio"` не найдено, bundle повреждён. Нет лога → невозможно диагностировать.

---

### Шаг 1.12 — Абсолютный путь `/GUI/Viewport` (`Main.cs:93`)
- `GameObject.Find("/GUI/Viewport")` — абсолютный путь к GUI иерархии игры
- Если структура GUI изменится в обновлении UBOAT → `viewport = null` → тихий `return` (строка 95)
- Шаг 1.6 проверяет имена children в prefab'е, но **не проверяет** внешний путь к GUI
- **Решение**: добавить `Debug.LogWarning("[TestUI] '/GUI/Viewport' not found — GUI hierarchy may have changed")` перед `return`

**✅ Нужен.** `Main.cs:93`: `GameObject.Find("/GUI/Viewport")` — внешняя зависимость. При изменении GUI hierarchy → тихий failure, мод не работает, нет ошибки.

---

### Шаг 1.13 — Тихие `return` без логов в `OnSceneStart` (`Main.cs:90-99`)
- В `SceneEventsListener_OnSceneStart` есть 3 точки раннего выхода без логов:
  - `if (scene.name != "Main Scene") return;` (строка 91)
  - `if (viewport == null) return;` (строка 95)
  - `if (radio == null) return;` (строка 99)
- При отладке невозможно понять, почему мод не появился
- Шаг 7.1 (checklist) требует проверять, но нет шага добавить логи
- **Решение**: добавить `Debug.Log`/`Debug.LogWarning` в каждую точку раннего выхода:
  - `Log($"[TestUI][Main] Scene '{scene.name}' is not Main Scene, skipping")` для scene name
  - `LogWarning` для viewport и radio null

**✅ Нужен.** `Main.cs:90-99`: три `return` без логов. При отладке невозможно определить причину, почему мод не появился.

---

### Шаг 1.14 — CursorHandler + SpinnerHandlerBase: null-check Image в Start (`CursorHandler.cs:21`, `SpinnerHandlerBase.cs:38-40`)
- `CursorHandler.cs:21`: `GetComponent<Image>().raycastTarget = true` — нет проверки на null Image. Если у Cursor нет Image компонента → NPE в Start
- `SpinnerHandlerBase.cs:38-40`: аналогичная проблема, но **уже имеет** null-check (`if (img != null)`)
- Добавить `Image img = GetComponent<Image>(); if (img != null) img.raycastTarget = true;` в `CursorHandler`
- **Приоритет**: критично — старт крашнется при отсутствии Image

**✅ Нужен.** `CursorHandler.cs:21`: `GetComponent<Image>().raycastTarget = true` — нет null-check. Если Image отсутствует → NPE в `Start()`. Мод не загрузится. `SpinnerHandlerBase.cs:38` — аналогичный код, но null-check уже есть.

---

### Шаг 1.15 — RadioHandler: null-check canvasGroup в IsVisible setter (`RadioHandler.cs:33`)
- `IsVisible` setter обращается к `canvasGroup.alpha`, `canvasGroup.interactable`, `canvasGroup.blocksRaycasts` без null-check
- `Start()` (строка 49-50) делает fallback `GetComponent<CanvasGroup>()`, но если и тот вернёт null → `canvasGroup` остаётся null
- Любое обращение к `IsVisible` → NPE
- **Решение**: добавить `if (canvasGroup == null) return;` в начало setter
- **Приоритет**: критично — toggle/Escape крашнется

**✅ Нужен.** `RadioHandler.cs:33`: `canvasGroup.alpha = value ? 1f : 0f;` — `canvasGroup` может быть null если `GetComponent<CanvasGroup>()` в Start вернул null. Setter крашнется при любом обращении.

---

### Шаг 1.16 — SpinnerHandlerBase: protected virtual Start вместо private Start (`SpinnerHandlerBase.cs:33`)
- `private void Start()` — Unity message. Если `VolumeSpinnerHandler` или `FeedbackSpinnerHandler` определит свой `Start()` — базовый **не вызовется**
- → `spinnerRect`, `currentAngle`, `raycastTarget` не инициализируются → NPE в `OnDrag()`
- **Решение**: переименовать `private void Start()` → `protected virtual void Awake()`, вызывать `base.Awake()` в наследниках (если определяют свой Awake)
- **Альтернатива**: вынести логику в `protected virtual void OnInit()` с вызовом из `Awake()` в базовом классе
- **Приоритет**: критично — наследование сломано, текущие наследники работают только потому что не определяют `Start()`

**✅ Нужен.** `SpinnerHandlerBase.cs:33`: `private void Start()` — если любой наследник определит `Start()` → базовый не вызовется → `spinnerRect` = null → NPE в `OnDrag()`. Текущие наследники не переопределяют `Start()` — баг **скрыт**, но проявится при добавлении нового наследника.

---

### Шаг 1.17 — SpinnerHandlerBase.OnBeginDrag: нет проверки spinnerRect (`SpinnerHandlerBase.cs:49-53`)
- `OnBeginDrag` выставляет `isDragging = true` без проверки `spinnerRect != null`
- Если `Start()` не выполнился (объект disabled при старте, потом enabled) → `spinnerRect` = null → `OnDrag` крашнется
- `CursorHandler.cs:24-29` — аналогичная проблема: `OnBeginDrag` без проверки `cursorRect`
- **Решение**: добавить `if (spinnerRect == null) return;` в `OnBeginDrag` для обоих классов
- **Приоритет**: критично — drag-операция крашнется при неинициализированном Rect

**✅ Нужен.** `SpinnerHandlerBase.cs:49`: `OnBeginDrag` выставляет `isDragging = true` без проверки `spinnerRect`. Если `Start()` не выполнился (disabled → enabled) → `OnDrag` → `spinnerRect.position` → NPE. `CursorHandler.cs:24` — аналогично.

---

### Шаг 1.18 — Race condition: GroundHandler vs ToggleHandler Start (`GroundHandler.cs:77-82`, `ToggleHandler.cs:62-64`)
- `GroundHandler.Start:77-82` выставляет `radioToggle.isOn = false` и `radioToggle.interactable = false`
- `ToggleHandler.Start:62-64` делает то же самое: `toggleComp.isOn = false; toggleComp.interactable = false`
- **Порядок Start не гарантирован Unity** — оба метода выполняются в порядке обнаружения компонентов
- Если `ToggleHandler.Start` выполнится **после** `GroundHandler.Start`:
  1. `GroundHandler.Start` выставляет `isOn=false, interactable=false`
  2. `ToggleHandler.Start:62` выставляет `isOn=false` (не критично)
  3. `ToggleHandler.Start:63` выставляет `interactable=false` (не критично, но redundancy)
  4. `ToggleHandler.Start:66` вызывает `OnToggleChanged(false)` → выполняет `img.enabled = false` (дублирование)
- **Хуже**: если `ToggleHandler.Start` выполнится **до** `GroundHandler.Start`:
  1. `ToggleHandler.Start:62-63` выставляет `isOn=false, interactable=false`
  2. `ToggleHandler.Start:64` подписывает `onValueChanged` listener
  3. `GroundHandler.Start:80` выставляет `radioToggle.isOn = false` — **но isOn уже false**, listener не вызывается
  4. `GroundHandler.Start:81` выставляет `radioToggle.interactable = false` — **перезаписывает** то же значение
  5. При первом клике Ground: `GroundHandler.OnPointerClick:91` проверяет `radioToggle.isOn` → false → `radioToggle.isOn = false` (бесконечный loop защиты)
- **Решение**: определить порядок через `[DefaultExecutionOrder]` или убрать дублирование: `GroundHandler` управляет `isOn`/`interactable` только если `ToggleHandler` не сделал это первым
- **Приоритет**: важно — race condition между компонентами

**✅ Нужен.** `GroundHandler.cs:77-82` и `ToggleHandler.cs:62-64` — оба управляют `toggle.isOn`/`toggle.interactable` в `Start()`. Unity не гарантирует порядок `Start()` между компонентами. Дублирование может привести к невоспроизводимым багам.

---

### Шаг 1.19 — InjectionFramework: null-check + try-catch (`Main.cs:77`)
- `InjectionFramework.Instance.InjectIntoAssembly(Assembly.GetExecutingAssembly())` — `Instance` это property getter
- **Проблема 1**: `Instance` может быть null если DI не инициализирован → `NullReferenceException`
- **Проблема 2**: `Instance` может бросить исключение (не только вернуть null) → `InvalidOperationException` (取决于 реализации)
- Шаг 1.7 проверяет результат инъекции (`resourceManager == null`), но **не обрабатывает** ни null `Instance`, ни исключения из него
- **Решение**: добавить guard + try-catch:
  ```csharp
  if (InjectionFramework.Instance == null)
  {
      Debug.LogError("[TestUI] InjectionFramework.Instance is null — DI not initialized. Ensure 'Injection' permission in Manifest.json");
      return;
  }
  try
  {
      InjectionFramework.Instance.InjectIntoAssembly(Assembly.GetExecutingAssembly());
  }
  catch (Exception e)
  {
      Debug.LogError("[TestUI] Injection failed: " + e);
  }
  ```
- **Консолидация**: объединяет Phase 1.19 (try-catch) и Phase 5.7 (null-check). Phase 5.7 — ❌ Не нужен (покрыто этим шагом).
- **Приоритет**: важно — мод не загрузится без DI

---

### Шаг 1.20 — Escape conflict с другими UI (`RadioHandler.cs:54-58`)
- `Input.GetKeyDown(KeyCode.Escape)` обрабатывается в `RadioHandler.Update()` напрямую
- Если игра также обрабатывает Escape (меню паузы, подсказки), оба обработчика сработают одновременно
- Текущее поведение: мод **всегда** перехватывает Escape, даже если другая UI-панель активна
- **Решение**: проверять, не перехвачен ли Escape другими UI-элементами (`EventSystem.current.currentSelectedGameObject != null`) перед скрытием
- Альтернатива: использовать `BlockInputUI` base class для контроля приоритетов
- **Приоритет**: критично — Escape в меню паузы скрывает панель радио, игрок выходит из меню, а выходит из мода

**✅ Нужен.** `RadioHandler.cs:54-58`: `Input.GetKeyDown(KeyCode.Escape)` — мод **всегда** перехватывает Escape. Если открыто меню паузы — оба обработчика сработают. Игрок нажимает Escape в меню паузы → мод скрывает панель радио, меню паузы тоже закрывается.

---

### Шаг 1.21 — Идемпотентность инъекции T3PLL38 (`RadioStoryIntegration.cs:70-138`)
- `InjectT3PLL38Decision` выполняется при **каждом** `ContinueStory` на `stage == 1`
- Не проверяет, существует ли уже решение с ключом `LocaleKey` в `Decisions`
- Если игра перезапускает `ContinueStory` (save/load, scene transition) — инжекция повторяется → **дубли** в меню
- **Решение**: перед `InsertDecision` итерировать `decisions`, проверять `_localizationKeyProp.GetValue(item)` на `LocaleKey`. Если найден → пропустить инжекцию, залогировать
- **Приоритет**: критично — дубли decisions ломают game flow

**✅ Нужен.** `RadioStoryIntegration.cs:70-138`: `InjectT3PLL38Decision` не проверяет наличие `LocaleKey` в `Decisions`. При повторном `ContinueStory` (save/load, scene transition) → дублирование T3PLL38 в меню выбора.

---

### Шаг 1.22 — Защита от дублирования панели Radio (`Main.cs:86-164`)
- Каждый `OnSceneStart` для `"Main Scene"` создаёт новый `GameObject` через `InstantiatePrefab`
- Нет проверки, существует ли уже активная панель с `RadioHandler`
- **Сценарий**: `OnSceneStart` вызывается повторно (перезагрузка сцены, bug в event system) → несколько панелей, несколько handlers, `RadioHandler.Instance` указывает на последний → старые панели — orphaned objects
- **Решение**: до `InstantiatePrefab` проверить `RadioHandler.Instance != null && RadioHandler.Instance.gameObject != null`. Если панель уже существует — reuse, не создавать новую. Добавить лог `"Reusing existing Radio panel"`
- **Альтернатива**: `Object.FindObjectOfType<RadioHandler>()` если singleton сброшен
- **Приоритет**: критично — несколько панелей = несколько обработчиков ввода, NPE на singleton

**✅ Нужен.** `Main.cs:86-164`: каждый `OnSceneStart` создаёт `Radio`. Без guard → дублирование панелей при повторном вызове. `RadioHandler.Instance` — один, а объектов может быть N.

---

### Шаг 1.23 — Атомарность Harmony-патчинга (`Main.cs:21-67`)
- Один `try/catch` охватывает все 5 патчей. Если `GetMethod` вернул `null` для одного target (обновление игры) → `harmony.Patch(null, ...)` → исключение → **все** оставшиеся патчи пропущены
- **Сценарий**: `RadioStory.ContinueStory` патчится, `Locale.RetrieveString` — нет (изменена сигнатура), остальные — пропущены из-за catch
- Мод частично работает: UI появляется, но story injection работает, locale — нет
- **Решение**:
  1. **Pre-flight**: собрать все `(targetMethod, patchMethod)` пары в список. Если **любой** targetMethod == null → `LogError` с именем метода и **не патчить ничего**
  2. Если все target валидны → применить все патчи в одном блоке
  3. Если патчинг частичный (один `harmony.Patch` упал) → `UnpatchSelf()` и логировать
- **Приоритет**: критично — частичный патчинг = непредсказуемое поведение мода

**✅ Нужен.** `Main.cs:21-67`: один `try/catch` на 5 патчей. Частичная ошибка = частично пропатченный мод. Нет отката и нет diagnostics какому target не удалось.

---

### Шаг 1.24 — HandleCustomDecisions fail-open policy (`RadioStoryIntegration.cs:195-218`)
- `HandleCustomDecisions` — Harmony prefix на `RadioStory.OnDecisionSelected`
- `return false` блокирует оригинальный метод (строка 215)
- Если reflection exception в prefix (например `_localizationKeyProp` == null, `decision` == null) → **оригинальный метод не вызывается**
- **Сценарий**: обновление игры изменило тип `decision` → `_localizationKeyProp.GetValue(decision)` → `InvalidCastException` → prefix падает → оригинальный `OnDecisionSelected` не вызывается → **game flow сломан**
- **Решение**: обернуть тело prefix в `try/catch` с `return true` (allow original):
  ```csharp
  public static bool HandleCustomDecisions(RadioStory __instance, object decision)
  {
      try { /* текущая логика */ }
      catch (Exception ex)
      {
          Debug.LogWarning("[TestUI] HandleCustomDecisions failed (fail-open): " + ex);
          return true; // allow original method
      }
  }
  ```
- **Приоритет**: критично — fail-closed prefix ломает game flow при обновлении игры

**✅ Нужен.** `RadioStoryIntegration.cs:195-218`: Harmony prefix без catch → reflection exception блокирует оригинальный `OnDecisionSelected`. При обновлении игры → game flow сломан, нет восстановления.

---

### Шаг 1.25 — Сброс static state при unload/reload (`RadioStoryIntegration.cs`, `RadioHandler.cs`)
- `RadioStoryIntegration`: `_initialized = true`, `_decisionCtor`, `_stageField`, `_insertMethod` — static поля, не сбрасываются при unload мода
- `RadioHandler`: `Instance` — static singleton, не обнуляется при unload
- При перезагрузке мода без перезапуска игры: `OnLoaded()` вызывается повторно, но `_initialized` уже `true` → `Init()` пропускается → если типы изменились (hot reload) — mod работает со старыми ссылками
- **Решение**: добавить `internal static void ResetRuntimeState()`:
  ```csharp
  internal static void ResetRuntimeState()
  {
      _initialized = false;
      _decisionType = null;
      _decisionCtor = null;
      _localizationKeyProp = null;
      _stageField = null;
      _decisionsProp = null;
      _insertMethod = null;
      _onUpdatedMethod = null;
  }
  ```
- Вызвать `ResetRuntimeState()` из lifecycle hook мода (если API позволяет) или из `OnLoaded()` перед Harmony patching
- Аналогично: `RadioHandler.Instance = null` в `OnDestroy` (шаг 1.1), но **дополнительно** — в lifecycle hook мода
- **Приоритет**: критично — stale static state после reload мода, мод никогда не переинициализируется корректно

**✅ Нужен.** `RadioStoryIntegration._initialized` и кэшированные reflection fields — static, не сбрасываются. При unload/reload мода → stale state, `_initialized` блокирует `Init()`, мод молча не работает.
