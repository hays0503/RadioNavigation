# F149. SpinnerHandlerBase — multi-touch state leak при interrupted drag

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | UI · SpinnerHandlerBase · State Management |
| **Файл** | `Source/UI/SpinnerHandlerBase.cs` |
| **Строки** | 63-75, 102-107 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

Если drag прерывается (объект disabled во время drag), `OnEndDrag` может не вызваться:

```csharp
// SpinnerHandlerBase.cs:63-75 (OnBeginDrag)
isDragging = true;
activePointerId = eventData.pointerId;
```

```csharp
// SpinnerHandlerBase.cs:102-107 (OnEndDrag)
if (eventData.pointerId != activePointerId) return;
isDragging = false;
activePointerId = -1;
```

Если `SpinnerHandlerBase` или parent disabled во время drag:
1. `OnEndDrag` не вызывается → `isDragging` остаётся `true`
2. При re-enable `OnDrag` events от **другого** pointer ID игнорируются (проверка `IsActiveDrag`)
3. Пользователь должен начинать drag заново

Тот же паттерн в `PanelDragHandler`.

## Корневая причина

Нет safety reset для `isDragging` при disable/enable lifecycle.

## Предлагаемое исправление

Добавить `OnEnable` reset:

```csharp
private void OnEnable()
{
    isDragging = false;
    activePointerId = -1;
}
```

## Зависимости

- Связан с F118 (ZoneHandler PointerClick) — тот же input handling pattern
- Связан с F133 (Escape key blocked) — related UI state management issue
