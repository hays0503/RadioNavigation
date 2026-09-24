# SEXTANT_PLAN — Секстант для RadioNavigation (PIP-две камеры, всё программно)

> Версия: 1.13 (2026-08-30). Статус: исполнен (eee0274…9aa7506); PIP вода+пост.
> 2.2 (2026-09-16): ручное счисление по Солнцу/Луне/звёздам — ADR-033. См. §15.
> 1.13 (2026-08-30): SHA-формат + фикс-снимок по I. Авторская верификация
> in-game (обратный пересчёт сферической астрономии): fix/h₀/Az сходятся с
> ошибкой 0.006–0.013′ — цепочка точна. Найденная «аномалия γ+SHA ≠ GHA на
> 2′» — артефакт отображения: SHA выводился как 0.1° десятичных (±3′),
> 198.3° читалось как 198°18.0′ вместо истинных 198°16.4′ → SHA теперь в
> D°M.m′ (FormatDm, как γ/GHA). Перенос округлялся только в выводе — расчёт
> и так вёл непрерыванное значение. Главное: **фикс-снимок по I** —
> Versetzung/Геометрия/FIX/ошибка строятся один раз в CaptureObservation
> (после записи №2, третий I пересчитывает) с курсом/скоростью момента
> записи; 1 Гц-рефреш и переоткрытия секстанта НЕ пересчитывают (fixSection-
> Text). SolveSunCalibration при открытой серии не пересчитывается (иначе
> №1/№2 оказываются в разных эфемеридах) — только при пустой серии. Тесты
> 330. In-game smoke — на авторе.
> 1.12 (2026-08-30): латинская легенда + hold 2.5 с + ластик + бандл.
> In-game v1.11: тултип маркера «шир/дол» = квадраты — TMP-шрифт карты без
> кириллицы → легенда латиницей «lat 56°21.9'N lon 3°32.8'E» (ASCII-апостроф,
> U+2032 в TMP тоже нет; карточка остаётся кириллицей — legacy OS Arial
> рендерит). IdentifyHoldSeconds 5 → 2.5 с (запрос автора, все светила).
> Ластик: MapChartingUI.UpdateEraserTool (UBOAT.Game.UI:397) стирает только
> MapPointUI.HoveredPoint — наш маркер не MapPointUI → эмуляция: в
> FixMapMarker.OnMarkerPositioned (каждый кадр) hovered (свои pointer-
> события) + MapChartingUI.CurrentTool == Tool.Eraser (public, instance
> через FindObjectOfType) + Input.GetMouseButton(0) → ClearByEraser()
> (Suppressed=true). Set() игнорирует Suppressed — иначе 1 Гц карточка
> мгновенно републиковала бы стёртый фикс; снятие подавления: новое
> наблюдение (I → OnNewObservation) или сброс серии (O → Clear).
> Бандл пересобран через Unity MCP (обновлённая картинка секстана + PDF
> журнала в проекте), выход StreamingAssets/Packages/uboat/Bundles → копия.
> Тесты 330. In-game smoke — на авторе.
> 1.11 (2026-08-30): индикатор геометрии + иконка IconSextan + легенда текстом.
> In-game верификация v1.10: калибровка Δt −6.5 мин (ровно прогноз v1.5),
> интерцепты +23.6/+28.4 → +5.7/+2.4 NM, маркер FIX на карте работает.
> Остаточная ошибка фикса 31.8 NM — геометрия: азимуты 149/165° → пересечение
> 16°, амплификация ~1/sin(θ). `CrossingAngleDeg` (чистый, +3 теста) →
> карточка: «Геометрия 16° — слабо» (>30° хорошо / 15–30° посредственно /
> <15° слабо + совет разнести замеры). Маркер: иконка IconSextan (префаб
> SpriteRenderer в бандле; фолбэк-цепочка удалена по решению автора — только
> IconSextan + Warning), тултип текстом «шир/дол» вместо φ/λ. Бандл
> пересобран через Unity MCP (RadioNavBundleBuilder.Build в живом редакторе —
> редактор НЕ закрывать, batchmode не нужен), выход StreamingAssets/Packages/
> uboat/Bundles → копия в Bundles/ мода, byte-scan верифицирован. Тесты 330.
> 1.10 (2026-08-30): калибровка SUNDRIFT + маркер FIX на карте. Решатель
> `SunEphemerisCalibration.SolveDelta` (чистый, +8 тестов): игровой солнец =
> эфемерида, сдвинутая по времени (вывод v1.5); скан ±15 мин + 2 уточнения,
> приём при residual ≤ 0.25°. Δt применяется ко ВСЕЙ эфемериде бланка
> (`CalibratedJd` в BuildObservation/RecomputeHcAz/TryGetOwnSun), решается
> автоматически при каждом открытии секстанта, хранится в PlayerPrefs
> `RadioNav.SunCalibrationMinutes`; карточка показывает «Калибровка Δt».
> Супersedes решение v1.5 «без компенсации» (явное решение автора 2026-08-30:
> ошибка фикса ~40 NM → единицы NM). Маркер: `FixMapMarker` (сабкласс
> NavMapMarkerBase, шаблон StationMapMarker, иконка IconSonne) публикует
> последний FIX на навкарте с тултипом φ/λ/UT; сброс — O. Проводка Main.cs.
> Тесты 327 PASS. In-game smoke — на авторе.
> 1.9 (2026-08-30): астронавигационный бланк F-Tafel — running fix по двум
> наблюдениям Солнца. `SextantRunningFix.cs` (чистая математика, 15 тестов):
> SextantObservation (UT/DR/γ/SHA/GHA/LHA/δ/h_b/h1/Az/a), LOP, Versetzung,
> пересечение, `CalculateFix(iterations:2)` с пересчётом Hc/Az (наблюдение №1
> считается в точке корабля на t₁ = fix − ход). Эфемеридные поля: γ = GMST,
> GHA☉ = γ − RA☉, **SHA☉ = GHA☉ − γ ≠ 0** (не «SHA=0»!), LHA = GHA☉ + λg,
> квадрант I–IV → W/O. Геометрия: (E,N) в NM, û=(sinAz,cosAz),
> t=(cosAz,−sinAz), Δlon через cos(φ_среднее). UI: клавиши **I** (запись
> замера при идентифицированном Солнце) / **O** (новая серия); FixColumn
> левее колонки чтения (x=−700, 380 ед.; на 4:3 стек под ReadingColumn),
> карточка = один Text, обновление 1 Гц; курс/скорость из трека (окно 10 с,
> GeoMath, порог 0.5 kn). Тесты 319 PASS. In-game smoke — на авторе.
> 1.6 (2026-08-26): белый окуляр = PIP без воды и без тонмапа. Фикс: PIP-камера
> регистрируется в `AdditionalCameraData.AttachedCameras` главной (вода,
> тип из com.uboat.rendering — сборка в референсах мод-компилятора, Mod.cs:330)
> + `PostProcessLayer.Init` с копией `volumeLayer`/`m_Resources` через
> reflection (форк стека прячет поля). Локальные csproj/build.ps1 +2 DLL.
> 1.5 (2026-08-26): SUNDRIFT-вывод — систематика = временной сдвиг ~6.5 мин
> (daz/dalt отношение совпало с суточными rates); решение — БЕЗ компенсации
> (аутентичная астрономия), вопрос компенсации открыт. Кириллица: TMP dynamic
> не подтянул глифы → лейблы переведены на legacy UI.Text + OS Arial. Фильтры
> затемняют трубу (filterDim α=0.15×N — против белого неба без пост-стека).
> Маска/кольцо 256² (гладкий край).
> 1.4 (2026-08-26): кириллица — динамический OS-Arial TMP (дефолтный шрифт без
> кириллицы, квадраты в hint/ФИЛЬТР); окуляр = круглая маска (углы показывают
> veil); SUNDRIFT + азимуты (daz). Наблюдение: sep=1.591° dalt=−0.94° стабильно
> — систематика (время/рефракция), не дрейф.
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

> **Решение 2026-09-22: квант 0.1′ = 6″ = 1/600°** (`SextantArc.StepsPerDeg = 600`). Верньер на 10 делений (Bowditch Ch.15 §1502) и немецкая практика F-Tafel в десятых минуты; причина — разряд отображения (0.1′) должен достигаться одним щелчком точной настройки (Ctrl+стрелки). Спека §2.4 выше (0.2′ = 1/300°) оставлена как исходное решение.

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
- **v1.6 ФАКТ: сторонняя RT-камера без регистрации даёт белый круг** —
  вода рендерится только для камер из `AdditionalCameraData.AttachedCameras`
  главной камеры (тип `UBOAT.Game.Scene.Rendering.AdditionalCameraData`,
  com.uboat.rendering; перископ-паттерн Periscope.cs:581), а без
  `PostProcessLayer` HDR-небо/море клипаются в белый. Обе сборки в
  референсах мод-компилятора (Mod.cs:330 IImageEffect → rendering,
  Mod.cs:349 PostProcessLayer). Форк пост-стека прячет `volumeLayer`/
  `m_Resources` — копировать reflection'ом с главной камеры.
  Пасс-лист: `CameraPass` — ctor(int cullingMask), `Enabled` rw,
  `ScreenSpaceReflections` get-only (v1.6.1: сеттер бросал исключение —
  пасс-лист копируется глубокой копией с главной камеры).
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
  `sep=…° dalt=…° daz=…° meeus(alt=… az=…) game(alt=… az=…)`; устойчивый sep > 0.5° —
  дрейф эфемериды/конвенции.
- **Наблюдение in-game 2026-08-26**: `sep=1.591° dalt=−0.940°` стабильно
  (не растёт) → систематическое смещение ~0.94° по высоте + ~1.28° по азимуту.
  **Вывод v1.5 (по daz/dalt)**: отношение −0.603 совпало с теоретическим
  суточным dAlt/dAz (−0.62 при alt 25°/az 250°/φ≈54°N) → расхождение —
  **чисто временной сдвиг ~6.5 мин** (игровое солнце = campaign UTC − 6.5 мин,
  вероятно сглаженное время UnStorm). Рефракция/конвенции исключены.
  Решение: секстант считает чистую астрономию БЕЗ компенсации (аутентично);
  автокалибровка по SUNDRIFT — возможное будущее решение автора.
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
    SextantArc.cs        [НОВЫЙ] чистая математика дуги (кламп, квант 0.2′ → 0.1′ с 2026-09-22, см. решение у §2.4)
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
// с 2026-09-22: 600 вместо 300 — квант 0.1′ = 6″ (верньер 10 делений, Bowditch §1502)
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
       - PIP вода+пост (v1.6): `AdditionalCameraData` на PIP + регистрация в
         `AttachedCameras` главной при открытии / снятие при закрытии и
         OnDestroy; `PostProcessLayer` с копией `volumeLayer`/`m_Resources`
         (reflection) и `Init(resources)`, AA None.
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
| Глифов «·»/«↑↓»/кириллицы нет в шрифте | Снят в v1.5: legacy UI.Text + OS Arial несёт всё; TMP dynamic (v1.4) кириллицу не подтянул |

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
| LeftView RawImage (RT левой камеры) | child PIP, левая половина `[0,0.5]×[0,1]` | **круглая маска** (Mask + circle-спрайт 256², углы = veil), мягкий радиальный край (кольцо 256²) |
| RightView RawImage (RT правой камеры) | child PIP, правая половина `[0.5,1]×[0,1]` | та же маска |
| VerticalSplit | center | 2×600, белый α0.35 — стык половин |
| FilterDim (v1.5) | child PIP | чёрный α=0.15×filterCount поверх RT — фильтры затемняют всю трубу |
| Glare-вспышка | child PIP | тот же rect, белый радиальный материал, α = glare·0.9 |
| Колонка чтения | слева от круга, pivot(1,0.5) | угол f44 → имя f32 → «боль» f32 → «ФИЛЬТР n» f28; maxWidth 320 + ellipsis |
| Подсказка | bottom-center, y=60 | f26, серый: `Стрелки: угол · Shift: быстрее · 1–3: фильтры · Z: выход`, постоянная |

- Окуляр (v1.8, 2026-08-27): **две игровые PIP-камеры** по periscope-паттерну
  (`AdditionalCameraData.AttachedCameras` + SRP-passes + `PostProcessLayer`-клон) —
  левая смотрит pitch 0 (неподвижный горизонт), правая pitch = `arcAngle`
  (0–130°, стрелки). Контент рисуют **шейдеры самой игры** (небо/море/звёзды/солнце),
  мод меняет только угол. Кастомный шейдер окуляра, фолбэк-картинки и
  UI-точки звёзд удалены.
