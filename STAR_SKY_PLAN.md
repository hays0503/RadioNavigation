# STAR_SKY_PLAN — Точное звёздное небо для RadioNavigation

> Версия: 1.1 (2026-08-19). Статус: реализация частично выполнена; Phase 0 и live-приёмка не завершены.
> Актуализация 2026-08-24: планеты/Луна/каталог/созвездия реализованы и приняты (ADR-021/022/023/024/025/026); тестовая база выросла 182→263 PASS; конвертер каталога в репо отсутствует (см. §5).
> Цель документа: стартовать реализацию в новой сессии без повторного исследования.

---

## 1. Цель

Точное звёздное небо для астронавигации в кампании 1939–1945:
- Звёзды по каталогу Hipparcos/Gaia3 из Stellarium (позиции точнее 0.001″)
- Созвездия 1:1 с современной культурой Stellarium (те же полилинии между теми же HIP-звёздами)
- Планеты Венера/Марс/Юпитер/Сатурн по средним кеплеровым элементам (реализовано; точность ~1′ ещё не подтверждена)
- Луна — реализована low-precision модель; решение после Phase 0 остаётся открытым
- Декоративное небо игры остаётся фоном; наш слой — поверх

### Критерии приёмки

1. Alt/Az 10 контрольных звёзд совпадает со Stellarium с точностью ≤ 0.1° на даты 1939/1942/1945
2. Полярная ≈ широте места (±0.2°); Канопус/Ахернар невидимы на 50°N
3. Фигуры созвездий визуально совпадают со Stellarium 1:1; линии «тонут» в горизонте
4. Появление/исчезновение звёзд синхронно с игровым небом (сумерки)
5. 60 fps при ускорении времени ×32
6. Тесты зелёные (`powershell -File tests.ps1`)

---

## 2. Зафиксированные решения (согласовано с автором)

| # | Вопрос | Решение |
|---|--------|---------|
| 1 | Нутация | Включаем (~10 главных членов, точность ~0.5″) |
| 2 | Fade появления звёзд | Синхронно с игрой: alpha = `(1 − Fogginess) · (1 − todSky.LerpValue)` — та же формула, что у игрового звёздного купола |
| 3 | Солнце/Луна | Phase 0: сверка обеих против Stellarium → потом решаем судьбу Луны |
| 4 | Планеты | Венера, Марс, Юпитер, Сатурн |
| 5 | Измерения | Секстант с мостика; перископ не окучиваем (слой может быть виден, не мешает) |
| 6 | Цвет линий созвездий | Тусклый красновато-коричневый, как дефолт Stellarium (подобрать по скриншоту) |
| 7 | Навигационные звёзды | 57 (56 + Полярная) — заметно ярче и крупнее остальных |
| 8 | Набор звёзд | Все концы линий созвездий (~600–700) ∪ 57 навигационных |
| 9 | Формат данных | Сгенерированный C#-код (`Source/Data/StarCatalog.cs`) — конвенция мода (как `BeaconData.cs`) |
| 10 | Тумблер созвездий | Хоткей + GUI-кнопка (паттерн `ToggleHandler`) |
| 11 | Куллинг | Предфильтр по широте + per-frame alt < 0 (звёзды); линии всегда целиком, оклюзия водой |
| 12 | Эпоха | Каталог J2016.0 → PM + прецессия к экватору 1942.0 (середина кампании); рантайм — доворот прецессии 1942→дата в кватернионе сферы |

---

## 3. Проверенные факты (база знаний этой сессии — не перепроверять)

### 3.1 Игра: небо (UniStorm / TOD_Sky)

