# ADR-027: Собственный маркер маяка на карте

> Статус: Accepted (2026-08-22)  
> Связи: `Source/UI/LighthouseMapMarker.cs`, `Source/Logic/LighthousePlacer.cs`, `Source/UI/StationMapOverlay.cs`, `Source/Logic/Main.cs`

## Решение

- Маркер маяка на карте — **собственный компонент** `LighthouseMapMarker` (world-space Canvas + Image + TextMeshProUGUI), а не игровой `EntityMarkerUI`.
- Маркер — прямой ребёнок `EntityMarkersManager.transform` («Entity Markers»): тот же canvas-контекст (порядок рендера над картой, наследуемый масштаб — наблюдаемый `lossyScale` менеджера 1500), что и у портовых маркеров.
- Сущность маяка держится `DisplayInUI=false` / `IsPublic=false` (обе ветки плейсера) — игра не создаёт свой маркер с иконкой Unknown.
- Размер иконки — **пиксельный, зависит от зума**: 1× база = 25 px, линейно до 50×50 px на ближнем зуме (`CameraElevation` 1000 → 1400000, `Mathf.Lerp(50, 25, t)`). Масштаб пересчитывается каждый кадр:
  `unitsPerPixel = 2·elevation·tan(fov/2)/Screen.height` (FOV-fallback 60°),
  `scale = iconPx·unitsPerPixel / (IconSize·max(parentLossy, 0.0001))`.
- Гейт видимости: маркер показывается, когда `CurrentMode == Map`, камера активна и режим не `Headquarters` — на любом зуме (в отличие от игровых маркеров, скрываемых ниже 500 м высоты камеры).
- Маркер поднят над поверхностью карты: `y = 1.5 + elevation * 0.0005 + SpriteLiftY`, где `SpriteLiftY = 100` (формула `MapPointUI.UpdatePoints` + 100 unit лифт).
- Иконка лежит **горизонтально карте** (`Quaternion.Euler(90,0,0)`), не билборд.
- Спрайт грузится из бандла как prefab (`IconLighthouse`): `Image.sprite` или `SpriteRenderer.sprite`, фолбэк — прямой `RetrieveAsset<Sprite>`.
- Ховер → тултип с именем; клик → пин «имя · характеристика» (из `LighthouseProfile`); повторный клик — обратно; при уходе курсора скрывается, если не запинен.
- Тултип: `TextMeshProUGUI` (шрифт `TMP_Settings.defaultFontAsset`, фолбэк — динамический Arial), фон-Image по `GetPreferredValues() + padding (20, 4)`, отступ над иконкой 40, `fontSize 30` (≈12 px при 25 px иконке).
- За чёрным глифом — светлая круглая подложка (радиальный фоллофф-текстура 64×64, генерируется один раз, размер 80); невидимый hitbox 96 для ховера/клика.
- Раз в секунду сносятся стейловые игровые `EntityMarkerUI` для сущностей `Lighthouse *` (старые сейвы с флагами true).
- Диагностика: раз в 2 с лог `entityPos / worldPos / elevation / scale / px`.

## Причина

- Игровой путь иконок (`EntityMarkerUI.RefreshIcon` → `"Icons/Map/" + IconOverride`) не может адресовать спрайт мод-бандла: ассеты бандла регистрируются под голыми именами (`IconLighthouse`), а игра всегда добавляет префикс `Icons/Map/` — попытка с `IconOverride` давала fallback `Unknown`.
- Вотчер на игровом маркере (`marker.Icon = RetrieveAsset<Sprite>`) не работал: ассет — prefab, не Sprite → невалидный `Resource<Sprite>` → сеттер `MapMarkerBase.Icon` прятал маркер.
- Свой компонент даёт полный контроль: видимость на всех зумах, пиксельный размер, тултип, без борьбы с `RefreshIcon`.
- Родительство под `EntityMarkersManager` (а не его parent) — единственный рабочий вариант: сиблинг с собственным Canvas рендерился **за поверхностью карты** (порядок сортировки), а масштаб поддерева менеджера (lossy 1500) требовал компенсации, которая ломала размер.

## Проверка

- Build: 0 ошибок; тесты: 232 PASS.
- В игре: иконка видна на всех зумах (25→50 px), над картой, тултип имя/характеристика по ховеру/клику.

```json
{"id":"ADR-027","decision":"Собственный world-space маркер маяка под EntityMarkersManager","reason":"Игровой EntityMarkerUI/IconOverride не адресует спрайты мод-бандла","fallback":"Пустой Image при отсутствии спрайта в бандле (лог-предупреждение)"}
```