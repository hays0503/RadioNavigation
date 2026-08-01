# F197: `ExpanderHandler.OnBeginDrag` — отсутствует проверка `ToggleHandler.IsInteractable`

**Приоритет:** P3 (Low)  
**Область:** UI · ExpanderHandler · Input · Consistency  
**Файл:** `Source/UI/ExpanderHandler.cs:32-39`  
**Дата обнаружения:** 2026-08-01  

---

## Проблема

`ExpanderHandler.OnBeginDrag` не проверяет `ToggleHandler.IsInteractable`, в отличие от `CursorHandler` и `SpinnerHandlerBase`:

```csharp
// ExpanderHandler.cs:32-39 (НЕТ проверки IsInteractable)
public void OnBeginDrag(PointerEventData eventData)
{
    if (eventData.button != PointerEventData.InputButton.Left) return;
    if (panelRect.IsNull()) return;
    isDragging = true;
    dragStartScale = panelRect.localScale;
    dragStartMouseX = eventData.position.x;
}
```

```csharp
// CursorHandler.cs:46-51 (ЕСТЬ проверка)
public void OnBeginDrag(PointerEventData eventData)
{
    if (eventData.button != PointerEventData.InputButton.Left)
        return;
    if (!ToggleHandler.IsInteractable) return;   // ← есть
    ...
}
```

```csharp
// SpinnerHandlerBase.cs:63-66 (ЕСТЬ проверка)
public void OnBeginDrag(PointerEventData eventData)
{
    if (eventData.button != PointerEventData.InputButton.Left)
        return;
    if (!ToggleHandler.IsInteractable) return;   // ← есть
    ...
}
```

---

## Влияние

Пользователь может масштабировать (увеличивать/уменьшать) панель радио, даже когда радио выключено (`ToggleHandler.IsRadioOn == false`) или ground неактивен (`groundActive == false`). Это приводит к несогласованному UX: курсор и спиннеры заблокированы, но масштабирование панели — нет.

`IsInteractable` = `IsRadioOn && groundActive`. Если хотя бы один из флагов false, UI не должен реагировать на drag.

---

## Исправление

Добавить проверку в `OnBeginDrag`:

```csharp
public void OnBeginDrag(PointerEventData eventData)
{
    if (eventData.button != PointerEventData.InputButton.Left) return;
    if (!ToggleHandler.IsInteractable) return;  // ← добавить
    if (panelRect.IsNull()) return;
    ...
}
```

---

## Acceptance Criteria

- [ ] `ExpanderHandler.OnBeginDrag` проверяет `ToggleHandler.IsInteractable` перед началом drag.
- [ ] Масштабирование панели невозможно, когда `IsInteractable == false`.
