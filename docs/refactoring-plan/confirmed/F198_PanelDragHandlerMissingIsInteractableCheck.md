# F198: `PanelDragHandler.OnBeginDrag` — отсутствует проверка `ToggleHandler.IsInteractable`

**Приоритет:** P3 (Low)  
**Область:** UI · PanelDragHandler · Input · Consistency  
**Файл:** `Source/UI/PanelDragHandler.cs:89-100`  
**Дата обнаружения:** 2026-08-01  

---

## Проблема

`PanelDragHandler.OnBeginDrag` не проверяет `ToggleHandler.IsInteractable`, в отличие от `CursorHandler` и `SpinnerHandlerBase`:

```csharp
// PanelDragHandler.cs:89-100 (НЕТ проверки IsInteractable)
public void OnBeginDrag(PointerEventData eventData)
{
    if (eventData.button != PointerEventData.InputButton.Left)
        return;

    if (expander.IsNotNull() && eventData.pointerPress.IsNotNull() &&
        eventData.pointerPress.transform.IsChildOf(expander.transform))
        return;

    isDragging = true;
    activePointerId = eventData.pointerId;
}
```

```csharp
// CursorHandler.cs:50 (ЕСТЬ проверка)
if (!ToggleHandler.IsInteractable) return;
```

```csharp
// SpinnerHandlerBase.cs:67 (ЕСТЬ проверка)
if (!ToggleHandler.IsInteractable) return;
```

---

## Влияние

Пользователь может перемещать панель радио по экрану даже при выключенном радио. Хотя перемещение панели может быть считаться допустимым действием вне зависимости от состояния радио, отсутствие проверки создаёт **несогласованность** с другими обработчиками drag (`CursorHandler`, `SpinnerHandlerBase`, `ExpanderHandler` — F197).

Если намерение — блокировать все взашдействия с UI при выключенном радио, этот обработчик нарушает это. Если намерение — разрешать перемещение панели всегда, это требует согласованного принятия решения и документации.

---

## Исправление

Вариант A (блокировать drag при выключенном радио — согласовано с другими handlers):

```csharp
public void OnBeginDrag(PointerEventData eventData)
{
    if (eventData.button != PointerEventData.InputButton.Left)
        return;
    if (!ToggleHandler.IsInteractable) return;  // ← добавить
    ...
}
```

Вариант B (оставить как есть, но документировать намерение).

---

## Acceptance Criteria

- [ ] Принято сознательное решение: либо добавить `IsInteractable` check (вариант A), либо документировать, что перемещение панели разрешено в любом состоянии (вариант B).