- Небо игры — UniStorm (TOD_Sky из com.uboat.dependencies.dll; UniStormSystem из com.unistorm.dll)
- Звёзды игры — декоративная текстура `_Starmap` (4096×4096 DXT5, «Starmap (Light/Strong/Very Strong Constellations)») на куполе; НЕ астрономические (проверено декодированием текстуры: бимодальная гистограмма, нарисованные полосы «Млечного пути», ~37% ярких пикселей). Мы её НЕ трогаем — она остаётся фоном
- `TOD_StarParameters { Size, Brightness, Position }`; `TOD_StarsPositionType { Static, Rotating }`
- `TOD_Sky.UpdateCelestials` в Rotating-режиме крутит купол: `SpaceTransform.localRotation = StarTransform.localRotation = Euler(90−Lat, 0, 0) * Euler(0, 90+LST°, 0)`
- `TOD_Sky.UpdateShaderProperties` ставит глобалы `TOD_StarBrightness/Size/Visibility`; `TOD_StarVisibility = (1−Fogginess)·(1−LerpValue)`
- `LerpValue = InverseLerp(97, 86.5, SunZenith)` — звёзды игры проявляются при SunZenith 86.5°→97°

### 3.2 Игра: публичный API (проверено через IL-интроспекцию Cecil)

`TOD_Sky` (получаем через `weather.Sky`; `[Inject] private static Weather weather` — паттерн `LighthouseBeamGlow.cs:72`):

```
SunZenith, SunAltitude, SunAzimuth, MoonZenith, MoonAltitude, MoonAzimuth,
LocalSiderealTime, LerpValue, IsNight, Components, World, Atmosphere,
Cycle (DateTime), Instance (singleton)
```

`TOD_WorldParameters { Latitude, Longitude, UTC }` — публичные поля.
`TOD_AtmosphereParameters.Fogginess` — публичное поле.
`TOD_MoonPositionType { OppositeToSun, Realistic }` — «Realistic» = низкоточные лунные элементы (константы 125.1228−0.05295381·d, 318.0634+0.1643573·d, 5.1454, 60.2666, 115.3654+13.06499·d из IL `UpdateCelestials`) → ошибка ~0.3°, поэтому Phase 0, скорее всего, вынесет вердикт «своя Луна».

### 3.3 Игра: время, позиция, камера

- Время: `[Inject] GameTime` (UBOAT.Game.Core.Time) → `GmtDateTime` (DateTime). Фолбэк-рефлексия в стиле `PlayerPositionProvider`
- Позиция: мод уже имеет `IPlayerPositionProvider`/`GamePlayerPositionProvider` (Source/Logic/PlayerPositionProvider.cs) — лат/лон из `Entity.playerShip` через рефлексию + `GeoPositionConverter.ToLatLong`
- Камера: мод уже имеет проверенную логику `LighthouseBeamGlow.ResolveCamera` (Camera.main, проверка utility-камер, periscope-камеры, farClipPlane-защита) — скопировать/вынести в общий хелпер
- Мап-режим: `mainCamera.CurrentMode == CameraMode.Map` — в мапе слой не рисуем

### 3.4 Мод: инфраструктура

- Тесты: `Tests/` + `tests.ps1` (xunit-core атрибуты, свой Runner, dotnet net10.0). Чистая математика без Unity тестируется через Tests (стабы в `Tests/Stubs/`)
- Dev-инструменты: `Tools/Import-Leuchtfeuer.ps1` — прецедент для конвертера
- Данные — статикой в C# (`Source/Data/BeaconData.cs` — прецедент)
- Манифест: Reflection permission уже есть
- Игра сама компилирует `Source/*.cs` (Roslyn) — НЕ класть конвертер в Source/

### 3.5 Stellarium: каталог звёзд (бинарный, НЕ сжат)

`C:\code\projects\stellarium\stars\hip_gaia3\`:

- `stars_0_0v0_21.cat` — 242 KB, mag −2.0…6.0 (~5000 звёзд), эпоха J2016.0 (Gaia DR3)
- `stars_1_0v0_16.cat` — 1 MB, mag 6.0…7.5 (добор, если у концов линий mag > 6.0)

Формат (по `src/core/modules/ZoneArray.cpp` и `Star.hpp:452-473`):

```
Заголовок файла (всё little-endian, x86):
  uint32 magic = 0x835F040A  (в файле байты 0A 04 5F 83)
  uint32 type                // 0 = Star1 (Hipparcos), 1/2 = SpecialZoneArray<Star2/3> (Gaia-без-HIP)
  uint32 major, uint32 minor
  uint32 level
  uint32 mag_min             // миллимагнитуды (int; может быть отрицательным)
  float  epochJD             // эпоха каталога

