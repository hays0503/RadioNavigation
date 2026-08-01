# F187: `PanelDragHandler.OnPointerExit` resets custom cursor while dragging outside panel bounds

**Приоритет:** P3 (Low)  
**Область:** UI · PanelDragHandler · User Experience  
**Файл:** `Source/UI/PanelDragHandler.cs:129-136`  
**Дата обнаружения:** 2026-07-31  

---

## Проблема

В `PanelDragHandler.cs`:

```csharp
public void OnPointerExit(PointerEventData eventData)
{
    if (!moveCursorActive)
        return;

    Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    moveCursorActive = false;
}
```

Если игрок зажимает левую кнопку мыши для перетаскивания радиопанели (`isDragging == true`) и быстро перемещает курсор за пределы колайдера/RectTransform панели, срабатывает `OnPointerExit`, который сбрасывает кастомный иконку перемещения (`moveCursorTexture`) на обычную стрелку мыши прямо во время перетаскивания.

---

## Влияние

Визуальное мигание и потеря иконки перемещения при быстром перетаскивании панели по экрану.

---

## Исправление

Не сбрасывать курсор в `OnPointerExit`, если идет процесс перетаскивания:

```csharp
public void OnPointerExit(PointerEventData eventData)
{
    if (!moveCursorActive || isDragging)
        return;

    Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    moveCursorActive = false;
}
```

И сбрасывать его в `OnEndDrag`.

---

## Acceptance Criteria

- [ ] Курсор перемещения не исчезает при сдвиге мыши за пределы RectTransform панели во время драга.
- [ ] Курсор сбрасывается корректно после отпускания кнопки мыши (`OnEndDrag`).