- Камеры: FOV = main/3 (`FovDivisor`), RT 512×1024 на каждую, `enabled=false`
  (рендер через pass-лист MainCamera), солид-чёрный клир.
- Шрифт (v1.5): **legacy `UnityEngine.UI.Text` + OS Arial**
  (`CreateDynamicFontFromOSFont`) — TMP dynamic font asset не подтянул
  кириллические глифы из OS-шрифта (in-game v1.4: боксы остались);
  legacy Text рендерит кириллицу гарантированно. Фолбэк-лог при отсутствии.
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

---

## 14. Астронавигационный бланк F-Tafel (v1.9, 2026-08-30)

### 14.1 Механика
1. Секстант открыт, Солнце идентифицировано (5 с в центре PIP, glare ≤ 0.6).
   ID Солнца целиится в ИГРОВОЕ направление (v1.9.1: диск рисуется по gameDir,
   против собственной эфемериды конус 0.5° не пробивается из-за систематики
   SUNDRIFT ~1.6°); фолбэк — собственная эфемерида. Замер Ho при этом идёт
   по дуге против собственной эфемериды → `a` несёт систематику SUNDRIFT
   (~1.6° ≈ 56 NM в «Ошибке от счисления») — осознанное решение v1.5
   (аутентичная астрономия без компенсации), не баг.
2. **I** — записать наблюдение: замораживаются UT, DR (позиция игры),
   замер Ho = Quantize(arc) и вся эфемеридная цепочка бланка. Без Солнца —
   «НЕТ НАБЛЮДЕНИЯ» (3 с). Третье нажатие заменяет наблюдение №2.
3. Ход 20–30 мин, второе наведение, **I** — наблюдение №2 → авто FIX.
4. **O** — новая серия (сброс №1/№2/FIX). Серия живёт через закрытие/открытие.

### 14.2 Расчёт (SextantRunningFix.cs, чистый, 15 тестов)
- Эфемеридные поля бланка: γ = GMST(jd); GHA☉ = norm360(γ − RA☉);
  **SHA☉ = norm360(GHA☉ − γ) — для Солнца НЕ ноль** (вычисляется из RA);
  LHA = norm360(GHA☉ + λg); квадрант I–IV, правило gleichnam.t:
  LHA I,II → West, III,IV → Ost.
- Standlinie: касательная точка = DR + a·û, a = (Ho − Hc)·60 NM,
  û = (sinAz, cosAz) в (E,N); линия перпендикулярна азимуту
  (t = (cosAz, −sinAz)). Перевод в градусы: Δlat = ΔN/60,
  Δlon = ΔE/(60·cos(φ_среднее)).
- Versetzung: сдвиг LOP1 по курсу/скорости на run = speed·Δt
  (курс/скорость — из трека позиции, окно 10 с, порог 0.5 kn).
- FIX = пересечение перенесённой LOP1 и LOP2; refinement
  `CalculateFix(iterations: 2)`: Hc/Az наблюдения №1 пересчитываются в
  точке корабля на t₁ (fix − ход) и линия переносится заново, №2 — в фиксе.
  Ловушка (найдена тестами): пересчёт №1 в самом фиксе уводит fix на ~30 NM.
- «Ошибка от счисления» = Haversine(fix, текущая позиция игры) — только
  индикатор, НЕ критерий корректности. Критерий — синтетический тест
  (истина 54°N/18°E против DR 5′N/10′E → fix ±0.05 NM).

### 14.3 UI
- FixColumn: вторая колонка слева, x = −(300+40+320+40) = −700 от центра,
  pivot(1,0.5), 380×560 ед.; на 4:3 (боковая зона < 740 ед.) — стек под
  ReadingColumn (pivot(1,1), y=−110). Карточка = один legacy Text
  (OS Arial, f11, Wrap), обновление 1 Гц + мгновенно при записи/сбросе.
- Порядок блоков = бланк F-Tafel: НАБЛЮДЕНИЕ №1 (UT → γ/SHA/GHA →
  λg/LHA/кв → δ → hb/h1 → Az/a) → ВЕРСЕТЦУНГ (Курс/Скор/Δt/Перенос) →
  НАБЛЮДЕНИЕ №2 → FIX (φ/λ + ошибка от счисления).
- Табличные поля F-Tafel (FI/FII/V/U/Verb.f) не эмулируются — расчёт
  прямой, бланк используется как образец процесса.
- Hint дополнен: «I: замер · O: серия».

### 14.4 Калибровка SUNDRIFT + маркер FIX (v1.10, 2026-08-30)

**Калибровка** (`SunEphemerisCalibration.cs`, чистый, +8 тестов):
- Модель: игровой солнец = собственная эфемерида, сдвинутая по времени Δt
  (вывод v1.5: daz/dalt = суточные rates) — один параметр правит высоту И
  азимут. Супersedes «без компенсации» v1.5 (решение автора 2026-08-30).
- `SolveDelta(utc, lat, lon, targetXYZ, ...)`: скан Δt ∈ [−15,+15] мин шаг
  1.0 → два уточнения (0.1, 0.01 мин); приём при residual ≤ 0.25°, иначе
  сохраняется прежняя калибровка. ~70 вызовов эфемериды, раз на открытие.
- Применение: `CalibratedJd` в `BuildObservation`/`RecomputeHcAz`/
  `TryGetOwnSun` (звёздные ветки НЕ калибруются — звёзды рендерит мод без
  сдвига). При Δt=0 поведение идентично прежнему.
- Контроллер: решение при каждом `TryOpen` (gameDir + позиция), хранение в
  PlayerPrefs `RadioNav.SunCalibrationMinutes`, загрузка в `Awake`;
  карточка показывает «Калибровка Δt ±N.N мин».
- Эффект: intercept a ≈ 0 при правильном наведении, «Ошибка от счисления»
  падает с ~40 NM до единиц NM.

**Маркер FIX** (`FixMapMarker.cs`, сабкласс `NavMapMarkerBase`):
- Статика `HasFix/FixLat/FixLon/FixLabel` — публикует `BuildFixCard` при
  успешном `CalculateFix`, чистит `ResetSeries` (O).
- Позиция каждый кадр через `Coordinates.ToWorldPosition` +
  `EncounterToSceneSpace` (origin-safe, шаблон StationMapMarker); иконка
  `IconSonne` из бандла; тултип «FIX HH:MM / φ … λ …» по ховеру/клику.
- Проводка: `Main.cs` AddComponent после StationMapMarker. Переживает
  закрытие карты; после перезапуска игры появляется снова с новым FIX
  (серия в памяти — осознанное решение v1.9).

---

## 15. Ручное счисление по Солнцу, Луне и звёздам (v2.2, 2026-09-16)

См. ADR-033. Кратко:

### 15.1 Ядро
- `Source/Logic/CelestialBody.cs` — `CelestialBodyKind {None,Sun,Moon,Star}`,
  `CelestialBody {Kind,Name,Hip}`, `AsciiName` для карты.
- `Source/Logic/CelestialEphemeris.cs` — `Compute(body,utc)` → SunRaDec /
  MoonRaDec / StarCatalog; `JdForKind` → `CalibratedJd` **только для Солнца**.
- `SextantRunningFix`: `SextantObservation` +`BodyKind/BodyName/BodyHip/
  BodyRaDeg/BodyDecDeg`, `ShaSunDeg/GhaSunDeg` → `ShaBodyDeg/GhaBodyDeg`;
  `BuildObservation(..., CelestialBody)`; `RecomputeHcAz` по замороженным
  RA/Dec (телос-агностичен).
- `SextantStarId` → `IdentifiedBody`: конус Sun→Moon→звезда; Луна при
  `alt>0` и `MoonIllumination ≥ 0.05`.

### 15.2 Понятность
- Правая колонка: азимуты к высотам, статус Луны, рекомендация второго
  светила (`BodyRecommendation`, кандидат ближе к 90° от азимута №1,
  alt ≥ 10°).
- Карточка: имя тела в «НАБЛЮДЕНИЕ №n»; флаг «Луна ±0.3°»; «низко — грубо»
  при Ho<10°; «a > 30 NM — проверьте наведение»; «Линии совпадают…» при
  пересечении <3°.
- «Ошибка от счисления» → **«Δ от места игры (проверка)»**: эталон —
  истинная позиция игры, это самопроверка замеров.
- Футер допущений на карточке; экранная справка F-Tafel по клавише **P**
  (`SextantHelpPanel`): расшифровка полей, порядок действий, допущения.
- Маркер FIX: имена светил в тултипе (латиница — TMP без кириллицы).

### 15.3 Допущения игры (раскрываются, не считаются)
- Рефракция, параллакс Луны, наклон горизонта (dip) — не моделируются;
  замер геоцентрический, согласован с отрисованным небом.
- Эфемерида Луны low-precision ±0.3° (~18 NM).
- Звёзды — каталог J1942 без прецессии.
- Смешанная серия (Солнце + звезда) использует разные эфемеридные часы
  (Δt vs UTC) — корректно относительно отрисованного неба.
- Серия не переживает save/load (решение автора).

### 15.4 Гейты
- `tests.ps1` 812 PASS; SourceCheck 53 PASS (`CelestialEphemeris_RoutesBodies`,
  `SextantStarId_SkipsNoHipSentinel`); `build.ps1` + PVS clean;
  `MethodComplexity_NoSuperMethods` PASS (FindNearestBody разбит на
  FindSunInCone/TryFindMoonCos/TryFindStar).
- In-game smoke — на авторе.

### 15.5 Регрессионные фиксы (v2.3, 2026-09-16)
- **F1**: `Hip=0` sentinel — `IsSightableStar(hip) => hip>0` в `CelestialEphemeris`,
  guard в `TryGetStarRaDec`, пропуск в `SextantStarId.TryFindStar`.
- **N1**: `CaptureObservation` отклоняет тело без эфемериды (WarnOnce), чтобы
  fallback на Солнце не дал тихо неверный фикс.
- **N2**: порог «линии совпадают» 3° → **1°**
  (`RunningFixCalculator.MinUsableCrossingAngleDeg` / `IsCrossingUsable`).
- **N3/N4**: контекстный совет `FixCardComposer.GeometryAdvice(crossDeg, sameBody)`
  и `CoincidentMessage(sameBody)`.
- **F3**: `BodyRecommendation.Recommend(..., excludeName)` + `Observation1Name`.
- **V1**: `SextantGameSun.TryGetMoon`/`LogMoonParity` (PlayerPrefs
  `RadioNav.MoonDiag=1`, throttle 10 с) — сравнение игровой/мод-Луны;
  `sep>0.5°` = две Луны.
- **v2.4 (лунная калибровка)**: MOONDIAG показал `sep≈1.6°` (тот же сдвиг
  неба: 6.6 мин × 15.04°/ч ≈ 1.65°). Δt распространён на Луну согласованно:
  `CelestialEphemeris.KindUsesCalibration` (Sun+Moon), `PlanetRenderer`
  (moonJd/moonLst), `SextantBodiesReadout`, `SextantStarId.TryFindMoonCos`,
  `SextantController.TryGetOwnMoon`. Звёзды — plain UTC. Проверка: `sep`
  должен упасть до ≲0.3°.
- Планеты не счисляются — отмечено в справке P и README.

---

## 16. Интерьер лодки в окуляре (Route B, 2026-09-17)

### 16.1 Проблема
Внутри лодки PIP-камеры копировали **главную** камеру (её culling mask +
pass-лист), а интерьер рисует отдельная `InteriorCamera`. Итог: окуляр
смотрел сквозь борт (небо/море), лодки не видно.

### 16.2 Механизм (IL, не перепроверять)
- SRP `DeepPipeline.CollectCameras` (com.dws.pipeline.Runtime) добавляет
  интерьерную `VirtualCamera` вторым `CameraData` **только если у
  рендерящейся камеры на GameObject есть `VirtualSceneRenderer`**
  (`DeepCameraData::.ctor` → `camera.GetComponent<VirtualSceneRenderer>()`;
  guard: `enabled` + `RenderingEnabled` + `VirtualCamera != null` +
  `isActiveAndEnabled`).
- `CollectCameras` копирует `near/far/FOV/projection` с корневой камеры на
  `VirtualCamera` и рендерит обе в **общий g-buffer** → один lighting/blit в
  таргет корневой камеры. Forward-путь `CollectCameras` не вызывает.
- `VirtualSceneRenderer` per-GameObject; `LateUpdate` ставит
  `virtualCamera.localPosition/localRotation` из `vs.Target` + своего
  transform; `SetRenderingEnabled(true)` пишет `localCamera.clearFlags=Nothing`
  (его `localCamera` = PIP-камера, т.к. `GetComponent<Camera>()` на её
  GameObject). Поле `interiorCamera` у VSR — **static**, не трогать.
