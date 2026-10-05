# План сопровождаемости RadioNavigation — ADR на каждую фазу + ловля багов

> Версия: 1.0 / 2026-08-19. Статус: утверждён, запись в файл.  
> Режим: hybrid — ADR Proposed (15м) → код 1 коммит → `tests.ps1` → live 2м → ADR Accepted + доки sync (15м).  
> Принцип: **код только под ADR/гипотезу F** — код ради кода не генерим. Best practice точечно, не big-bang.

---

## 0. База (прочитано построчно)

- **Мод:** `UBOAT.Mods.RadioNavigation` v2.1 `Manifest.json:3`, `netstandard2.0` `radionavigation.csproj:3`, `Assembly radionavigation:4`, 34 файла `~4197 LOC` `SAD/03:95`
- **Сборка:** игра компилит `Source/**/*.cs` сама (`UBOAT, UBOAT_2024_1, UNITY_STANDALONE_WIN` без `DEBUG` `AGENTS.md:28`), локально `build.ps1` + `dotnet build` 14 `HintPath $(GameManaged)` `csproj:24` — DLL в корне не используется в рантайме
- **Тесты:** `Tests/Runner` 182 PASS `tests.ps1:12` (20 файлов, 23 линка `csproj:48`), покрыто математика, **0%** `Main`/`SonneController`/`UI`/`Lighthouse*` `GRAPH_REPORT:365` god `SonneController 49`, `IPH 38`, `LighthouseBeamGlow 53`
- **Граф:** `graphify-out/GRAPH_REPORT.md:3` 3473 nodes / 4293 edges / 351 communities от `8b3021fa` — `graphify update .` после каждой фазы
- **Бандл:** `Bundles/testui` 61MB `Unity 2021.3.33` vs игра `2021.3.45` — совместимо `SAD/03`

### Механики под защитой (не ломать)

`тюнинг Dial→Gain→BFO 300-1800` | `пеленг MapBearing→сектор 60°→таблица SonneBeam 60` | `морзе decode` | `DSP carrier→Atmosphere→Receiver → OutputVolume` | `панель Zone/Cursor/Volume/Ground + PlayerPrefs` | `маяк flare/dot/streak/window/spot 6км` | `STAR_SKY` `R=3900<0.9*farClip`

---

## 1. Best practice — 5 групп, применяем точечно (без порождения кода ради кода)

1. **Архитектура:** SOLID по 1 выносу за фазу — `CameraResolver` из `LighthouseBeamGlow.cs:361-493`, `SonneController` god не трогать до щита `Ф1`
2. **Код-стиль:** `readonly struct` где `mutable struct` `StationState.cs:3`, `IsNull()` guard `Main.cs:162`, `volatile` только флаг `ToggleHandler.cs:13` `isRadioOn`, `Clamp` `DspPipeline.cs:101,185`, `DEBUG` лог `Main.cs:120` `#if DEBUG`
3. **Тесты:** AAA `Fact/Theory` как `Runner/Program.cs:66`, `182→184→210` к цели `SAD/11 >50% UI`, каждый прод-коммит — `tests.ps1` green
4. **Доки:** `ADR Proposed→Accepted` `SAD/12_Architecture_Decisions.md` + `docs/adr/ADR-0xx.md` шаблон `docs/DESIGN.md:231` D1-D8, `README/SAD/03/VERIFICATION` sync каждую фазу
5. **Git/Tooling:** `feat/fix(ADR-0xx): …` 1 коммит=1 ADR, `revert` 30с, позже `.editorconfig` + `nullable` `radionavigation.csproj:10` (после щита), `graphify update` AST-only

Не порождаем сейчас: `.editorconfig`, `nullable`, `streak` разделение — high risk, отложены.

### Порядок доки ↔ код

- **Архитектура/камера/аудио:** `ADR Proposed → код → доки Accepted`
- **Тактика 1 строка:** `код+тест → ADR/доки вместе` (дешевле самой строки)
- **Разведка:** `лог-код 30м → ADR → код` (`farClip*0.9:150`, `LerpValue 86.5→97:55`)
- Каждый код-шаг заканчивается `Accepted + SAD/README sync`, иначе `revert`.

---

## 2. Инфраструктура ловли ошибок — 30м (Ф0) — ставится первой

