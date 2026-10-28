# Native и mod Sun/Moon: проверенные контракты, 2026-10-08

## Read-only доступ к игровому TOD

`TOD_Sky` находится в `com.uboat.dependencies`; мод читает его через Weather.Sky
и reflection. Проверены исходным DLL и строгими live getters:

- `SunDirection`, `MoonDirection`: Vector3;
- `SunAltitude`, `SunAzimuth`, `MoonAltitude`, `MoonAzimuth`: float;
- `Components.SunRenderer`, `Components.MoonRenderer`: native renderers;
- вложенные `enabled`, `gameObject.layer`, `sharedMaterial.name`,
  `sharedMaterial.shader.name`, `sharedMaterial.renderQueue` читаются;
- `Components.SunTransform`, `Components.MoonTransform`,
  `Components.SunMeshFilter`, `Components.MoonMeshFilter` существуют;
- `Cycle.Year/Month/Day/Hour`, `World.Latitude/Longitude/UTC`, `Moon.Position`,
  `Sun.MeshSize`, `Moon.MeshSize` читаются.

`TOD_Sky.UpdateCelestials` каждый кадр переутверждает SunRenderer.enabled и
MoonRenderer.enabled по положению над/около горизонта. Однократное отключение
renderer поэтому не является устойчивым подавлением.

Штатная Realistic-Луна вычисляется геоцентрически: в числовом пути нет вычитания
позиции земного наблюдателя. MeshSize — параметр native mesh/shader; не считать
его самостоятельно измеренным видимым диаметром.

## Действующий модный путь и обнаруженные границы

- SunDiscRoot — layer 7, только PIP; с 2026-10-09 направление нового измерительного
  диска — SH5 apparent centre, без native/TOD fallback. Не live-приёмка.
- MoonDiscRoot — default layer 0, наружный вид; **MoonTopocentricRaDec**.
- PIP CopyFrom + OR layer 7 не исключают native Sun/Moon. Скопированные SRP
  pass masks проверяются отдельно, camera mask не доказывает draw.
- GameStarLayerSuppressor скрывает звёздный dome, не native Sun/Moon.
- В исходниках 2026-10-08 `SextantController.TryGetOwnMoon`/`MOONDIAG`/probe
  ownMoon переведены на **MoonTopocentricRaDec**, как сам модный диск. Это
  исправляет диагностическую систему координат, не доказывает совпадение native.
- `BuildAlphaField` — освещённая доля, **не силуэт**. `MoonDiscRenderer` теперь
  берёт alpha из отдельного `BuildCoverageField`; тёмная половина непрозрачна,
  дневное затухание меняет RGB. Presented-проверка ещё не выполнена.
- `SextantCelestialRenderScopePatch` подавляет native Sun/Moon только в явно
  зарегистрированных PIP, prefix до culling + restoring finalizer. Camera-local
  `SextantCelestialSources` читается по имени типа между копиями сборки. На
  Main Camera native sources/light/sky не меняются.
- Проверенные private targets в `com.dws.pipeline.Runtime`:
  `DeepPipeline.RenderDeferredCamera(ScriptableRenderContext, Camera)` и
  `RenderForwardCamera(ScriptableRenderContext, Camera)`. Deferred attached-путь
  рекурсивно зовёт `Render(context, list)` (decompiled L860–868), так что обе
  PIP-маршрутизации проходят эти методы. VertexLit идёт отдельным путём.
- Геометрия native Sun: `Sun.MeshSize=0.5`, TOD scale и извлечённый compiled
  shader дают границу core **2.000209641°**, quad extent около **3.9984°**.
  Это офлайн-контракт mesh/shader, не замер presented-пикселей или яркого halo.

В live-снимке native Sun layer=11, Moon layer=28; queues 1030/1040, mod queues
1020. Это датированная конфигурация, не универсальная гарантия и не GPU capture.
Выбор source по `Active`/filter не удостоверяет, какой limb видит пользователь.

