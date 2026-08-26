# SEXTANT_PLAN — Секстант для RadioNavigation (PIP-две камеры, всё программно)

> Версия: 1.3 (2026-08-26). Статус: исполнен (eee0274, 43b154b); солнце — своя эфемерида.
> 1.3 (2026-08-26): TOD_Sky недоступен in-game компилятору (CS0246/CS0012) →
> `AstroMath.SunRaDec` (Meeus) + диагностика SUNDRIFT (reflection, 10 с); тесты 298.
> 1.2 (2026-08-26): секстант=Z, созвездия=Shift+Z — игра ставит действие в
> очередь на Ctrl, Ctrl выведен из биндинга полностью.
> 1.1 (2026-08-26): + §13 разметка/разрешения; §3.2 veil/RT 1024/квадратный
> aspect; §7.4 Z-блок при открытом секстанте; §7.8 гейт камеры + layout;
> §9 глифы; §10 пп.7–9; §12 оценка 70–100 мин. Согласовано с автором.
> Цель документа: реализовать в новой сессии без повторного исследования.
> Спецификация согласована с автором (2026-08-25, см. §2 — все решения зафиксированы).

---

## 1. Цель и механика

Секстант — инструмент измерения высоты светил. Реализация полностью программная
(без 3D-моделей и без пересборки бандла) → просто тестировать и внедрять.

Механика (по автору):
1. Игрок жмёт **Z** → экран темнеет (α≈0.85), в центре — «окуляр» с картинкой.
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
| 1 | Открыть/закрыть | **Z** (созвездия — **Shift+Z**; v1.2: Ctrl убран — игра ставит действие в очередь на Ctrl) |
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
  ChronometerOverlayHandler.cs:92 (хронометр). Наш секстант: **Z**
  (v1.2; исходный Ctrl+Z убран — игра ставит действие в очередь на Ctrl).
- Занятые клавиши игры (полная таблица `knowledge/inventory/game-hotkeys.md`):
  F4 телеграф, F5 глубиномер, F6 аксиометр, F7 хронометр, F8 Quick Save,
  F9 Quick Load, F10 Capture Screenshot, **F11 bug report dialog** (хардкод,
  НЕ InputAxis — в дамп осей не попадает!), Space, G, J, 1, M, B, H, U, K, L,
  Q, E, C, V, R, N, F, W/A/S/D, Tab, Esc, Return, BackQuote, Ctrl, Shift,
  PageUp/Down, +/=/-, Keypad0, 2..9 (персонажи), Mouse0/1/2, стрелки (Zoom).
- **Свободно**: Z (мод с v1.2: секстант=Z, созвездия=Shift+Z),
  F12 (но это скриншот Steam — избегать), буквы I/O/P/T/X/Y.

### 3.2 Камеры и оверлеи
- Основная камера: `CameraResolver.TryResolve(out cam)` (мод), `cam.fieldOfView`.
- PIP-камера рендерит мир (звёзды — билборды на слое Default вокруг
  `cam.transform.position + dir·3900`, см. StarsMesh.cs:134) — вторая камера
  **в той же точке** видит их автоматически. Skybox в PIP = игровое небо
  (аутентично), звёзды аддитивные поверх.
- RT: `new RenderTexture(1024, 1024, 24)` статически (512 мылится на
  1440p/4K: апскейл до 800–1200 px; 1024² = 4 MB VRAM, одна камера —
  дёшево, без логики пересоздания при ресайзе); `pipCamera.targetTexture
  = rt`; включать только при открытом секстанте (экономия).
  **Aspect PIP не наследовать от основной камеры — квадрат**: vertical
  FOV = main/3 даёт равномерный ×3 по обеим осям на любом аспекте
  дисплея (16:9/21:9/4:3); наследование aspect даст эллипс-сжатие.
