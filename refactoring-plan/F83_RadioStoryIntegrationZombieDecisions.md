# F83: RadioStoryIntegration — zombie decisions при hot-reload

**Приоритет:** P1 (High)
**Область:** Logic · RadioStoryIntegration
**Файл:** `RadioStoryIntegration.cs:27-38`
**Дата обнаружения:** 2026-07-31

---

## Проблема

При hot-reload мода:
1. `harmony.UnpatchAll()` — снимает патчи
2. `ResetRuntimeState()` — очищает reflection cache
3. **НО** ранее инжектированные решения в `RadioStory` остаются в состоянии игры

Если игрок на radio story menu при перезагрузке мода → **zombie entry** в меню выбора.

---

## Исправление

В `OnSceneAwake` (или `OnDestroy`) — проверять и удалять ранее инжектированные решения из story state.

---

## Acceptance Criteria

- [ ] После hot-reload нет zombie entries в radio story menu
