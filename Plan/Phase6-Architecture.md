# Фаза 6: Архитектурные улучшения

**Приоритет:** Красота | **Сложность:** Высокая | **Шагов:** 8 (из 9)

---

### Шаг 6.1 — Main.cs: разбить God Method
- Вынести setup компонентов в отдельный `RadioInstaller.SetupComponents(GameObject radio)`
- `SceneEventsListener_OnSceneStart` уменьшится с 84 до ~15 строк

**✅ Нужен.** `Main.cs:86-169`: 78 строк, setup компонентов,DI, event wiring — все в одном методе. Разбиение улучшает readability.

---

### Шаг 6.2 — Localization через Dictionary (`RadioStoryIntegration.cs`)
- Заменить hardcoded строки на `Dictionary<string, (string ru, string en)>`
- Легко добавлять новые ключи, масштабируется

**✅ Нужен.** `RadioStoryIntegration.cs:155-162`: hardcoded `"russian"`, `"T3PLL38..."`, `"T3PLL38 on-board..."`. Dictionary масштабируется — добавление нового языка/ключа без изменения логики.

---

### Шаг 6.3 — RadioHandler: event-based visibility
- Вместо прямого обращения `RadioHandler.Instance.IsVisible = false` из 3 мест
- Использовать event: `RadioHandler.OnVisibilityChanged`
- Убрать singleton dependency из `RadioStoryIntegration`, `CursorHandler`

**✅ Нужен.** `RadioStoryIntegration.cs:207`: `RadioHandler.Instance.IsVisible = false` — direct singleton access. `RadioStoryIntegration.cs:224,229`: то же. Event-based decoupling.

---

### Шаг 6.4 — CursorAngle property: оценка необходимости (`RadioHandler.cs:13-23`)
- Setter проверяет изменение, но не вызывает side effects (только присвоение)
- Gеттер не используется — запись только из `CursorHandler.cs:43`
- Если ни один consumer не читает значение → property избыточен
- Решение: либо удалить property (оставить field), либо добавить callback при изменении

**❌ Не нужен.** Write-only property — не мешает. Setter с change detection — нет overhead. Удаление без benefit.

---

### Шаг 6.6 — Стандартизация лог-уровней
- Определить правила:
  - `Log` — информационные (Start,Awake, патч применён)
  - `LogWarning` — не критичные ошибки (fallback на default)
  - `LogError` — критичные ошибки (тип не найден, injection failed)
- Пройтись по всем файлам и выровнять уровни

**✅ Нужен.** Текущее состояние: `Debug.Log` для ошибок, `Debug.LogWarning` для info. Путаница с уровнями затрудняет фильтрацию.

---

### Шаг 6.7 — InjectT3PLL38Decision: God Method (`RadioStoryIntegration.cs:70-138`)
- 68 строк, 4 уровня вложенности, смешивает 3 ответственности:
  1. Диагностика (Debug.Log по всем decisions)
  2. Валидация (проверка stage, _decisionCtor, _stageField)
  3. Инжекция (создание + вставка decision)