- Оверлей: свой screen-space overlay Canvas, sortingOrder **1000**
  (точки маяка LighthouseBeamGlow уже 500), **GraphicRaycaster включён**
  (модальность: поглощать клики), CanvasScaler ScaleWithScreenSize
  ref 1920×1080 matchWidthOrHeight=1 (детали — §13). Тёмный veil —
  сплошной Image α0.85 БЕЗ дырки; PIP `RawImage` (texture = RT) с мягким
  радиальным краем (паттерн `NavMapMarkerBase.GetBackdropSprite()` —
  градиентное кольцо поверх края PIP, не маска с вырезом: растянутая
  маска станет эллипсом на не-16:9).
- Шрифт: `NavMapMarkerBase.GetTooltipFont()` — **public static** (открыт
  2026-08-25 для переиспользования).
- Полноэкранный режим устройства НЕ нужен: оверлей поверх геймплея
  (IExternalController/DeviceManualMode — не наш путь).

### 3.3 Ввод-блокада и фокус
- При открытии: `gameInput.AddBlockade(this)` — глушит зум игры на стрелках.
- При закрытии/OnDestroy: `RemoveBlockade(this)`. Пропустить Remove → блокада
  навсегда (частый баг — тестировать закрытие).

### 3.4 Небо и светила
- **v1.3 ФАКТ: `TOD_Sky` недоступен компилятору мода in-game** (тип живёт в
  `com.uboat.dependencies`, которой нет в reference-сете `Mod.Compile` —
  CS0246/CS0012, проверено 2026-08-26). Локальный `dotnet build` её видит —
  не ориентир. Солнце считается собственной формулой Meeus
  (`AstroMath.SunRaDec`, точность ~0.01°) через тот же `RaDecToEnu`, что и
  звёзды; оси уже доказаны игрой. Игровые значения читаются только для
  диагностики дрейфа — reflection-кешем (`Weather.Sky` → `SunDirection`/
  `SunAltitude`), без именования типа в коде.
- Диагностика `SUNDRIFT`: раз в 10 с при открытом секстанте лог
  `sep=…° dalt=…° meeus(alt=…) game(alt=…)`; устойчивый sep > 0.5° —
  дрейф эфемериды/конвенции.
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
  `EnabledChanged` event; v1.2: эффективный биндинг **Shift+Z** (plain Z —
  секстант), при открытом секстанте заблокирован — см. §7.4.
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
    ConstellationMesh.cs [ПРАВКА] созвездия = Shift+Z, гейт IsOpen (§7.4)
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
- Направление солнца (v1.3): собственная эфемерида — `AstroMath.SunRaDec(jd)`
  → `RaDecToEnu` (та же конвенция осей, что у звёзд); высота — `altRad` того
  же вызова. Weather/TOD_Sky в расчёте не участвуют (см. §3.4).

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
       `Input.GetKeyDown(ToggleKey) && ShiftHeld() && !SextantController.IsOpen`
       (v1.2: созвездия = Shift+Z; при открытом секстанте звёзды не тумблятся;
       исходный фикс конфликта с Ctrl устарел — Ctrl полностью выведен из
       схемы, игра ставит действие в очередь на Ctrl).
- [ ] 7.5 `Source/Logic/SextantGlare.cs` (чистый): Glare(angleDeg,
       sunAltitudeDeg, filters) по §6.2; константы σ-делитель, transmission.
- [ ] 7.6 `Tests/SextantGlareTests.cs` (+5): гауссиан пик/спад, transmission
       1/2/3, ночь = 0, кламп.
- [ ] 7.7 Генератор `Data/NavStarNames.cs`: скрипт (PowerShell) читает
       `C:\code\projects\stellarium\stars\hip_gaia3\name.fab`, выбирает 57
       имён из списка STAR_SKY_PLAN §3.7 → эмиссит
       `static readonly Dictionary<int, string> Names` + `TryGetName(int hip,
       out string name)`. Формат name.fab сверить по первой строке.
