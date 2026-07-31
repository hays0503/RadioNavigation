# F95: CursorHandler — добавляет лишний ScrollRect

**Приоритет:** P3 (Low)
**Область:** UI · CursorHandler
**Файл:** `CursorHandler.cs:37-43`
**Дата обнаружения:** 2026-07-31

---

## Проблема

ScrollRect добавляется с `horizontal=false, vertical=false` solely для intercept scroll events, но `IScrollHandler` уже реализован. ScrollRect может конфликтовать с другими event handlers.

---

## Исправление

Убрать ScrollRect, полагаться на IScrollHandler.

---

## Acceptance Criteria

- [ ] ScrollRect не добавляется
- [ ] Scroll events работают через IScrollHandler
