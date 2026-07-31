# F97. CursorHandler.OnScroll — NRE: RadioHandler.Instance dereferenced before null check

| Поле | Значение |
|------|----------|
| **Приоритет** | P1 (High) |
| **Область** | UI · CursorHandler · NRE |
| **Файл** | `Source/UI/CursorHandler.cs` |
| **Строки** | 109, 114 |

---

## Описание

В методе `OnScroll` свойство `RadioHandler.Instance.CursorAngle` разыменовывается на строке 109 **до** проверки на null на строке 114:

```csharp
// Строка 108-115:
float newAngle = Mathf.Clamp(
    RadioHandler.Instance.CursorAngle + delta * step,  // <-- NRE если Instance == null
    MinAngle, MaxAngle);

cursorRect.localRotation = Quaternion.Euler(0, 0, -newAngle);

if (RadioHandler.Instance.IsNotNull())                 // <-- проверка ПОСЛЕ разыменования
    RadioHandler.Instance.CursorAngle = newAngle;
```

Если `RadioHandler.Instance` равен `null` (например, при уничтожении сцены или до вызова `Start`), строка 109 выбросит `NullReferenceException`.

## Корневая причина

Порядок операций: сначала чтение `CursorAngle`, потом проверка `IsNotNull()`. Нужно наоборот.

## Предлагаемое исправление

```csharp
public void OnScroll(PointerEventData eventData)
{
    if (!ToggleHandler.IsInteractable) return;
    if (cursorRect.IsNull()) return;
    if (RadioHandler.Instance.IsNull()) return;  // <-- добавить в начало

    float delta = eventData.scrollDelta.y;
    if (delta.IsZero()) return;

    float step = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
        ? ScrollStepFine
        : ScrollStepNormal;

    float newAngle = Mathf.Clamp(
        RadioHandler.Instance.CursorAngle + delta * step,
        MinAngle, MaxAngle);

    cursorRect.localRotation = Quaternion.Euler(0, 0, -newAngle);
    RadioHandler.Instance.CursorAngle = newAngle;

    eventData.Use();
}
```

## Зависимости

- Независим от других F-проблем
- Похож на F07 (NRE risks), но это отдельный site
