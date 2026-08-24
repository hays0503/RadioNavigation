# Playbook: собственный персистентный маркер на карте UBOAT

## 1. Пререквизиты: DI-инъекция

Менеджер маркеров игры (`EntityMarkersManager`) — `MonoBehaviour, ISingleton` с `[Inject]`-статиками. Мод воспроизводит паттерн:

```csharp
namespace UBOAT.Mods.RadioNavigation.Map;

public class CustomMarkersManager : MonoBehaviour, ISingleton
{
    [Inject] private static Sandbox sandbox;
    [Inject] private static MainCamera mainCamera;
    [Inject] private static GameUI gameUI;
    [Inject] private static SavesManager savesManager;

    private void Awake() => savesManager.Loaded += OnLoaded; // пересоздание после загрузки сейва
}
```

Сборку мода инжектит сам мод: `InjectionFramework.InjectIntoAssembly(assembly)` из `Main.cs`. Инстанс менеджера создаётся один раз и живёт через закрытие карты — `ISingleton` не умирает при смене сцены карты. Если класс нельзя сделать `ISingleton`, держите его в DontDestroyOnLoad-объекте и кэшируйте статики вручную после `OnSceneAwake`.

## 2. Цепочка преобразования координат (проверено по исходникам)

Три слоя: гео (градусы) → encounter (км) → scene (метры Unity).

```csharp
// Coordinates.cs:47-55 — гео -> encounter, км
Vector2 ToWorldPosition(double lat, double lon)      // (lon*111.32, lat*111.32)
Vector2 ToWorldPosition(float lat, float lon)        // (lon*40075.16f/360, lat*40075.16f/360)
Vector2 ToWorldPosition(Vector2 latlong)             // ВАЖНО: latlong=(lat,lon), выход (lon,lat)

// Sandbox.cs:958-986 — encounter -> scene, метры; origin = SandboxSceneOrigin.Position
Vector3 EncounterToSceneSpace(Vector2 p)             // ((p.x-o.x)*1000, 0, (p.y-o.y)*1000)
Vector2 EncounterToSceneSpace2D(Vector2 p)           // то же без Y
Vector2 SceneToEncounterSpace(Vector2 scenePos)      // обратное
```

```csharp
// полный путь для точки (lat, lon):
var encounter = Coordinates.ToWorldPosition(lat, lon);    // км
var scene     = sandbox.EncounterToSceneSpace(encounter); // метры мира
```

Никогда не храните scene-координаты в данных мода: origin сдвигается между сессиями. Храните только lat/lon или encounter-км.

## 3. Два пути рендера

**A. World-space** — объект в сцене на высоте над террейном (так живут `TaskMarker`-token и 3D-представления):

```csharp
marker.transform.position = scene.MaskY(elevationY); // elevationY поднимайте вместе с картой
```

Масштаб привязывайте к высоте камеры: игра делит `MapController.CameraElevation` на константу (`EntityMarkerUI.cs:336`: `/1400000f`).

**B. Screen-space uGUI** — RectTransform под оверлеем, позиция каждый кадр:

```csharp
Vector3 sp = camera.WorldToScreenPointNonJittered(scene);
if (sp.z >= 0f) { marker.transform.position = sp.MaskZ(0f); marker.gameObject.SetActive(true); }
else marker.gameObject.SetActive(false);
```

Это паттерн `GroupMarkerUI.cs:214-216` и `EntityMarkerUI.UpdateMarker` (`camera.WorldToScreenPoint(...)`, проверка `z >= 0`). `NonJittered` обязателен — обычный дрожит при TAA.

## 4. Гейтинг видимости по кадру

Обновляйте в подписчике `executionQueue.AddUpdateListener(DoUpdate)` (не в `Update()` — очередь игры синхронизирует кадры). Проверки как у `EntityMarkersManager.DoUpdate`:

```csharp
bool visible = mainCamera.CurrentMode == CameraMode.Map
            && mainCamera.isActiveAndEnabled
            && mainCamera.transform.position.y > 500f                       // высота над сценой
            && gameUI.CurrentMode != GameUI.Mode.SoftDisabled
            && mode.HasFlag(MapDisplayMode.Normal);                          // ваш набор режимов
```

Флаги `MapCameraController.MapDisplayMode` (MapCameraController.cs:41-50): `Normal=1, Headquarters=2, WorldMap=4, TacticalMap=8, Vacation=0x10, AssignmentSelection=0x20, AssignmentSummary=0x40`. На смену режима подписка: `mainCamera.MapController.MapModeChanged += ...` (event `Action`, строка 487). Для плавного появления используйте формулу игры: `Mathf.Clamp01((CameraElevation - 1100f) / 300f)`.

## 5. Выживание при save/load

Игра уничтожает/пересоздаёт сущности при загрузке — маркеры пересобирают в `SavesManager.Loaded` (сигнатура обработчика: `void Handler(Queue<Action> queue)`). Схема:

```csharp
private void OnLoaded(Queue<Action> queue) => RebuildAll();
private void RebuildAll()
{
    foreach (Transform c in transform) Destroy(c.gameObject);
    foreach (var m in myMarkerData) Spawn(m.latLon);
}
```

Отписку делайте в `OnDestroy` (`savesManager.Loaded -= OnLoaded;`), иначе NRE после выгрузки сцены. Данные маркеров сериализуйте сами через API сохранений мода — игра их не знает.

## 6. Hover/подсказка по образцу MapMarkerBase

`MapMarkerBase` реализует `IPointerEnterHandler/IPointerExitHandler`:

```csharp
public class MyMarker : MapMarkerBase
{
    protected override void OnAwake() { }
    public override void OnPointerEnter(PointerEventData e)
    {
        transform.SetAsLastSibling();          // поверх остальных
        LabelBackground.gameObject.SetActive(true);
    }
    public override void OnPointerExit(PointerEventData e) => LabelBackground.gameObject.SetActive(false);
}
```

`Title`-сеттер базового класса сам ресайзит фон: `label.GetPreferredValues() + (20, 4)`. Иконку задавайте через `Icon` (`Resource<Sprite>`) — сеттер вызывает `SetNativeSize()` и освобождает старый спрайт; в `OnDestruct` базовый класс делает `sprite.Free()`. Нужен активный `EventSystem` на канвасе.

## 7. Подводные камни
1. **Перепутан порядок lat/lon**: `ToWorldPosition(Vector2)` ждёт `.x=lat, .y=lon`, а возвращает `.x=lon_km, .y=lat_km` — смещение «на полкарты» почти всегда это.
2. **Кэш scene-позиций между запусками**: origin меняется — маркер улетает. Пересчитывайте каждый кадр или в `Loaded`.
3. **Забыт `MaskZ(0f)`** в screen-space: сортировка канваса ломается.
4. **`Update()` вместо ExecutionQueue**: рассинхрон с кадрами игры, мерцание.
5. **Маркер переживает загрузку сейва со старой ссылкой на entity**: пересоздавайте в `SavesManager.Loaded`.