**Флаг:** `PlayerPrefs RadioNav.Diagnostics=1` включает verbose без пересборки бандла  
**Логи (троттл 2с как `DiagnosticInterval 2с:68`):** `SonneController.cs:622` `BEARING mapStationToBoat/mapReciprocal` + `BeamGlow.cs:549` `farClip/distance` + `CameraResolver` `resolving:` + `Main.cs:188` `InstantiatePrefab`  
**Авто:** `powershell -File tests.ps1` + `dotnet build -c Release/Debug` каждый коммит  
**Визуал-чеkлист 5м на фазу:** `M` карта → `Esc` панель → `Zone LKM/RKM` `ZoneHandler.cs:70` → `Ground` `GroundHandler.cs:106` → `Cursor drag` `CursorHandler.cs:46` → `Volume/Feedback` → ночь 22:00 `IsDarkWeather:590` → `Periscope/Binocle` `ResolveDeviceCamera:417` → `MapMode`

**Откат каждой фазы:** `git revert HEAD` 30с + `tests.ps1` зелёные. Фича-флаги `SkipValidation` `Diagnostics` `Resolver off` — без реверта.

---

## 3. Фазы — 7 + 2 отложено (13ч) — легко исполнить, легко ловить

### Ф0 Baseline — 1ч — `ADR-014 Baseline 182 + farClip`

- **Код:** 0 прод. `git tag baseline-182` + `build.ps1` OK + `graphify update`
- **ADR:** Proposed — зафиксировать 182 PASS, `Application.version`, `com.uboat.game.dll` хеш, `farClip` мостик/перископ/бинокль (лог `BeamGlow:549`)
- **Доки:** `README.md:39` `174→182` + `SAD/03:95` loc + `AGENTS.md:40` убрать "No tests"
- **Ловля:** `tests` не 182 → стоп. `GamePath` `build.ps1:2` `C:\Program Files\...UBOAT` проверь до старта
- **Откат:** `git reset --hard baseline-182`

### Ф1 Щит — 2ч — `ADR-015 Test Shield Gain+Geo` — без прод кода

- **Код тестов:** `Tests/SonneController.GainTests.cs` — `gain=1/sqrt(1+Qf²ratio²):507`, `Qf 20→200:53`, `Bfo 300-1800:512`, `dominant gain<0.01→-1` (пинит F203 315 vs 319 0.89) + `GeoMath.MapBearing 179→-179 270°:52` `Haversine 222км` `SAD/11`
- **Best:** AAA `Fact/Theory` `Runner:66`, `182→184`
- **Ловля:** `Theory` падает → Ф2 не едет. Live не нужен
- **Доки:** `SAD/11` покрытие `182→184`

### Ф2 Данные — 1ч — `ADR-016 Data Accuracy` — пеленг

- **Код:**
  - `Bushmills.json:616` `89.9:57→53` 5м — low risk, `FindClosest 89.95→53 dots` `SonneBeam.cs:121`
  - `GeoMath.cs:52` `dLon wrap ((d+540)%360)-180` 10м — фикс антимеридиана `GeoMathTests 179→-179` `Haversine` тоже
  - `ConsolCycleConfig.cs:76` `Cycle60_5s` Lugo 60.5с `dossier:152` 10м — `Abs<0.01` → новая константа
  - `BeaconData.cs:188` `Lugo 270→268.5` + `223` `Seville 261→263` 30м — medium, `Sector 18/24 знаков`, требует `Cap59` согласия
- **Доки:** `SAD/03` `BeaconData:71`, `VERIFICATION`, `dossier` сверка
- **Ловля live 2м:** `55.2N 6.5W` ночь `FRQ 257` `Dots 53` монотонно `SonneBeamTests`; `10N 179E` `BEARING 270°` не 90°
- **Откат:** 1 строка `89.9` или `dLon wrap` по отдельности

### Ф3 Маяк-утечки — 1.5ч — `ADR-017 Lighthouse Leaks` — low визуал

- **Код:** `BeamGlow.cs:74 ResetStatics Destroy(dotTexture/streakTexture)+onPreCull-= :476` + `Main.cs:38` рядом с `Placer.Reset()` + refCount `instanceCount` (не чистить пока 1 glow жив) — `Placer:45 Grace 600` keep, `FindChildRecursive:457` `maxDepth 8`
- **Best:** `hideFlags DontSave:790,845` уже, `Destroy` в `OnDestroy:324` дополнить
- **Ловля:** 3× save/load → `Texture2D Count` не растёт, нет дубля `glow camera:` лога. Если flare пропал → `revert`
- **Доки:** `SAD/03 LighthousePlacer:50` + `F42`

### Ф4 Камера — 2.5ч — `ADR-018 CameraResolver` — медиум, блокирует звезду

