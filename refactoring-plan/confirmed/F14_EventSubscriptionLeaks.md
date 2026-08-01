# F14: Утечки подписок на события

**Приоритет:** P1 (High)
**Область:** UI · Все handler-ы
**Тип:** Memory leak · Event lifecycle

---

## Проблема

Несколько UI handler-ов подписываются на события других компонентов в Start()/Awake(), но не отписываются в OnDestroy(). Unity не чистит event delegates при уничтожении GameObject — утекает ссылка на экземпляр, GC не может его собрать.

---

## Evidence

### 1. RadioHandler → ZoneHandler.OnZoneChanged

RadioHandler.cs:81-84:

```csharp
zoneHandler = GetComponentInChildren<ZoneHandler>(true);
if (zoneHandler.IsNotNull())
    zoneHandler.OnZoneChanged += OnZoneChanged;
```

RadioHandler.cs:69-73:

```csharp
private void OnDestroy()
{
    if (Instance == this)
        Instance = null;
    // OnZoneChanged не отписан!
}
```

При уничтожении RadioHandler `ZoneHandler.OnZoneChanged` всё ещё держит ссылку на старый экземпляр. При пересоздании панели (например, при reload мода) старый handler зависает в памяти. Каждый reload добавляет ещё одну копию в список делегатов.

### 2. SpinnerHandlerBase → ToggleHandler.IsInteractable (косвенно)

SpinnerHandlerBase.cs:67, 84 обращается к `ToggleHandler.IsInteractable`. Не событие, но статическая связь. Если ToggleHandler уничтожен, `IsInteractable` отдаст stale значение. Не утечка, но хрупкая архитектура.

### 3. InformationPanelHandler — зеркальная Subscribe/Unsubscribe

InformationPanelHandler.cs:172-202 — 5 пар подписок/отписок. Работает корректно *при условии*, что `Unsubscribe()` вызван. Но нет проверки, что вызов был — если `OnDestroy` не сработал (например, приложение выгружает мод без вызова OnDestroy), подписки висят.

---

## Предлагаемое решение

1. RadioHandler: добавить `Unsubscribe()` блок в OnDestroy:

```csharp
private void OnDestroy()
{
    if (zoneHandler.IsNotNull())
        zoneHandler.OnZoneChanged -= OnZoneChanged;
    if (Instance == this)
        Instance = null;
}
```

2. Все подписки в UI handler-ах должны иметь соответствующие отписки в OnDestroy.
3. Рассмотреть использование `CancellationToken` или `WeakReference` для cross-component подписок.

---

## Acceptance Criteria

- [ ] RadioHandler отписывается от ZoneHandler.OnZoneChanged в OnDestroy
- [ ] SpinnerHandlerBase не использует ToggleHandler.IsInteractable напрямую (через событие)
- [ ] Все Subscribe() имеют парный Unsubscribe() в OnDestroy
