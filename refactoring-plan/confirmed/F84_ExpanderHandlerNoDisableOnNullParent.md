# F84: ExpanderHandler — не отключает себя при отсутствии parent

**Приоритет:** P2 (Medium)
**Область:** UI · ExpanderHandler
**Файл:** `ExpanderHandler.cs:19-23`
**Дата обнаружения:** 2026-07-31

---

## Проблема

```csharp
if (transform.parent.IsNull()) {
    Debug.LogError(...);
    return;  // ← НЕТ enabled = false!
}
```

При отсутствии parent: handler остаётся активным, `panelRect` = null. `OnBeginDrag` проверяет `panelRect.IsNull()` — crash-protected, но handler processing events needlessly.

---

## Исправление

Добавить `enabled = false;` перед `return;`.

---

## Acceptance Criteria

- [ ] При отсутствии parent handler отключается
