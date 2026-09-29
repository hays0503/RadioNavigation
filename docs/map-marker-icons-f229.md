# Иконки маркеров карты: разблокировка через бандл (F229)

## Симптом

Все три семейства маркеров карты (маяки, станции, фикс) рисовались **пустыми белыми
квадратами**: `Sprite` иконки резолвился в `null`, а точка получала дефолтный спрайт.

До фикса в логе были ровно эти три строки (по одной на семейство, каждая — один раз
за сессию из-за `iconWarned`):

```text
[RadioNav][Map] lighthouse icon 'IconLighthouse' not found in bundle (prefab Image/SpriteRenderer or Sprite); marker icon will be blank
[RadioNav][Map] station icon 'IconSonne'/'sone' not found in bundle; station marker icons will be blank
[RadioNav][Map] fix icon 'IconSextan' not found in bundle; fix marker icon will be blank
```

## Две причины (наложились друг на друга)

### Причина 1 — `ResourceManager` не видит бандл мода (F229)

Каждый `LoadIconSprite()` сначала спрашивал иконку у игры —
`resourceManager.RetrieveAsset<GameObject>(...)`, затем `RetrieveAsset<Sprite>` /
`RetrieveAsset<Texture2D>`. На 2026.1-patch21 игра не регистрирует бандл мода в
`ResourceManager`, поэтому каждый такой вызов деградирует до `Resources.Load` и для
ассетов мода возвращает `null` (тот же корень, что у непрозрачного материала окуляра
и шейдера звёзд). Обходной путь — прямой `AssetBundle.LoadAsset` из уже загруженного
бандла (`ModBootstrapper.LoadBundleAsset`) — существовал, но иконки им не
пользовались.

### Причина 2 — контейнерные ключи вида `Sprite/<имя>`

Даже `ModBootstrapper.LoadBundleAsset` сначала промахивался: он пробовал **голое имя**
(`IconLighthouse`) и **путь проекта** (`Assets/Packages/testui/IconLighthouse`), а Unity
называет записи бандла по корню сборки. Проверено UnityPy по `Bundles/testui`:
контейнерные ключи иконок —

```text
Sprite/IconLighthouse
Sprite/IconSonne
Sprite/IconSextan
Sprite/sone
```

Голое `LoadAsset("IconLighthouse")` их не находит — нужен точный контейнерный ключ.

## Фикс

1. `Source/Logic/ModBootstrapper.cs:510` — `LoadBundleAsset` получил третий шаг:
   после голого имени и пути проекта он читает `AssetBundle.GetAllAssetNames()` и
   ищет запись по **листу имени** (`BundleLeafMatches`, `Source/Logic/ModBootstrapper.cs:568`),
   после чего грузит по найденному контейнерному ключу. Лист сравнивается
   без учёта регистра, с допуском расширения с любой стороны (`IconSonne` ↔
   `Sprite/IconSonne.prefab`).
2. `Source/UI/NavMapMarkerBase.cs:680` — новый общий
   `LoadBundleIconSprite(prefabName, spriteName)`: префаб → первый UI `Image`
   (включая неактивных детей) → `SpriteRenderer`; если задано голое имя суб-ассета —
   напрямую `Sprite`, иначе `Texture2D` + `Sprite.Create`. Все загрузки идут через
   `ModBootstrapper.LoadBundleAsset`.
3. Все три семейства вызывают хелпер **до** предупреждения-последнего-шанса:
   `StationMapMarker.cs:90`, `LighthouseMapMarker.cs:132`, `FixMapMarker.cs:217`.

Итог: маркер больше не остаётся белым, если иконка есть в бандле, — даже при
сломанном `ResourceManager`.

## Гейт

`RadioNavigation.SourceCheck/SourceCheckTests.cs:5076` —
`MapMarkerIcons_ResolveThroughBundleFallback`. Гейт требует, чтобы каждый
`LoadIconSprite()` трёх семейств шёл через `ModBootstrapper.LoadBundleAsset` (напрямую
или через `LoadBundleIconSprite`), а сам хелпер действительно вызывал `LoadBundleAsset`.

**Мутационная проверка:** удаление вызова хелпера из `FixMapMarker` → FAIL,
возврат строки → PASS.

## Живая приёмка (2026-11-09)

После перезапуска игры (источники новее `Temp\radionavigation.dll`, штатный путь
компиляции):

- в логе **ноль** строк `not found in bundle` / `will be blank`;
- все три спрайта иконок непустые;
- скриншот карты показывает реальные иконки маяка/станции/фикса вместо белых квадратов.

## Гейты

`tests.ps1` — **1205 PASS / 0 FAIL**; `RadioNavigation.SourceCheck` — **80/80**
(было 79; новый гейт выше); build + PVS clean.