- `VirtualSceneRenderer.VirtualScene` в игре = null (сериализованное поле);
  сцена приходит per-frame override'ом. Канонический источник —
  `PlayerShip.Interior.VirtualScene` (`Target` = лодка, `SceneCamera` =
  `Interior Camera`).
- **Interior-пространство**: interior-сцена живёт в root-объекте
  `Player Ship Interior Locator` (`pos (0,100,0)`, `rot identity`);
  `InteriorCamera.parent` = этот locator, `InteriorCamera.localPosition` =
  ship-local смещение глаза (FPP-контроллер кладёт его через
  `OverrideLocalTransformation(virtualScene, container.InverseTransformPoint(eye))`).
  VSR `LateUpdate` считает `vs.Target.InverseTransformPoint(...)` в ship-local
  фрейме — это же значение, но применять его надо в **locator-пространстве**
  (иначе промах на 100 м по Y).

### 16.3 Решение (`SextantInteriorMirror.cs`)
На каждую PIP-камеру:
1. **Clone-камера** (`<PIP>InteriorClone`) — child к
   **`InteriorCamera.transform.parent`** (interior locator; fallback —
   `VirtualScene.Target`): `Camera` (копия `InteriorCamera.Camera`) +
   `AdditionalCameraData` (pass-лист с `InteriorCamera.AdditionalCameraData`);
   `StreamingController` нет (null-guard). Parent обязателен именно locator:
   `LateUpdate` пишет ship-local `localPosition`, и в locator-пространстве он
   попадает ровно в world-позицию игровой `InteriorCamera`
   (live 2026-09-17: clone `(-0.8,100.9,0.0)` == `InteriorCamera`
   `(-0.76,100.90,-0.01)`).
2. **`VirtualSceneRenderer`** на GameObject PIP-камеры (reflection:
   `GameTypeCache.Resolve("UBOAT.Game.Scene.Effects.VirtualSceneRenderer",
   "com.uboat.rendering")` + `AddComponent(Type)`), поля `virtualCamera` =
   clone, `virtualScene` = `PlayerShip.Interior.VirtualScene`.
3. Per-frame (`SextantPipPose.Update` → `InteriorMirror.Sync(IsInsideBoat)`):
   `vsr.enabled = IsInsideBoat` (снаружи — выкл, окуляр снова небо/море);
   clone `cullingMask` = `InteriorCamera.Camera.cullingMask`
   (`545264640` / `8393728` / `131072`) + маски pass-ов; вернуть PIP
   `clearFlags = SolidColor` (VSR ставит Nothing один раз).
4. `Attach` — **до первого `Render()`**: `DeepCameraData` кэширует VSR в
   ctor. Teardown: `Close` → `SetEnabled(false)`, `Destroy` → destroy clone+VSR.

### 16.4 Почему корректно
Ровно тот же путь, что у главной камеры: `CollectCameras` → `CAM1.Initialize`
→ `SetCamera`/`RenderGBuffer` в общий g-buffer → один lighting/blit. Поза/FOV
— от PIP, pass-лист/маска — от интерьерной камеры. Clone изолирован: главную
`VirtualCamera`/`StreamingController` не шарит (иначе драка в LateUpdate и
`OnDisable` гасит общий интерьер).

### 16.5 Верификация (2026-09-17)
- Первая итерация: clone был child лодки → world y≈0.1 вместо 100.9 → в
  окуляре интерьер сверху, снизу небо (пользовательский скрин «НЕ РАБОТАЕТ»).
  Фикс — parent = `InteriorCamera.transform.parent` (locator).
- In-game FPP внутри: окуляр заполнен интерьером (стены, люк с сеткой,
  оборудование), неба сквозь борт нет; clone world == `InteriorCamera`
  world (y≈100.9).
- Мостик (`insideBoat=false`): окуляр снова небо/море, интерьера нет.
- Главный вид не задет (screenshot closed = чистый интерьер).
- Перф: закрытый секстант ~50 fps / 19.9 ms, открытый (интерьер) ~32 fps /
  31.6 ms. Модальный прибор — допустимо.
- Гейты: build.ps1 + PVS clean; tests.ps1 812 PASS; SourceCheck 53 PASS.

### 16.6 Gotcha: игра компилирует мод в `Temp`
`Mod.LoadAssembly` пишет сборку в
`%USERPROFILE%\AppData\LocalLow\Deep Water Studio\UBOAT\Temp\<assemblyName>.dll`,
а НЕ в корень мода; build.ps1-DLL в корне игра не читает. Если Temp-DLL не
обновляется (наблюдалось 2026-09-17), можно скопировать build.ps1-DLL в Temp и
проставить ему `LastWriteTime = now` (тогда `IsAssemblyOutdated` вернёт false и
игра загрузит готовую сборку).

### 16.7 GPU-верификация артефактов окуляра и фиксы (2026-09-17, RenderDoc)

Захват кадра (`captures/sextant_frame6198.rdc`, D3D11, 4033 действия; экспорты
`frame_final.png`, `rt_34023.png`, `rt_34040.png`) дал GPU-правду.

**Измерено:**
- Вью окуляра (`LeftView` event 40630, `RightView` event 40634) сэмплят две
  512×512 `R16G16B16A16` RT мода: `34040` (левая) и `34023` (правая).
- Пиксельный шейдер (5133) премультиплицирует: `mul o0.xyz, r0.www, r0.xyzx`
  (RGB × alpha), blend `One / InvSrcAlpha` → итог `out = tex.rgb × tex.a` над
  чёрным `LensBacking`. Альфа RT: `34040` = 0.029..0.644, `34023` = 0.028..1.203
  → картинка окуляра затемняется/гейтится альфой.
- Содержимое половин разное: `34040` RGB ≤ 0.025 (практически чёрная),
  `34023` RGB ≤ 0.917 (засвеченная белая заливка). Каждая RT пишется 2–3 раза
  за кадр (`34040`: 10953/17170/22201; `34023`: 5794/27339) — ручной рендер + SRP.
- Основной вид рендерится в 1129×635 и апскейлится (FSR, `fsrMode=2` в
  settings.ubt); PIP — 512×512; Present — event 40742.

**Причины (подтверждены):**
1. F229: игра не регистрирует бандл мода в `ResourceManager`, поэтому
   `RetrieveAsset<Material>("RadioNavOpaqueView")` уходит в `SimpleResource` →
   `Resources.Load` → null; вью остаются на стоковом UI-шейдере, который
   умножает картинку на альфу RT (то самое «alpha darkening» из комментария
   `SextantOcularBuilder`).
2. Левая PIP-камера была `enabled=false` и «рендерилась» только вручную
   `Camera.Render()` в `Update` (до LateUpdate; для disabled-камеры — no-op) →
   левая RT чёрная, правая засвечена.

**Фиксы:**
- `ModBootstrapper.LoadBundleAsset(name)` — прямое `AssetBundle.LoadAsset` из уже
  загруженного бандла (сначала по имени, затем по полному пути
  `Assets/Packages/testui/<name>`); `SextantOcularBuilder.ResolveOpaqueViewMaterial`
  пробует `ResourceManager` → бандл-материал → бандл-шейдер + `new Material`.
- Обе PIP-камеры `enabled=true` (`SextantController.AttachAndConfigureCameras`);
  ручной `Camera.Render()` из `SextantPipPose.Update` убран — один путь, рендер
  SRP-циклом после LateUpdate. Регистрация в `AttachedCameras` сохранена (вода).

**Открыто:** засвет правой половины (белая заливка) — вероятно, отсутствие
пер-камерных драйверов неба/экспозиции на PIP (в дампе нет `TOD_Camera`) и/или
не скомпозитился интерьер; проверять после фиксов.

### 16.8 Семантика «внутри лодки» и наведение интерьера (2026-09-17, live-верифицировано)

**Требования (пользователь):** секстант — для наблюдения светил; внутри лодки корпус логично
блокирует небо, поэтому окуляр показывает интерьер, идентификация не срабатывает, моря нет;
интерьер в окуляре наводится алидадой так же, как небо снаружи (правая половина опускается/
поднимается). Снаружи — без изменений (правая камера от алидады, небо/море, идентификация).

**Реализация:**
1. **Наведение интерьера** — `SextantInteriorPoseFollower` (`[DefaultExecutionOrder(10000)]`,
   LateUpdate после VSR) на каждом клоне: `Clone.rotation = Source.rotation` (мировая ориентация
   PIP-камеры; locator сам переводит в локальную). До этого VSR писал клону только `localPosition`
   → интерьер был статичен. Живой POSE-лог: left src/clone `(0, yaw, 0)`, right `(300°, yaw, 0)`
   при arc=60; пиксельный тест (timeScale=0): контроль arc0/arc0 → RIGHT=1.55, тест arc0/arc60 →
   RIGHT=33.36, LEFT от алидады не зависит.
2. **Море внутри** — `SextantPipFactory.SyncWaterRenderList(insideBoat)` (per-frame из
   `SextantPipPose.Update`): `WaterCamera.SetCustomWaterRenderList(emptyList)` внутри, `null`
   снаружи — тот же механизм, что у игры (`FPPCameraController.cs:827`); PIP-камеры хранят ссылки
   на свои `WaterCamera`.
3. **Идентификация внутри** — `SextantStarId.Update(dt, pain, canSight, ...)`: при
   `canSight=false` (`MainCamera.IsInsideBoat`) цикл сбрасывается, имя очищается. Live: наведение
   ровно на Солнце внутри → `identified=None`; снаружи → `identified=Sun`.
4. **Баг имени** — `SextantEffectMirror.GetMirrorMembers` копировал `UnityEngine.Object.name`
   (public writable property) с главной камеры на добавленные компоненты → PIP-объекты
   переименовывались в «Main Camera» (путаница в логах/поисках по имени). Фикс: пропуск
   свойств, объявленных в `UnityEngine.Object` (`name`, `hideFlags`).

**Верификация:** build.ps1 + PVS clean; analyze.ps1 quick baseline clean (включая SourceCheck);
SourceCheck 53 PASS; tests.ps1 812 PASS; live MCP внутри/снаружи (скриншоты + `sextant`/POSE-логи).
Открытым остаётся засвет/bloom PIP из 16.7 — в финальной сборке пересвет не воспроизведён
(интерьер рендерится штатно); проверить по кадрам пользователя при следующем прогоне.

### 16.9 Зум ×1 по умолчанию и «картинка как из глаз FPP» (2026-09-17/18, live-верифицировано)

**Требования (пользователь):** «по умолчанию зума нет»; «картинка как будто мы просто смотрим из
глаз игрока» (окуляр = 1:1 обычный вид FPP).

**История диагноза (важно):**
1. Засвет ламп в окуляре (огромные цветные пятна) сначала связали с PPv2-bloom: фильтр-тест
   (bloom-cut том) убирал пятна. Затем «внутри эффектов нет» (полное отключение поста PIP) убрало
   пятна, но дало клиппинг эмиссивных объектов (сырой HDR без экспозиции/тонмаппинга) — тоже неверно.
2. Финальный live A/B (рефлексия по компонентам PIP) показал правду: **пятна даёт непрогретая
   авто-экспозиция legacy-стека** (PPv1 `EyeAdaptation`). Свежий `PostProcessingBehaviour` на PIP
   стартует неадаптированным (пересвеченным) и сходится за ~30–45 с; в этот период яркий рендер +
   bloom = огромные ореолы. После прогрева картинка PIP совпадает с глазами (проверено скриншотами:
   внутри — тот же тёмный интерьер, что и главный вид; снаружи — небо/море как было).

**Как это устроено в игре (для протокола):** ни один вид не рендерится с выключенным постом.
Каждый режим ставит свой legacy-профиль на старте и восстанавливает на выходе
(`FPPCameraController.cs:638/693`, `SectionViewCameraController.cs:1134`, `FreeFlyCameraController.cs:478`,
`MapCameraController.cs:604-605/701`, устройства `GenericCameraController.cs:307-308/349`); PPv2-тома
интерьер/экстерьер переключаются по `IsInsideBoat` (`MainCamera.cs:1568-1587`), у устройств —
`CustomPostProcessControl` (`PeriscopeController.cs:312/568`) и свои тома (`:372-384`); SSMS-туман —
per-camera (`FPPCameraController.cs:631-636`). GPU-подтверждение (RenderDoc, старый кадр): PPv1-пасс
1920×1080 читает 34×19 luminance-текстуру (адаптация экспозиции) + PPv2-bloom мип-цепочки.

