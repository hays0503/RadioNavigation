# F26: PrefabNames.cs — File Exists But Never Used

**Приоритет:** P2 (Medium)
**Область:** UI · PrefabNames
**Тип:** Hygiene · Dead Code

---

## Проблема

`PrefabNames.cs` определяет ~20 констант для путей к UI-элементам префаба, но используется только в `RadioInstaller.cs:7`. Все остальные места используют raw string literals.

---

## Evidence

PrefabNames.cs:5-23 — 20 констант, но потребитель один:

| Файл | Строка | Hardcoded string |
|------|--------|------------------|
| `Main.cs` | 165-178 | `"CursorSpinner"`, `"Toggle"`, ... (7 paths) |
| `ToggleHandler.cs` | 83 | `"ciferbllat"` (опечатка! `ciferblat`?) |
| `GroundHandler.cs` | 56, 73 | `"Background"`, `"GroundCheckmark"` |
| `CursorHandler.cs` | ~71 | `"DialHandle"` |
| `ExpanderHandler.cs` | ~15 | `"ExpanderArrow"` |
| `DebugAngleHandler.cs` | ~18 | `"InputField"` |
| `VolumeSpinnerHandler.cs` | ~12 | `"VolumeSlider"` |

Итого ~20 hardcoded strings в 7+ файлах. При переименовании элемента префаба в Unity надо менять N файлов.

---

## Предлагаемое решение

1. Подключить `PrefabNames` константы во всех `transform.Find()` вызовах
2. Исправить опечатку `"ciferbllat"` через PrefabNames
3. Добавить missing константы в PrefabNames (GroundCheckmark, DialHandle, ExpanderArrow, InputField, VolumeSlider)

---

## Acceptance Criteria

- [ ] All `transform.Find("...")` use `PrefabNames.X` instead of raw strings
- [ ] `"ciferbllat"` typo fixed
- [ ] Grep for `.Find("` returns 0 results (outside PrefabNames.cs)
