# F133. RadioHandler.Update — Escape не закрывает панель после выбора дочернего UI

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | UI · RadioHandler · Escape Key |
| **Файл** | `Source/UI/RadioHandler.cs` |
| **Строки** | 89-98 |

---

## Описание

Обработчик Escape (строки 89-98) проверяет `currentSelectedGameObject` и возвращает, если объект выбран:

```csharp
// RadioHandler.cs:89-98
private void Update()
{
    if (!Input.GetKeyDown(KeyCode.Escape) || !IsVisible)
        return;

    if (UnityEngine.EventSystems.EventSystem.current.IsNotNull() &&
        UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.IsNotNull())
        return;  // <-- возврат при ЛЮБОМ выбранном объекте

    IsVisible = false;
}
```

`CursorHandler.Update` (CursorHandler.cs:127-128) при наведении и прокрутке колёсика вызывает:

```csharp
if (EventSystem.current.IsNotNull() && EventSystem.current.currentSelectedGameObject != gameObject)
    EventSystem.current.SetSelectedGameObject(gameObject);
```

После этого `currentSelectedGameObject` равен CursorSpinner — дочернему объекту радио-панели. Нажатие Escape **не закрывает панель**, потому что `currentSelectedGameObject.IsNotNull()` возвращает `true`. Игрок должен кликнуть в пустое место за пределами радио, чтобы снять выделение.

## Корневая причина

`Update` проверяет наличие выбранного объекта, но не проверяет, является ли он частью радио-панели.

## Предлагаемое исправление

Блокировать Escape только если выбранный объект НЕ является дочерним по отношению к панели:

```csharp
private void Update()
{
    if (!Input.GetKeyDown(KeyCode.Escape) || !IsVisible)
        return;

    var selected = EventSystem.current.IsNotNull()
        ? EventSystem.current.currentSelectedGameObject
        : null;

    if (selected.IsNotNull() && !selected.transform.IsChildOf(transform))
        return;

    IsVisible = false;
}
```

## Зависимости

- Связан с F60 (CursorHandler per-frame SetSelectedGameObject) — SetSelectedGameObject создаёт условие для этой проблемы
- Связан с F97 (CursorHandler.OnScroll NRE) — тот же CursorHandler