- [ ] 7.8 `Source/UI/SextantController.cs` (ядро, ~370 строк):
       - поля: open, arcAngle, filterCount, holdTimer, identifiedHip,
         pipCamera, rt, overlay-ссылки, GameInput (GetInstance) +
         Weather только для SUNDRIFT-reflection (v1.3);
         `public static bool IsOpen { get; private set; }` (гейт Z в 7.4).
       - Z toggle (Shift+Z игнорируется — там созвездия); гейт камеры
         (§13.4): невалидный вид → тихий игнор + один Debug.Log
         `[RadioNav]` на нажатие; открытие: blockade, билд оверлея+PIP,
         filterCount = 0, arcAngle = 0.
       - PIP: GO "RadioNavSextantCamera", Camera (cullingMask/depth копия
         main, clearFlags Skybox, targetTexture RT 512², FOV = main/3),
         позиция/ yaw = main каждый кадр, pitch = arcAngle.
       - Оверлей: Canvas overlay sortingOrder 1000 + GraphicRaycaster +
         CanvasScaler(1920×1080, match=1) → сплошной veil α0.85 →
         круглый RawImage(RT) 600² с мягким краем → glare-ребёнок →
         линия-тик (ровно диаметр) → колонка текста слева от круга →
         hint снизу (состав и якоря — §13.3).
       - Стрелки: rate 2°/с (Shift 10°/с), SextantArc.Clamp+Quantize,
         текст = SextantArc.Format.
       - ID звезды: §6.3, имя из NavStarNames; солнце: угол до собственного
         вектора (SunRaDec→ENU, v1.3) < 0.5° → "Солнце"; ID заблокирован
         при glare > 0.6.
       - Засвет: glare = SextantGlare.Glare(...); белая Image α = glare·0.9;
         pain-сообщение при glare > 0.6 (гистерезис 0.5).
       - SUNDRIFT (v1.3): раз в 10 с при открытом — лог расхождения нашей
         эфемериды с игровой (reflection-кеш, §3.4).
       - Закрытие: RemoveBlockade, PIP camera disable, оверлей off.
       - OnDestroy: RemoveBlockade + Destroy(RT/cameras/materials).
- [ ] 7.9 `Source/Logic/Main.cs`: AddComponent<SextantController>() после
       ConstellationToggleButton.
- [ ] 7.10 Сборка Release+Debug → 0 ошибок; tests.ps1 → 295 PASS
       (283 база + 7 arc + 5 glare; факт 2026-08-26).
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
| Дрейф нашей эфемериды vs игровое солнце (v1.3) | Диагностика SUNDRIFT: sep в логе раз в 10 с; устойчивый sep > 0.5° — сигнал разбора конвенций |
| Sky null (меню/погода не готова) | glare = 0 + Warning once |
| Стрелки крутят зум игры при открытом секстанте | AddBlockade обязателен; проверить ДО первого теста |
| Ctrl в биндинге — игра ставит действие в очередь на Ctrl | Снят в v1.2: секстант=Z, созвездия=Shift+Z, Ctrl не участвует |
| Звёзды в PIP не видны днём | Так и задумано (день = солнце через фильтры) |
| 5046 звёзд ID тормозит | Считать раз в 0.2 с, не каждый кадр |
| name.fab отсутствует | Fallback: имя = "HIP " + hip (ID работает, без имени) |
| Глифов «·»/«↑↓» нет в GetTooltipFont() | Primary hint уже без стрелок («Стрелки: угол · ...»); если «·» рисуется квадратом — заменить на запятые |

---

## 10. Верификация in-game (чеклист)

1. Z — затемнение, центральный круг с PIP, угол слева белым.
2. Стрелки — угол 0→130→0 плавно; Shift ускоряет ×5.
3. Ночь: навести звезду в центр PIP → через 5 с появляется имя; увести — имя исчезает.
4. День: навести на солнце без фильтров → белое пятно + «НЕ МОГУ, ГЛАЗАМ БОЛЬНО — УСТАНОВИТЕ ФИЛЬТР»; фильтры 1→2→3 — пятно гаснет, «Солнце» + замер.
5. Закрытие по Z — блокада снята (стрелки снова зумят игру); созвездия =
   Shift+Z, при открытом секстанте заблокированы.
