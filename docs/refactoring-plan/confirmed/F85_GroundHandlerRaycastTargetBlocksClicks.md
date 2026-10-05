# F85: GroundHandler — raycastTarget = true блокирует клики

**Приоритет:** P2 (Medium)
**Область:** UI · GroundHandler
**Файл:** `GroundHandler.cs:82`
**Дата обнаружения:** 2026-07-31

---

## Проблема

```csharp
groundCheckmark.raycastTarget = true;
```

Image с `raycastTarget = true` перехватывает клики до `OnPointerClick` на GroundHandler. Клик точно на спрайт чекмарка может **не toggling ground state**.

---

## Исправление

```csharp
groundCheckmark.raycastTarget = false;
```

---

## Acceptance Criteria

- [ ] Клик на checkmark toggles ground state