Зоны (точную раскладку сверить по ZoneArray.cpp, SpecialZoneArray ctor, строки ~245-400):
  uint32 nr_of_zones
  uint32 zone_size[nr_of_zones]
  для каждой зоны: ZoneData { Vec3f center; uint32 size; ... } затем записи звёзд

Запись Star1 — 52 байта:
  qint64  gaia_id           // 8 б
  qint32  x0, x1, x2        // единичный вектор положения; значение ÷ 2e9
  qint32  dx0, dx1, dx2     // собственное движение, µas/год; ÷1000 = mas/год
  qint16  b_v               // B-V, миллимагнитуды
  qint16  vmag              // V-величина, миллимагнитуды
  quint16 plx               // параллакс, ×0.02 = mas
  quint16 plx_err           // ÷100 = mas
  qint16  rv                // лучевая скорость, ÷10 = км/с
  quint16 spInt
  quint8  objtype
  quint8  hip[3]            // HIP-номер (24 бита) + компонент (A, B, ...)
```

Вектор → RA/Dec: `ra = atan2(x1, x0)`, `dec = asin(x2/|v|)` (проверить знаки по известной звезде, например Сириус HIP 32349: RA≈101.29°, Dec≈−16.72°).

### 3.6 Stellarium: созвездия

`C:\code\projects\stellarium\skycultures\modern\index.json` (209 KB):

- Секция `"constellations"`: `{"id": "CON modern Aql", "lines": [[hip,hip,hip],[hip,hip],...], ...}` — полилинии HIP-номеров
- `"asterisms"` с `"is_ray_helper": true` — служебные (Stellarium их не рисует), ИСКЛЮЧАЕМ
- 1:1 = парсим секцию `constellations` целиком

### 3.7 Stellarium: имена навигационных звёзд

`stars/hip_gaia3/name.fab` — имена ↔ HIP. Список 56+Полярная (классический штурманский список алманаха 40-х):
Alpheratz, Ankaa, Schedar, Diphda, Achernar, Hamal, Acamar, Menkar, Mirfak, Aldebaran, Rigel, Capella, Bellatrix, Elnath, Alnilam, Betelgeuse, Canopus, Sirius, Adhara, Procyon, Pollux, Avior, Suhail, Miaplacidus, Alphard, Regulus, Dubhe, Denebola, Gienah, Acrux, Gacrux, Alioth, Spica, Alkaid, Hadar, Menkent, Arcturus, Rigil Kentaurus, Kochab, Alphecca, Antares, Atria, Sabik, Shaula, Rasalhague, Eltanin, Kaus Australis, Vega, Nunki, Altair, Peacock, Deneb, Enif, Al Na'ir, Fomalhaut, Markab, **Polaris**.

### 3.8 Почему НЕ skybox.ssc

`scripts/skybox.ssc` печёт статический кубмап текущего неба (alt/az на момент запуска, с рефракцией, с впаянными планетами). Для навигационной сферы непригоден. Используем только каталоги и линии из репозитория.

---

## 4. Архитектура

```
активная камера (логика ResolveCamera)
└── StarSkyRoot (GameObject, позиция = камера, world-меш каждый кадр)
    ├── StarsMesh            — 1 меш: ~700 звёзд (билборд-квады, аддитив)
    └── ConstellationsMesh   — 1 меш: все полилинии (тонкие полосы, аддитив)
