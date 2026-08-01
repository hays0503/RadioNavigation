# F148. GroundHandler — OnPointerClick не фильтрует по кнопке мыши

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | UI · GroundHandler · Input |
| **Файл** | `Source/UI/GroundHandler.cs` |
| **Строки** | 106-141 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

`IPointerClickHandler.OnPointerClick` срабатывает для **всех** кнопок мыши (left, right, middle):

```csharp
// GroundHandler.cs:106
public void OnPointerClick(PointerEventData eventData)
{
    bool isActive = IsGroundEffectivelyActive();
    // ... переключает ground state — нет проверки eventData.button
```

Правая или средняя кнопка переключает ground state идентично левой. Это тот же класс проблем, что F118 (ZoneHandler), но для GroundHandler.

## Корневая причина

Отсутствие проверки `eventData.button` в обработчике.

## Предлагаемое исправление

```csharp
public void OnPointerClick(PointerEventData eventData)
{
    if (eventData.button != PointerEventData.InputButton.Left)
        return;
    // ...
```

## Зависимости

- Связан с F118 (ZoneHandler PointerClick no button filter) — та же проблема, другой handler
- Связан с F85 (GroundHandler raycastTarget blocks clicks) — тот же файл, другой input aspect
