# F30: RadioHandler.OnDestroy не отписывается от OnZoneChanged

**Приоритет:** P1 (High)
**Область:** UI · RadioHandler
**Тип:** Memory Leak · Event Lifecycle

---

## Проблема

RadioHandler.cs:84 подписывается на `zoneHandler.OnZoneChanged`, но OnDestroy (строка 69-73) только чистит `Instance = null`, не отписываясь. При scene reload — утечка и NRE.

---

## Evidence

RadioHandler.cs:69-73:

```csharp
private void OnDestroy()
{
    if (Instance == this)
        Instance = null;       // ← не отписывается от zoneHandler.OnZoneChanged!
}
```

RadioHandler.cs:82-84 (Start):

```csharp
zoneHandler = GetComponentInChildren<ZoneHandler>(true);
if (zoneHandler.IsNotNull())
    zoneHandler.OnZoneChanged += OnZoneChanged;  // ← подписка без unsubscribe
```

### Почему это проблема

- ZoneHandler — MonoBehaviour в том же prefab. При scene reload ZoneHandler может пережить RadioHandler
- Stale subscriber (RadioHandler) держит ссылку на уже уничтоженный объект
- При вызове `OnZoneChanged?.Invoke()` — InvokeList содержит мёртвую ссылку → NRE или Warning

### Сравнение с другими файлами

SonneController.cs:112-119 — правильно отписывается от 3-х событий.
InformationPanelHandler.cs:469-471 — правильно вызывает Unsubscribe().

---

## Предлагаемое решение

```csharp
private void OnDestroy()
{
    if (zoneHandler.IsNotNull())
        zoneHandler.OnZoneChanged -= OnZoneChanged;
    if (Instance == this)
        Instance = null;
}
```

---

## Acceptance Criteria

- [ ] RadioHandler.OnDestroy отписывается от zoneHandler.OnZoneChanged
- [ ] Нет stale subscriber после scene reload