└── PlanetRenderer            — 4 билборда планет + low-precision Луна
```

- 2–3 материала, 2–3 draw call
- Пересборка ~10k вершин/кадр ≈ 0.5 мс (оптимизация позже: пересборка раз в N кадров при малом повороте)

Меш строится в **мировых координатах** относительно позиции камеры (GO с transform.identity), вершины = `camPos + R · v_world`, R ≈ 3900 м (внутри farClipPlane, вне тумана; проверить в Phase 0).

---

## 5. Конвертер (dev-time, один запуск)

Расположение: `Tools/StarCatalogConverter/` — консольный проект dotnet net10.0 (как Tests).
НЕ входит в мод; запускается вручную при обновлении данных.
**⚠️ Факт 2026-08-24:** каталог уже сгенерирован и вшит (`Source/Data/StarCatalog.cs`, 5046 звезды + `ConstellationLines.cs`), а папки `Tools/StarCatalogConverter/` в репозитории **нет** — конвертер не сохранился либо выполнялся во временной директории. При регенерации каталога восстановить конвертер по §5 (входы ниже).

Вход:
- `C:\code\projects\stellarium\stars\hip_gaia3\stars_0_0v0_21.cat` (основа)
- `stars_1_0v0_16.cat` (добор концов линий с mag > 6.0)
- `C:\code\projects\stellarium\skycultures\modern\index.json`
- `C:\code\projects\stellarium\stars\hip_gaia3\name.fab`

Шаги:
1. Парсинг .cat (формат — §3.5), проверка магии 0x835F040A, чтение epochJD
2. Для каждой звезды: вектор положения (x0-2)/2e9 → RA/Dec (J2016.0, ICRF)
3. Собственное движение: `pos_1942 = normalize(pos + pm·(−74.0 лет))`, где pm = (dx0-2)/1000 mas/год → радианы/год; Δt = 1942.0 − epochJD/365.25
4. Прецессия к экватору 1942.0: матрица IAU-1976 (§7.2) с T = (1942.0 − 2016.0)/100 = −0.74
5. Сборка набора: все уникальные HIP из `constellations[].lines` + 57 навигационных (имена → HIP через name.fab)
6. Генерация кода `Source/Data/StarCatalog.cs` + `Source/Data/ConstellationLines.cs` (см. ниже)
7. Самопроверка конвертера: Сириус (HIP 32349) → RA≈101.29°, Dec≈−16.72° (J2000, до прецессии; после прецессии к 1942 — сверка со Stellarium)

Выход — `Source/Data/StarCatalog.cs`:

```csharp
namespace UBOAT.Mods.RadioNavigation
{
    public struct StarEntry
    {
        public int Hip;
        public float RaRad;    // RA, радианы, экватор 1942.0
        public float DecRad;   // Dec, радианы, экватор 1942.0
        public float Vmag;     // V-величина
        public float Bv;       // B-V (цвет)
        public bool IsNav;     // навигационная звезда
    }

    public static class StarCatalog
    {
        public static readonly StarEntry[] Stars = { ... }; // ~700 записей
    }
}
```

Выход — `Source/Data/ConstellationLines.cs`:

```csharp
    public static class ConstellationLines
    {
        // полилинии: индексы в StarCatalog.Stars
        public static readonly int[][] Polylines = { new[]{12, 45, 78}, ... };
    }
```

---

## 6. Рантайм-компоненты

### 6.1 `Source/Logic/AstroMath.cs` — чистая математика (тестируемая, без Unity)

Статический класс, всё в double:

```csharp
// JD из DateTime (UTC)
static double ToJulianDay(DateTime utc);

// GMST, градусы 0-360 (Мeeus гл.12)
static double GmstDeg(double jd);
// LST = GMST + lon (восточная долгота, градусы)
static double LstDeg(double jd, double lonDeg);

// Прецессия IAU-1976: матрица 3x3 из эпохи J2000 в эпоху JD (Мeeus гл.21)
static void PrecessionMatrix(double jdFrom, double jdTo, out double[] m33);

// Нутация: Δψ, Δε (Мeeus гл.22, главные члены)
static void NutationDeg(double jd, out double dPsiDeg, out double dEpsDeg);

