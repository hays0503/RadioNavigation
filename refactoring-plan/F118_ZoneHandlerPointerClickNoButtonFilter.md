# F118. ZoneHandler — PointerClick entries не фильтруют по кнопке

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | UI · ZoneHandler · EventTrigger |
| **Файл** | `Source/UI/ZoneHandler.cs` |
| **Строки** | 53-81 |

---

## Описание

`SetupClickHandler` регистрирует **два** `EventTrigger.Entry` с одинаковым `eventID = PointerClick`:

```csharp
// ZoneHandler.cs:53-65 — LMB handler
EventTrigger.Entry leftClick = new EventTrigger.Entry
{
    eventID = EventTriggerType.PointerClick
};
leftClick.callback.AddListener((data) =>
{
    PointerEventData ped = data as PointerEventData;
    if (ped.IsNotNull() && ped.button == PointerEventData.InputButton.Left)
    {
        NextZone();
    }
});

// ZoneHandler.cs:68-81 — RMB handler
EventTrigger.Entry rightClick = new EventTrigger.Entry
{
    eventID = EventTriggerType.PointerClick
};
rightClick.callback.AddListener((data) =>
{
    PointerEventData ped = data as PointerEventData;
    if (ped.IsNotNull() && ped.button == PointerEventData.InputButton.Right)
    {
        PreviousZone();
    }
});
```

Unity `EventTrigger` **не различает кнопки мыши** — оба callback'а вызываются при **любом** клике (LMB, RMB, Middle). При LMB-клике `leftClick.callback` вызывается (correct) **и** `rightClick.callback` вызывается (unnecessary),尽管 внутренняя проверка `ped.button` предотвращает неправильное действие.

**Проблемы:**
1. Лишние вызовы callback при каждом клике (performance overhead)
2. При добавлении логики в callback без проверки button — **непредсиденное поведение**
3. Если `ped` будет `null` (edge case) — оба callback'а молча пропускаются без логирования

## Корневая причина

EventTrigger не поддерживает фильтрацию по кнопке на уровне `eventID`. Единственный способ — проверять `ped.button` внутри callback.

## Предлагаемое исправление

Один `PointerClick` entry с проверкой кнопки:

```csharp
private void SetupClickHandler(GameObject child)
{
    EventTrigger trigger = child.GetComponent<EventTrigger>();
    if (trigger.IsNull())
        trigger = child.AddComponent<EventTrigger>();

    EventTrigger.Entry click = new EventTrigger.Entry
    {
        eventID = EventTriggerType.PointerClick
    };
    click.callback.AddListener((data) =>
    {
        PointerEventData ped = data as PointerEventData;
        if (ped.IsNull()) return;

        if (ped.button == PointerEventData.InputButton.Left)
            NextZone();
        else if (ped.button == PointerEventData.InputButton.Right)
            PreviousZone();
    });
    trigger.triggers.Add(click);
}
```

## Зависимости

- Независим от других F-проблем
