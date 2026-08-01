# F90: RadioHandler.OnVisibilityChanged — event никто не подписывает

**Приоритет:** P3 (Low)
**Область:** UI · RadioHandler · Events
**Файл:** `RadioHandler.cs:10`
**Дата обнаружения:** 2026-07-31

---

## Проблема

```csharp
public static event Action<bool> OnVisibilityChanged;
```

Event определен и firing в `IsVisible` setter, но **ни один subscriber** во всём коде.

---

## Исправление

Удалить event если он не нужен, или добавить subscribers если планируется использование.

---

## Acceptance Criteria

- [ ] Event удалён или имеет subscribers