- **План Шаг 6.1** дробит `Main.cs` (78 строк), но игнорирует этот метод
- Решение: вынести в отдельные методы:
  - `LogDecisions(object decisions)` — цикл логирования (#if DEBUG)
  - `CreateT3PLL38Decision()` — создание object[]
  - `InjectDecision(RadioStory story, object decision)` — вставка через InsertDecision
  - `InjectT3PLL38Decision` — orchestrator (вызов валидации → создания → инжекции)

**✅ Нужен.** `RadioStoryIntegration.cs:70-138`: 68 строк, 3 ответственности, 4 уровня вложенности. Разбиение улучшает testability и readability.

---

### Шаг 6.10 — RadioHandler: BlockInputUI — неполное использование (`RadioHandler.cs:7`)
- `RadioHandler` наследует `BlockInputUI` (базовый класс для UI-блокировки ввода)
- Но **ни один метод** `BlockInputUI` не переопределён — наследование чисто номинальное
- Если `BlockInputUI` предоставляет виртуальные методы (OnShow/OnHide, Focus/Unfocus) — их стоит использовать для управления видимостью
- **Решение**: проверить API `BlockInputUI`, переопределить релевантные методы

**✅ Нужен.** `RadioHandler.cs:6`: `BlockInputUI` — базовый класс, но ни один метод не переопределён. Если `BlockInputUI` предоставляет `OnShow/OnHide` — использование улучшит контроль видимости.

---

### Шаг 6.11 — UI State Machine: владение состоянием питания (`GroundHandler.cs`, `ToggleHandler.cs`)
- **Текущее состояние**: `GroundHandler` управляет `radioToggle.isOn` и `radioToggle.interactable` (строки 80-81, 93-94, 112). `ToggleHandler` тоже управляет `toggleComp.isOn` и `toggleComp.interactable` в `Start()` (строки 62-63). Оба компонента пишут в один Toggle без coordination
- **Проблема**: нет единого владельца состояния. `GroundHandler.OnPointerClick` меняет `isOn` → `OnToggleChanged` выполняется → `GroundHandler` продолжает менять `interactable`. Промежуточные состояния visible для event subscribers
- **Шаг 1.18** решает race condition через `DefaultExecutionOrder`, но это **скрывает** проблему, а не решает её
- **Решение**: определить single source of truth:
  - `GroundHandler` — **единственный** владелец состояния `isOn`/`interactable`. Управляет через метод `SetPowered(bool powered)`:
    ```csharp
    public void SetPowered(bool powered)
    {
        if (radioToggle == null) return;
        radioToggle.SetIsOnWithoutNotify(powered); // Unity 2021+
        radioToggle.interactable = powered;
    }
    ```
  - `ToggleHandler` — **только отображает** состояние. Не управляет `isOn`/`interactable` в `Start()`. Только реагирует на `onValueChanged`
  - `GroundHandler.Start()` устанавливает начальное состояние: `SetPowered(false)`
  - Убрать дублирование: `ToggleHandler.Start()` не выставляет `isOn=false; interactable=false` — это делает `GroundHandler`
- **Альтернатива** (без `SetIsOnWithoutNotify`): отписываться от `onValueChanged` на время программного изменения, затем подписывать обратно
- **Приоритет**: важно — чёткое разделение ответственности, устранение race condition на архитектурном уровне

**✅ Нужен.** `GroundHandler` и `ToggleHandler` оба управляют одним Toggle. Нет single source of truth → промежуточные состояния, race conditions. Архитектурное решение надёжнее `DefaultExecutionOrder`.

---

### Шаг 6.12 — AssetBundle ownership model (`Main.cs`, `Manifest.json`)
- **Текущее состояние**: `resourceManager.InstantiatePrefab("Radio")` предполагает что `ResourceManager` уже загрузил `Bundles/testui`. Но **нет документации** кто, когда и как загружает bundle
- **Два варианта**:
  1. **ResourceManager автоматически** — по соглашению UBOAT, все `Bundles/` в папке мода загружаются автоматически при старте. Тогда `InstantiatePrefab` —唯一 точка доступа, manual loading не нужен
  2. **Ручная загрузка** — мод должен сам вызывать `AssetBundle.LoadFromFile` перед `InstantiatePrefab`. Тогда нужен try-catch, lifecycle unload, cache
- **Решение**:
  - **Подтвердить** контракт UBOAT mod loader: как `ResourceManager` относится к `Bundles/` папке
  - Если вариант 1 (auto): добавить diagnostic log `"[TestUI] ResourceManager bundles: " + resourceManager.AvailableBundles` перед `InstantiatePrefab`
  - Если вариант 2 (manual): вынести загрузку в `Main.OnLoaded()` с try-catch и lifecycle management
  - **В любом случае**: добавить в `Manifest.json` поле `bundlePath: "Bundles/testui"` (если API поддерживает) для явного указания
- **Приоритет**: важно — без определённого ownership model невозможно гарантировать корректную загрузку и выгрузку 46 MB bundle

**✅ Нужен.** Неизвестно кто владеет AssetBundle lifecycle. Если mod загружает вручную — нет unload. Если ResourceManager автоматически — нет diagnostics. Ownership model необходим для production-ready мода.