Доказательства и ограничения:
`../../artifacts/reports/dual-celestial-audit-20261008.md` (исходная инвентаризация)
и `../../docs/celestial-disc-sizes.md` (правки, проверки и открытая live-приёмка).
Исходники: `Source/UI/SextantGameSun.cs`, `SextantController.TryGetOwnMoon`,
`Source/Logic/MoonDiscRenderer.cs`, `CelestialEphemeris.ComputeForObserver`.

## Среднее море для SH5 (исходный контракт, не измерение пикселей)

`PlayWay.Water.WaterCamera.MainWater` возвращает `Water`; mean level берётся с
`MainWater.transform.position.y`. `WaterLevel` — отдельный мгновенный уровень,
не подмена среднего моря. `SextantSolarOptics` кэширует WaterCamera выбранного
view Camera и читает глаз как разность y. Curvature gate — глобальный float
`_EarthCurvatureCompensation` (только 0/1). PIP far clip — контракт `SextantPipClip`.
API проверен clean reference. **Источник кривизны исправлен 2026-10-09:** includes
TestUI (`DeepRenderPipeline/Includes/EarthCurvature.cginc`, `WaterDisplace.cginc`)
не совпадают с installed shader! Старый drop на 250 м = 0.01210269698 м;
текущий installed DXBC = **0.004905038978904486 м**. В 71 проверенном shader
найдено 484 curvature-bearing D3D11 программы / 172 уникальных DXBC с одной
битово-идентичной таблицей 81 узла, шагом 250 м, `min(index,79)` и линейным
интерполированием. Shader gate сравнивает compensation **с 1**, как адаптер.
Installed `uboat.core` SHA-256:
`ce26da620ea3f4a377bb47538192640743ab79d32be2ffc7b5c198620c8ccc9c`.
Fixture: `Tools/solar-sight/water-curvature-verified.json`; воспроизведение:
`audit_water_shader.py` (read-only, без игры/сети, выход только `artifacts/`).
При глазах 5.5 м / far=9000 м installed-модель = **−0.07529415393001057°**,
максимальный луч на 8250 м; старое −0.11827238574173227° / 5250 м — ошибка
источника моей модели, не измерение воды. **Перекрёстная сверка 2026-10-09
(`Tools/solar-sight/sea_horizon.py` + 13 тестов):** installed-таблица при глазу
5.8505 м даёт 4.6593′ против геометрического дипа 4.6586′, то есть модель есть
точный геометрический горизонт; отозванная копия TestUI давала постоянные
**1.571×** (эффективный радиус 2581 км) — только это и создавало исторические
≈2.8′. Не заменять эту таблицу EarthCurvature.cs
из игровой физики или radius/dip-формулой; не менять глобальный shader/native море.
Идеальная 250-м knot-модель не удостоверяет wave/fog/land silhouette или mesh;
пиксельная приёмка открыта. См. `../../docs/sh5-solar-navigation.md`.

## Runtime и безопасная приёмка — 2026-10-09

Разрешённая игровая компиляция + загрузка `Autosave_2`: campaign pack loaded,
оба render-scope hooks installed; контроллер один. Render A/B не начат:
worksheet пуста, обычный `SextantController.TryOpen` зовёт `SunCalibrator.Solve`,
который при успехе пишет `SetDelta` и `RadioNav.SunCalibrationMinutes`.
Запрет записи калибровки требует остановиться **до open**, не восстановить Δt
после записи и выдать это за отсутствие изменений. Source/readiness ≠ pixels.

Проверен игровой контракт `UBOAT.Game.Core.Data/AutoSaver.cs`:
`CheckAutoSaveTime` зарегистрирован в `TimedUpdatesQueue.UnscaledContinuous`,
читает `userSettings.ControlSettings.autosaveFrequency` (минуты), не проверяет
`GameTime.Paused`; `Loaded` обнуляет таймер. `ControlSettings.cs` содержит
`autosaveOnDock`, `autosaveFrequency`, `autosaveCount`, `autosaveOnExit`.
Оставленная на паузе игра **не** гарантирует отсутствие ночного автосейва.
В этом заходе процесс, запущенный агентом, остановлен по PID + creation time
без `/to-menu`/save; повторная сверка всех 23 `.save` и protected files чистая.
Runtime receipts — `../../artifacts/reports/solar-table-20261008/live-validation/`.
