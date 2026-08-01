# F119. RadioHandler.OnDestroy — не отписывается от zoneHandler.OnZoneChanged

| Поле | Значение |
|------|----------|
| **Приоритет** | P1 (High) |
| **Область** | UI · RadioHandler · Events · Memory |
| **Файл** | `Source/UI/RadioHandler.cs` |
| **Строки** | 69-73 (OnDestroy), 84 (Start) |

---

## Описание

В `Start()` RadioHandler подписывается на `zoneHandler.OnZoneChanged`:

```csharp
// RadioHandler.cs:83-84
if (zoneHandler.IsNotNull())
    zoneHandler.OnZoneChanged += OnZoneChanged;
```

В `OnDestroy()` отписка **только** очищает статическое поле `Instance`:

```csharp
// RadioHandler.cs:69-73
private void OnDestroy()
{
    if (Instance == this)
        Instance = null;
    // <-- zoneHandler.OnZoneChanged НЕ отписан
}
```

Если `ZoneHandler` переживёт `RadioHandler` (zone — child, radio — parent; при `Destroy(parent)` children уничтожаются первыми, но при `Destroy(child)` parent сохраняется), при следующем переключении зоны `OnZoneChanged` вызовет **NRE** на уничтоженном RadioHandler.

Даже если ZoneHandler уничтожается раньше — делегат удерживает ссылку на RadioHandler → **memory leak** до сборщика мусора.

## Корневая причина

`OnDestroy` неполный: отписывается от `Instance`, но **пропускает** `zoneHandler.OnZoneChanged`.

## Предлагаемое исправление

```csharp
private void OnDestroy()
{
    if (Instance == this)
        Instance = null;

    if (zoneHandler.IsNotNull())
        zoneHandler.OnZoneChanged -= OnZoneChanged;
}
```

## Зависимости

- Связан с F14 (Event Subscription Leaks)
- Связан с F30 (RadioHandler missing OnDestroy unsubscribe) — та же модель, другой event
