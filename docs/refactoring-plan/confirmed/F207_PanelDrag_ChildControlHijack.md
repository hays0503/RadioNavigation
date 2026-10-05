# F207. PanelDragHandler — перехват drag, начатого на Zone-кнопках / Toggle / Ground: панель двигается вместе с кликом

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | UI · PanelDragHandler · Input |
| **Файл** | `Source/UI/PanelDragHandler.cs:89-100` |
| **Дата обнаружения** | 2026-08-01 |

---

## Описание

`OnBeginDrag` исключает из перетаскивания только потомков Expander:

```csharp
// PanelDragHandler.cs:94-96
if (expander.IsNotNull() && eventData.pointerPress.IsNotNull() &&
    eventData.pointerPress.transform.IsChildOf(expander.transform))
    return;
```

`StandaloneInputModule` вычисляет `pointerDrag` через `ExecuteEvents.GetEventHandler<IBeginDragHandler>(pressObject)`, поднимаясь вверх по иерархии. Дети панели, не имеющие собственного `IBeginDragHandler`, резолвятся на корень панели:

- **Zone-кнопки** — `EventTrigger` (только click-обработчики, см. `ZoneHandler`), без `IBeginDragHandler`;
- **Toggle** (`ToggleHandler`) — `Selectable`, без `IBeginDragHandler`;
- **Ground** (`GroundHandler`) — только `IPointerClickHandler`.

### Следствия

1. Чистый клик по кнопке зоны/переключателю начинает и завершает drag с нулевым смещением — панель не двигается (безвредно).
2. **Drag с кнопки зоны** двигает панель **и** по отпусканию срабатывает `PointerClick` на кнопке → панель сдвинулась + зона переключилась (двойное действие).
3. **Drag с Toggle** двигает панель и переключает радио на отпускании.

---

## Влияние

- Случайные движения панели при попытке аккуратно навести на кнопку зоны/переключатель.
- Двойное действие (сдвиг + переключение) дезориентирует: панель уезжает, частота/зона меняются.
- Не покрыто: **F197/F198** — отсутствие `IsInteractable`-проверок в Expander/PanelDrag (другая проверка), **F149** — multitouch, **F118** — фильтрация кнопок в ZoneHandler.

---

## Сценарий воспроизведения

1. Нажать на кнопку зоны и, не отпуская, сдвинуть мышь на несколько пикселей.
2. Панель начинает двигаться; после отпускания зона переключается.

---

## Исправление

Расширить исключение на все интерактивные потомки, у которых есть собственный обработчик клика/перетаскивания:

```csharp
Transform press = eventData.pointerPress.transform;
if (press.IsChildOf(expander.transform)
    || press.GetComponentInParent<ZoneHandler>() != null
    || press.GetComponentInParent<ToggleHandler>() != null
    || press.GetComponentInParent<GroundHandler>() != null)
    return;
```

Либо универсально: игнорировать drag, если `eventData.pointerPress != gameObject` (drag разрешён только с пустой области панели).

---

## Зависимости

- **F197/F198** — те же хендлеры; при рефакторинге ввода объединить проверки в один guard.
- **F118/F148** — фильтрация кнопок в Zone/Ground — связанная область ввода.
- **F14/F30/F119** — события ZoneHandler — при изменении обработчиков перепроверить подписки.

---

## Acceptance Criteria

- [ ] Drag с кнопок зоны/Toggle/Ground не двигает панель
- [ ] Клик по кнопкам зоны по-прежнему переключает зону
- [ ] Drag с пустой области панели по-прежнему работает
