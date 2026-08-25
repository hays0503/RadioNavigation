# SEXTANT_PLAN — Секстант для RadioNavigation (PIP-две камеры, всё программно)

> Версия: 1.0 (2026-08-25). Статус: план готов к исполнению в новой сессии.
> Цель документа: реализовать в новой сессии без повторного исследования.
> Спецификация согласована с автором (2026-08-25, см. §2 — все решения зафиксированы).

---

## 1. Цель и механика

Секстант — инструмент измерения высоты светил. Реализация полностью программная
(без 3D-моделей и без пересборки бандла) → просто тестировать и внедрять.

Механика (по автору):
1. Игрок жмёт **Ctrl+Z** → экран темнеет (α≈0.85), в центре — «окуляр» с картинкой.
2. Картинка в центре = **PIP второй камеры** (render-to-texture). В реальном
   секстанте вторая ветка — зеркало; у нас — вторая камера, поворачиваемая по
   pitch на угол секстанта (мировой pitch, 0 = горизонт).
3. Основной вид (по краям затемнения) — живой горизонт из основной камеры.
4. **Стрелки Вверх/Вниз** — увеличивать/уменьшать угол секстанта.
5. Игрок совмещает звезду (в PIP) с горизонтом (основной вид) → показанный угол
   = измеренная высота светила. Угол мировозависим от наклона основной камеры —
   по построению НЕ зависит (PIP pitch мировой) → аутентично.
6. Слева на тёмной зоне — белым текстом текущий отсчёт.
7. **Фильтры 1/2/3** (клавиши 1/2/3 при открытом секстанте) против ослепления
   солнцем; пересвет зависит от интенсивности солнца (сезон/время суток — из
   живой высоты солнца игры). При засвете — сообщение «глазам больно».

---

## 2. Зафиксированные решения (согласовано с автором)

| # | Вопрос | Решение |
|---|--------|---------|
| 1 | Открыть/закрыть | **Ctrl+Z** (Z один = созвездия — см. §7.4 фикс конфликта) |
| 2 | Дуга | **0–130°**, кламп с обеих сторон |
| 3 | Управление углом | Стрелки Вверх/Вниз, **плавно 2°/с**, Shift ×5 (=10°/с) |
| 4 | Отсчёт (дискрета) | **0.2′ = 12″** (1/300°) — отображение и хранение квантуются |
| 5 | Формат отсчёта | Морской: `45°23.4′` (градусы + минуты, 1 знак) |
| 6 | Труба (PIP FOV) | FOV основной / **3.0** (оптика Плат 2.5–3.5×, середина) |
| 7 | Фильтры | Клавиши **1/2/3** = 1/2/3 фильтра, **0** = снять все; сброс в 0 при открытии |
| 8 | Пересвет | Модель §6: интенсивность солнца (по высоте) × гауссиан угла до солнца; белая заливка PIP α=glare |
| 9 | Ослепление | glare > 0.6 → сообщение «НЕ МОГУ, ГЛАЗАМ БОЛЬНО — УСТАНОВИТЕ ФИЛЬТР», ID светил заблокирован; гистерезис выход 0.5 |
| 10 | Идентификация звезды | Звезда в центре PIP (< 0.5°) непрерывно **5 с** → рядом с углом пишется **имя** (из name.fab, 57 навигационных) |
| 11 | Солнце как цель | Центр PIP на солнце + glare ≤ 0.6 → имя «Солнце» (дневной замер) |
| 12 | Персистентность | Не нужна: угол сбрасывается в 0 при открытии, фильтры сбрасываются в 0 |

---

## 3. Проверенные факты — ИГРА (не перепроверять)

Все факты верифицированы в сессиях 2026-08-24/25 (дампы, чтение исходников,
логи игры). Полные таблицы: `knowledge/` (reference `uboat-kb`), Q&A-корпус
`knowledge/qa/`, хоткеи `knowledge/inventory/game-hotkeys.md`.

