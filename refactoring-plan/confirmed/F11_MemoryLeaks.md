# F11: Memory Leaks — Утечки Ресурсов и Подписок

**Приоритет:** MEDIUM
**Область:** UI + Logic
**Тип:** Memory

---

## Проблема

Пять источников утечек памяти: от безымянных lambda-делегатов, которые нельзя отписать, до ресурсов Unity (Texture2D, Font), которые не уничтожаются при разрушении объекта.

---

## Evidence

### 1. Lambda-подписки на keyer события — никогда не отписываются

`SonneController.cs:90-91`:

```csharp
keyer.OnElementCompleted += (elemType, isCallsign) => OnStationElementCompleted(stationIndex, elemType, isCallsign);
keyer.OnSequenceFinished += () => OnStationSequenceFinished(stationIndex);
```

**Проблема:** Анонимные лямбды. Их нельзя отписать через `-=` (нужен именованный делегат или переменная). Каждая лямбда захватывает `this` (текущий SonneController), удерживая экземпляр от GC.

При релоаде мода (код Main.cs: `loaded = true` → сцена перезагружается) старый SonneController может не собраться GC, потому что `keyers[]` — не MonoBehaviour, а plain C# объект, и его event-делегаты держат ссылку на старый экземпляр:

```
SonneController → keyers[] → MorseKeyer
                               ├── OnElementCompleted → lambda → this (SonneController)
                               └── OnSequenceFinished → lambda → this (SonneController)
```

**Фикс:** Создать именованный метод-обработчик и подписаться через `+= new Action<...>(HandleElementCompleted)` / `-= HandleElementCompleted` в `OnDestroy`.

---

### 2. Texture2D не чистится на OnDestroy

`PanelDragHandler.cs:34-36, 80-84`:

```csharp
// Start (строка 34-36):
if (cachedCursorTexture.IsNull())
    cachedCursorTexture = GenerateMoveCursor();
moveCursorTexture = cachedCursorTexture;

// GenerateMoveCursor (строка 80-84):
Application.quitting += () => {
    if (tex.IsNotNull())
        Object.Destroy(tex);
};
```

- Texture2D создаётся в `GenerateMoveCursor()`
- Чистится **только** при `Application.quitting` через лямбду (которую тоже нельзя отписать)
- Если `PanelDragHandler` уничтожается mid-game (scene transition, manual destroy), Texture2D остаётся в памяти навсегда
- `cachedCursorTexture` (static) держит сильную ссылку → Asset не выгружается
- Нет `OnDestroy` → нет вызова `Resources.UnloadAsset`

**Фикс:** Убрать `Application.quitting`, добавить `OnDestroy`:

```csharp
private void OnDestroy()
{
    if (cachedCursorTexture != null)
    {
        Object.Destroy(cachedCursorTexture);
        cachedCursorTexture = null;
    }
}
```

---

### 3. Dynamic Font утечка (×7 каждый Start)

`InformationPanelHandler.cs:134`:

```csharp
text.font = Font.CreateDynamicFontFromOSFont("Arial", 14);
```

Вызывается в `CreateLabel()`, который дёргается **7 раз** из `Start()`. Каждый вызов создаёт новый `Font`-объект (managed + native memory). Эти Font-ы **никогда не уничтожаются** — ни в `OnDestroy`, ни через `Resources.UnloadUnusedAssets`.

При каждом рестарте панели (scene reload) — 7 новых Font-ов. `Resources.FindObjectsOfTypeAll<Font>()` будет показывать растущее число Arial-объектов.

**Фикс:** Создать Font один раз, закэшировать в static поле. Переиспользовать во всех `CreateLabel` вызовах.

---

### 4. Missing unsubscribe — zoneHandler.OnZoneChanged

См. F14 (EventSubscriptionLeaks) — `RadioHandler` не отписывается от `ZoneHandler.OnZoneChanged`.

---

### 5. Static `silentClip` — живёт весь AppDomain

`SonneController.cs:14`:

```csharp
private static readonly AudioClip silentClip = AudioClip.Create(...);
```

Static readonly → создаётся один раз, живёт до выгрузки AppDomain. `AudioClip.Create` аллоцирует native audio data buffer (~0.5 MB для 2 секунд при 44100 Hz × 2 channels × 2 bytes = ~353 KB). Не критично, но не освобождается. На данный момент это не баг, а сознательный выбор (кэш). Оставлено как наблюдение: при статическом кэше теряется контроль над lifetime.

---

### 6. Static events `RadioHandler.OnVisibilityChanged` / `OnFrequencyChanged`

`RadioHandler.cs:10-11`:

```csharp
public static event Action<bool> OnVisibilityChanged;
public static event Action<float, int> OnFrequencyChanged;
```

Static events + instance методов = GC не может собрать подписчика, пока событие не обнулено. В текущем коде `OnFrequencyChanged` подписывается `SonneController.Awake()` [SonneController.cs:48] и никогда не отписывается — нет `-=` в `SonneController.OnDestroy()`.

**Фикс:** Сделать события instance + DI, или отписывать в `OnDestroy`.

### 7. EventTrigger anonymous lambda leak

`ZoneHandler.cs:47-81`:

```csharp
private void SetupClickHandler(GameObject child)
{
    EventTrigger trigger = child.GetComponent<EventTrigger>();
    if (trigger.IsNull())
        trigger = child.AddComponent<EventTrigger>();

    EventTrigger.Entry leftClick = new EventTrigger.Entry
    {
        eventID = EventTriggerType.PointerClick
    };
    leftClick.callback.AddListener((data) =>   // ← anonymous lambda
    {
        // ...
    });
    trigger.triggers.Add(leftClick);

    EventTrigger.Entry rightClick = new EventTrigger.Entry
    {
        eventID = EventTriggerType.PointerClick
    };
    rightClick.callback.AddListener((data) =>   // ← anonymous lambda
    {
        // ...
    });
    trigger.triggers.Add(rightClick);
}
```

Две анонимные лямбды на каждый zone child (5 зон = 10 лямбд). `AddListener` с анонимным делегатом нельзя отписать. Хотя `OnDestroy` (строка 117) чистит `.triggers.Clear()`, при повторном вызове `Start()` (re-enable) они накапливаются.

**Фикс:** Заменить анонимные лямбды на именованные методы, отписывать в OnDestroy:

```csharp
private void OnLeftClick(PointerEventData ped) { ... }
private void OnRightClick(PointerEventData ped) { ... }

// В SetupClickHandler:
leftClick.callback.AddListener(OnLeftClick);
rightClick.callback.AddListener(OnRightClick);

// В OnDestroy:
trigger.triggers.Clear(); // уже есть
```

---

## Acceptance Criteria

- [ ] Keyer event подписки отписываются (именованные методы, `-=` в OnDestroy)
- [ ] Texture2D уничтожается при `OnDestroy`, не только при `Application.quitting`
- [ ] Dynamic Font создаётся один раз (static cache)
- [ ] `zoneHandler.OnZoneChanged` отписан в `OnDestroy`
- [ ] Static events не держат instance-подписчиков