**Реализация (финал):**
1. **Зум ×1** — `SextantZoom.DefaultIndex = 2` → `5` (`Steps = {6,4.5,3,2,1.5,1}`; индекс 0 = ×6).
   Тесты: `Default_Is1x_NoZoom`, `PipFov_DividesMainFov` (60/1 = 60 + три шага внутрь = 3x),
   `ZoomInOut_RoundTrips` (с дефолта ZoomOut — no-op).
2. **Гейт эффектов убран** (откат «эффектов нет»): `SextantPipPose` больше не вызывает
   `SyncPostEffects`; `SextantPipFactory.SyncPostEffects` и `SextantEffectMirror.SetEffectsEnabled`
   удалены. PIP всегда рендерится полным зеркальным стеком FPP-камеры (legacy + PPv2 + SSMS).
3. **Per-frame follow-up в `RefreshMirroredState`:** `SextantEffectMirror.RefreshPostProfile()` копирует
   живой `profile` источника на все зеркалированные `PostProcessingBehaviour` (игра меняет профиль по
   виду/отсеку, `SectionViewCameraController.cs:1134`) — member `profile` резолвится один раз.
4. **Per-frame синк камеры в `SyncCameraState`:** `nearClipPlane`, `farClipPlane`, `depthTextureMode`
   (игра пересчитывает far каждый кадр: 40 м внутри, туман-зависимый снаружи — `FPPCameraController.cs:942`).
5. **POSE-спам после закрытия** — `SextantInteriorPoseFollower.LateUpdate` early-return при
   `!SextantController.IsOpen` (раньше фолловеры писали POSE каждые ~5 с после `closed`).

**Live-верификация (2026-09-18, MCP):** `ATTACH ... fov=65.0 pipFov=65.0` (зума нет); строки
`POSTEFFECTS` в логе больше нет; внутри (после прогрева ~40 с) окуляр = тёмный интерьер как у глаз,
пятен нет; на мостике — небо/море как было; после `closed` — 0 POSE-строк. Гейты: build.ps1 + PVS
clean; analyze.ps1 quick baseline clean (173 диагностики, −2 от удалённого гейта); SourceCheck 53 PASS;
tests.ps1 812 PASS.

**Известное поведение (не баг):**
- **Прогрев экспозиции** — после каждого открытия секстанта первые ~30–45 с картинка PIP
  пересвечена (и на ярких лампах bloom раздувается) — legacy `EyeAdaptation` на свежем компоненте
  сходится к уровню глаз. Тот же паттерн у игровых device-видов (камера перископа включается только
  на время вида). Если мешает — возможный follow-up: прайминг состояния адаптации с главной камеры
  (рефлексия во внутренности PPv1) или удержание компонента тёплым.
  **Устарело (2026-09-18): см. §16.10 — legacy-стек снят с PIP, диагноз §16.9 о прогреве
  `EyeAdaptation` не подтвердился (его RT на главной камере всегда null, т.е. эффект выключен).**
- **×3 на яркую лампу** — при зуме в лампу она клиппуется (яркий источник в тёмной сцене) — как у
  реальной камеры; дефолт ×1 (для замеров) этого не показывает.

### 16.10 Legacy-стек снят с PIP-камер; остаточный пересвет окуляра (2026-09-18, live A/B)

**Проблема:** после §16.9 пользователь показал серию кадров — окуляр пересвечивает яркие объекты в
огромные шары с радужными каймами («это не то»). Диагностика:

1. **Половины окуляра идентичны** (arc=0 → обе PIP-камеры в одной позе; лог POSE + совпадающие
   сэмплы RENDERCHECK) — шар это объект в кадре, не рассинхрон половин. Фильтры 0/0 — солнечный
   диск (`SunDiscRenderer.Active = IsOpen && rightFilterCount > 0`) не при чём.
2. **A/B №1 (PIP):** `PostProcessingBehaviour.enabled=false` на обеих PIP-камерах → шар схлопывается
   до нормальной лампы (проверено в моторном/торпедном отсеках).
3. **A/B №2 (глаза):** то же выключение на главной камере → картинка глаз **не меняется** (legacy-стек
   на главной камере инертен). Вывод: legacy PPv1 применяется только к PIP и именно он раздувает лампы.
4. Внутренности (рефлексия): legacy `EyeAdaptation` на главной камере не активен (RT null — эффект
   выключен в профиле), PPv2 `AutoExposure` в игре выключен (`enabled=false`; `BrighterNights.cs`
   крутит его `minLuminance` — включается ночью). Состояния на PIP не было ни у того, ни у другого.
5. **Фикс:** legacy PPv1 больше **не зеркалируется** на PIP — `SextantEffectMirror
   .AddMirroredEffectComponents` пропускает `PostProcessingBehaviour` (комментарий класса);
   удалены `RefreshPostProfile`/`SyncEyeAdaptation` и вся мёртвая машинерия. На PIP остаются:
   PPv2 `PostProcessLayer`, `SSMSGlobalFog`, `SSMS` (per-frame `RefreshSsms`) и синк PPv2-экспозиции
   `SyncAutoExposure` (dormant, пока игра не включит AutoExposure ночью).
6. **Остаток (открыто):** в части сцен окуляр всё ещё «замывает» яркий источник мягким пятном.
   При выключенном PPv2 сырой рендер PIP = ядовито-насыщенный HDR **без пятна** → пятно делает
   PPv2-тонмаппинг пересвеченного raw-рендера PIP. Кандидаты: настройки `AdditionalCameraData`
   клона/интерьера (пока совпадают с defaults), per-layer состояние PPv2 (DOF/TAA), SSMS,
   яркость интерьерного композита (клон+VSR). Следующий шаг: бисекция PPv2-эффектов через per-PIP
   том (по образцу `SextantBloomCut`).

**Гейты:** build.ps1 + PVS clean; SourceCheck 53 PASS; Roslynator baseline clean (174); tests.ps1
812 PASS. Temp-DLL обновлён, игра перезапущена.

### 16.11 КОРНЕВАЯ ПРИЧИНА шара найдена: `AdditionalCameraData.RenderScene` (2026-09-18, live A/B на паузе)

**Симптом:** в отсеках окуляр пересвечивал лампы в огромный белый шар с радужными каёмками
(скриншоты пользователя), глаза в той же сцене — чисто; снаружи не воспроизводилось.

**Метод (важно для будущих A/B):** сцена динамична, кадры «до/после» не сравнимы, если между ними
успел пройти персонаж/смена света. Решающие A/B сняты **на паузе** (`pause on`): поза и свет
заморожены, меняется ровно один флаг через `reflect_set`. Дополнительно: `McpReflect.FindInstance`
(`Mods/McpBridge/Source/Logic/McpReflect.cs:468`) молча подставляет первый компонент типа в сцене,
если на указанном объекте его нет — **проверять `old` в ответе `reflect_set`** (там был реальный
`old=false`, т.е. попали именно в PIP, а не в главную камеру).

**Причина:** мод ставил PIP-камерам `AdditionalCameraData.RenderScene = true` навсегда
(`SextantPipFactory` + `SextantRenderPasses.Setup`). Игра пересчитывает этот флаг каждый кадр:
`GenericCameraController.PreCull:483` держит `false`, пока интерьер рисует `VirtualSceneRenderer`
(внутри лодки), и `true` снаружи. С `true` внутри PIP рисует **внешнюю сцену поверх интерьерного
композита** → пересвеченный вход → PPv2-bloom + тонмаппинг = белый шар.

**Доказательство (live, пауза, одна поза, `RenderScene` только на двух PIP):**

| Состояние | Окуляр |
|---|---|
| `RenderScene=true` (как было) | гигантский белый шар с радужными каёмками = артефакт пользователя |
| `RenderScene=false` (как у главной внутри) | чистый интерьер, совпадает с глазами |

**Дополнительно проверено и отклонено:**
- `StencilMask` на PIP: у главной внутри `true`, у PIP/клона `false` (клоны совпадают с игровой
  `Interior Camera` — у неё тоже `false`). Принудительный `true` на PIP **тоже даёт шар** (и при
  `RenderScene=false`) — значит на PIP его надо оставлять `false`, **зеркалить нельзя**.
- `UseCustomAmbient`/`Desaturation`/`ForceUpscaleBeforePostprocess`/`RenderScene` у `Interior Camera`
  = `false`/`false`/`false`/`true` — клон совпадает, трогать не нужно.
- Звёздная сфера внутри отключена (`StarsMesh.cs:80-84`), солнечный диск — только при фильтрах
  (`SunDiscRenderer.Active`), при `filters=0` не рисуется.

**Фикс:** `SextantPipFactory.SyncRenderScene(viewCamera)` — покадровое зеркалирование живого
`RenderScene` с view-камеры на оба PIP (кэш компонентов: `GetComponent` только при смене камеры).
Вызывается из `RefreshMirroredState(viewCamera)` (сигнатура изменена, вызывающий один —
`SextantPipPose.cs:67`). Плюс начальное значение берётся с живой камеры сразу при создании.
Живая проверка: после `reflect_set RenderScene=true` значение **само вернулось в `false` за кадр** —
покадровый синк работает. Внутри `RenderScene=false`, `StencilMask=false`, как надо.

**Остаток (открыто, меньший):** у ярких ламп остаётся умеренный ореол (синие лампы/щели), крупнее,
чем у глаз. Причина класса: PIP рендерит 512² и показывает половину RT, а bloom считается по всему
RT. Если помешает — следующий шаг: per-PIP том `Bloom.enabled=false` (механика `SextantBloomCut`
уже есть) или подъём RT.

**Гейты:** build.ps1 + PVS clean; analyze.ps1 quick baseline clean (174); tests.ps1 812 PASS.
Temp-DLL обновлён, игра перезапущена.

### 16.12 Bloom-cut всегда при открытом секстанте + RenderDoc-захват (2026-09-18, ночь)

**Артефакты пользователя (второй раунд, 4 скрина):** цветные ореолы вокруг ламп (синий/красный) +
жёсткие чёрные квадраты в окуляре; нестабильны (мигают). Разбор кадра пользователя (`crop` 4x):
чёрные квадраты — **чистые прямоугольники с резкими краями** (48²/32²/24²/16² px на экране, т.е.
~5–15 px в RT), вложенных размеров — сигнатура мип-цепочки; bloom мягкий → квадраты **не bloom**.
Кандидаты: SSMS/SSMSGlobalFog (мип-пирамида, зеркалится на PIP) либо портальный клиппинг.

**Фикс ореолов (сделан):** `SextantBloomCut` теперь **всегда активен, пока секстант открыт**
(`Update(open, ...)`: `rightOn = open`, `leftOn = open`; раньше — только при `filters>0`), плюс
вызов `bloomCut.Update` на открытии (`SextantController.AttachAndConfigureCameras` после `IsOpen=true`)
— иначе том ждал первого нажатия фильтра. Логика: PIP 512², bloom настроен под 1080p → ореолы ×2
и NaN-блоки от переярких ламп.

**Доказательства (живые, RenderDoc):**
- Лог: `BLOOMCUT right on (filters=0)` / `BLOOMCUT left on (filters=0)` сразу при открытии.
- A/B томов через `reflect_call SetActive(false)` → кадр с bloom: **+51 draw** (цепочка bloom
  работает), max RT `1.021–1.038`; с bloom-cut: max `0.952` (срез), −51 draw. Т.е. том реально
  выключает bloom на PIP.
- `LogAppliedOnce` теперь логирует и при `filters=0` (фильтры в строке как поля).

**RenderDoc-рецепт (рабочий, для будущих захватов):**
1. Закрыть игру. Запуск: `renderdoccmd capture -d <gamedir> -c <out> UBOAT.exe "<launcher>" <flags>`
   с env: `SteamAppId=494840; SteamGameId=494840; SteamClientLaunch=1; SteamPath=...` — **без Steam-env
   игра выходит через 3 с** после D3D-инициализации (проверено). Без RenderDoc игра запускается
   только через Steam-цепочку (Steam → Launcher → UBOAT.exe).
2. Late-injection (`renderdoccmd inject`) для D3D11 **не работает**: девайс создан до инжекта, хуки
   ловят только `D3D11CreateDevice*` → RegisterAPI не приходит, TriggerCapture молчит. Только запуск
   под RenderDoc с самого старта.
3. **Сплэш-ханг:** без флагов игра виснет на видео Deep Water Studio (`IntroScene` ждёт
   `videoPlayer.isPlaying`; под RenderDoc видео не играет). Обход: `-quicklatestgamestate`
   (грузит последний сейв) или `-quickatlantic`; в обоих режимах `IntroScene.Update` уходит в
   `LoadSandbox`/`LoadLatestGameState` на первом кадре, видео пропускается.
