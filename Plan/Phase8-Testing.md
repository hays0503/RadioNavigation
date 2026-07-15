# Фаза 8: Тестирование и диагностика

**Приоритет:** Важно | **Сложность:** Средняя | **Шагов:** 5 (из 5)

---

### Шаг 8.1 — Data-driven конфиг имён children (`Main.cs`, компоненты)
- Текущие имена children хардкодены в `Find()` вызовах: `"CursorSpinner"`, `"Toggle"`, `"Ground"`, `"Background"`, `"GroundCheckmark"`, `"Expander"`, `"RadioZone"`, `"VolumeSpinner"`, `"FeedbackSpinner"`, `"on"`, `"off"`, `"ciferbllat"`, `"Zone1"`..."`Zone5"`
- Если в обновлении UBOAT prefab переименован — мод молча ломается
- **Решение**: создать `static class PrefabNames` с константами имён:
  ```csharp
  public static class PrefabNames
  {
      public const string Cursor = "CursorSpinner"; // или "Cursor"
      public const string Toggle = "Toggle";
      // ...
  }
  ```
- Все `Find()` вызовы ссылаются на константы → при переименовании меняется одно место
- **Приоритет**: важно — maintenance при обновлениях UBOAT

**✅ Нужен.** Хардкод имён children в 8+ местах. При переименовании в prefab'е — молча ломается без ошибки. Единый источник правды упрощает поддержку.

---

### Шаг 8.2 — Diagnostic hierarchy dump в `#if DEBUG` (`Main.cs`)
- Шаг 1.6 предлагает вывести иерархию children в лог — но не описывает формат
- **Решение**: рекурсивный dump в `OnSceneStart` под `#if DEBUG`:
  ```csharp
  #if DEBUG
  void DumpHierarchy(Transform t, string indent = "")
  {
      Debug.Log($"{indent}{t.name} [{string.Join(",", t.GetComponents<Component>().Select(c => c.GetType().Name))}]");
      foreach (Transform child in t)
          DumpHierarchy(child, indent + "  ");
  }
  #endif
  ```
- Вызывать **один раз** при старте мода: `DumpHierarchy(radio.transform, "[TestUI] Prefab hierarchy: ")`
- Проверить что все имена совпадают с ожидаемыми (8.1)
- **Приоритет**: важно — верификация иерархии без AssetBundle

**✅ Нужен.** Без рекурсивного dump невозможно верифицировать что имена children совпадают с `Find()` вызовами. Данные для сравнения с `PrefabNames` (8.1).

---

### Шаг 8.3 — Smoke test: автоматическая проверка загрузки мода (`Main.cs`)
- Текущий код: `OnLoaded()` → Harmony patches → event subscriptions
- Если что-то упало — молча не работает
- **Решение**: добавить `Debug.Log("[TestUI] Mod loaded successfully. Patches applied: X")` в конце `OnLoaded()` с подсчётом количества успешных патчей
- В `SceneEventsListener_OnSceneStart`: `Debug.Log("[TestUI] Scene load complete. Radio active: " + radio.activeSelf)`
- **Версионирование**: в начале `OnLoaded()` добавить лог версии мода и игры:
  ```csharp
  Debug.Log($"[TestUI] TestUI v{Assembly.GetExecutingAssembly().GetName().Version}, UBOAT {Application.version}");
  ```
  Критично для диагностики: при баг-репорте сразу видно совместимость версий
- **Приоритет**: важно — подтверждение что мод загрузился и работает

**✅ Нужен.** Нет способа автоматически проверить что мод загрузился успешно. Два лог-сообщения (load + scene) подтверждают полный путь выполнения.

---

### Шаг 8.4 — Reproducible build pipeline (репозиторий, `.csproj` или build script)
- **Текущее состояние**: нет `.csproj`, нет dependency manifest, нет build command, нет output path. Сборка описана как "Compile externally with game assemblies + Harmony references"
- **Проблема**: невозможно гарантировать воспроизводимую сборку. Разные машины, разные версии Harmony, разные game assemblies → разный output
- **Решение**: создать один из вариантов:
  1. **`.csproj` файл** с explicit references:
     ```xml
     <Reference Include="Assembly-CSharp">
       <HintPath>$(GamePath)\UBOAT_Data\Managed\Assembly-CSharp.dll</HintPath>
     </Reference>
     <Reference Include="0Harmony">
       <HintPath>$(GamePath)\UBOAT_Data\Managed\0Harmony.dll</HintPath>
     </Reference>
     ```
     Output: `bin\Release\testui.dll`
  2. **Build script** (`build.ps1` или `build.sh`):
     ```powershell
     csc /target:library /out:testui.dll /reference:... Source\*.cs
     ```
  3. **AGENTS.md**: зафиксировать exact build command с environment variables
- **Дополнительно**: pinned Harmony version, pinned game assemblies path, output verification (`testui.dll` exists, size check)
- **Приоритет**: важно — без воспроизводимой сборки невозможно guarantee что все développeurs получают одинаковый output

**✅ Нужен.** Нет build system в репозитории. Сборка вне репозитория → разные результаты на разных машинах. Воспроизводимость необходима для production-ready мода.

---

### Шаг 8.5 — Automated smoke test: structured diagnostics output (`Main.cs`)
- **Текущее состояние**: шаг 8.3 предлагает `Debug.Log("[TestUI] Mod loaded successfully")` — простой текст, не machine-readable
- **Проблема**: невозможно автоматически проверить результат. Требуется ручной поиск в Player.log
- **Решение**: добавить structured diagnostics, который можно парсить:
  ```csharp
  // В конце OnLoaded():
  int patchesApplied = 0;
  if (continueStory != null) patchesApplied++;
  if (retrieveString1 != null) patchesApplied++;
  if (retrieveString2 != null) patchesApplied++;
  if (onDecision != null) patchesApplied++;
  if (onStopped != null) patchesApplied++;
  
  Debug.Log($"[TestUI][DIAG] version={Assembly.GetExecutingAssembly().GetName().Version}" +
      $" game={Application.version}" +
      $" patches={patchesApplied}/5" +
      $" di={(InjectionFramework.Instance != null ? "ok" : "null")}" +
      $" resourceManager={(resourceManager != null ? "ok" : "null")}");
  
  // В конце SceneEventsListener_OnSceneStart:
  Debug.Log($"[TestUI][DIAG] scene={scene.name}" +
      $" radio={(radio != null ? "created" : "null")}" +
      $" handlers={radio.GetComponents<MonoBehaviour>().Length}" +
      $" visible={RadioHandler.Instance?.IsVisible}");
  ```
- **Парсинг**: `grep "[TestUI][DIAG]" Player.log` → все diagnostics в одном месте. Можно автоматически проверять `patches=5/5`, `radio=created`, `di=ok`
- **Дополнительно**: вывести compatibility info при старте (шаг 8.3), вывести summary при каждом scene load
- **Приоритет**: важно — автоматизация проверки загрузки мода без ручного просмотра логов

**✅ Нужен.** Текущий smoke test — текстовый лог. Structured diagnostics с `[DIAG]` тегом позволяет автоматически проверять состояние мода и Pars'e Player.log.
