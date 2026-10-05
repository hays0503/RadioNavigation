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

## 7. PIP и активность главной камеры (проверено 2026-10-03)

- `AdditionalCameraData.AttachedCameras` — `List<UnityEngine.Camera>` (публичный
  getter); в этой сборке список может быть null. Это **маршрут рендера**, а не
  только список для воды: `DeepPipeline.RenderDeferredCamera` рекурсивно вызывает
  `Render(context, list)` для каждого attachment без проверки `Camera.enabled`.
  Проверено по декомпиляту `com.dws.pipeline.Runtime.dll` и живыми счётчиками.
- Камера одновременно в `AttachedCameras` и с `enabled=true` рисуется дважды:
  attachment + внешний список Unity. Родной оконный перископ использует
  `periscopeCamera.enabled=false` + attachment (`Periscope.FullScreenView`,
  `UBOAT.Game.Scene.Items/Periscope.cs:569–583`). Ручной `Camera.Render()` для
  этого маршрута не нужен.
- Для PIP мода отключать standalone можно **только** при фактическом членстве
  конкретной камеры в списке и `rootCamera.isActiveAndEnabled`. При выключенном
  главном виде или отсутствующем attachment сохранять standalone-fallback.
  Проверять это при смене camera mode: корень может выключиться при открытом UI.
- `PostProcessLayer.ResetHistory()` сбрасывает истории PPv2. Игровой
  `GameCamera.ResetTemporalEffectsNow()` дополняет его присваиванием
  `AdditionalCameraData.TemporalEffectsResetRequested=true`. Для открытия PIP
  применять к своим двум камерам до первого рендера, не сбрасывать главный вид.
  Один сброс при сохранённом двойном маршруте не устранил смаз первого кадра.
- Рецепт и живая приёмка: `SEXTANT_PLAN.md` §16.24. 24 записанных открытия
  нового маршрута на 30 fps чистые; старый путь даёт положительный контроль.
   Это не замена отдельной приёмке внешнего звёздного поля.

## 8. CullingScissorRect и интерьерный PIP (проверено RenderDoc 2026-10-04)

- `AdditionalCameraData.CullingScissorRect` — **не пиксельные ножницы**:
  `DeepPipeline.CameraSettings.Initialize` сужает им четыре боковые плоскости
  `ScriptableCullingParameters` **до** `context.Cull`. Отключение
  `Camera.useOcclusionCulling` не отменяет это геометрическое отсечение.
- `ExteriorViewPortalsController.PreCull` задаёт этот rect по viewport bbox
  внешних порталов (`:106–109`), либо полный `(0,0,1,1)` (`:40–43`). Это
  ограничение внешнего writer, не всей интерьерной сцены.
- Корневой интерьерный PIP single-writer не должен наследовать portal rect
  основной камеры. В живой сцене скопированный X=0.7284 отсекал AABB лампы
  с viewport X=0.4533–0.5399 ещё до submission. Большие меши стен пересекали
  frustum и оставались видимыми, маскируя ошибку.
- Изолированный A→B→A′ (только rect PIP, поза/FOV/маски/main без правок):
  Lamp draws в 1267² target **0 → 12 → 0**, в main **1 → 1 → 1**; лампа на
  финальной PIP RT отсутствует → видна → отсутствует. Меш подтверждён теми
  же VB/IB и SHA-256 IB. Это причинный эксперимент до продуктовой правки.
- Доказательства: `%LOCALAPPDATA%\Temp\opencode\captures\lamp-mesh\LAMP_CULLING_FINDING.md`
  и `lamp_scissor_frame5980/16201/19323.rdc`; сравнение `lamp-culling-ABA.png`.
- **Исправлено 2026-10-04:** `SextantInteriorMirror.Sync` вместе с включением
  корневого интерьерного writer задаёт `(0,0,1,1)`, строго после выхода для
  `!inside`. `SextantPipFactory.SyncRenderScene` по-прежнему зеркалирует rect
  основной камеры на обе PIP перед интерьерным override, поэтому внешний
  путь и основная камера не меняются. Поза/наклон алидады не затронуты.
- Игровая перекомпиляция **без зонда**, RTX 4080 SUPER:
  `lamp_scissor_productfix_frame6082.rdc`, Lamp PIP **12**, main **1**,
  тот же SHA-256 IB; лампа видна в цветовой PIP RT и в полном UI-композите.
  Гейт `SextantPip_InteriorWriterUsesFullCullingViewport` защищает реальную
  запись, inside-only scope, порядок и восстановление внешнего rect;
  **8/8** мутаций пойманы. Детали приёмки и её границы — `SEXTANT_PLAN.md` §16.25.
