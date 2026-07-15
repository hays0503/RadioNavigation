# Фаза 4: Производительность

**Приоритет:** Важно | **Сложность:** Низкая | **Шагов:** 6 (из 7)

---

### Шаг 4.1 — Debug.Log в hot paths (все файлы)
- Оставить только в `Start()` / `Awake()`, остальное обернуть в `#if DEBUG`
- Затронутые места:
  - `GroundHandler.OnPointerClick`
  - `ToggleHandler.OnToggleChanged`
  - `ToggleHandler.OnPointerClick` — генерирует 4 world corners + `DebugImg()` каждый клик
  - `ZoneHandler.NextZone/PreviousZone`
  - `VolumeSpinnerHandler/FeedbackSpinnerHandler.OnAngleUpdated`

**✅ Нужен.** `ToggleHandler.cs:71-82`: `OnPointerClick` генерирует 4 world corners + `DebugImg()` **каждый клик**. `VolumeSpinnerHandler.cs:11`: `Debug.Log` **каждый кадр** drag. GC pressure.

---

### Шаг 4.2 — Texture2D оптимизация (`PanelDragHandler.cs`)
- Заменить 1024 `SetPixel` на `Color32[]` массив + `SetPixels32`
- Кэшировать текстуру в static поле (одна и та же для всех панелей)

**✅ Нужен.** `PanelDragHandler.cs:29-62`: 1024 вызова `SetPixel()` — каждый создаёт managed object. `SetPixels32` с `Color32[]` — 1 allocation. Кэширование в static — текстура одна для всех панелей.

---

### Шаг 4.3 — EventTrigger GC (`ZoneHandler.cs`)
- Заменить `EventTrigger` на `IPointerClickHandler` (ZoneHandler сам implementует)
- Убрать динамическое добавление Entry через lambda

**❌ Не нужен.** ZoneHandler на **родительском** "RadioZone", клики происходят на **детях** "Zone1"..."Zone5". `IPointerClickHandler` на ZoneHandler **не получит** клики детей без EventTrigger. Замена не уменьшает сложность — EventTrigger здесь обоснован.

---

### Шаг 4.4 — `Input.mousePosition` вместо `eventData.position` (`CursorHandler.cs:36`, `SpinnerHandlerBase.cs:61`) ⚠️ КРИТИЧНО
- `CursorHandler.OnDrag`: `Vector2 dir = Input.mousePosition - cursorRect.position;`
- `SpinnerHandlerBase.OnDrag`: `Vector2 dir = Input.mousePosition - spinnerRect.position;`
- Оба используют `Input.mousePosition` вместо `eventData.position`
- **Баг корректности** (не только производительность):
  - `Input.mousePosition` не учитывает EventSystem input module (TouchInputModule vs StandaloneInputModule)
  - Неконсистентно с `PanelDragHandler.cs:81`, который использует `eventData.delta`
  - Проблемы с multi-touch: `Input.mousePosition` возвращает последнее known position, `eventData.position` — позицию конкретного pointer
  - На mobile/touch: `Input.mousePosition` может возвращать неверную позицию если есть несколько касаний
- **Решение**: заменить `Input.mousePosition` на `eventData.position` в обоих классах

**✅ Нужен (КРИТИЧНО).** `CursorHandler.cs:36` и `SpinnerHandlerBase.cs:61`: `Input.mousePosition` — глобальный ввод, не привязан к конкретному pointer. На touch-устройствах это **баг корректности**: drag-операция использует неверную позицию. `eventData.position` — корректный путь для drag-обработки в Unity UI.

---

### Шаг 4.5 — `StateLog()` создаёт строку при каждом вызове (`GroundHandler.cs:15-48`)
- `StateLog()` вызывается в `Start()` (строка 84) и `OnPointerClick` (строка 114)
- Каждый вызов генерирует ~10 string concatenations +多次 `.ToString()`
- Даже если обернуть `Debug.Log` в `#if DEBUG` — метод `StateLog()` всё равно вызывается (он не в `Debug.Log` напрямую)
- `OnPointerClick` — hot path (каждый клик)
- **Решение**: обернуть вызов `StateLog()` в `#if DEBUG`:
  ```csharp
  #if DEBUG
      Debug.Log("[TestUI][Ground] Start " + StateLog());
  #endif
  ```
  Или вынести `StateLog()` в `Conditional("DEBUG")` метод

**✅ Нужен.** `GroundHandler.cs:84,114`: `StateLog()` создаёт сложную строку при каждом вызове. Даже при обёртывании `Debug.Log` в `#if DEBUG` — `StateLog()` всё равно вызывается. GC pressure в hot path.

---

### Шаг 4.6 — `Vector3[]` аллокация на каждый клик (`ToggleHandler.cs:73`)
- `OnPointerClick` создаёт `new Vector3[4]` **на каждый клик**:
  ```csharp
  Vector3[] corners = new Vector3[4];
  toggleRect.GetWorldCorners(corners);
  ```
- Шаг 4.1 оборачивает `Debug.Log` в `#if DEBUG`, но **не упоминает** аллокацию `corners`
- Даже без лога — аллокация остаётся (4 * 12 = 48 байт managed memory на клик)
- **Решение**: вынести `corners` в приватное поле класса, переиспользовать:
  ```csharp
  private readonly Vector3[] _corners = new Vector3[4];
  ```
  Или обернуть весь блок в `#if DEBUG` (corners используются только для лога)

**✅ Нужен.** `ToggleHandler.cs:73`: `new Vector3[4]` на каждый клик — avoidable allocation. `corners` используются **только** для `Debug.Log` → обернуть весь блок в `#if DEBUG`.

---

### Шаг 4.7 — SpinnerHandler: Debug.Log каждый кадр drag (`VolumeSpinnerHandler.cs:11`, `FeedbackSpinnerHandler.cs:11`)
- `OnAngleUpdated(float angle)` вызывается из `SpinnerHandlerBase.OnDrag()` — **каждый кадр** пока пользователь перетаскивает
- `VolumeSpinnerHandler.cs:11`: `Debug.Log("[TestUI][VolumeSpinner] angle=" + angle)` — строковая конкатенация + лог **на каждый кадр**
- `FeedbackSpinnerHandler.cs:11`: аналогично
- При 60 FPS и 2 секундах drag = 120 логов = 120 string allocations + 120 записей в лог
- План 4.1 упоминает Spinners в общем списке, но **не конкретизирует** что `OnAngleUpdated` это hot path (каждый кадр, не каждый клик как Ground/Toggle)
- **Решение**: обернуть `Debug.Log` в `#if DEBUG`:
  ```csharp
  protected override void OnAngleUpdated(float angle)
  {
  #if DEBUG
      Debug.Log("[TestUI][" + DebugTag + "] angle=" + angle);
  #endif
  }
  ```
- **Приоритет**: важно — GC pressure + лог-шум при каждом drag-действии

**✅ Нужен.** `VolumeSpinnerHandler.cs:11`, `FeedbackSpinnerHandler.cs:11`: `Debug.Log` **каждый кадр** drag. 60 FPS × N секунд = N×60 строковых аллокаций. Наибольший GC contributor среди всех Debug.Log в проекте.
