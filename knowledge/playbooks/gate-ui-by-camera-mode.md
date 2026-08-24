# Рецепт: Показ мод-UI только в правильном режиме камеры/игры

Источник: `UBOAT.Game.Scene.Camera.MainCamera`, `UBOAT.Game.Scene.CameraMode`,
`UBOAT.Game.UI.GameUI` (исходник игры, проверено 2026-08-24).

## 1. Два независимых гейта

Видимость мод-панели определяют **два** режима, и они меняются несинхронно:

- `MainCamera.Instance.CurrentMode` — `CameraMode` ([Flags]): что рисует камера.
- `GameUI.CurrentMode` — `GameUI.Mode`: какой набор UI показан.

Карте-подобные панели игра требует **оба** гейта сразу (`MapUI.OnEnable`,
GameUI.cs:473): `mainCamera.CurrentMode == CameraMode.Map && gameUI.CurrentMode == GameUI.Mode.Map`.

## 2. Таблица режимов

| Задача мода | CameraMode (MainCamera) | GameUI.Mode |
|---|---|---|
| Обычный вид с разрезом лодки | `SectionView = 1` | `Normal` |
| Карта (свои маркеры/оверлеи) | `Map = 4` | `Map` |
| От первого лица | `FPP = 8` | `FPP`, `FPPEx` |
| Прибор крупным планом | `Generic = 0x10` | `DeviceManualMode` |
| Свободная камера | `FreeFly = 0x20` | любой из обычных |
| От третьего лица | `TPP = 0x40` | `Stage`, `FPPEx` и др. |
| Диалог/катсцена | `Dialogue = 0x80` | `Dialogue`, `StoryPlayerUI` |

Прочие `GameUI.Mode`: `Headquarters`, `Disabled`, `SoftDisabled`, `MapCutscene`,
`FullscreenUI`, `FullscreenHighBarUI`, `PortActivitiesUI`. Прочие флаги
`CameraMode`: `Transition = 2` (устаревший, бросает исключение), `All = 0xFF`.
`CameraMode` — маска: проверяйте `(CurrentMode & CameraMode.Map) != 0` при
комбинированных состояниях; для точного сравнения — `==`.

## 3. Поллинг (просто и надёжно) vs события

Так делает сама игра (`TorpedoCalculatorUI.cs:148`, `MapUI.cs:473`) — `SetActive`
в `Update`/`OnEnable`. Дёшево: сравнение enum каждый кадр.

```csharp
[Inject] private static MainCamera mainCamera;
[Inject] private static GameUI gameUI;

private void Update()
{
    bool show = mainCamera.CurrentMode == CameraMode.Map
             && gameUI.CurrentMode == GameUI.Mode.Map;
    if (panel.activeSelf != show)
        panel.SetActive(show);
}
```

Событийный вариант — реакция в момент смены, без кадра задержки:

```csharp
private void OnEnable()
{
    mainCamera.ModeChanged += Refresh;   // event Action
    gameUI.ModeChanged += Refresh;       // event Action
    Refresh();
}

private void OnDisable()
{
    mainCamera.ModeChanged -= Refresh;
    gameUI.ModeChanged -= Refresh;
}

private void Refresh()
{
    panel.SetActive(mainCamera.CurrentMode == CameraMode.Map
                 && gameUI.CurrentMode == GameUI.Mode.Map);
}
```

Выбор: поллинг — для панелей, где важна только видимость (идиоматично для игры);
события — когда нужно выполнить действие один раз на переход (открыть/закрыть,
подписаться). Учтите: `MainCamera.ModeChanged` летит после завершения фейда
(`OnModeChanged`), а не при вызове `SetMode`; `RefreshCurrentMode()` переэмитит
его без смены режима.

## 4. Путь DeviceManualMode (приборы)

Полноэкранный UI прибора показывается, когда активен его контроллер:
`CameraMode.Generic` + `GenericController.ExternalController is МойКонтроллер`
(паттерн `BasicDeviceController`). Контроллер регистрируется через
`SetExternalCameraController(IExternalController, PlayableCharacter, ...)`;
прямой `SetMode(CameraMode.Generic)` запрещён. `BasicDeviceController.StartController`
сам ставит `gameUI.SetMode(GameUI.Mode.DeviceManualMode)` и запоминает прежний
режим, `StopController` восстанавливает. Своему UI достаточно поллинга:

```csharp
private void Update()
{
    bool show = mainCamera.CurrentMode == CameraMode.Generic
             && mainCamera.GenericController.ExternalController is MyDeviceController;
    panel.SetActive(show);
}
```

## 5. Приглушение звука под полноэкранным UI

Игра глушит интерьерный звук под любым полноэкранным интерфейсом (`BoatManagementUI`,
`OverviewUI` и др.) через `AudioController.SetFullscreenInterfaceMode(true)`
(AudioController.cs:220: твэн интерьерного модификатора к 0.18 за 1.5 с, назад к 1
за 1 с). Для модальной панели мода:

```csharp
[Inject] private static AudioController audioController;

private void Show()
{
    audioController.SetFullscreenInterfaceMode(value: true);
}

private void Hide()
{
    audioController.SetFullscreenInterfaceMode(value: false); // обязательно вернуть
}
```

## 6. Подводные камни

- **Прямой `SetMode(FPP/TPP/Generic)` пишет `Debug.LogError`** со стеком
  (MainCamera.cs:1116) — используйте `SetFPPController` / `SetTPPController` /
  `SetExternalCameraController`.
- **`IsInTransition => !transitionTargetMode.Equals(mode)`** (MainCamera.cs:476):
  во время фейда `CurrentMode` ещё старый. Не телепортируйте UI/логику по
  `CurrentMode` внутри перехода; ждите `ModeChanged` или `SetModeAsync`.
- `AllowedModes` может отсечь запрос молча: если бит режима не разрешён,
  `SetMode` просто ничего не сделает.
- События — multicast `Action` без аргументов: читайте `CurrentMode` заново в
  хендлере; отписывайтесь в `OnDisable`/`OnDestroy` (статические singleton'ы
  переживают сцену).
- `GameUI.SetMode` игнорирует повторную установку того же режима (`uiMode != this.uiMode`),
  так что «лишних» событий `ModeChanged` не будет — но и пинка при рестарте тоже.