// RA/Dec (радианы) → направление в ENU (E=+x, N=+z, U=+y):
//   H = LST − RA
//   E = −cosδ·sinH;  N = sinδ·cosφ − cosδ·cosH·sinφ;  U = sinδ·sinφ + cosδ·cosH·cosφ
// alt = asin(U); az = atan2(E, N) (0 = север, +восток)
static void RaDecToEnu(double ra, double dec, double lst, double lat,
                       out double alt, out double az, out double e, out double n, out double u);

// Планеты: геоцентрические RA/Dec на дату, экватор даты (Мeeus гл.31, кеплеровы элементы + поправки)
static bool PlanetRaDec(int planetId, double jd, out double raRad, out double decRad);

// Луна: ELP2000 усечённый (Мeeus гл.47), точность ~10″ — если Phase 0 потребует
static void MoonRaDec(double jd, out double raRad, out double decRad);
```

Планеты: сначала Мeeus гл. 31 (таблицы средних элементов + секулярные поправки; точность ≤ 1′ — достаточно). Если захочется лучше — VSOP87 усечённый (гл. 32), структура кода готова.

### 6.2 `Source/Logic/StarSkyRenderer.cs` + `Source/Logic/StarsMesh.cs`

MonoBehaviour-синглтон мода (создаётся из Main.cs при OnSceneAwake, как остальные):

```
[Inject] Weather weather;   // weather.Sky = TOD_Sky
[Inject] GameTime gameTime; // GmtDateTime
IPlayerPositionProvider positionProvider; // существующий
```

- `LateUpdate()`:
  1. `TryGetPosition(out lat, out lon)` — нет позиции (меню/карта/NaN) → слой скрыт
  2. `jd = ToJulianDay(gameTime.GmtDateTime)`; `lst = LstDeg(jd, lon)`
  3. Куллинг-набор по широте (§8); если lat сменилась > 0.5° — пересчитать набор
  4. Fade: `alpha = (1 − weather.Sky.Atmosphere.Fogginess) * (1 − weather.Sky.LerpValue)` (фактически = TOD_StarVisibility игры — синхронно)
  5. Сборка меша звёзд (§9)
  6. Сборка меша линий (§9) — если тумблер включён
- Камера: `ResolveCamera()` (вынести общий хелпер из LighthouseBeamGlow); при смене камеры — позиция корня = камера
- `TryGetAltitude(int hip, out float altDeg, out float azDeg)` — API для секстанта: считает §7.3 на лету для любой звезды каталога (не только видимой)

### 6.3 `Source/Logic/ConstellationMesh.cs`

- Полилинии из `ConstellationLines.Polylines`
- Тонкие полосы-квады вдоль сегментов (ширина ~2′ угловых, подобрать)
- Линии рисуются ЦЕЛИКОМ всегда (оба конца, независимо от alt) — горизонт закрывает вода (ZTest LEqual, ZWrite Off)
- Страховочный фейд вершин при alt < 0° (для free-cam/камеры над водой)
- Тумблер: хоткей (выбрать на реализации, например F9 или Ctrl+S? — НЕ конфликтовать с игрой; предложить Numpad* — нет, оставить «выбрать в реализации») + GUI-кнопка в существующей панели радио
- Цвет: тусклый красновато-коричневый под Stellarium (подобрать по скриншоту Stellarium, стартовая оценка RGB ≈ (0.55, 0.22, 0.15), альфа-подгонка)

### 6.4 `Source/Logic/PlanetRenderer.cs`

- 4 планеты: `PlanetRaDec` per-frame → тот же пайплайн видимости, что у звёзд; билборды чуть крупнее ярких звёзд; цвета: Венера белая, Марс красноватый, Юпитер бледно-кремовый, Сатурн желтоватый
- Fade по той же формуле
- Луна: по вердикту Phase 0. Если заменяем: скрыть игровую (`MoonRenderer.enabled = false`), свой билборд с процедурной фазой (затенённый диск: освещённость = (1+cos(фаза))/2, терминатор упрощённо), ELP2000 из AstroMath

### 6.5 Точка входа

`Main.cs`: после существующей инициализации создать `StarSkyRenderer` (AddComponent на новом GO, DontDestroyOnLoad как у остальных).

---

## 7. Формулы (конкретные)

### 7.1 JD

`JD = 2451545.0 + (DateTime.UtcTicks − DateTime(2000,1,1,12,0,0).Ticks) / TimeSpan.TicksPerDay` — или стандартная формула из календарной даты (Мeeus гл. 7).

### 7.2 Прецессия IAU-1976 (Мeeus гл. 21)

T — юлианские столетия от J2000 (может быть отрицательным):

```
ζ = (2306.2181″ + 0.30188″·T + 0.017998″·T²) · T
z = (2306.2181″ + 1.09468″·T + 0.018203″·T²) · T
θ = (2004.3109″ − 0.42665″·T − 0.041833″·T²) · T
```

Всё в дуговых секундах → радианы. Матрица перехода из эпохи J2000 в эпоху J2000+T:
`M = R3(−z) · R2(θ) · R3(−ζ)` (активное вращение; сверить знаки на Сириусе: за 1942 vs 2016 ΔRA ≈ −1°·…, Dec почти не меняется для экваториальных).

### 7.3 Звёзды: направление и Alt/Az

```
H = LST − RA
E = −cosδ·sinH
N = sinδ·cosφ − cosδ·cosH·sinφ
U = sinδ·sinφ + cosδ·cosH·cosφ
alt = asin(U)
az  = atan2(E, N)        // 0 = север, +90 = восток (сверить с компасом игры в Phase 0)
```

Кватернион сферы (для калибровки/тестов): `Q = Q_prec(1942→дата) · Q_nut(дата) · Q_rot` — в реализации mesh-per-frame култернион не обязателен, но AstroMath должна уметь собрать его для тестов.

### 7.4 Нутация (главные члены)

```
T (столетия от J2000)
L☉ = 280.4665 + 36000.7698·T        (средняя долгота Солнца)
L☾ = 218.3165 + 481267.8813·T       (средняя долгота Луны)
Ω  = 125.04452 − 1934.136261·T      (долгота восходящего узла)