### 3.1 Ввод
- `GameInput` (UBOAT.Game.Core.Input): `[Inject] static GameInput gameInput;` —
  паттерн всей игры. `AddBlockade(object)` / `RemoveBlockade(object)` /
  `HasActiveBlockades()` — глушит игровую обработку ввода (зум на стрелках).
- Прецедент мод-хоткея: `Input.GetKeyDown(KeyCode.F7)` в
  ChronometerOverlayHandler.cs:92 (хронометр). Наш секстант: **Ctrl+Z**.
- Занятые клавиши игры (полная таблица `knowledge/inventory/game-hotkeys.md`):
  F4 телеграф, F5 глубиномер, F6 аксиометр, F7 хронометр, F8 Quick Save,
  F9 Quick Load, F10 Capture Screenshot, **F11 bug report dialog** (хардкод,
  НЕ InputAxis — в дамп осей не попадает!), Space, G, J, 1, M, B, H, U, K, L,
  Q, E, C, V, R, N, F, W/A/S/D, Tab, Esc, Return, BackQuote, Ctrl, Shift,
  PageUp/Down, +/=/-, Keypad0, 2..9 (персонажи), Mouse0/1/2, стрелки (Zoom).
- **Свободно**: Z (занят нашим тумблером созвездий), Ctrl+Z (секстант),
  F12 (но это скриншот Steam — избегать), буквы I/O/P/T/X/Y.

### 3.2 Камеры и оверлеи
- Основная камера: `CameraResolver.TryResolve(out cam)` (мод), `cam.fieldOfView`.
- PIP-камера рендерит мир (звёзды — билборды на слое Default вокруг
  `cam.transform.position + dir·3900`, см. StarsMesh.cs:134) — вторая камера
  **в той же точке** видит их автоматически. Skybox в PIP = игровое небо
  (аутентично), звёзды аддитивные поверх.
- RT: `new RenderTexture(512, 512, 24)`; `pipCamera.targetTexture = rt`;
  включать только при открытом секстанте (экономия).
- Оверлей: screen-space overlay Canvas (sortingOrder ~100), тёмный Image,
  PIP `RawImage` (texture = RT) с радиальной альфа-маской (процедурная
  текстура — паттерн `NavMapMarkerBase.GetBackdropSprite()`).
- Шрифт: `NavMapMarkerBase.GetTooltipFont()` — **public static** (открыт
  2026-08-25 для переиспользования).
- Полноэкранный режим устройства НЕ нужен: оверлей поверх геймплея
  (IExternalController/DeviceManualMode — не наш путь).

### 3.3 Ввод-блокада и фокус
- При открытии: `gameInput.AddBlockade(this)` — глушит зум игры на стрелках.
- При закрытии/OnDestroy: `RemoveBlockade(this)`. Пропустить Remove → блокада
  навсегда (частый баг — тестировать закрытие).

### 3.4 Небо и светила
- `[Inject] static Weather weather;` (паттерн LighthouseBeamGlow.cs:72) →
  `weather.Sky` = TOD_Sky: `SunDirection` (Vector3), `SunAltitude`,
  `SunZenith`, `LerpValue`, `IsNight`; `weather.Sky.Atmosphere.Fogginess`.
- **ВНИМАНИЕ (§7.2 исполнения): знак `SunDirection` не верифицирован** —
  днём вектор к солнцу должен иметь +Y; проверить дотом с up, при
  отрицательном — инвертировать. Фолбэк: `weather.Sky.SunAltitude` для
  интенсивности + направление из SunAzimuth/SunAltitude вручную.
- Звёзды: `StarCatalog.Stars` — {Hip, RaRad, DecRad (экватор 1942), Vmag, Bv,
  IsNav}. Направление в момент t: `AstroMath.RaDecToEnu(raRad, decRad, lstDeg,
  latDeg, out alt, out az, out e, out n, out u)`; dir = (e, u, n) normalized;
  alt > 0 = над горизонтом. lstDeg = `AstroMath.LstDeg(AstroMath.ToJulianDay(
  GameTimeResolver.GetUtcNow()), lon)`; lat/lon — из провайдера позиции.
