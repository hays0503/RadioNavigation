# F31: 93 Debug.Log — 81 активны в Release builds

**Приоритет:** P2 (Medium)
**Область:** Project-wide
**Тип:** Performance · Hygiene

---

## Проблема

93 вызова `Debug.Log([LogWarning/LogError])` в проекте. Только 12 (13%) обёрнуты в `#if DEBUG`. Остальные 81 (87%) выполняются в Release-сборке, аллоцируя строки и вызывая managed-to-native bridge.

---

## Evidence

| Файл | Debug.Log | `#if DEBUG` | Release-активны |
|------|:---------:|:-----------:|:---------------:|
| `RadioStoryIntegration.cs` | 30+ | 1 | ~30 |
| `Main.cs` | 18 | 2 | 16 |
| `InformationPanelHandler.cs` | 16 | 1 | 15 |
| `ZoneHandler.cs` | 9 | 2 | 7 |
| `ToggleHandler.cs` | 6 | 2 | 4 |
| `GroundHandler.cs` | 4 | 2 | 2 |
| `AudioSynthesisEngine.cs` | 1 | 0 | 1 (F13) |
| `CursorHandler.cs` | 3 | 0 | 3 |
| `RadioHandler.cs` | 2 | 0 | 2 |
| `SpinnerHandlerBase.cs` | 3 | 0 | 3 |
| `VolumeSpinnerHandler.cs` | 1 | 1 | 0 |
| `FeedbackSpinnerHandler.cs` | 1 | 1 | 0 |
| `DebugAngleHandler.cs` | 2 | 0 | 2 |
| `PanelDragHandler.cs` | 2 | 0 | 2 |
| `RadioInstaller.cs` | 2 | 0 | 2 |
| `ExpanderHandler.cs` | 2 | 0 | 2 |
| **Total** | **93** | **12** | **81** |

### Почему это проблема

1. `Debug.Log()` аллоцирует строку через string.Format/concat
2. Каждый вызов проходит managed-to-native bridge (Unity Engine C++ call)
3. В AudioSynthesisEngine.cs:97 — Debug.Log в DSP callback (44.1k samples/sec, но вызывается ~5-50 раз/sec)
4. RadioStoryIntegration.cs имеет 30+ Debug.Log на каждый story interaction
5. Release-сборка НЕ должна выводить отладочную информацию

---

## Предлагаемое решение

1. Заменить все Debug.Log в production-путях на `#if DEBUG` блоки
2. Или использовать `[Conditional("DEBUG")]` обёртку:
   ```csharp
   [Conditional("DEBUG")]
   internal static void Log(string msg) => Debug.Log("[RadioNav] " + msg);
   ```
3. AudioSynthesisEngine.cs:97 — срочно убрать из DSP (F13)

---

## Acceptance Criteria

- [ ] Release build не содержит ни одного `Debug.Log` в production-пути
- [ ] Audio callback не содержит Debug.Log
- [ ] Все 81 Release-активных вызова обёрнуты в #if DEBUG