Δψ = −17.20″·sin Ω − 1.32″·sin 2L☉ − 0.23″·sin 2L☾ + 0.21″·sin 2Ω
Δε =  +9.20″·cos Ω + 0.57″·cos 2L☉ + 0.10″·cos 2L☾ − 0.09″·cos 2Ω
```

Нутацию применяем ТОЛЬКО в повороте сферы (каркас), не дублируем в LST.

### 7.5 Сознательно пропускаем (документировано)

- Рефракция — входит в счисление секстанта (не рендер)
- Аберрация (±20″ = 0.33′) — ниже точности игрового секстанта
- Параллакс звёзд (<0.8″)
- Собственное движение в рантайме (максимум 0.4′ за кампанию у α Cen; PM учтён при бейке на 1942.0)

---

## 8. Видимость и куллинг

1. **Предфильтр по широте:** звезда с δ < φ − 90° невидима НИКОГДА (южное небо с северной широты). Набор пересчитывается при Δφ > 0.5° (дешёвая фильтрация массива)
2. **Per-frame:** alt < 0 → звезда исключается из меша (долгота и время дают текущую видимость — южные звёзды днём невидимы автоматически)
3. **Fade:** `alpha_global = (1 − Fogginess) · (1 − LerpValue)` — вершинный альфа-множитель всего слоя. Синхронно с игровым небом по построению
4. **Линии:** всегда целиком; ниже горизонта — оклюзия водой (ZTest LEqual, ZWrite Off) + плавный фейд вершин при alt < 0°
5. **Карта/меню:** нет позиции или `CameraMode.Map` → слой выключен

---

## 9. Рендеринг-детали

- **Спрайт звезды:** 64×64 RGBA, радиальный гауссов спад (генерится в рантайме один раз), материал `Shader.Find("Sprites/Default")` + аддитивный блендинг (`Blend SrcAlpha One`, ZTest LEqual, ZWrite Off)
- **Размер/яркость от mag:** угловой радиус ≈ `k · 10^(−0.2·(mag+1.5))` (подобрать k визуально: Sirius заметно ярче, mag 6 еле видна); навигационные ×1.6 размера, альфа ×1.5
- **Цвет звёзд из B-V:** стандартная карта B-V→RGB (Stellarium-подобная, упрощённая: B-V −0.4…2.0 → сине-белый → белый → жёлтый → оранжевый)
- **Билбординг:** вершины квада в плоскости, перпендикулярной лучу «камера→звезда» (в мировых координатах), размер в метрах = R · tg(угловой радиус)
- **Линии:** полосы шириной ~2′: для сегмента AB строим квад из 4 вершин, смещённых перпендикулярно (A−B)×viewDir; аддитив, цвет константа, alpha = global_alpha · horizonFade(alt)
- **Порядок отрисовки:** transparent queue; звёзды дальше воды → вода перекрывает корректно при сортировке
- **Материалы создаются в рантайме** — НЕ через AssetBundle (никаких новых ассетов)

---

## 10. Phase 0 — пробы ДО основного кода

Лог-патч на 1 вечер (или отдельный тестовый класс в моде):

1. **Луна игры:** `weather.Sky.MoonAltitude/MoonAzimuth` vs Stellarium на 2 даты (1942-06-15, 1943-01-10, 00:00 и 06:00 UT) → вердикт «своя Луна». Ожидание: ошибка ~0.3° (низкоточные элементы) → заменяем
2. **Солнце игры:** `SunAltitude/SunAzimuth` vs Stellarium (те же даты) → контроль (ожидание ~0.01°)
3. `Shader.Find("Sprites/Default")` ≠ null в билде
4. farClipPlane активных камер (мостик/обзор) — выбрать R < 0.9·farClip
5. Визуально: вода пишет глубину (наш тестовый квад под горизонтом скрыт водой)
6. Сверить оси N/E: `GeoPositionConverter`-направление на север vs компас игры (или `weather.Sky.World.Latitude/Longitude` vs `IPlayerPositionProvider`)
7. `weather.Sky.LerpValue` монотонно растёт 86.5°→97° SunZenith (проверить живьём формулу fade)

---

## 11. Тестирование

### 11.1 Unit-тесты (Tests/, dotnet, без Unity)

- `AstroMathTests.cs`:
  - GMST на JD 2451545.0 = 280.4606°; GMST известного момента (сверка со Stellarium-значением)
  - Прецессия: Сириус J2000 → 1942.0 (эталон из Stellarium)
  - Нутация: |Δψ| ≤ 20″, монотонность Ω
  - Alt/Az: Полярная на 50°N → alt ≈ 50°, az ≈ 0 (север); Сириус в известный момент — эталон Stellarium
  - Куллинг: Канопус (δ≈−52.7°) на 50°N — alt всегда < 0
- `StarCatalogTests.cs`: размер каталога, уникальность HIP, все 57 навигационных присутствуют, все концы линий в каталоге, mag/цвет в разумных диапазонах
- `ConstellationLinesTests.cs`: каждый индекс внутри каталога; полилинии непустые
- `PlanetMathTests.cs`: Юпитер 1942-06-15 RA/Dec vs Stellarium (эталон снять заранее) ≤ 2′

### 11.2 In-game

1. Слой включается ночью, выключается днём синхронно с игровым небом
2. Полярная визуально на севере, высота ≈ широте
3. Орион/Б. Медведица/Кассиопея визуально 1:1 со Stellarium (скриншоты рядом)
4. Линии уходят за горизонт и исчезают в воде
5. ×32 ускорение — плавно, 60 fps
6. Тумблер хоткеем и GUI-кнопкой

---

## 12. Риски и фолбэки

| Риск | Фолбэк |
|------|--------|
| `Sprites/Default` нет в билде | Шейдер из ресурсов игры (найти в Phase 0), либо Legacy Particles/Additive |
| Вода не пишет глубину → линии под горизонтом висят | Жёсткий фейд линий по alt (альфа 0 при alt < −1°) |
| farClipPlane камеры < 4 км | Уменьшить R; дальний план не нужен — слой всегда рядом с камерой |
| Игровой патч меняет `Entity.playerShip`/камеры | Рефлексия с фолбэком (стиль PlayerPositionProvider), лог ошибок |
| Луна игры точнее, чем ожидалось (Phase 0 покажет обратное) | Свою Луну не делаем, только планеты |
| Компас игры ≠ наши az (разный ноль отсчёта) | Константа смещения азимута, калибровка по Phase 0 |
| 700 звёзд пересборка — дорого на слабых машинах | Пересборка меша раз в N кадров / по дельте поворота > порога |

---

## 13. Порядок реализации (checklist)

`[~]` означает частично выполнено.

- [ ] 1. `Tools/StarCatalogConverter/` — конвертер (§5) + самопроверка по Сириусу
- [x] 2. Сгенерировать `Source/Data/StarCatalog.cs`, `Source/Data/ConstellationLines.cs` — 5046 звёзд, 215 линий
- [x] 3. `Source/Logic/AstroMath.cs` + unit-тесты (§11.1) — JD/GMST/LST, прецессия, нутация, Alt/Az, планеты, Луна
- [~] 4. Phase 0 лог-патч (§10) → вердикт по Луне — probe `Source/Logic/StarSkyPhase0Probe.cs` готов (PlayerPrefs `RadioNav.Phase0Probe=1`, лог Sun/Moon/farClip/shader/Lerp/pos), живые замеры в игре pending
- [~] 5. `StarSkyRenderer` + `StarsMesh` — корень, куллинг, fade, меш звёзд, `TryGetAltitude`; нет runtime quaternion epoch turn
- [x] 6. `ConstellationMesh` — линии и цвет Stellarium; тумблер реализован: **F11** + кнопка CONST на панели + DevConsole `RadioNavConstellations [on|off]`, состояние в PlayerPrefs `RadioNav.ConstellationsEnabled` (2026-08-25; F9→F11 после дампа биндингов — F9=Quick Load, F10=Screenshot, см. `knowledge/inventory/game-hotkeys.md`)
- [~] 7. `PlanetRenderer` — 4 планеты и low-precision Луна; Phase 0/ELP2000 точность не подтверждены
- [x] 8. Точка входа в `Main.cs`
- [x] 9. `powershell -File tests.ps1` зелёные; Release/Debug build без ошибок
- [ ] 10. In-game проверки (§11.2), сверка со Stellarium на 3 датах
- [~] 11. Полировка размеров/цвета/ширины выполнена базово; live-подбор и хоткей остаются

---

## 14. Ссылки на источники

- Игра (исходники): `C:\code\projects\uboat`
  - `UBOAT.Game.Scene.Environment\Weather.cs` — интеграция TOD_Sky (Sky, World.Latitude/Longitude:1361, fade-связки)
  - `UBOAT.Game.Core.Time\GameTime.cs` — GmtDateTime
- Stellarium: `C:\code\projects\stellarium`
  - `stars/hip_gaia3/` — каталоги (.cat), `defaultStarsConfig.json` (магнитудные диапазоны зон), `name.fab`
  - `skycultures/modern/index.json` — линии созвездий
  - `src/core/modules/ZoneArray.cpp`, `src/core/modules/Star.hpp` — формат .cat
- Мод: `Source/Logic/LighthouseBeamGlow.cs` (инжект Weather + ResolveCamera), `Source/Logic/PlayerPositionProvider.cs` (лат/лон), `Source/Data/BeaconData.cs` (конвенция статик-данных)
- Математика: J. Meeus, «Astronomical Algorithms», 2nd ed. — гл. 7 (JD), 12 (GMST), 21 (прецессия), 22 (нутация), 31 (планеты, кеплеровы), 32 (VSOP87), 47 (Луна ELP2000)