4. Триггер захвата: Python + `renderdoc.pymodules` (`C:\Tool\renderdoc\x64\Release\pymodules`):
   `EnumerateRemoteTargets('',0)` → ident, `CreateTargetControl('', ident, 'opencode', True)`,
   `TriggerCapture(1)`, ждать `NewCapture`. Порт по умолчанию 38920.
5. `-quickatlantic` **в отключённой сессии даёт чёрный экран** (не RenderDoc-баг).

**Отключённая RDP-сессия (важно):** `WTSQuerySessionInformation(WTSConnectState)` = `Disconnected`,
`LogonUI` запущен → игра **не рендерит главный вид вообще** (в кадре только compute-пост + чёрный
блит; 23 действия), игровой скриншот чёрный, PrintWindow чёрный. GPU при этом падает на **AMD iGPU**
(NVIDIA без активного вывода) — в логе RenderDoc `New D3D11 device created: AMD / AMD Radeon(TM)
Graphics`. PIP-камеры мода **рендерят в RT всегда** — их содержимое в захвате есть, диагностика
возможна и в отключённой сессии, но **воспроизведение артефактов и финальная проверка требуют
активного сеанса** (в тёмной disconnected-сцене артефакты не ловятся: скан RT — нет чёрных блоков
≥16×16).

**Гоча Temp-DLL (подтверждена снова):** игра не перекомпилировала мод, хотя исходники новее
(`IsAssemblyOutdated` должен был вернуть true). Обход: после `build.ps1` скопировать
`Mods\RadioNavigation\radionavigation.dll` → `%LocalLow%\...\UBOAT\Temp\radionavigation.dll` и
выставить `LastWriteTime = now`, затем рестарт. Проверка, что новый код загружен — BLOOMCUT-логи.

**Открыто:** (1) живое воспроизведение чёрных квадратов и бисекция SSMS/SSMSGlobalFog
(`reflect_set` `enabled=false` на `RadioNavSextantLeftCamera`/`RightCamera`, тип `SSMS.SSMS` /
`SSMS.SSMSGlobalFog`) → затем фикс; (2) 8+ чистых кадров одного фона в окуляре внутри лодки
(критерий пользователя) — нужен активный сеанс.

**Гейты:** build.ps1 + PVS clean; analyze.ps1 quick baseline clean (174); tests.ps1 812 PASS.

### 16.13 Автоматическая приёмка окуляра через RenderDoc (2026-09-18, ночь)

**Скрипт `ocular_scan.py`** (temp): `capture N` — триггерит N захватов через TargetControl; `scan f1.rdc ...`
— открывает каждый через replay API (`OpenCaptureFile`/`OpenCapture`), находит RT по имени
(`GetResources().name == RadioNavSextantLeftRT/RightRT`), читает данные (`GetTextureData`,
R16G16B16A16_FLOAT → numpy) и ищет: NaN/Inf, чистые чёрные блоки ≥16×16 (сетка шагом 16), негативы,
mean/max/maxpos. Exit code 0 = все чисто.

**Серии (отключённая сессия, ночь 22:00, моторный отсек, лампа в кадре, ~стабильная поза):**
- Фикс активен (bloom-cut ON): **8/8 CLEAN** (mean≈0.031, max≈1.23, nan=0, inf=0, black_blocks=[]).
- Bloom-cut OFF (bloom включён): **8/8 CLEAN** — артефакты в отключённой сессии не воспроизводятся
  ни с bloom, ни без.
- Повтор с фиксом после close/open: **8/8 CLEAN**.

**Вклад PPv2 bloom измерен:** max RT 0.953 (cut ON) vs 1.021–1.038 (OFF); профиль вокруг лампы
(ядро/16–48/48–96/96–160 px): ON 0.0101/0.0113/0.0102/0.0073 vs OFF 0.0139/0.0120/0.0097/0.0067 —
разница ~6%, т.е. **PPv2 bloom слабый**; большие цветные ореолы пользователя — скорее всего НЕ он.

**SSMS (декомпиляция `com.unity.standardassets.Runtime.dll`, `SSMS.SSMS`)**: это bloom-вариант
(OCASM) с мип-пирамидой `_blurBuffer1/2[16]`, итерации `log2(height)+_radius-8` (512²→8, 1080p→9) —
т.е. **размер ореола экранно-независимый**; параметры `_radius=7`, `_blurWeight=1`
(«artifacts are more pronounced»), `interiorIntensity=0.327`. Кандидат на чёрные квадраты — NaN в
его HDR-пирамиде, но в наших кадрах NaN=0.

**Вывод по воспроизведению:** артефакты (жёсткие чёрные квадраты + крупные цветные ореолы) в
отключённой сессии не появляются даже при включённом bloom; вероятная причина — они требуют
рендера ГЛАВНОГО вида (общие ресурсы/таймингы SRP между главной камерой и PIP), а в disconnected
сессии главный вид не рендерится вовсе. **Приёмка 8+ чистых кадров требует активного сеанса.**

**Бисекция на живом сеансе (готово к запуску):** `reflect_set` не резолвит `SSMS.SSMS` (bridge
allowlist — только `UBOAT./DWS./HarmonyLib./UnityEngine./EnigmaMod.`), поэтому в мод добавлен
probe-метод **`SextantProbe.SetSsmsForProbe("true"|"false")`** (+`SextantController.SetSsmsEnabledForProbe`):
находит на обоих PIP-камерах компоненты с именем типа `SSMS`/`SSMSGlobalFog` (по строке — без
compile-time зависимости от сборки SSMS) и переключает `enabled`. Вызов:
`reflect_call type=UBOAT.Mods.RadioNavigation.RadioNavigation.SextantProbe method=SetSsmsForProbe args=["false"]`
→ `{"ok":true,"enabled":false,"components":4}` (проверено live). Порядок бисекции: (1) SSMS+SSMSGlobalFog
`enabled=false` на обоих PIP; (2) bloom-cut off/on; (3) `useOcclusionCulling` на PIP; (4) портальные
флаги `AdditionalCameraData`. После каждого — `ocular_scan.py scan <rdc>`. A/B обязательно **на паузе**
(`pause on`) — иначе качка/персонажи двигают кадр и сравнение недействительно (проверено: даже на
паузе поза слегка дрейфует между захватами, сверять `maxpos`).

**Гейты (после добавления probe-метода):** build.ps1 + PVS clean; analyze.ps1 quick baseline clean
(174); tests.ps1 812 PASS. DLL синхронизирован в Temp, игра перезапущена.

### 16.14 Живой сеанс: артефакты НЕ воспроизводятся, приёмка пройдена (2026-09-18/19)

**Сеанс активирован** (WTS Active) → перезапуск под RenderDoc дал NVIDIA RTX 4080 и полный рендер
(8000+ действий на кадр: главный вид + PIP + UI). Проверки в моторном отсеке (та же сцена, что на
скринах пользователя: манометры слева, коридор справа), filters=0:

- **RT PIP чистые:** нет NaN/Inf, нет чистых чёрных блоков в данных RT; RT — нормальный тёмный рендер
  интерьера (mean 0.013–0.03, max 0.95–1.25).
- **Pixel history по чёрной зоне бэкбуфера** (ResourceId::180, (850,254)): draw вида (61196) падает по
  **stencil-тесту** (вне круга маски) → видна чёрная подложка `LensBacking` — это дизайн (круговое
  маскирование вида), а не сбой.
- **CircleMask (256², R8G8B8A8_SRGB)**: идеальный круг, радиус 124–126 px при любом угле (разброс ≤2 px),
  мягкая кромка только по краям текстуры.
- **`useOcclusionCulling` на PIP уже `false`** (не причина).
- **Бисекция SSMS** (probe off, тот же кадр): mean 0.0126→0.0116, max 0.954→0.946, dark 38.7%→44.8% —
  вклад мал, источником артефактов не является.
- **Приёмка (критерий пользователя):** 8/8 свежих скриншотов одного фона (моторный отсек) + 9 более
  ранних — **0 чистых чёрных пикселей внутри круга вида (r=250 от (960,540))**; окуляр рендерится
  связно, без квадратов и ореолов.

**Важная гоча анализа:** PNG-экспорт RT/бэкбуфера и просмотр скриншотов **нормализуют яркость** —
тёмные кадры (mean 0.03) выглядят «яркими», а alpha<1 композитится на белом. Все выводы о яркости
делать ТОЛЬКО по числам (float-данные через `GetTextureData` / пиксели PNG), не по глазам на превью.

**Итог:** в текущей сборке (RenderScene-синк + bloom-cut always-on) артефакты не воспроизводятся;
приёмка 8+ чистых кадров выполнена в активном сеансе. Если пользователь всё ещё видит артефакты —
нужен его свежий скриншот/условия (время суток, вид, состояние), т.к. в моторном отсеке при
10:00–10:34 и filters=0 дефект не ловится.

### 16.15 Артефакт «чёрная вспышка PIP»: пойман, механика вскрыта, фиксы не сработали (2026-09-18, вечер)

**Пойман:** `catch_artifact.py` (детект по RT, не по backbuffer) ловит **3–13% кадров**: один PIP-рендер
целиком тёмный (видимая половина RT near-black 70–93% против ~30–50% в норме; G3 mean падает до 0.001–0.01).
Backbuffer-метрика бесполезна в disconnected-сессии (кадр весь чёрный) — RT-метрика работает всегда.

**Механика (захваты + live-эксперименты):**
- G0/G1/G2 (геометрия/альбедо/нормали) у обоих PIP-кластеров **идентичны** → геометрия та же, ломается
  **освещение** (G3 — единственный тёмный буфер).
- Артефакт бьёт **случайный глаз** (сводка по прогонам: и правый, и левый) — вывод «ломается вторая
  камера» по одному кадру был ошибкой выборки.
- Портал/RenderScene — **не причина**: scissor-фикс загружен, артефакт остался.

**Проверенные и не сработавшие фиксы (не повторять):**
1. `CullingScissorRect` mirror (оставлен — корректен сам по себе, закрывает белую вспышку/утечку экстерьера).
2. RT 512×520 у правого глаза — без эффекта, откачено.
3. Dummy-камера-поглотитель 64×64 между глазами — без эффекта (dummy чист, rtD_nb≈1%), откачена.
4. Depth-порядок (PIP после main): live-прогон 0/60+1/60 оказался удачей сессии; честный A/B в одной
   сессии 8/120 (+1/+2) vs 4/120 (0/0) — эффекта нет, откачено.
5. SSMS probe off — без эффекта (3/60 vs 3/60).

**Остаток:** транзиент внутри SRP игры (похоже на сбор света/состояния на камеру), ~3–7% в активной
сессии, 12–13% в disconnected. Мод-сторонними средствами не лечится; варианты: детект+перерендер RT
(хак), копать light-коллекцию SRP, принять/задокументировать.

**Инструменты:** `catch_artifact.py` (RT+backbuffer детект, хранит пойманные .rdc), `cluster2.py`
(кластеры/RT по именам), `gbuf_check.py`, `src_check2.py`, `sets_check.py`, `win_capture.ps1`.
RenderDoc-запуск: `launch_renderdoc.ps1` — **полный путь к exe обязателен** (относительный `UBOAT.exe`
→ «Failed to launch process»); env SteamAppId/SteamGameId/SteamClientLaunch/SteamPath + `-quicklatestgamestate`.

**Гейты:** build.ps1 + PVS clean; tests.ps1 812 PASS.

### 16.16 Живой прогон лестницы фиксов (2026-09-19): ambient, SSMS, StencilMask — все без эффекта; дефект изолирован в PIP-путь

**Контекст.** В этой сессии RenderDoc-разбор довёл цепочку до буферов: композит PIP (шейдер 445/446)
читает обнулённый/NaN lit-буфер в одном кластере (C4: 35242 = 0,0,0,0; C3: 35238 живой). NaN рождается
**в light-combine** (шейдер 1068): все его входные текстуры чистые, а выход MRT [1060872,35242] содержит
Inf в канале B (C3 — сатурация 65504, C4 — Inf→NaN). Кандидат-вход: t8 = **673606** (R32, 1506×834,
`_GBufferZ`-подобный full-res буфер) с sentinel-значениями ±1.7e38; PIP рендерит 512² и читает его без
масштабирования. Game-source: `_GBufferZ` пишется только в `FogOfWarIME.cs:157` (копия в `_CameraDepthTexture`
при y>100000); `interiorAmbientColor` ставится каждый кадр в `FPPCameraController.cs:1042` и др.

