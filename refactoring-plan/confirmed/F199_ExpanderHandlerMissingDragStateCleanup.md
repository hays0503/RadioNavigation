# F199: `ExpanderHandler` — отсутствует очистка состояния drag при `OnDisable`/`OnDestroy`

**Приоритет:** P3 (Low)  
**Область:** UI · ExpanderHandler · State Management  
**Файл:** `Source/UI/ExpanderHandler.cs` (отсутствует `OnDisable`/`OnDestroy`)  
**Дата обнаружения:** 2026-08-01  

---

## Проблема

`ExpanderHandler` не имеет метода `OnDisable` или `OnDestroy` для сброса флага `isDragging`. Если drag прерван (например, компонент отключён mid-drag через `enabled = false` или `SetActive(false)`, или сцена переключается), флаг `isDragging` остаётся `true`:

```csharp
// ExpanderHandler.cs:13, 36, 49-52
private bool isDragging;          // ← состояние

public void OnBeginDrag(...)      // ← устанавливает isDragging = true
{
    ...
    isDragging = true;
    ...
}

public void OnDrag(...)           // ← проверяет только isDragging (без pointerId!)
{
    if (!isDragging) return;
    ...
}

public void OnEndDrag(...)        // ← сбрасывает isDragging = false
{
    isDragging = false;
}
```

После сброса (`OnDisable`/`OnDestroy` → снова включить):
- `OnDrag` будет срабатывать **без** `OnBeginDrag`, потому что `isDragging == true` из прошлого.
- `OnEndDrag` сбросит флаг, но за это время уже выполнится логика drag.

В отличие от `PanelDragHandler`, у которого есть `OnDisable` (PanelDragHandler.cs:138-145) для сброса `moveCursorActive`, `ExpanderHandler` не имеет никакого lifecycle-метода для очистки состояния.

---

## Влияние

F149 документирует аналогичную проблему для `SpinnerHandlerBase` (мультитач + `isDragging`/`activePointerId` не сбрасываются). `ExpanderHandler` имеет **ту же самую проблему**, но не документирована.

Кроме того, `ExpanderHandler.OnDrag` не проверяет `activePointerId` (в отличие от `SpinnerHandlerBase.IsActiveDrag` и `CursorHandler.IsActiveDrag`), что усугубляет проблему: если `isDragging` застрял в `true`, любой последующий drag от любого пальца будет обрабатываться.

---

## Исправление

Добавить `OnDisable` в `ExpanderHandler`:

```csharp
private void OnDisable()
{
    isDragging = false;
    activePointerId = -1;
}
```

Или добавить проверку `pointerId` в `OnDrag`/`IsActiveDrag` (как в `SpinnerHandlerBase` и `CursorHandler`).

---

## Related Issues

- F149 (SpinnerHandlerBase multitouch state leak — аналогичная проблема в другом handler)
- F197 (ExpanderHandler missing IsInteractable check)

---

## Acceptance Criteria

- [ ] `ExpanderHandler` имеет `OnDisable`/`OnDestroy` сбрасывающий `isDragging` и `activePointerId`.
- [ ] Drag состояние корректно сбрасывается при отключении компонента mid-drag.