6. Перезапуск игры — пересборка, секстант на месте.
7. Раскладка на 1024×768 и ultrawide: круг круглый, тексты не обрезаны,
   hint читаем (§13).
8. Z на тактической карте/перископе — молча ничего; в логе один
   `[RadioNav]`.
9. Под секстантом HUD/радистка недоступны (veil + raycaster) — так
   задумано.

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

Исполнение: 70–100 мин (генератор имён ~15 мин, ядро ~45 мин, layout/§13
~10 мин, тесты ~10 мин, проводка/доки ~10 мин). In-game верификация —
на авторе (ночь + день).

---

## 13. Разметка и разрешения (добавлено 2026-08-26, согласовано с автором)

### 13.1 Канвас
- Свой ScreenSpaceOverlay Canvas: sortingOrder **1000** (точки маяка
  LighthouseBeamGlow уже занимают 500), **GraphicRaycaster включён**
  (модальность — поглощать клики, иначе они уйдут в UI игры под
  оверлеем).
- CanvasScaler: ScaleWithScreenSize, reference **1920×1080**,
  matchWidthOrHeight = **1.0** (match height). Вертикальные пропорции
  постоянны на любом разрешении; квадратный rect остаётся кругом.
- Игровой «масштаб интерфейса» игнорируется — сознательно
  (полноэкранный прибор, не HUD; согласуется с §2.6).

### 13.2 Инварианты вертикали (любое разрешение)

| Зона | 1080-единицы |
|---|---|
| Окуляр PIP (центр) | 600×600 (константа `OcularSizeUnits = 600`) |
| Тёмные полосы над/под кругом | по 240 — всегда |
| Боковые живые зоны 16:9 | по 660 |
| Боковые зоны 4:3 (минимум игры) | по 420 |
| 21:9 / 32:9 | ещё шире — не проблема |

- Минимум поддержки: **1024×768** (худший случай: колонка текста
  помещается в свободные 420 ед.). Ниже 900 px высоты hint не читаем —
  не тюним.
- Match=1 делает боковые зоны ≥ 420 ед. на любом аспекте — всё
  содержимое влезает всегда.

### 13.3 Элементы (anchor / размер в 1080-единицах)

| Элемент | Anchor | Размер/позиция |
|---|---|---|
| Veil | stretch full | сплошной чёрный α0.85 (константа `VeilAlpha`), БЕЗ дырки |
| PIP RawImage (RT) | center | 600×600, мягкий радиальный край (градиентное кольцо поверх края) |
| Glare-вспышка | child PIP | тот же rect, белый радиальный материал, α = glare·0.9 |
| Линия-тик | center | ровно 600×3 (без вылезания за край круга) |
| Колонка чтения | слева от круга, pivot(1,0.5) | угол f44 → имя f32 → «боль» f32 → «ФИЛЬТР n» f28; maxWidth 320 + ellipsis |
| Подсказка | bottom-center, y=60 | f26, серый: `Стрелки: угол · Shift: быстрее · 1–3: фильтры · Z: выход`, постоянная |

- Колонка привязана к краю круга (не к экрану): на ultrawide текст
  жмётся к прибору, на 4:3 помещается в свободные 420 ед.
- Мягкий край PIP — градиентное кольцо поверх края PIP-квадрата, не
  маска с вырезом (растянутая маска = эллипс на не-16:9).

### 13.4 Гейт камеры при открытии
- Открытие только из живого вида (мостик) — playbook
  `knowledge/playbooks/gate-ui-by-camera-mode.md`. PIP снимает main
  camera: поверх тактической карты/перископа замер бессмыслен.
- Невалидный вид → **тихий игнор Z** (без сообщения игроку);
  один `Debug.Log("[RadioNav] ...")` на нажатие — для отладки.