**Вариант A (ambient) — реализован, задеплоен, без эффекта.**
`SextantEffectMirror.SyncInteriorAmbient` (рефлексия private-поля `interiorAmbientColor` через
`TypeMemberCache`, вызывается из `SextantPipFactory.RefreshMirroredState`). Задеплоен через
`deploy-radionav.ps1 -SkipBuild`, live подтверждён загруженным (`reflect_members` видит метод и поле;
варнинга «not synced» нет → поле резолвится). **Артефакт остался** → ambient не первопричина.

**Лестница A/B (live, 2026-09-19):**

| Рычаг | Как | Кадров | Rblack% | diff L/R | Вывод |
|---|---|---|---|---|---|
| Базовая (рубка управления) | — | 6 | 0.31–0.57 | 42–123 | сломано |
| 1. `SSMS`/`SSMSGlobalFog` off на PIP | `SextantProbe.SetSsmsForProbe("0")` (4 компонента) | 8 | 0.31–0.57 | 45–85 | **без эффекта** |
| 2. `StencilMask=true` на PIP | reflect `AdditionalCameraData.StencilMask` (assembly **com.uboat.rendering**) | 8 | 0.31–0.57 | 42–91 | **без эффекта** |

**Ключевая изоляция (решающий кадр).** В той же точке рубки управления:
- **Главная камера** (около-вид, секстант закрыт): **чисто** — виден тот же сетчатый mesh-объект
  (реальный предмет сцены, «сеть»), никаких чёрных прямоугольников, L/R-раскола, мадженты.
- **PIP-окуляр** (секстант открыт): тот же вид **битый** — жёсткие чёрные прямоугольники,
  маджента/красный blowout, правая половина стабильно 46–59% чёрная.
→ Дефект **изолирован в PIP-путь рендера**, не в сцене и не в пост-обработке (SSMS/StencilMask/ambient
все off/on — без разницы).

**Разница видов (новое):**
- Радиорубка (дерево, 31 кадр): black%=0.00, hard-blocks=0 → **чисто**.
- Рубка управления (16+ кадров): black% 16–73%, стабильный L/R-раскол → **тяжело битый вид**.
Т.е. дефект **view-dependent**: проявляется в геометрически/свето-насыщенном виде (рубка управления:
лампа + много геометрии), не в простом.

**Остаток/следующее.** Все мод-рычаги уровня постобработки исчерпаны. Гипотеза-остаток: PIP-путь
игры получает несогласованный full-res вход (`t8`/`_GBufferZ`-подобный) при 512²-рендере, либо
per-camera light-коллекция SRP рассинхронит два PIP-кластера. Кандидаты: (a) дать PIP собственный
depth/GBuffer-контекст 512²; (b) `hold-on-dark` — детект битого RT и подмена кадра на предыдущий целый
(снимает симптом гарантированно); (c) копать `DeepPipeline.CollectCameras`/light-коллекцию.

**Гейты:** build.ps1 + PVS clean; SourceCheck 53 PASS; tests.ps1 815 PASS (+3 от TypeMemberCache non-public).

### 16.17 КОРЕНЬ НАЙДЕН: вторая (интерьерная клон) камера в общем `_GBufferZ` PIP-прохода (2026-09-19, live A/B на паузе + декомпиляция DeepPipeline)

**Механизм (game-source, `com.dws.pipeline.Runtime.dll`, `DeepPipeline.Render`/`CollectCameras`, декомпиляция ilspycmd):**
- `CollectCameras` собирает список камер на один корневой рендер: `cameras[0]` = корневая камера
  (`isFirstCamera:true`), и **если у корневой камеры активен `VirtualSceneRenderer.VirtualCamera`**,
  добавляет **вторую камеру** — `cameras[1]` = `VSR.VirtualCamera` (`isFirstCamera:false`).
- Обе камеры рендерятся в **ОДИН общий `_GBufferZ`** (`GetTemporaryRT(_GBufferZ, width, height, 32,
  RenderTextureFormat.Depth, ...)`), затем `CopyTexture(_GBufferZ, _CameraDepthTexture)`, и на нём же
  строится освещение (light-combine читает `_GBufferZ`/`_CameraDepthTexture`).
- Мод (`SextantInteriorMirror`) ставит каждому PIP свой `VirtualSceneRenderer` с клоном интерьерной
  камеры (Route B) → **в PIP-проходе две камеры пишут в один GBuffer/depth**; порядок/перезапись
  рассогласуют depth, который читает light-combine → тот самый NaN/Inf/overflow в канале B и
  L/R-раскол.

**Решающий live A/B (пауза, одна и та же точка рубки управления, один кадр):**

| Состояние | Окуляр | Вывод |
|---|---|---|
| **VSR OFF** (обе PIP-камеры) | сетка рендерится корректно (оранж/красн), **нет магенты/blowout**, нет чёрных прямоугольников | чисто-механически |
| **VSR ON** | сетка **маджента/белый blowout**, жёсткий прямоугольный срез, синий патч | сломано |

**Как воспроизвести A/B:** `pause on` → reflect `enabled=false` на `UBOAT.Game.Scene.Effects.
VirtualSceneRenderer` (assembly `com.uboat.rendering`) на `RadioNavSextantLeftCamera`/`RightCamera` →
`Screenshot_frame`. **Гоча:** без паузы `SextantInteriorMirror.Sync` возвращает `enabled=true` каждый кадр
(`reflect_set` вернёт `old:true`), поэтому A/B обязателен **на паузе**.

**Уточнение:** VSR OFF убирает грубую коррупцию (маджента/чёрные прямоугольники/L-R blowout), но остаётся
остаточный дефект (сине-фиолетовый патч на меsh + общая темнота) — это отдельный, ранее известный
residual (цвет/ambient). Первопричина грубого артефакта — **вторая камера в общем GBuffer**.

**Следствие для фикса.** Правильный фикс — дать PIP-проходу **раздельные GBuffer/depth на камеру**, либо
не добавлять вторую камеру (не ставить VSR), а композитить интерьер иначе; либо убрать клон-камеру и
рендерить интерьер прямой камерой (тогда PIP = одна камера, `_GBufferZ` согласован). Кандидаты:
(1) `hold-on-dark` как симптом-гард; (2) рефактор Route B без второй камеры; (3) Harmony-патч
`DeepPipeline.CollectCameras`/`Render` для per-camera `_GBufferZ` (рискованно).

**Гейты:** build.ps1 + PVS clean; SourceCheck 53 PASS; tests.ps1 815 PASS.

### 16.18 Hold-on-dark: реализован, детект работает, НО порядок кадра не даёт удержать (2026-09-19, live)

**Что сделано.** Новый `SextantRenderGuard` (+ чистый `RenderGuardPolicy`, +15 тестов): per-frame копирует
хороший кадр каждого PIP-RT в hold-RT и, если кадр «тёмный», показывает hold вместо live. Порог:
≥45% сэмплов ниже luma 16/255.

**Калибровка (важные негативные результаты, НЕ повторять ошибки):**
1. `ReadPixels` большого региона в мелкую текстуру возвращает **чёрное** на этом Unity-билде (все 8×8-чтения
   мерили 1.00 dark) — не использовать.
2. Разреженная 1×1-сетка **недо-сэмплит** артефакт (точечные яркие пятна сбивают долю).
3. Рабочий способ: `Graphics.Blit` в 8×8 RT → `ReadPixels` ровно 8×8 → доля тёмных.
4. Артефакт в RT-чтении **чисел не даёт**: NaN/Inf/blowout (>2.0) = 0 на 400 кадров, Lmax ≤ 0.95. То есть
   финальный RT (после тонмапа/clamp) численно «чистый»; коррупция видна только пространственно/на дисплее.
5. Рубка управления — **изначально тёмная сцена**: чистые кадры тоже 0.73–0.86 «тёмных». Порог 45% поэтому
   срабатывает почти всегда → детект ненадёжен для этого вида.

**Live-проверка.** Guard загружен (`reflect_members`), лог `RENDERGUARD starting hold` появился 1 раз. Но
захваченные битые кадры (правый глаз: чёрный + зелёные/синие зубчатые фигуры, R_blk=0.90–0.99) **всё равно
показаны** — guard не удержал.

**Причина (порядок кадра).** `SextantController.Update` (где вызывается guard) идёт **до** SRP-рендера PIP
на этот кадр: guard проверяет RT **прошлого** кадра, а окуляр показывает **текущий** (ещё не проверенный)
RT. Подмена текстуры не помогает — свежий битый контент уже лёг в live-RT и выводится.

**Следствие.** Чтобы hold-on-dark работал, детект+подмена должны идти **после** рендера камеры:
`RenderPipelineManager.endCameraRendering` (SRP) или `Camera.onPostRender`, а не `Update`. Только тогда
guard видит текущий кадр до его показа.

**Гейты:** build.ps1 + PVS clean; SourceCheck 53 PASS; tests.ps1 830 PASS (+15 RenderGuardPolicy).

### 16.19 Hold-on-dark закрыт как НЕосуществимый в этой архитектуре (2026-09-19, live-калибровка)

**Попытка.** Перенёс guard с `Update` на пост-рендер хуки, чтобы увидеть текущий кадр до показа.

**Калибровка хуков (все негативные):**
1. `RenderPipelineManager.endCameraRendering` — **0 вызовов** за сессию (`bind l=True r=True`, но ни одного
   cb-лога). Игровой `DeepPipeline` вызывает пакетный `RenderPipeline.Begin/EndCameraRendering` напрямую,
   события менеджера не поднимаются.
2. `RenderPipelineManager.endFrameRendering` — **тоже 0** вызовов.
3. `Camera.OnPostRender` на PIP-`MonoBehaviour` — **тоже 0** (проверено по существующему
   `SextantCameraProbe.OnPreRender`: в этой сессии `PRERENDER` = 0 логов, хотя компонент на обеих PIP
   камерах есть и `POSE`/`MANUALRENDER` идут). То есть Unity не шлёт per-camera render-сообщения для этих
   камер в текущей конфигурации SRP (более ранние PRERENDER-логи — из другой сессии/конфигурации).

**Вывод.** Ни одного пост-рендер хука, доступного моду, не существует: SRP рисует PIP **после**
`LateUpdate`, а все коллбеки (pipeline-события и camera-сообщения) либо не срабатывают, либо идут раньше.
Значит детект+подмена физически не могут увидеть текущий кадр до его показа → **hold-on-dark неосуществим**
мод-сторонними средствами. Код guard'а (SextantRenderGuard/RenderGuardPolicy/тесты) **удалён**, чтобы не
держать мёртвый код; `SextantCameraProbe`/`SextantPipFactory`/`SextantController` возвращены к состоянию
до эксперимента.

**Остаётся единственный реальный путь:** архитектурный фикс §16.17 — развести GBuffer/depth PIP-камеры и
её интерьерного клона (сейчас общий `_GBufferZ`). Это либо Harmony-патч `DeepPipeline.CollectCameras`/
`RenderDeferredCamera`, либо переделка Route B так, чтобы интерьер попадал в PIP без второй камеры.

**Гейты (после откатa):** build.ps1 + PVS clean; SourceCheck 53 PASS; tests.ps1 815 PASS.

### 16.20 Попытка «интерьер напрямую на PIP, без клона» — чёрный окуляр; VSR обязателен (2026-09-19)

**Гипотеза.** Убрав вторую (клон) камеру и заставив PIP рендерить интерьер сам (interior mask +
`RenderScene=true`), получить одного писателя `_GBufferZ` и устранить рассинхрон §16.17.

**Живой A/B на паузе (успешный по виду).** `VirtualSceneRenderer.enabled=false` на обоих PIP +
`RenderScene=true` + `Camera.cullingMask=545264640` (маска Interior Camera; дефолт PIP 1599384213) →
окуляр **чистый**: обе половины корректно освещённый интерьер, без чёрных прямоугольников/магенты/blowout.
Отсюда и родилась гипотеза.

**Реализация в моде → ЧЁРНЫЙ окуляр.** Переписал `SextantInteriorMirror` на прямой режим (без клона/VSR),
`SextantPipPose.SyncCameraState` получил interior-mask, удалил `SextantInteriorPoseFollower`. Граф
резолвится (`INTERIOR graph resolved: source='Interior Camera' mask=545264640`), оба PIP привязаны,
`RenderScene=true`, `cullingMask=545264768` (interior|sun) — но **обе половины 20/20 кадров = 100% чёрные**.

**Причина (важно).** Интерьер живёт в **отдельной VirtualScene** («Type IXC Interior»), чьи трансформы
двигает именно `VirtualSceneRenderer` (`vs.Target` / `OverrideLocalTransformation`). Без VSR геометрия
интерьера **не позиционируется** относительно камеры → в фрустум PIP ничего не попадает → чёрный.
В успешном A/B клон-камера **существовала** (я лишь отключил VSR-компонент, но клон и его позиция
остались), поэтому интерьер был виден.