- **Код:** новый `Source/Logic/CameraResolver.cs` `TryResolve main→Periscope:417→hook:383→FindScreen:457` + `IsUtility:444` + `IsMapMode` `CameraMode.Map` `STAR_SKY:77` + `SafeRadius Min(3900, farClip*0.85):150` — `BeamGlow:136` и `StarSkyRenderer` → `Resolver`
- **Ловля live 3м:** `Mостик→Periscope→Binocle` лог `PeriscopeCamera rt=True`, `R 3900 < farClip`, dot в перископе не улетает `191` (был tex-coords баг), `Map` скрывает StarSky не маяк-точку `StationMapOverlay:145`
- **Доки:** `SAD/02` + `STAR_SKY:76` `R=3900` общий
- **Откат:** вернуть инлайн `ResolveCamera`, флаг `Resolver off`

### Ф5 Панель — 1ч — `ADR-019 Panel Hardening` — зона/панель

- **Код:** `Main.cs:197 → PrefabNames.Cursor/Toggle/...:5` + `SkipValidation` flag `Warning` вместо `Destroy:207` + `PanelStateStore.cs:17 guard if !initialized return` `Clamp zone 0-4/angle 0-140` + `Toggle volatile isInteractable:13`
- **Best:** `MainSceneViewportPath "/GUI/Viewport":21` single-source `F176`, `InjectIntoAssembly:139` idempotent guard `diInjected`
- **Ловля live 2м:** удалить 1 путь → `Missing:` но панель открылась при флаге; `Zone LKM 1→2` + `Ground Active→Toggle` `GroundHandler:106` + `Esc` `RadioHandler:89` `IsChildOf` F133
- **Доки:** `docs/DESIGN.md:110` `requiredChildren` 9 путей, `SAD/06`

### Ф6 STAR_SKY log — 1ч — `ADR-020 StarSky Phase0` — 0 риска

- **Код:** 0 прод, только лог `LerpValue 86.5→97:55` `Weather.Sky.SunZenith`, `Shader Sprites/Default`, `farClip`, `Sun/Moon` vs Stellarium 3 даты `STAR_SKY:10` + `Tools/StarCatalogConverter/` вне `Source` `STAR_SKY:5`
- **Доки:** `STAR_SKY_PLAN` вердикт Луна → `SAD/12`
- **Ловля:** сверка `weather.Sky.MoonAltitude` ошибка `~0.3°` → своя Луна `ELP2000:249`

### Отложено (не генерим пока щит не зелёный)

- **Ф7 5ч** `DspPipeline.cs:61 xorshift` 1ч + `double-buffer Gain:509` 4ч — `smoothing 0.1:34`, `silentClip mono:17`, `qsbPhase wrap:89` — low R на x86, чинить после Ф1
- **Ф8 3ч** `streak` пайплайн `BeamGlow:212` `DotOnly vs Streak` `GetHorizonAngleDeg:855` — high risk крен/горизонт, отложен

---

## 4. Порядок исполнения — зависимости

```
Ф0 Baseline ─┬─→ Ф1 Щит ─┬─→ Ф2 Данные (Ф2.1 5м → Ф2.2 45м)
             │          ├─→ Ф3 Утечки ─→ Ф4 Камера ─→ Ф6 Log
             │          └─→ Ф5 Панель
             └─→ (параллельно) Ф6 после Ф4
Ф7/Ф8 после Ф1 зелёного
```

Каждый фаза — 1 PR, `graphify update`, `tests.ps1`, live 5м, `ADR Accepted + README/SAD sync`.

---

## 5. Открытые решения (зафиксировать в ADR-014)

- ADR язык `RU` + хранить `SAD/12` и `docs/adr/ADR-0xx.md` дубли (SAD — сводка, docs — полный)
- Старт: `Ф0→Ф1→Ф2.1` 3.5ч до `STAR_SKY log` (безопасный) vs `Ф0→Ф2.1` 35м быстрый — выбрано `Ф0→Ф1→Ф2.1` (щит окупает 15м)
- `Lugo 268.5 / Seville 263` правим только после `Ф1` зелёного

---

## 6. Следующие действия (после записи плана)

1. `git tag baseline-182` + `powershell -File tests.ps1` артефакт `182 PASS`
2. Создать `docs/adr/ADR-014-baseline.md` из шаблона `DESIGN D1:231`
3. `Ф1` — добавить 2 `Theory` `Gain/MapBearing` → PR → `Ф2.1` 1 строка `Bushmills 89.9`

---

*План утверждает: hybrid доки→код, best practice точечно, код только под ADR/F, каждая фаза ловится за 2м live + `tests`.*
