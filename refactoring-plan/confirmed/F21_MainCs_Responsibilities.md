# F21: Main.cs — несколько ответственностей

**Приоритет:** P2 (Medium)
**Область:** Logic · Main
**Тип:** Architecture

---

## Проблема

`Main.cs` (196 строк) реализует 4+ отдельные ответственности в одном классе. Каждая — с разным жизненным циклом и требованиями к тестированию.

---

## Evidence

Main.cs:18-194 — 5 ответственностей:

| Ответственность | Метод | Строки |
|----------------|-------|--------|
| Mod lifecycle | `OnLoaded()` | 24-97 |
| Harmony patching | `OnLoaded()` | 35-84 |
| DI injection | `SceneEventsListener_OnSceneAwake()` | 108-126 |
| Scene management | `SceneEventsListener_OnSceneStart()` | 128-194 |
| Prefab validation | `SceneEventsListener_OnSceneStart()` | 165-178 |
| Diagnostics | `DumpHierarchy()` | 100-105 |

### Проблемы:

1. **Prefab validation** смешан с scene logic — если validation упадёт, OnSceneStart целиком прерывается
2. **Harmony patching** использует анонимный массив `targets` с inline-конфигурацией — не тестируется отдельно
3. **DI injection** зависит от `InjectionFramework.Instance` — статический сервис-локатор
4. **Дважды подписывается на OnSceneStart** — строки 86-89 сначала отписываются, потом подписываются (защита от reload), но хрупко

### Ранние return-ы в OnSceneStart

4 точки выхода (строки 142, 149, 155, 162) каждая с отдельным условием. Если мод не загрузился — нет сообщения пользователю, только Debug.Log в консоль.

---

## Предлагаемое решение

1. Выделить `HarmonyPatchManager` — установка/откат патчей
2. Выделить `PrefabValidator` — проверка иерархии префаба
3. `SceneEventsListener_OnSceneStart` оставить как orchestrator, делегирующий шаги

---

## Acceptance Criteria

- [ ] Main.cs содержит только lifecycle-логику (вызов шагов)
- [ ] Harmony patching тестируется отдельно
- [ ] Prefab validation тестируется отдельно