**Вывод.** VSR **обязателен** для позиционирования интерьера. Проблема §16.17 — не наличие VSR, а то,
что клон является **вторым писателем** `_GBufferZ`. Правильный фикс должен сохранить VSR, но убрать
второго писателя: например, не добавлять клон-камеру в `CollectCameras` (Harmony-патч
`DeepPipeline.CollectCameras`, чтобы PIP не получал `cameras[1]`), либо дать клону отдельный GBuffer.

**Откат.** `SextantInteriorMirror.cs` восстановлен (clone-based), `SextantInteriorPoseFollower.cs`
восстановлен, `SextantPipPose.cs` возвращён к исходной сигнатуре. Гейты зелёные, live-проверено: окуляр
снова показывает интерьер (радиорубка), базовая линия 815 PASS.

**Гейты:** build.ps1 + PVS clean; SourceCheck 53 PASS; tests.ps1 815 PASS.

### 16.21 Harmony-патч DeepPipeline.CollectCameras: один писатель GBuffer (2026-09-19, реализован, применён)

**Идея.** §16.20 доказал: убирать VSR нельзя (он позиционирует интерьер), но клон-камера — лишний ВТОРОЙ писатель _GBufferZ. Патч глушит участие клона в рендер-проходе, оставляя VSR.

**Реализация.** Source/Logic/PipCloneCameraPatch.cs: Harmony prefix+postfix на DeepPipeline.CollectCameras(ScriptableRenderContext, Camera) (private instance; тип резолвится строкой из com.dws.pipeline.Runtime). Prefix: если корневая камера — PIP (SextantController.IsPipCamera), временно сбрасывает приватное поле renderingEnabled у VirtualSceneRenderer (через TypeMemberCache), из-за чего guard в CollectCameras не добавляет cameras[1]; postfix возвращает значение. Позиционирование интерьера делает VSR.LateUpdate (до SRP-рендера), поэтому глушение во время CollectCameras его не ломает. Регистрация — в HarmonyPatchInstaller.Apply (отдельный target-assembly, non-fatal).

**Гейты:** build+PVS clean; SourceCheck 53; tests 815; analyze.ps1 baseline clean (RN00x — 0 на новом патче).

**Live (control room, 20 кадров):** в логе Patched DeepPipeline.CollectCameras (PIP single-writer). Один кадр с тёмной левой половиной (L_blk=0.94) показал: слева — структурная геометрия интерьера (силуэты механизмов), справа — сетка рендерится корректно с синей подсветкой лампы, БЕЗ магенты, БЕЗ жёстких чёрных прямоугольников (сравн. с pre-patch кадром: маджента + жёсткие срезы + зелёно-синие зубцы). Грубая коррупция класса §16.17 исчезла.

**Честная оговорка.** Числовые метрики (hard-block count, saturated-color fraction) на моих выборках НЕ различают pre/post, потому что (а) метрики ловят легитимную тёмную технику и синюю подсветку лампы, (б) pre/post кадры сняты в разных видах/сессиях. Строгий controlled A/B (тот же вид, патч вкл/выкл) не сделан. Остаточная тёмная левая половина — известный отдельный residual (тёмный регион).

**Осталось:** controlled A/B на одном виде (патч вкл/выкл) + проверка снаружи лодки; при подтверждении — закрепить SourceCheck-контрактом.

**CONTROLLED A/B (2026-09-19, тот же вид, пауза, gate RadioNav.PipSingleWriter 1/0) — ПОДТВЕРЖДЕНО:**
- PATCH ON: связный интерьер (сетка рендерится сплошным оранжево-песочным объектом, трубы/механизмы видны, синяя подсветка лампы по краю), БЕЗ белого blowout, БЕЗ жёстких прямоугольников, БЕЗ зелёно-циановой каши, половины согласованы. Метрика: mean 27-31, blk 0.59, sat ~0.02, стабильно.
- PATCH OFF: грубая коррупция возвращается — белый/циановый blowout на левой половине, жёсткие срезы, сетка разбита на белые фрагменты, зелёно-циановые каймы. Метрика: mean 24-57, blk 0.48-0.71, sat 0.009-0.049 (нестабильно — сигнатура бага).
- Тот же вид, та же пауза, различается только gate → эффект патча изолирован. Артефакт §16.17 устранён.

> **ОТЗЫВ §16.21 (2026-09-19, вечер).** Пометка «ПОДТВЕРЖДЕНО» выше **отозвана**: префикс патча был инертен — позиционный `object __0` привязался к первому аргументу оригинала (`ScriptableRenderContext`), а не к камере, `__0 is Camera` всегда false → выход на первой проверке, `__state` всегда null → постфикс no-op. Т.е. ON≡OFF, а разница в «контролируемом» A/B — дрейф сцены между захватами (кадры различаются даже на паузе: мерцание ламп/туман — зафиксировано отдельно). Доказательства инертности: код + ноль строк подавления в логе + `renderingEnabled=true` live на VSR обеих PIP. По контракту Harmony `__n` — zero-based индекс аргумента оригинала (`CollectCameras(ScriptableRenderContext, Camera)` → камера это `__1`); тип `object` для инжекта легален, поэтому Harmony молча вызывал префикс каждый раз — и он молча ничего не делал. Настоящий фикс — связка §16.22.

### 16.22 Один писатель GBuffer: фикс сигнатуры + корень-писатель + прямое позиционирование (2026-09-19, реализовано, применено)

**Почему не хватило одной строки.** Цикл GBuffer (`for i=1..camerasCount RenderGBuffer(array[i])`, затем `array[0]` только `if (RenderScene)`): при *работающем* подавлении клона и `RenderScene=false` на корне PIP (зеркалилось с view-камеры) писателей становилось **ноль** — GBuffer пуст, окуляр полностью чёрный (проверено live: suppression + root false = black). Поэтому фикс — связка из трёх частей:

1. **Сигнатура** (`Source/Logic/PipCloneCameraPatch.cs`): `SuppressCloneForPip(Camera __1, ref object __state)` — привязка ко второму параметру оригинала по позиции (имя в обфусцированной сборке неизвестно). Гейт `RadioNav.PipSingleWriter` (PlayerPrefs, default 1) — A/B вкл/выкл без передеплоя.
2. **Корень — единственный писатель** (`Source/UI/SextantInteriorMirror.cs:Sync`): внутри лодки, после `SyncRenderScene`, форсит `RenderScene=true` на корне PIP (view-камера внутри держит false) + interior-маска через `SextantPipPose.ApplyMaskToPasses` (+ `InteriorMask` с игровой Interior Camera).
3. **Прямое позиционирование** (новый `Source/UI/SextantPipInteriorPose.cs`, `[DefaultExecutionOrder(10000)]`, LateUpdate после VSR): `Pip.position = InteriorCamera.position` — мировая позиция игровой интерьерной камеры, которую двигает сам пайплайн (обратной связи нет). Поворот не трогается (yaw/алидада из Update). Попытка копировать позицию МОД-клона первой дала расходящийся фидбэк (PIP улетел в [1915, 250822, 7891]): позицию клона считает `VSR.LateUpdate` ИЗ PIP-трансформа.

**Положительное доказательство срабатывания.** Счётчик `PipCloneCameraPatch.SuppressionCount` (+ одноразовый лог `PIP single-writer suppression active (first CollectCameras intercepted)`, читается live через рефлексию): верификация опирается на факт подавления, а не на отсутствие артефакта. Урок `__0`: «нет ошибок» ≠ «работает» — тихий no-op детектируется только позитивным счётчиком.

**Live-верификация.** В логе: `Patched DeepPipeline.CollectCameras (PIP single-writer)` + `suppression active`. Рубка управления + торпедный отсек: грубая коррупция класса §16.17 (маджента/чёрные прямоугольники/L-R blowout) отсутствует. Пользователь подтвердил ~90% (22:45 2026-09-19: «ничего не моргает не трясётся»). Остаток — расхождение освещения отдельных предметов (трубы торпедного отсека, «белый/серый → синий» в окуляре): отдельный класс дефекта (рассогласование, не коррупция), расследуется далее (§16.23-трек: ContactShadows, PPv1-градация, pass-структура).

**Гейты:** build.ps1 + PVS clean; SourceCheck 53 PASS; tests.ps1 815 PASS; analyze.ps1 baseline clean.

### Стенд «Трубы»: золотой прогон (2026-09-19, реализован)

**Назначение.** Детерминированный repro + численный контроль остаточного расхождения «белый/серый → синий» (модель изображения не видит — приёмка только по числам).

**Золото.** Сейв `sextant-pipes`: Klaus Graf в Stern Torpedo Room лицом к кормовым аппаратам, FPP, день 12:17 04.11.1941, облачность 0.37, тумана 0, лодка на поверхности. Фолбэк позы: `player_jump point="stern_torpedo"` (слоты `Slot 01/02, Work` + `Stern Torpedo Launcher` — имена неуникальны, алиас надёжнее).

**Протокол пары (на итерацию).** `load sextant-pipes` → `time_skip to_hour=11` → FPP → `pause on` → кадр A (секстант закрыт, `screenshot_frame`) → открыть пробой (`arc=0`, фильтры 0/0, позу не трогать) → кадр B (`screenshot_frame`) → закрыть → `pause off`. Зум заморожен **div=3.0** (липкий `SextantZoom`, на reopen не сбрасывается; 65/21.667 live; инжект `Minus` мост отклонил `bad-key` — сброс на ×1 только кодом, позже).

**Метрика** (`Tools/pipes_metric.ps1 -A a.png -B b.png [-Rect "x,y,w,h"]`): диск `r=250@(960,540)` (DISC) + популяция ярких поверхностей диска `min>110, max<250` (BRIGHT — без ручных ректов, устойчива к прицелу/тексту) + опц. RECT + survey-GRID для выбора ректов. Выход: mean/med RGB, **синий избыток `b_r`/`b_g`**, max, blackfrac.

**Золотые числа (пара 2026-09-19, div=3.0):**
- A (глаза): `DISC mean=85.0,81.2,78.9 b_r=-6.0 blackfrac=0.000`; `BRIGHT n=12236 mean=187.6,185.1,185.0 med=191,189,190 b_r=-2.6 b_g=-0.1` — нейтраль.
- B (окуляр): `DISC mean=81.1,65.9,58.7 b_r=-22.4 blackfrac=0.124`; `BRIGHT n=6550 mean=173.7,183.7,195.7 med=175,184,197 b_r=+21.9 b_g=+11.9` — синий избыток.
- **Баг-метрика: `BRIGHT b_r` +21.9 (B) vs −2.6 (A), Δ≈24.5.** Критерий фикса: Δ→~0 (|Δ|<5), без регресса max/blackfrac/коррупции. Кадры: `Screenshots/mcp-frame-639254413629300119.png` (A), `...14329321580.png` (B).
- Побочно: лампа в окуляре тёплая (R−B≈+80), синеют именно диффузные поверхности — подпись отсутствующей тёплой градации, не экспозиции (автоэкспозиция днём мертва — лог `:218 srcSet(enabled=False)`).

**Гочи стенда.** RENDERCHECK-периодики больше нет (ушёл с RenderGuard) — пары только через `screenshot_frame`. PNG: сравнивать дельты одним инструментом (§16.14). Пауза обязательна (качка/экипаж/мерцание). Ночная серия — отдельно (`to_hour=23`, под экспозицию).

### Шаг 3: ContactShadows-миррор — реализован, тинт не двигает (2026-09-19)

**Реализация** (`Source/UI/SextantPipFactory.cs`): `SyncContactShadows()` из `RefreshMirroredState` — копия-if-changed контактного блока с `cachedViewData` (main) на оба PIP: флаг + 3 пишущихся параметра типизированно, 4 get-only (`Length/FadeDistance/DistanceScaleFactor/SampleCount`, CS0200) — сравнением через геттеры, записью через приватные backing fields (`TypeMemberCache`, паттерн `SyncInteriorAmbient`). Гейт `RadioNav.ContactShadowMirror` (default 1). One-shot лог `CONTACTSHADOWS mirrored onto PIP (first apply)`. Без `GetComponent` в горячем пути (кэши), явные null-гарды, C# 7.0.

**Live:** лог есть; на PIP `ContactShadows=true` (было false), `contactShadowsLength=0.2` (было 0.15) — рефлексия пишет приватные поля. Гейты: build+PVS clean, SourceCheck 53, tests 815.