- Видимость звёзд (fade): `SkyVisibilityResolver.GetVisibility()` — тот же
  альфа-множитель на слой (днём звёзды невидимы — секстант днём меряет
  солнце, ночью звёзды).
- Имена 57 навигационных звезд: список в STAR_SKY_PLAN §3.7; HIP↔имя —
  `C:\code\projects\stellarium\stars\hip_gaia3\name.fab` (формат строк
  сверить при чтении: ожидание `Имя|HIP`).

### 3.5 Мод-инфраструктура
- Игра сама компилирует `Source/*.cs` (Roslyn, ~13 с при запуске); **язык
  C# 7.0-safe** (локальный csproj LangVersion=latest — НЕ ориентир).
- `Main.cs` (OnSceneStart, Main Scene, /GUI/Viewport): компоненты добавляются
  после `ConstellationToggleButton` — добавить `SextantController`.
- Тесты: `powershell -ExecutionPolicy Bypass -File tests.ps1` (xunit, свой
  Runner); **283 PASS** база. `Tests/RadioNavigation.Tests.csproj` —
  EnableDefaultCompileItems=false → новые файлы добавлять руками
  (`<Compile Include>` + `<Compile Include="..\Source\..." Link>`).
- PlayerPrefs-стаб в тестах есть; PlayerPrefs-ключи мода: префикс `RadioNav.`.
- AGENTS.md содержит mojibake — правки только по точному тексту из grep.
- Пересборка бандла НЕ нужна (всё программно).

---

## 4. Проверенные факты — МОД (не перепроверять)

- `ConstellationMesh.cs`: `ToggleKey = KeyCode.Z`, `SetEnabled(bool)` static,
  `EnabledChanged` event; **требуется фикс**: Z при зажатом Ctrl должен
  игнорироваться (иначе Ctrl+Z тумблит созвездия) — см. §7.4.
- `NavMapMarkerBase.GetTooltipFont()` — public static TMP_FontAsset
  (TMP_Settings.defaultFontAsset → Arial fallback).
- Радиальный градиент-спрайт: паттерн `NavMapMarkerBase.GetBackdropSprite()`
  (Texture2D 64², falloff (1−r)², Sprite.Create, HideFlags.DontSave).
- `GameTimeResolver.GetUtcNow()` — UTC времени кампании.
- `SkyVisibilityResolver.GetVisibility()` — общий fade слоя (день → 0).
- Позиция лодки: `GamePlayerPositionProvider.TryGetPosition` — валиден только
  при `Sandbox.Instance != null` (гейт a23d24b); секстанту позиция не нужна
  (угол мировой), но lat/lon пригодятся для лога замера.
- Лог-префикс мода: `[RadioNav]`; стиль логов — см. соседние компоненты.
- Коммит-конвенции: conventional, subject ≤50, тело когда «почему» не очевидно.

---

## 5. Архитектура файлов

```
Source/
  Logic/
    SextantArc.cs        [НОВЫЙ] чистая математика дуги (кламп, квант 0.2′)
    SextantGlare.cs      [НОВЫЙ] чистая модель засвета (формула §6)
  UI/
    SextantController.cs [НОВЫЙ] весь секстант: оверлей, PIP-камера, ввод,
                         идентификация, фильтры, сообщение о засвете
    ConstellationMesh.cs [ПРАВКА] Z игнор при зажатом Ctrl (§7.4)
    NavMapMarkerBase.cs  [без правок] GetTooltipFont уже public
  Logic/Main.cs          [ПРАВКА] AddComponent<SextantController>()
Data/
    NavStarNames.cs      [НОВЫЙ, генерируется] HIP → имя (57 навигационных)
Tests/
    SextantArcTests.cs   [НОВЫЙ] +6
    SextantGlareTests.cs [НОВЫЙ] +5
    RadioNavigation.Tests.csproj [ПРАВКА] +4 строки Compile/Link
```

---

## 6. Формулы

