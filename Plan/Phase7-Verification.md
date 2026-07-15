# Фаза 7: Верификация

**Приоритет:** Критично | **Сложность:** Низкая | **Шагов:** 5 (из 5)

---

### Шаг 7.1 — Post-refactor validation checklist
- [ ] Запустить игру, переключить сцены — панель появляется и скрывается
- [ ] Проверить Escape — панель скрывается без NPE
- [ ] Проверить Toggle — on/off переключает фон и checkmark
- [ ] Проверить Ground клик — dimmed/max alpha, toggle interactable
- [ ] Проверить Zone клик (LMB/RMB) — переключение зон
- [ ] Проверить Expander drag — масштаб панели
- [ ] Проверить Panel drag — перемещение панели
- [ ] Проверить Cursor drag — вращение стрелки
- [ ] Проверить Volume/Feedback spinner — поворот
- [ ] Проверить логи — нет повторных entry, нет NPE, корректные уровни
- [ ] Проверить повторный load — нет накопления подписок
- [ ] **Проверить иерархию prefab'а `Radio`**: все имена детей (`CursorSpinner`/`Cursor`, `Toggle`, `Ground`, `Expander`, `RadioZone`, `VolumeSpinner`, `FeedbackSpinner`, `ciferbllat`, `Zone1`-`Zone5`, `on`, `off`, `Background`, `GroundCheckmark`) совпадают с `Find()` вызовами
- [ ] **Проверить загрузку AssetBundle**: `Bundles/testui` загружается, `resourceManager.InstantiatePrefab("Radio")` возвращает непустой `GameObject`
- [ ] **Проверить singleton при перезагрузке**: после scene reload `RadioHandler.Instance` указывает на актуальный объект
- [ ] **Проверить Escape при других UI**: если открыто меню паузы, Escape не скрывает панель радио одновременно

**✅ Нужен.** Чеклист — основа тестирования после рефакторинга. Без этого невозможно гарантировать что ничего не сломано.

---

### Шаг 7.2 — Проверка Manifest.json (`Manifest.json`)
- Текущий `permissions: ["Reflection"]` — но мод использует:
  - `HarmonyLib` (патчинг) — может требовать `"Harmony"` permission
  - `BlockInputUI` (UI) — может требовать `"UI"` permission
  - `InjectionFramework` (DI) — может требовать `"DI"` или `"Injection"` permission
- **Действие**: проверить документацию UBOAT mod loader на required permissions
- Обновить `supportedGameVersions: ["2026.1"]` при необходимости
- Проверить что `assemblyName: "testui"` совпадает с именем скомпилированной DLL

**✅ Нужен.** `Manifest.json:8`: `permissions: ["Reflection"]` — но мод использует HarmonyLib, BlockInputUI, InjectionFramework. Если API требует явных permissions — мод может не загрузиться.

---

### Шаг 7.3 — Проверка загрузки AssetBundle (`Main.cs:97`)
- `resourceManager.InstantiatePrefab("Radio")` может вернуть `null` если:
  - `Bundles/testui` не загрузился (файл отсутствует, неверный формат, повреждён)
  - Имя `"Radio"` не найдено в bundle'е
  - `ResourceManager` не зарегистрировал bundle
- План 1.11 добавляет лог при `radio == null`, но **не проверяет** почему bundle не загрузился
- **Действие**: добавить diagnostic log в `OnSceneStart` до `InstantiatePrefab`:
  ```csharp
  Debug.Log("[TestUI][Main] resourceManager=" + (resourceManager != null ? "ok" : "null"));
  ```
- Если `resourceManager` != null но `InstantiatePrefab` вернул null — лог с инструкцией проверить `Bundles/testui` файл
- **Приоритет**: критично — без AssetBundle мод полностью неработоспособен

**✅ Нужен.** `Main.cs:97`: `InstantiatePrefab("Radio")` — если AssetBundle не загружен → тихий null. План 1.11 добавляет лог, но не диагностирует причину (отсутствие файла vs неверное имя vs повреждение). Дополнительный лог перед вызовом помогает при troubleshooting.

---

### Шаг 7.5 — Compatibility matrix: документирование среды тестирования (`Manifest.json`, README/release notes)
- **Проблема**: нет формального способа зафиксировать и передать информацию о среде, в которой мод был протестирован
- При баг-репорте невозможно определить: какая версия UBOAT, какая платформа, какой hash DLL/bundle, был ли smoke test
- **Решение**: создать `COMPATIBILITY.md` или секцию в `README.md`:
  ```
  ## Tested Environment
  | Параметр | Значение |
  |----------|----------|
  | UBOAT Version | 2026.1.xxxx |
  | Platform | Windows x64 |
  | testui.dll hash | sha256:... |
  | Bundle testui hash | sha256:... |
  | Smoke test | Pass/Fail |
  | Date | YYYY-MM-DD |
  ```
- **Дополнительно**: шаг 8.3 (smoke test) выводит версию мода и игры в лог. Compatibility matrix фиксирует результат **тестирования**, а не только запуска
- В `Manifest.json`: добавить `"testEnvironment"` поле (если API поддерживает) или поддерживать вручную
- **Приоритет**: важно — воспроизводимость, поддержка, сравнение между сборками

**✅ Нужен.** Нет compatibility matrix. При баг-репорте невозможно определить среду. Документирование версий, хэшей и результата smoke test необходимо для поддержки.

---

### Шаг 7.4 — AssetBundle lifecycle: загрузка и дамп ассетов (`Main.cs`)
- `Bundles/testui` — 46 MB AssetBundle. Способ загрузки зависит от API: `ResourceManager` может загружать автоматически, или мод должен загружать вручную через `AssetBundle.LoadFromFile`
- Если ResourceManager сам подхватывает bundle (по соглашению о расположении модов) — добавить диагностику: логировать `resourceManager` перед `InstantiatePrefab`
- Если мод загружает bundle вручную — обернуть `AssetBundle.LoadFromFile` в try-catch с логом:
  - `FileNotFoundException` → файл `Bundles/testui` отсутствует
  - `InvalidDataException` → неверный формат или повреждённый файл
  - `UnityException` → платформа несовместима (Windows/Linux/macOS)
- В `#if DEBUG`: добавить дамп ассетов в bundle (`bundle.GetAllAssetNames()`) для верификации что `"Radio"` присутствует
- **Дополнительно**: логировать размер файла bundle (`new FileInfo("...").Length`) для сравнения между сборками
- **Приоритет**: критично — без AssetBundle мод полностью неработоспособен, текущий код не диагностирует **почему** bundle не загрузился
