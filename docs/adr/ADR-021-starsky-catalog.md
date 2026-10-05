# ADR-021: StarSky — точное звёздное небо для астронавигации

> Статус: Accepted (2026-08-19)  
> Связи: `docs/plans/STAR_SKY_PLAN.md`, `docs/plans/CELESTIAL_REALISM_PLAN.md`, `Source/Logic/AstroMath.cs`, `Source/Logic/CelestialEphemeris.cs`, `Source/Data/StarCatalog.cs`, `Source/Logic/StarsMesh.cs`

## Дополнение: действующая реализация (2026-10-04)

Решение использовать статический каталог и чистую астрономическую математику
остаётся Accepted. **Остальные разделы — запись решения и чеклист 2026-08-19**, а
не текущая спецификация или поручение реализовать перечисленное заново.

- Каталог содержит **5046 строк**, **59** с `IsNav=true` (**57 уникальных HIP**);
  критерий «48 / V<2» ниже устарел. Есть 17 строк `Hip=0` (без HIP); `StarsMesh`
  их пропускает, идентификация/измерения тоже. Повторяющийся HIP
  разрешается первой строкой.
- Фактически запечённая эпоха — **1926.0**, JD **2424516.5**, несмотря на
  историческую шапку `StarCatalog.cs` «J1942.0». Ошибка исходного конвертера
  установлена независимым сравнением: best-fit 1926.0 для 10 звёзд.
  `CelestialEphemeris.PrecessCatalog` переводит каждую строку от этой эпохи к
  запрошенной дате; нельзя начать прецессию от 1942.0 или повторно преобразовать
  данные, доверившись старому комментарию.
- Активный renderer — `StarsMesh`; `StarSkyRenderer.cs` удалён. Он использует
  B−V/величину, PSF и размер в пикселях текущей камеры. Bundled `PointSky`
  (`LightMode=Sky`, очередь 1020) рисуется до cloud composite; остальные passes
  скрыты от автоматического pipeline draw. Игровой декоративный звёздный слой
  подавляется `GameStarLayerSuppressor`.
- `ConstellationMesh`, четыре планеты и отдельные солнечный/лунный диски уже
  реализованы. Время — `GameTimeResolver.GetUtcNow()` с игровым UTC; `UtcNow`
  системных часов остаётся fallback, не штатный источник загруженной кампании.
- Прежнее «точность <0.001″» не является подтверждённой точностью всей цепочки
  каталог → эфемериды → камера → измерение. Офлайн-тесты и GPU-приёмка cloud
  stage не заменяют ещё открытую внешнюю эстетическую приёмку звёзд/PSF.

Контракты и ограничения — `docs/plans/CELESTIAL_REALISM_PLAN.md`,
`docs/sextant-optical-visuals.md`, `docs/sky-vs-clouds.md`; свежие счётчики — `DOCS.md`.

## Исходная запись решения (2026-08-19)

## Контекст

- Мод добавляет панель радионавигации Consol/Sonne, но для полной навигационной системы нужен секстант + звёздный каталог.
- Исторически UBOAT кампания 1939-1945 — навигация по звёздам была основным методом определения широты в открытом море.
- Stellarium имеет точный каталог Hipparcos/Gaia3 (`stars_0_0v0_21.cat`, 5046 звёзд mag -2..6), но в формате бинарного `.cat` с собственной системой координат.
- Нужен конвертер `.cat` → C# static data + астрономическая математика (JD/GMST/прецессия/нутация/AltAz).

## Решение

### 1. AstroMath.cs — чистая математика
- `ToJulianDay(DateTime)` — JD из UTC (Meeus гл.7)
- `GmstDeg(jd)` / `LstDeg(jd,lon)` — GMST/LST градусы (Meeus гл.12)
- `PrecessionMatrix(jdFrom,jdTo,m[9])` — матрица прецессии IAU-1976 (Meeus гл.21)
- `NutationDeg(jd,out dPsi,out dEps)` — нутация Δψ/Δε (Meeus гл.22, 4 главных члена)
- `RaDecToEnu(ra,dec,lst,lat,out alt,out az,...)` — экваториальные → горизонтальные координаты

### 2. StarCatalog.cs — статические данные
- Python конвертер парсит `stars_0_0v0_21.cat` (Stellarium hip_gaia3)
- Применяет собственное движение (PM) к эпохе J1942.0 (середина кампании)
- Генерирует `StarEntry[] Stars` с полями `Hip/RaDeg/DecDeg/Vmag/Bv/IsNav`
- 5046 звёзд, 48 навигационных (V<2) — помечены `IsNav=true` для крупного рендера

### 3. StarSkyRenderer.cs — MonoBehaviour
- `LateUpdate()` — вычисляет LST, считает видимые звёзды (alt>0)
- `TryGetStarAltAz(hip,out alt,out az)` — API для секстанта
- Использует `IPlayerPositionProvider` для lat/lon
- Лог каждые 5с для диагностики

## Причина

- Без точного каталога звёзды будут "декоративными" как в игре (`TOD_Sky._Starmap`) — не подходят для навигации.
- Hipparcos/Gaia3 даёт точность <0.001″ — достаточно для секстанта.
- PM к 1942.0 уменьшает ошибку от собственного движения (~1° за 74 года без PM).

## Альтернативы

- **A. Использовать игровое небо `TOD_Sky`** — отвергнуто: декоративная текстура, не астрономическая.
- **B. JSON каталог** — отвергнуто: C# static data быстрее, конвенция мода (`BeaconData.cs`).
- **C. Динамическая загрузка** — отвергнуто: 5046 звёзд × 20 байт = 100KB — тривиально для static readonly.

## Tradeoff

+ Точность <0.001″, PM учтён, 203 теста PASS  
- Прецессия к 1942.0 пока не применена (ошибка ~0.5°) — добавим позже через `PrecessionMatrix`  
- Созвездия (линии) не реализованы — добавим в `ConstellationLines.cs` после фазы 2

## Статус

- [x] `AstroMath.cs` + 6 тестов (JD/GMST/Nutation/RaDecToEnu/Polaris/Canopus)
- [x] `StarCatalog.cs` 5046 звёзд (Python конвертер из Stellarium .cat)
- [x] `StarSkyRenderer.cs` видимость + API для секстанта
- [x] `StarCatalogTests.cs` 5 тестов (count/nav/range/Sirius/Canopus)
- [ ] Прецессия к 1942.0 (использовать `PrecessionMatrix`)
- [ ] Mesh рендер (билборды + цвет от B-V)
- [ ] Созвездия (`ConstellationLines.cs` из `skycultures/modern/index.json`)
- [ ] Планеты (Венера/Марс/Юпитер/Сатурн через Meeus гл.31)
- [ ] Луна (ELP2000 упрощённый, Meeus гл.47)

```json
{"id":"ADR-021","decision":"StarSky: AstroMath + StarCatalog (5046 звёзд из Stellarium .cat) + StarSkyRenderer","reason":"Точный каталог для астронавигации, PM к 1942.0, static data как BeaconData","alternatives":["Игровое небо TOD_Sky","JSON каталог","Динамическая загрузка"],"tradeoff":"Прецизия <0.001″ vs прецессия/созвездия/планеты отложены"}
```