### 6.1 Дуга и отсчёт
```
Clamp(a)       = clamp(a, 0, 130)
Quantize(a)    = Math.Round(a * 300) / 300          // 0.2′ = 1/300°
Display(a)     = deg = floor(q); min = (q − deg)·60 // "45°23.4′"
```

### 6.2 Засвет (SextantGlare.Glare)
```
sunIntensity = clamp01(SunAltitude / 60)              // сезон/время — внутри живой высоты
angleDeg     = угол между forward PIP и направлением на солнце
base         = sunIntensity · exp(−(angleDeg / σ)²),  σ = PIP_FOV_deg / 4
glare        = clamp01(base · 0.35^filters)           // 1..3 фильтра
pain         = glare > 0.6 (выход из ослепления < 0.5 — гистерезис в контроллере)
```
- Ночь (SunAltitude < 0): sunIntensity = 0 → glare = 0 (звёзды без засвета).
- Направление солнца: `weather.Sky.SunDirection` (знак проверить, §3.2).

### 6.3 Идентификация звезды
```
для каждой звезды каталога (alt > 0):
  angle = Acos(dot(starDir, pipForward))
если minAngle < 0.5°: holdTimer += dt; иначе holdTimer = 0
holdTimer ≥ 5 с → identified: имя (NavStarNames.TryGetName(hip))
```
5046 итераций RaDecToEnu/кадр — допустимо (~100k flops); считать только при
открытом секстанте.

---

## 7. Реализация (checklist по порядку)

- [ ] 7.1 `Source/Logic/SextantArc.cs`:
       `Clamp(double)`, `Quantize(double)`, `Format(double)` → "45°23.4′"
       (инвариантная культура). Без Unity.
- [ ] 7.2 `Tests/SextantArcTests.cs` (+6): кламп 0/130/отриц/перебор,
       квант 12″ (0.005° → 1/300°), формат строки.
- [ ] 7.3 csproj тестов: + SextantArcTests, + Link SextantArc.cs.
- [ ] 7.4 `Source/Logic/ConstellationMesh.cs`: в Update условие
       `Input.GetKeyDown(ToggleKey) && !Input.GetKey(KeyCode.LeftControl)
       && !Input.GetKey(KeyCode.RightControl)`.
- [ ] 7.5 `Source/Logic/SextantGlare.cs` (чистый): Glare(angleDeg,
       sunAltitudeDeg, filters) по §6.2; константы σ-делитель, transmission.
- [ ] 7.6 `Tests/SextantGlareTests.cs` (+5): гауссиан пик/спад, transmission
       1/2/3, ночь = 0, кламп.
- [ ] 7.7 Генератор `Data/NavStarNames.cs`: скрипт (PowerShell) читает
       `C:\code\projects\stellarium\stars\hip_gaia3\name.fab`, выбирает 57
       имён из списка STAR_SKY_PLAN §3.7 → эмиссит
       `static readonly Dictionary<int, string> Names` + `TryGetName(int hip,
       out string name)`. Формат name.fab сверить по первой строке.
- [ ] 7.8 `Source/UI/SextantController.cs` (ядро, ~330 строк):
       - поля: open, arcAngle, filterCount, holdTimer, identifiedHip,
         pipCamera, rt, overlay-ссылки, [Inject] GameInput/Weather.
       - Ctrl+Z toggle (Update); открытие: blockade, билд оверлея+PIP,
         filterCount = 0, arcAngle = 0.
       - PIP: GO "RadioNavSextantCamera", Camera (cullingMask/depth копия
         main, clearFlags Skybox, targetTexture RT 512², FOV = main/3),
         позиция/ yaw = main каждый кадр, pitch = arcAngle.
       - Оверлей: Canvas overlay sortingOrder 100 → тёмный Image →
         круглый RawImage(RT) c маской → горизонтальная линия-тик →
         тексты (угол + строка имени/боли).
       - Стрелки: rate 2°/с (Shift 10°/с), SextantArc.Clamp+Quantize,
         текст = SextantArc.Format.
       - ID звезды: §6.3, имя из NavStarNames; солнце: угол до SunDirection
         < 0.5° → "Солнце"; ID заблокирован при glare > 0.6.
       - Засвет: glare = SextantGlare.Glare(...); белая Image α = glare·0.9;
         pain-сообщение при glare > 0.6 (гистерезис 0.5).
       - Закрытие: RemoveBlockade, PIP camera disable, оверлей off.
       - OnDestroy: RemoveBlockade + Destroy(RT/cameras/materials).
