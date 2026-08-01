# F93: RadioInstaller — Debug.Log без #if DEBUG

**Приоритет:** P3 (Low)
**Область:** Build · Debug Logging
**Файл:** `RadioInstaller.cs`
**Дата обнаружения:** 2026-07-31

---

## Проблема

10 `Debug.Log` вызовов не обёрнуты в `#if DEBUG` → активны в Release builds.

---

## Исправление

Обернуть в `#if DEBUG`.

---

## Acceptance Criteria

- [ ] Все Debug.Log в RadioInstaller обёрнуты в #if DEBUG
