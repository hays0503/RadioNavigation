# Фаза 5: Стиль и консистентность

**Приоритет:** Красота | **Сложность:** Низкая | **Шагов:** 12 (из 13)

---

### Шаг 5.1 — String interpolation (повсеместно)
- Заменить `"[TestUI] " + x + " " + y` на `$"[TestUI] {x} {y}"`

**✅ Нужен.** String concat vs interpolation — компилятор оптимизирует interpolation, меньше аллокаций. Стандарт C# convention.

---

### Шаг 5.2 — Consistent field naming
- Привести к единому стилю без `_` prefix для private полей (C# convention)

**✅ Нужен.** `_cursorAngle`, `_isVisible` — `_` prefix для private полей. C# convention: `_camelCase` для backing fields, но консистентность важнее. В проекте mixed стиль — нужно унифицировать.

---

### Шаг 5.3 — Magic numbers → constants
- `GroundHandler.cs` — `0.5f` alpha threshold → `private const float ActiveAlphaThreshold = 0.5f`
- `PanelDragHandler.cs` — `new Vector2(16, 16)` → именованная константа `CursorHotspot`

**✅ Нужен.** `GroundHandler.cs:89`: `bgImage.color.a > 0.5f` — magic number, непонятно что代表ляет. Именованная константа объясняет intent. **Дополнительно**: `ZoneHandler.cs:113` — `groundBg.color.a <= 0.5f` — тот же magic number. Вынести `0.5f` в общую константу `ActiveAlphaThreshold` в `GroundHandler` (или shared static), использовать в обоих классах. При изменении порога нужно менять в двух местах.

---

### Шаг 5.4 — Sealed classes
- `VolumeSpinnerHandler`, `FeedbackSpinnerHandler` — добавить `sealed`

**❌ Не нужен.** `sealed` на leaf classes — micro-optimization (virtual call devirtualization). Без 의미ового эффекта в данном масштабе. Нет наследников и не планируется.

---

### Шаг 5.5 — SpinnerHandlerBase рефакторинг (`SpinnerHandlerBase.cs`)
- `Start()` содержит `Debug.Log` с string concatenation → перейти на interpolation
- `[SerializeField] protected` поля → `[SerializeField] private` + публичные свойства (если доступ нужен наследникам через property)
- Добавить null-check на `GetComponent<Image>()` в `Start()` (строка 38-40)

**❌ Не нужен.** Null-check на `GetComponent<Image>()` **уже есть** (`SpinnerHandlerBase.cs:38-40`: `if (img != null)`). Interpolation — косметика (покрыто 5.1). Property refactoring — стиль, не баг.

---

### Шаг 5.7 — Null-check `InjectionFramework.Instance` (`Main.cs:77`)
- `InjectionFramework.Instance.InjectIntoAssembly(...)` без null-check
- Если DI недоступен — NPE в `SceneEventsListener_OnSceneAwake`
- Добавить guard

**❌ Не нужен.** Покрыто шагом 1.19 (InjectionFramework: null-check + try-catch). Phase 1.19 решает обе проблемы: и null `Instance`, и исключения из `InjectIntoAssembly`.

---

### Шаг 5.8 — Magic color value (`ToggleHandler.cs:101`)
- `new Color32(0xFF, 0xEC, 0xB6, 0xFF)` — магический цвет подсветки цiferблата
- Вынести: `private static readonly Color DialActiveColor = new Color32(0xFF, 0xEC, 0xB6, 0xFF);`
- Добавить комментарий: какой оттенок (тёплый жёлтый) и зачем (подсветка при включении)

**✅ Нужен.** `ToggleHandler.cs:101`: `0xFFECB6FF` — magic color. Именованная константа `DialActiveColor` + комментарий объясняет intent.

---

### Шаг 5.9 — CursorHandler pivot магическое значение (`CursorHandler.cs:18`)
- `cursorRect.pivot = new Vector2(0.5f, 0.5f)` — центр, но задан магически
- Вынести в именованную константу или `static readonly` поле
- Аналогично `SpinnerHandlerBase.cs:36` — тот же паттерн

**❌ Не нужен.** `Vector2(0.5f, 0.5f)` — pivot center, **универсально понятен**. Over-extraction усложняет код без benefit.

---

### Шаг 5.10 — Lifecycle cleanup: GroundHandler, ExpanderHandler, ZoneHandler
- Нет `OnDestroy()` — при уничтожении объекта не выполняется cleanup
- **Уточнение**: `ToggleHandler.cs:113-115` **уже имеет** `OnDestroy()` с отпиской от `onValueChanged` — этот компонент в порядке
- GroundHandler и ExpanderHandler не подписываются на события → `OnDestroy` не обязателен, но **ZoneHandler** добавляет `EventTrigger.Entry` (Шаг 2.6) — при уничтожении GameObject старые lambda closures не собираются GC сразу
- Добавить `OnDestroy()` в ZoneHandler: очищать `trigger.triggers` перед уничтожением
- Для консистентности: добавить пустой `OnDestroy()` в GroundHandler/ExpanderHandler с комментарием "no subscriptions to clean up"

**✅ Нужен (только ZoneHandler).** ZoneHandler добавляет `EventTrigger.Entry` с lambda closures — при destroy GC не может собрать сразу. GroundHandler/ExpanderHandler — **не нужны** (нет subscriptions).

---

### Шаг 5.12 — CursorHandler: `[SerializeField]` на приватных полях без Inspector (`CursorHandler.cs:15-16`)
- `minAngle = -90f` и `maxAngle = 90f` — `[SerializeField] private float`, но никогда не меняются через Inspector
- Если значения фиксированы → `const` (или `static readonly` для float)
- Аналогично `ExpanderHandler.cs:8-10`: `sensitivity = 0.005f`, `minScale = 0.5f`, `maxScale = 3f` — те же `[SerializeField] private` без Inspector-изменений
- **Решение**: определиться — если Inspector-доступ нужен, оставить; если нет — в константы

**✅ Нужен.** `[SerializeField]` на фиксированных значениях — ложное впечатление что Inspector-доступен. `CursorHandler.cs:10-11`: `-90f/90f` фиксированы, `PanelDragHandler.cs:8-10`: `0.005f/0.5f/3f` фиксированы.

---

### Шаг 5.13 — RadioHandler: `[SerializeField]` на `_cursorAngle` (`RadioHandler.cs:17`)
- `[SerializeField] private float _cursorAngle` — поле сериализуется Unity
- Заполняется **только** из кода: `CursorHandler.cs:45`: `RadioHandler.Instance.CursorAngle = angle`
- Сериализация бесполезна + создаёт ложное впечатление что поле задаётся в Inspector
- **Решение**: убрать `[SerializeField]`, оставить просто `private float _cursorAngle`

**✅ Нужен.** `_cursorAngle` заполняется **только** из `CursorHandler.cs:45` (`RadioHandler.Instance.CursorAngle = angle`). Сериализация бесполезна, `[SerializeField]`误导.

---

### Шаг 5.14 — Стандартизация лог-префиксов (все файлы)
- Текущие варианты: `[TestUI]`, `[TestUI][Main]`, `[TestUI][Ground]`, `[TestUI][Toggle]`, `[TestUI][ZoneHandler]`, `[TestUI][VolumeSpinner]`, `[TestUI][FeedbackSpinner]`
- План 6.6 обсуждает **уровни** (`Log`/`Warning`/`Error`), но не **формат префикса**
- **Решение**: определить формат: `[TestUI][ComponentName]` — например `[TestUI][Main]`, `[TestUI][Ground]`, `[TestUI][Zone]`
- Пройтись по всем файлам и унифицировать

**✅ Нужен.** `[TestUI]` vs `[TestUI][Main]` vs `[TestUI][ZoneHandler]` — inconsistent. Фильтрация логов по префиксу затруднена.