**A/B (стенд, пауза, div=1.0):** B1 (флаг true) `BRIGHT b_r=+16.8` vs B0 (флаг false — `reflect_set` на оба PIP при гейте 0; параметры остались смирроренными) `b_r=+15.2` — Δ=1.6, сетки поблочно идентичны. **Флаг контактных теней тинт не двигает — H1 как драйвер синевы закрыта.** Миррор оставлен включённым (паритет с игровыми камерами по построению, после settle записей нет). Кадры: A1 `...7440166721`, B1 `...7708906338`, B0 `...9116866167`. Новая золотая пара ×1 (паритет FOV): A1 `BRIGHT b_r=-2.5`, B1 `+16.8`, Δ≈19.3.

**Уроки протокола:** (1) close секстанта PIP-камеры НЕ уничтожает (переиспользуются) — OFF для A/B только через гейт + `reflect_set`, не reopen; (2) после рестарта зум сброшен в ×1 (рантайм-стейт) — золото v2 на ×1, div=3.0 в историю; (3) открытие пробой сразу после лоада может дать `cannot-open` — ретрай.

### Шаг 4: PPv1 ре-тест — миррор жив, эффекта ноль (2026-09-19)

**Реализация** (`Source/UI/SextantEffectMirror.cs` + `SextantPipFactory.cs`): PIP получают `PostProcessingBehaviour`, профиль — живой ссылкой с view-камеры покадрово (`SyncPostProfile` из `RefreshMirroredState`; контроллеры меняют профили по режимам). Без типизированного API (сборки PPv1 нет в референсах `build.ps1` — CS0234): компонент как `Behaviour`, профиль как `object` через зеркалированные мемберы (`FindProfileField` + `TryGetProfile/TrySetProfile`, гарды+WarnOnce, паттерн SSMS). Гейт `RadioNav.Ppv1Mirror` (default 1; OFF = `enabled=false`, дофиксовый вид, reopen не нужен). One-shot лог `PPV1 profile mirrored: <name>`. Комменты §16.10-решения обновлены (вывод «не зеркалить» устарел вместе с битым пайплайном §16.17). Гейты: build+PVS clean, SourceCheck 53, tests 815.

**Live:** лог есть; профиль на PIP = `UBOOT Postprocessing (FPP NG)` — тот же, что на main. **A/B: B2 (ON) `BRIGHT b_r=+16.6` vs B0 (OFF) `+16.7`, Δ=0.1 — ноль.** Параллельно: PPv1-off на main (глаза, `old:true` проверен): −1.1 vs −1.0 — ноль; PPv1-off на Interior (PPv2-слой): −0.3 vs −0.3 — ноль. **Вывод: legacy PPv1 в этом SRP инертен везде (компоненты исполняются? нет видимого эффекта) — H2 как драйвер тинта закрыта.** Миррор оставлен (паритет по построению, после settle записей нет). Кадры B2 `...405406446`, B0 `...07663178`.

### A/B-реестр остаточного тинта: всё, что проверено (2026-09-19, стенд)

Метрика везде `BRIGHT b_r` (синий избыток ярких поверхностей диска), пары в одном paused-окне.

| Рычаг | B/A | Δ | Вердикт |
|---|---|---|---|
| ContactShadows PIP true/false | +16.8 / +15.2 | 1.6 | OUT |
| PPv1 PIP on/off | +16.6 / +16.7 | 0.1 | OUT |
| PPv1 main on/off (глаза) | −1.1 / −1.0 | 0.1 | OUT (PPv1 инертен) |
| PPv2 Interior on/off (глаза) | −0.3 / −0.3 | 0 | OUT |
| SSMS+SSMSGlobalFog PIP on/off | +22.5 / +22.4 | 0.1 | OUT |
| ShadowDistance PIP 370/30 | +24.6 / +24.6 | 0 | OUT |
| Pass default/custom PIP | +23.0 / +22.9 | 0.1 | OUT |
| ContactShadows main on/off (глаза) | −1.3 / −1.4 | 0.1 | OUT |
| ContactShadows interior on/off (глаза) | −1.3 / −1.3 | 0 | OUT |
| PPv2 PIP on/off | +16.6..25 / **+3.3** | −13..−22 | МЕХАНИЗМ (PPv2 охлаждает/темнит PIP) |
| PPv2 main on/off (глаза) | −1.1 / **−33.3** | −32 | МЕХАНИЗМ (PPv2 охлаждает глаза в нейтраль) |
| UseCustomAmbient=false PIP | чёрный кадр (max 69) | — | load-bearing флаг, не рычаг |
| Оверлеи UI (цвета) | все white/transparent | — | OUT |
| Автоэкспозиция | `srcSet(enabled=False)` day | — | OUT (день; ночь отдельно) |

**Решающий инструмент — `SextantProbe.RtMean`** (`Source/UI/SextantController.cs: RtMeanForProbe` + probe; Blit→8×8→ReadPixels, путь §16.18): means PIP RT **`left=[72,74,80] right=[72,74,80]`, b_r=+8 — синева УЖЕ в RT.** Дисплей (материал `RadioNavOpaqueView` из бандла, белые множители) оправдан. Тинт — camera-side, в рендере PIP.

**Пересмотренная модель.** Глаза = InteriorRAW + mainPPv2(+32 в холод) → −1; PIP = PIPRAW + PIPPPv2(+13..22 в холод) → +17..25. Имплицитные RAW: InteriorRAW ≈ −33, PIPRAW ≈ +3 — разрыв 36 единиц при тех же мешах/свете/пробах.

### Шаг 6b: generic probe + тогглы main/Interior — всё inert (2026-09-19)

**Инструмент** (`SextantController.SetComponentEnabledForProbe(camera, type, enabled)` + `SextantProbe.SetComponent(camera, type, "1"/"0")`): тоггл компонентов по ИМЕНИ ТИПА на именованной камере — без compile-зависимостей, открывает всё, что мост не резолвит. Гейты: build+PVS clean, SourceCheck 53, tests 815.

**Серия на глазах (пауза, секстант закрыт, BRIGHT b_r база −0.9):**

| Что выключено | b_r | Δ | Вердикт |
|---|---|---|---|
| SSMSGlobalFog на main | −0.5 | +0.4 | OUT |
| SSMSGlobalFog+SSMS на main | −0.5 | +0.4 | OUT |
| CP_SSSSS_Main на main (есть, components=1) | −0.8 | +0.1 | OUT |
| AmbientOcclusion на Interior (есть, components=1) | −0.8 | +0.1 | OUT |
| ScreenSpaceReflection на Interior (есть, components=1) | −0.8 | +0.1 | OUT |

**Вывод: весь per-camera эффект-стек глаз (SSMS/фог, CP_SSSSS, AO, SSR, PPv1, ContactShadows всех уровней, оба PPv2-слоя Interior) — inert для интерьерной картинки.** "Непроверенные подогреватели" из модели выше проверены и закрыты — разрыв RAW остаётся фактом: InteriorRAW ≈ −33 vs PIPRAW ≈ +3 при идентичных сценах.

**Остаток кандидатов (финал):** (1) writer-пассы Interior (2 кастомных vs default у PIP — единственный непроверенный структурный дельта; нужен дамп содержимого: маски/флаги/SSR); (2) разрешение 512²; (3) shared PPv2 `fog`-инстанс (аудит); (4) VSR-позиционирование (микро). PIP-only warm-том — production-формат независимо от корня (критерий Δ<5 + валидация рубка/ночь).

**Инцидент гейта.** `RadioNav.PipSingleWriter` найден в `0` (застрял с A/B §16.21; PlayerPrefs персистентны) — вся сессия шла с двумя писателями. Восстановлен в `1`, подавление проверено (`SuppressionCount=2496`), пара переснята: тинт остался (+25.4 vs −2.6) — гейт не причина тинта. УРОК: гигиена гейтов — фиксировать все `RadioNav.*` до/после A/B; добавлено в протокол.

**Уроки протокола (дополнение).** (4) лоадсейв ставит чёрный кадр если снимать сразу — ждать ~15 с settle после `load`; (5) стойка дрейфует между окнами (качка лодки unpaused!): валидны только внутриоконные пары, золотые числа — референс; (6) `screen_set` 1920×1080 каждый прогон (окно было 1360×745 — геометрия метрики плывёт); (7) `wait_for` падает сразу если мост в окне рестарта — смотреть `boot.log`, ретраить `get_state`; (8) открытие пробой после лоада — ретрай; (9) PIP-пробы чтения (`reflect_get` полей) работают и на приватные поля (`contactShadowsLength=0.2` проверен).

### 16.23 UseCustomAmbient: корневая причина синевы (2026-09-19, ИСПРАВЛЕНО)

**Механизм (декомпилят `com.dws.pipeline.Runtime.dll`, ilspycmd, `RenderDeferredCamera`/`CameraData.Initialize`).** `UseCustomAmbient=true` у камеры: (а) убирает `LightProbe` из per-object данных (`ReflectionProbes|LightData` вместо `+LightProbe|ProxyVolume`), (б) лимит источников 64 exterior vs 48, (в) `USE_CUSTOM_AMBIENT`-вариант GBuffer. Игра рендерит интерьер камерой с `false` (InteriorCamera → запечённые тёплые пробы); PIP нёс дефолт `true` (как exterior-корень) → пробы отсечены → холодный флэт. Контрольный A/B live: `false` → `BRIGHT b_r=+22.9 → −2.9` (Δ25.8; глаза −2.5). Первое измерение дало чёрный кадр (max 69) — транзиент переключения keyword-варианта, лечится settle (пауза + 5–6 с; перепроверено RtMean `[54,50,47]` + кадром).

**Шиппед:** `SextantInteriorMirror.SyncAmbientMode` (выделен из `Sync` — гейт сложности 15 ронял инлайн): внутри → значение Interior-камеры (`false`), снаружи → `true`; change-detected, гейт `RadioNav.AmbientModeMirror` (default 1), one-shot лог `AMBIENT-MODE mirrored onto PIP (inside=True)`. Live: лог есть, `UseCustomAmbient=false` на PIP. Гейты: build+PVS clean, SourceCheck 53, tests 815.

**Подтверждения:** стенд (числа выше) + пользователь визуально («рендер выглядит корректным», дверцы/трубы нейтральные в окуляре и глазами). Остаток валидации: сложный вид (рубка — возврат коррупции), ночь (экспозиция), 1024².

### Шаг 7: guard от самоследования PIP + гигиена гейтов (2026-09-19)

**Проблема (репорт пользователя):** камера секстанта иногда прилипает к одному углу (поворот стоит, позиция едет), лечится только полным рестартом игры.

**Механизм (доказан археологией лога + чтением кода):** `CameraResolver` возвращал PIP-камеры (`camera: 'RadioNavSextantLeftCamera' rt=True` — десятки строк в `output_log (previous).txt`, всегда на переходах открыть/закрыть) + строка `no rendering camera found` при загрузке сейва = окна `Camera.main == null` реально существуют. В таком окне резолвер падает на хук-кэш (последняя pre-cull камера — PIP, они рендерятся каждый кадр) → `SextantPipPose.Update` копирует PIP→PIP (угол frozen), а `SextantPipInteriorPose` (order 10000, источник — живой `InteriorCamera`, резолвер не использует) продолжает двигать позицию. Рестарт сносит мемоизацию (`renderingCamera`/`gameMainCamera`/статики) — чинится. `testAimActive` исключён (единственный сеттер — `SextantProbe.Aim`, вызовов не было); исключений в логе ноль; гасить контроллер модовому коду нечем (grep).

**Фикс** (`Source/UI/SextantPipPose.cs`, триггер-независимый): после `TryResolve` — `if (SextantController.IsPipCamera(viewCamera))` (готовый предикат, reference-равенство) → fallback на последнюю хорошую камеру (`lastGoodViewCamera`, сброс в `Reset()`) + one-shot warn `PipSelfFollow`; некуда откатываться — пропуск кадра. Резолвер может флэпить сколько угодно — поза никогда не следует сама за собой. Гейты: build+PVS clean, SourceCheck 53, tests 815. Live: варнинга нет в норме (ноль ложных срабатываний).

**Гигиена гейтов (выставлено live):** `PipSingleWriter=1`, `ContactShadowMirror=1`, `Ppv1Mirror=1`, `TestAim=0` (был 1 — чужой хвост старых A/B), `AmbientModeMirror` отсутствует (= default 1). Замедленный бут (~3 мин intro-кликов) — в протокол бута.

**Финальная верификация ambient-фикса (шиппед-билд, стенд, div=1.0):** A `BRIGHT b_r=−5.7` vs B `−6.0` — **Δ=0.3** (было Δ≈25 до фикса). Тинт закрыт end-to-end: синева ушла и численно, и визуально (подтверждение пользователя).