- [ ] 7.9 `Source/Logic/Main.cs`: AddComponent<SextantController>() после
       ConstellationToggleButton.
- [ ] 7.10 Сборка Release+Debug → 0 ошибок; tests.ps1 → 289 PASS.
- [ ] 7.11 In-game чеклист (§10).
- [ ] 7.12 Документация: STAR_SKY_PLAN §15 аддендум + supermemory
       (архитектура секстанта) + коммит.

---

## 8. Тесты (состав)

SextantArcTests: Clamp(−5)=0, Clamp(200)=130, Clamp(45)=45;
Quantize(0.005°)=1/300° (12″), Quantize(1°)=1°, Quantize(129.9999)=130;
Format(45 + 23.4′)= "45°23.4′" инвариантная культура.
SextantGlareTests: пик в 0° = sunIntensity; спад 2σ → <10%; фильтры 1/2/3
монотонно гасят; ночь (alt<0) = 0; clamp01 верх.

---

## 9. Риски и фолбэки

| Риск | Фолбэк |
|------|--------|
| Знак SunDirection инвертирован | Проверка дотом с up днём; инверсия константой |
| Sky null (меню/погода не готова) | glare = 0 + Warning once |
| Стрелки крутят зум игры при открытом секстанте | AddBlockade обязателен; проверить ДО первого теста |
| Ctrl+Z конфликтует с системным undo в оверлеях | Блокада + наш ввод читает raw Input — ок |
| Звёзды в PIP не видны днём | Так и задумано (день = солнце через фильтры) |
| 5046 звёзд ID тормозит | Считать раз в 0.2 с, не каждый кадр |
| name.fab отсутствует | Fallback: имя = "HIP " + hip (ID работает, без имени) |

---

## 10. Верификация in-game (чеклист)

1. Ctrl+Z — затемнение, центральный круг с PIP, угол слева белым.
2. Стрелки — угол 0→130→0 плавно; Shift ускоряет ×5.
3. Ночь: навести звезду в центр PIP → через 5 с появляется имя; увести — имя исчезает.
4. День: навести на солнце без фильтров → белое пятно + «НЕ МОГУ, ГЛАЗАМ БОЛЬНО — УСТАНОВИТЕ ФИЛЬТР»; фильтры 1→2→3 — пятно гаснет, «Солнце» + замер.
5. Ctrl+Z закрытие — блокада снята (стрелки снова зумят игру), созвездия по Z работают (Ctrl+Z не тумблит их).
6. Перезапуск игры — пересборка, секстант на месте.

---

## 11. Ссылки

- STAR_SKY_PLAN.md — §3.2 (TOD_Sky API), §3.7 (57 имён), §6.2 (AstroMath API),
  §9 (рендер звёзд), §15 (верификация тумблера)
- knowledge/classes/DI-and-resources.md — GameInput blockade, ExecutionQueue
- knowledge/qa/qa-classes.md — InjectionFramework правила
- knowledge/inventory/game-hotkeys.md — таблица хоткеев (F4–F11 заняты)
- knowledge/playbooks/gate-ui-by-camera-mode.md — паттерн оверлеев
- AstroMath.cs — RaDecToEnu (сигнатура §6.3), MoonRaDec (v2)
- Исходники игры: C:\code\projects\uboat (KeyboardShortcuts.cs — реестр осей)
- Математика: Meeus (высота светила — §7.3 STAR_SKY_PLAN)

---

## 12. Оценка

Исполнение: 60–90 мин (генератор имён ~15 мин, ядро ~40 мин, тесты ~10 мин,
проводка/доки ~10 мин). In-game верификация — на авторе (ночь + день).
