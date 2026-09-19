# Правильная камера: интерьер + экстерьер (UBOAT SRP)

Практическое руководство по камере, которая корректно рендерит и интерьер лодки,
и экстерьер. Собрано из цикла работ по окуляру секстанта (2026-09-17…19):
13 контролируемых A/B, декомпилят `com.dws.pipeline.Runtime.dll` (ilspycmd),
исходники игры (`C:\code\projects\uboat`), live-замеры стенда «Трубы».
Детали расследования — `SEXTANT_PLAN.md` §16; здесь только нормы и механизмы.

Термины — в §21. Числа без ссылки — из live-замеров стенда (день, торпедный отсек).

## 0. Главный инвариант: ОДИН писатель `_GBufferZ` на проход

Игра всегда держит ровно одного писателя на проход: внутри лодки root пропущен
(`RenderScene=false`), пишет только Interior-камера; снаружи — только root.
Два писателя с разными view-матрицами → рассинхрон depth → NaN/Inf в
light-combine → чёрные тайлы, маджента, вспышки (§16.17). Совпадающих проекций
НЕДОСТАТОЧНО — нужны совпадающие view-матрицы (позиция+поворот) либо
непересекающиеся пиксели.

Проверять позитивом, не отсутствием артефакта: счётчик срабатываний
(`PipCloneCameraPatch.SuppressionCount`) + one-shot лог. Урок `__0`: все
статические гейты были зелёными с инертным патчем (позиционный `__0` =
`ScriptableRenderContext`, камера — `__1`); тихий no-op ловится только
позитивным доказательством.

## 1. Архитектура: две камеры + VSR (а не одна с общей маской)

Интерьер живёт в отдельной VirtualScene; геометрию позиционирует ТОЛЬКО
`VirtualSceneRenderer` (per-frame override). Прямой рендер интерьерной маской
без VSR = чёрный окуляр (доказано §16.20).

Схема:

- **Root-камера** — кадр и compositing. **Клон** (`CopyFrom(InteriorCamera)`) —
  писатель интерьера. **VSR на объекте ROOT** (не клона!): `virtualCamera=клон`,
  `virtualScene=PlayerShip.Interior.VirtualScene`; добавить ДО первого `Render()`
  (`DeepCameraData` кэширует VSR в ctor).
- Пайплайн композитит `cameras[1]` в кадр root ТОЛЬКО при активном VSR
  (guard: `enabled && RenderingEnabled && VirtualCamera.isActiveAndEnabled`).
- `CollectCameras` копирует виртуалке только `near/far/FOV/обе projection-матрицы/
  jitter-флаг` — позиция/поворот НЕ копируются (их пишет `VSR.LateUpdate`).

Два рабочих паттерна:

| | Game-паттерн | §16.22-паттерн (мод) |
|---|---|---|
| Кто пишет интерьер | клон (`cameras[1]`, VSR активен в проходе) | root напрямую (клон подавлен из `CollectCameras`) |
| `RenderScene` root внутри | `false` | `true` (форс после миррора — иначе ноль писателей) |
| Позиция root | своя | = позиция InteriorCamera (прямое копирование!) |
| Когда выбирать | всегда, когда возможно | когда второй писатель corrupts (см. §0) |

Позицию копировать только НАПРЯМУЮ с игровой Interior-камеры. Копирование
с мод-клона даёт фидбэк-расходимость (зафиксировано `[1915, 250822, 7891]`),
т.к. позицию клона считает `VSR.LateUpdate` ИЗ PIP-трансформа.

## 2. Контракт пайплайна (`RenderDeferredCamera`, декомпилят)

Порядок: AttachedCameras (КАЖДАЯ — отдельный полный `Render()`! цена N камер =
N проходов) → FSR/DLSS-init → `GlobalEffects.PreCull` → `CollectCameras` →
GBuffer (виртуалки, затем root **только при `RenderScene`**; keyword
`USE_CUSTOM_AMBIENT` — только root и только при его флаге) → decals → water →
`CopyTexture(GBufferZ→CameraDepth)` → **SSAO-bake по слою ROOT** → Reflections
(root при `RenderScene` + каждая виртуалка) → Shadows root + Shadows/Lighting/
Contact **на каждую камеру со своими данными** → water → Sky (root при
`RenderScene`, keyword `TOD_OUTPUT_HDR`) → MotionVectors → GlobalEffects →
**legacy `IImageEffect` — ТОЛЬКО компоненты ROOT** → FSR/DLSS (данные root) →
**PPv2-post — ТОЛЬКО слой root** → GUI.

`CameraData.Initialize`: root + `!RenderScene` → пустой cull (механизм
zero-writer!); far-clip extension только при `UseCustomAmbient && 1500≤far`;
`ShadowDistance` per-data (fallback — дефолт ассета); `maximumVisibleLights`
**48 vs 64** по флагу; scissor-плоскости из `CullingScissorRect`.

`RenderGBuffer`: stencil-проход только при `stencilMask`; **`passes.Length != 1`
— единственное место чтения маски прохода** (сингл-пасс = маска игнорируется!);
`perObjectData` в Deferred: `UseCustomAmbient ? Lightmaps :
LightProbe|Proxy|Lightmaps` (в Forward/Transparent — та же вилка ± условие
`UseLightProbesInExteriorViews`); SSR-флаг прохода копит `ssrMask`;
decals по `RenderDecals`.

## 3. Per-camera данные (`AdditionalCameraData`): что с кого зеркалить

Писатель интерьера — со **Interior-камеры**, писатель экстерьера — с main.
Смешивание даёт тинт/артефакты:

| Поле | Источник внутри | Иначе |
|---|---|---|
| `UseCustomAmbient` | `false` (как Interior!) | `true` = strip LightProbe per-object данных → синий флэт `b_r +22` (ROOT CAUSE, Δ25.8). Первое переключение может дать чёрный кадр — транзиент keyword-варианта, settle 5–6 с |
| `ShadowDistance` | Interior (30) | main (370) — замерено нейтрально здесь, но паритет бесплатен |
| `ContactShadows` + 7 параметров | main (флаг + `Length/MaxDistance/...`; 4 шт. get-only — писать через backing fields рефлексией!) | PIP-дефолт `false/0.15/50` vs `true/0.2/50` |
| Passes | дефолтная структура как у main-root (`UseCustomPasses=false` — так и так live!) | кастомный 1-pass лист игнорируется пайплайном |
| `RenderDecals` / `Desaturation` | как у писателя (сейчас `false`/`0` везде) | — |
| GUI-флаги (`RenderPostprocessedGUI`, `ClearStencilFor3DGUI`, `GUITarget*`) | дефолты, не трогать | чужой GUI-контекст |
| `RenderScene` | живьём с view-камеры, НЕ пинить | `true` = белый шар; `false` + подавленный клон = чёрный окуляр |
| `StencilMask` | `false` (как Interior) | `true` течёт экстерьером сквозь корпус |
| `MotionVectorsTarget` | `null` (образец `Periscope.cs:602`) | — |

Контрасты игровых правил, оправдывающие исключения (FPP-контроллер, дословно):
`StencilMask = IsInsideBoat && !isExternalScene` (:940) — main внутри `true`,
а PIP держит `false` (иначе течь экстерьером!); `DisableDynamicShadows =
!IsInsideBoat || isExternalScene` (:941) — динамика теней живёт только внутри,
не-экстерьер.

## 4. Пост-стек: зеркалить vs инертно

Зеркалить: PPv2 `PostProcessLayer` (+ `volumeLayer`, автоэкспозицию per-frame —
dormant днём, жива ночью; `m_CurrentAutoExposure`/`m_AutoExposurePool`),
`SSMS`/`SSMSGlobalFog` + `interiorAmbientColor` per-frame (stale ambient →
overflow light-combine в Inf/NaN, доказано RenderDoc).

НЕ исполняется в SRP (все A/B в ноль — не зеркалить и не чинить!): legacy PPv1,
`AmbientOcclusion`, `ScreenSpaceReflection`, `CP_SSSSS_Main`, `TOD_Camera`,
`OutlinesIME`, `WaterCameraIME`. Механизм: legacy-список собирается только
с root-камеры (§2), а warmth глаз идёт из baked проб + main-PPv2 (измерено:
main-PPv2 off уводит глаза в `b_r −33`).

TAA-рецепт на будущее: свои TAA-параметры (образец `Periscope.cs:1295–1303`) +
`ResetHistory` через `FrameDelay(1)` + НЕ шарить `temporalAntialiasing`-инстанс;
под апскейлом пайплайн форсит `jitterSpread=1`.

Bloom-cut при RT ≤512² обязателен (`Bloom.enabled=false`, PIP-only том, слои
6/7, priority 1000): иначе NaN-блоки в мип-цепочке от ярких ламп (`max RT>1.0`
— сигнатура).

## 5. VSR и позиционирование

- Граф: `MainCamera.Instance → InteriorCamera → Camera + AdditionalCameraData`;
  `PlayerShip.Interior.VirtualScene + Target`; сериализованное поле VSR
  (`virtualScene`) в игре null — сцена приходит через per-frame override.
- Клон parented строго к `InteriorCamera.transform.parent` (locator space
  `(0,100,0)`, identity rotation) — иначе ~100 м смещения. Fallback —
  `VirtualScene.Target`.
- `interiorCamera` поле VSR — static, НЕ ТРОГАТЬ. `SetRenderingEnabled(true)`
  пишет `clearFlags=Nothing` на `localCamera` — восстанавливать свой clear.
- Follower клона: ротация с PIP после VSR (`[DefaultExecutionOrder(10000)]`,
  early-return при закрытом секстанте — иначе спам POSE в лог).
- PIP-root: позиция с InteriorCamera, поворот НЕ трогать (yaw/алидада из Update).

## 6. Маски, порталы, стенсил, вода

- Маски: interior `545264640`, PIP `545264768` (= +sun-бит 128, слой 7).
- Порталы (`ExteriorViewPortalsController.PreCull`): guard-fail → `RenderScene`+
  full scissor; иначе bbox порталов в 4.5 м (`sqrMagnitude>20.25` — пропуск) по
  проекциям Interior-камеры; валидный → scissor, иначе `false`. Миррорить ПАРУ
  целиком, иначе блочные вспышки у дверей.
- Stencil: проход пишет refs = passIndex; light-компайл `Equal(3)` vs
  `GreaterEqual(7)` для passIndex 2; `_SurfaceType` global; GUI-стенил и
  портальный `Equal`-тест — не пересекать.
- Вода: регистрация в `AttachedCameras` main (= отдельные полные рендеры!);
  гейт `SetCustomWaterRenderList` по `InteriorLevel>0.6` (`FPPCameraController`
  :826–830, два списка + `UnderwaterIME.enabled=false` внутри).
  Побочка подавления клона: условие `camerasCount>=2` в underwater-коде теперь
  всегда берёт `[0]`-ветку — принято сознательно.

## 7. Поза, резолвер, зум

- Левая: yaw view; правая: yaw + pitch алидады (`-arcAngle`); FOV/divisor;
  `near/far/depthTextureMode` — с view-камеры покадрово (FPP far пересчитывается
  игрой: 40 м внутри, 170 м во external-сценах, снаружи — туманная формула с
  clamp 300..far; `FPPCameraController:942`).
- Зум `{6,4.5,3,2,1.5,1}`, default idx 5 (×1); липкий (не сбрасывается на
  reopen) — фиксировать в протоколе.
- TestAim — только гейтированный проб, всегда сбрасывать (`ClearTestAim`).
- **Гард обязателен:** `IsPipCamera(viewCamera)` → fallback на последнюю хорошую
  + one-shot warn. Резолвер возвращает PIP в окнах `Camera.main==null`
  (лоады/переходы — доказано логом: `rt=True`-строки + `no rendering camera
  found`); самоследование морозит угол при живой позиции (follower не использует
  резолвер) — лечилось только рестартом (статики!).

## 8. Ночь / тревога / пресеты (читать до дебага цвета!)

Три набора света (`Normal/Red/Blue`): у ламп `Normal/Alarm/BlueIntensity`;
три цвета тумана + volumetric `0.11/0.14/0.18`; активен один набор проб.
**Silent running = СИНИЕ лампы by design** — не путать с тинтом!
`UpdatePresets`: `Alarm` при `TargetDepth>4`, иначе `Surface`. Стенд обязан
пинить глубину/пресет — погружение красит интерьер в красное.

Exterior-чеклист: Sky (`TOD_OUTPUT_HDR`, `DrawSkybox` лишь при
`clearFlags==Skybox`), звёзды (`StarsMesh` off внутри), море (п.6),
sun disc (`Active = IsOpen && rightFilters>0`), exterior-фог.

## 9. RT и дисплей

`512²/32bit/ARGBHalf/Linear`, clear `SolidColor` black; обе камеры `enabled`
(SRP-цикл, ручной `Render()` — no-op для disabled!). Материал — непрозрачный
из бандла (цепочка `ResourceManager → bundle → Assets/Packages/testui/<name>`;
стоковый UI-шейдер × alpha RT!). Аудит цветов: views/RawImages white,
dims transparent, glare ~0.05, backing black. Какая часть RT показывается в
круге окуляра — НЕ верифицировано (гипотеза «половина RT» из §16.11, остаток);
при расхождениях проверить crop-соответствие явно.

## 10. Тома и перископ-эталон

Тома кроссфейдятся ~1 с по `IsInsideBoat` (+jitter-твин) — A/B после settle;
override-тома (DOF-фокус: `1500f` перископ / `7.5f` FPP) — общие.
Перископ: линза = композит в кадр main (`AttachTo(main, interior, VisionUI)`),
свой том `"Periscope"`, main-PPv1 off на takeover (restore на выходе),
свой PPv1-профиль только в fullscreen (в lens `profile=null` — пост уже
применил main!). **Правило выбора:** RT-в-кадр → заимствуй пост main;
RT-как-финал (Overlay UI после поста) → неси полный стек сам.

## 11. Инфра мода (карта)

`TypeMemberCache`/`GameTypeCache` (резолв строкой, private OK),
`WarnOnce` (one-shot логи — детект тихих no-op!), `CameraResolver` (+гард),
`SextantPipFactory` (порядок §выше), `SextantEffectMirror` (порядок исходника,
приватные поля PPv2 — только `volumeLayer`+`m_Resources`+`Init`),
`SextantInteriorMirror` (граф + `Sync`/`SyncAmbientMode`), `SextantPipPose`
(поза + гард + кэши), follower'ы (10000), `PipCloneCameraPatch` (строки таргета,
`__1`, гейт, счётчик), `SextantBloomCut`, probe-поверхность
(`Snapshot/Set/Aim/SetSsmsForProbe/SetComponent/RtMean/PassDump`), дисплей-билд
(`SextantOcularBuilder` + `LoadBundleAsset`), гейты
`RadioNav.{PipSingleWriter,ContactShadowMirror,Ppv1Mirror,AmbientModeMirror}=1`,
`TestAim=0`.

## 12. Build / test / deploy

Raw `csc` (`build.ps1`) + **блокирующий PVS-гейт**; C# **7.0** + игровые дефайны
без `DEBUG` (`default;` = CS8107 → битый assembly; `const bool`-гейты = CS0162
→ `static readonly`); `SourceCheck` (neural-slop guard, 53); `tests.ps1`
(свой раннер, 815); `analyze.ps1` (RN0001–RN0010 + IDE-аудит, complexity ≤15 —
экстрактить); деплой копией DLL в `Temp` + **рестарт** (hot-reload сломан —
DOUBLE LOAD); live-работа через McpBridge.

## 13. Стенд приёмки (обязательный минимум)

Сейв-поза + фиксированные время/погода/глубина/пресет/зум/arc/фильтры; пауза;
пары A/B в одном окне; settle 15 с после load / 5 с после тогглов;
`screen 1920×1080`; метрика DISC (`r250@(960,540)`) + BRIGHT (`min>110,
max<250`) + GRID + `b_r`/`b_g`/max/blackfrac; порог `|Δb_r|<5`; гигиена гейтов
до/после; позитивное доказательство срабатывания. Гочи: PNG-превью лжёт
(только числа); R-кадры дрейфуют unpaused; keyword-транзиенты; load-fade.
Мост: `input_key Minus → bad-key` (зум только кодом — фолбэк freeze текущего
div с записью!); `screen_set 1920×1080` каждый прогон (окно плывёт!).

## 14. Диагностика по сигнатурам

Коррупция + `SuppressionCount=0` → второй писатель. Чёрный окуляр: ноль
писателей / VSR после первого Render / транзиент / load-fade. Белый шар:
запиненный `RenderScene`. Портальные блоки: scissor. `b_r>+10` + RT
`[72,74,80]`: `UseCustomAmbient=true` внутри. Угол стоит/позиция едет +
`rt=True`-строки: самоследование. L/R-разъезд: дивергенция поз при `arc=0`.
NaN-блоки + `max RT>1.0`: bloom на 512.

## 15. Открытый реестр (как закрывать)

Слои ламп (`dump_scene component=Light` → `reflect_get layer` — решает вопрос
дележа света по проходам!); содержимое прохода Interior[1] (слой 17);
бинарник шейдера бандла (RenderDoc-дизассемблер draw вида); тюнинги томов;
`CP_SSSSS/TOD/FogOfWar` внутренности; `StreamingController` bias;
`_SurfaceType` downstream; контент decals; 512-vs-1024 цена (`perf_sample`).

## 16. Глоссарий

VSR/VirtualCamera; root vs `cameras[1]`; writer; perObjectData (пробы вкл/выкл);
scissor; keyword-вариант; hook-cache; gate; стенд; DISC/BRIGHT/GRID; `b_r`;
settle; lens vs fullscreen; FPP NG; `__N`-привязки.

## 17. Дополнение: аудит полноты (проверка построчно с исходниками)

| Раздел гайда | Статус | Комментарий |
|---|---|---|
| Инвариант, архитектура, пайплайн, данные, пост, VSR, маски, вода, поза, ночь, RT, тома, перископ, инфра, build, стенд, диагностика, числа, процессы, реестр | OK | Сверены с кодом/декомпилятом/live — расхождений нет |
| Телеметрия | Было отсутствует | См. §17.1 |
| Open/Close lifecycle | Было частично | См. §17.2 |
| Wiring мода | Было отсутствует | См. §17.3 |
| Дискриминатор залипания | Был неточен (VEC-rotate идёт только при повороте!) | См. §17.4 |

### 17.1. Телеметрия (`SextantVectorLog`, контракт)

Строки: `VECL/VECR` (позы половин: pos/eul/fwd/up/rgt), `VECV` (view-камера),
`VECD` (arc/quant/fmt/angLR/dotLR), `VECSUN` (own Meeus vs game TOD_Sky:
alt/az/sep), `VECSP` (ndc/offDeg/fov). Гейты: rotate-дамп — только при смене
yaw view-камеры ≥ `RotateMinDeltaDeg=0.5°` не чаще `HoldIntervalSeconds=0.5с`;
`MANUALRENDER frame=N arc=X` — heartbeat ~1 Гц из `UpdatePipPose` (строка
сохранена ради существующих log-grep!). `Enabled=true` — `static readonly`,
не `const`, с причиной в коде дословно:
`static readonly (not const): a const bool folds away in the C# 7.0 game
compiler and produced CS0162 unreachable-code warnings at the call sites;
readonly keeps the gate checks live.`

### 17.2. Open/Close — точные эффекты (`SextantController`)

Open: blockade + canvas + `AttachToMain` (регистрация в `AttachedCameras`!) +
`enabled=true` обеим камерам + поза + `IsOpen=true` + `bloomCut.Update` на edge
(иначе ждёт первого keypress!). Close: `IsOpen=false` → `ClearTestAim` →
снять blockade → `Detach` (вода!) → VSR off → sun disc off/reset →
`bloomCut.Hide()` → камеры `enabled=false` → UI hide → лог `closed`.
**Переживают закрытие** (важно для A/B!): камеры, RT, зум (липкий!),
`lastGoodViewCamera`, `SuppressionCount`, PlayerPrefs-гейты.

### 17.3. Wiring-минимум (`Manifest.json` дословно)

`assemblyName: radionavigation`, `permissions: ["Reflection", "IO"]`
(рефлексия — для `TypeMemberCache`/`InjectIntoAssembly`; инжект — явно в
сборку, т.к. имя не совпадает с `uboat` и авто-инжект фреймворка не
срабатывает), `supportedGameVersions` игрой **не валидируется** (парсится,
не проверяется), версия `2.1`.

### 17.4. Дискриминатор залипания v2 (исправленный)

Старая формулировка («VEC идёт = Update жив») неверна: VEC-rotate молчит и в
здоровом простое. Правильно трёхуровнево: `MANUALRENDER` идёт → Update жив;
`MANUALRENDER` стоит → Update мёртв; `MANUALRENDER` идёт + `VEC-rotate` стоит
при повороте юзером → заморожен источник позы (дальше — VECV vs позиции камер
через `reflect_get`). Дрейф <0.5°/0.5с в логах НЕВИДИМ по построению.

## 18. Дополнение 2: дословная механика, процессы, разборы

### 18.1. `RenderLightsDeferred` — делёж света по проходам (decompiled.cs:3504+)

Цикл по проходам камеры: `_CameraIndex = passIndex++`, `_StencilComp =
(passIndex==2) ? 7 : 3`, маска света `passes[i].CullingMask & cullingMask.
Per-light: фильтр `((1 << light.layer) & mask) != 0`; WaterCamera
`EffectsLight` подменяет источник (`_LightPos`-хак!); volumetric intensity из
water-данных; `_VolumetricBlendFactor = (passIndex != 3) ? 1 : 8`; keywords
`POINT/SPOT/DIRECTIONAL` (+`COOKIE`), `VOLUMETRIC_LIGHT`; контактная маска по
`contactShadowIndices`. Следствие: 1-pass vs 2-pass рендеры делят свет и
константы бленда по-разному — эквивалентность НЕ гарантирована, только A/B.

### 18.2. `RenderPostProcess` — тонкая обёртка (:3778–3803)

`layer.Render(...)` — порядок эффектов внутри = собственность PPv2-пакета
(здесь не виден!). GUI-ветка vs plain. `PostMotionBlur`:
`IPostMotionBlurImageEffect` с `cameras[0]` (`GunReticleIME`, `PeriscopeIME`) —
четвёртый root-only гейт. `ColorGrading postExposure` → global
`_ColorGradingPostExposure` после рендера.

### 18.3. Тени, motion vectors, апскейл, вода, небо, GUI — точные точки

- Тени: `RenderShadows` per-камера (свои атласы exterior/interior); контактные
  тени per-камера по своему флагу; `ValidateShadowSettings`
  (`PlayerShipInteriorLighting.cs:686–711`, дословно; `{Shadows, Alternating,
  Static}` = SectionView | остальные): Extreme `{2,0,1}|{4,1,2}`;
  VeryGood `{2,0,0}|{3,1,2}`; Good `{1,1,0}|{2,1,0}`; Simple `0/0/0`;
  alternating — по `frameCount` (на паузе замирают).
- Motion vectors: override `MotionVectorsTarget`, temp `_CameraMotionVectors`,
  `SetMotionVectorsConstants` + `RenderCameraVelocity` (root); `useJittered...`
  для прозрачных; `depthTextureMode` копировать (иначе нечем кормить MB/TAA).
- Апскейл: `RenderFSR/RenderDLSS` по данным+слою root; `_GlobalMipMapBias =
  log2(width/num2)−1`; `jitterSpread=1` под апскейлом; `UpscalingBiasMask`.
  PIP — native (`Off`): achromatic-разница, не тинт.
- Вода: `GetWaterCamera` + `Type==Normal` → `Prepare/RenderStart/DepthPrepass/
  Render/Reflections/Lighting`; вне воды — `_WaterlessDepthTexture`=white.
  `AttachedCameras` = отдельные полные `Render()` (§2!).
- Небо: только root при `RenderScene` (+`TOD_OUTPUT_HDR`); `DrawSkybox` лишь при
  `clearFlags==Skybox` (у PIP `SolidColor` — пропуск!); `RenderSky`
  (opaquesOnly+stencil) + reflection-blit (pass 1) vs plain blit.
- GUI: `GUITargetOverride`, `RenderPostprocessedGUI`,
  `PostprocessGUIRenderer` получает массив камер (!), `ClearStencilFor3DGUI`;
  паттерн `interiorCanvas.worldCamera = mainCamera.GUICamera`.
- Декалы: `_DecalBuffer` (`R8G8_UNorm`), `AdvancedDecalBlending`, `BlendDecals`
  (pass 3) — контент не исследован.
- Туман-тома: `_FogTex`, `TransparencyFogTex`, хуки GlobalEffects
  (`RenderGBuffer`/`RenderPreImageEffects`) — точки расширения.

### 18.4. GBuffer-раскладка и сентинелы

MRT-набор прохода (из RenderDoc-разбора): `R8G8B8A8 ×3 + R16G16B16A16` +
`R32 _GBufferZ` (+ копия `_CameraDepthTexture`, `FilterMode.Point`).
Логарифмический depth; неинициализированные пиксели `±1.7e38`; смешанный
depth (два писателя) → light-combine читает чужое → Inf/NaN в канале B
(механика §16.17 на уровне буферов).

### 18.5. `CameraData` инвентарь

`camera/deepCamera/cullResults/guiCullResults/lightIndexMap/
directionalLightCount` + `Initialize(context, camera, defaultShadowDistance =
asset.shadowSettings.maxShadowDistance, waterPipeline, isFirstCamera,
allowSkippingSceneRendering)`. `ShadowDistance<0` → дефолт ассета.
`allowSkipping && !RenderScene` → пустой cull (механизм zero-writer!).

### 18.6. Отвергнутые дизайны (защита от ре-литигации)

- hold-on-dark: `endCameraRendering/endFrameRendering/OnPostRender` — 0 вызовов
  (SRP рисует после LateUpdate) → неосуществим модом.
- Прямой рендер интерьера без клона/VSR: чёрный (геометрия не позиционирована).
- Отдельный GBuffer на камеру: Harmony-риск на hot path — отклонён.
- Удаление legacy-стека: нечего удалять (инертен по построению §2).
- 1024² / warming volume: не мерялось / не тестилось — открыты (§15, §18.11).

### 18.7. Процессы: анализаторы и слепые пятна

RN0001 (metadata-фолбэк) / RN0002 (ambiguous) / RN0003 (сигнатуры через
`typeof`) / RN0006 (поиск в Update — кэшировать!) / RN0007 (аллокации в
`OnAudioFilterRead`) / RN0008 (Unity-API только main thread) / RN0009 (prefix
→ `bool`/`void`, postfix → `void`) / RN0010 (рефлексия по строкам — null-guard
обязателен) + IDE0051/52 CLI-аудит + complexity ≤15 (экстрактить!) +
SourceCheck 53 + tests 815. СЛЕПЫЕ ПЯТНА (не ловятся ничем статическим):
`__N`-привязки Harmony, hook-cache staleness, keyword-транзиенты шейдеров,
дрейф стоек, содержимое томов.

### 18.8. C# 7.0: разрешено/запрещено (свидетели в коде)

OK: `out`-переменные + `is`-паттерны (`TryGetProfile`), `string.Format
(InvariantCulture)` вместо `$""`, `static readonly` вместо `const bool`
(гейты!), expression-bodied где читаемо. BAN: `default`-литерал (CS8107 →
битый assembly в игре!), `const bool` как гейт (CS0162 в игровом компиляторе),
`GetComponent/Find` в hot path, рефлексия без null-guard, шаринг PPv2
model-инстансов между слоями (TAA/fog/dithering/SMAA — мина!).

### 18.9. Словарь лог-сигнатур (дословно)

`ATTACH water=.. attached=.. leftEn=.. rightEn=.. fov=.. pipFov=..` ·
`INTERIOR graph resolved: clone source=.. parent=..` · `PIP cameras created,
RT=512x512` · `BLOOMCUT right/left on (filters=N)` · `CONTACTSHADOWS mirrored
onto PIP (first apply)` · `PPV1 profile mirrored: <name>` · `AMBIENT-MODE
mirrored onto PIP (inside=True/False)` · `Patched DeepPipeline.CollectCameras
(PIP single-writer)` + `PIP single-writer suppression active (first
CollectCameras intercepted)` · `opened (two-camera split ocular)` / `closed` ·
`cannot-open` · `no rendering camera found` · `PipSelfFollow`-warn ·
`SUNDRIFT ...` · `MANUALRENDER frame=N arc=X` · `ZOOM div=X fov=Y` ·
`FILTERSIDE ...` · `VECL/VECR/VECV/VECD/VECSUN/VECSP` (гейты §17.1).

### 18.10. Золотая таблица пар (стенд «Трубы», div=1.0 кроме первой)

| Пара | BRIGHT `b_r` A | BRIGHT `b_r` B | Δ | Вердикт |
|---|---|---|---|---|
| Золото div=3.0 | −2.6 | +21.9 | 24.5 | баг воспроизведён |
| Золото div=1.0 | −2.5 | +16.8 | 19.3 | баг воспроизведён |
| Contact on/off | — | +16.8/+15.2 | 1.6 | OUT |
| PPv1 PIP on/off | — | +16.6/+16.7 | 0.1 | OUT |
| PPv1 main on/off (глаза) | −1.1/−1.0 | — | 0.1 | OUT (инертен) |
| PPv2 Interior on/off (глаза) | −0.3/−0.3 | — | 0 | OUT |
| SSMS PIP on/off | — | +22.5/+22.4 | 0.1 | OUT |
| ShadowDistance 370/30 | — | +24.6/+24.6 | 0 | OUT |
| Pass default/custom | — | +23.0/+22.9 | 0.1 | OUT |
| Contact main/interior on/off | −1.3/−1.4, −1.3/−1.3 | — | ≤0.1 | OUT |
| PPv2 PIP on/off | — | +16.6..25/+3.3 | −13..−22 | МЕХАНИЗМ |
| PPv2 main on/off (глаза) | −1.1/−33.3 | — | −32 | МЕХАНИЗМ |
| UseCustomAmbient PIP | — | +22.9/−2.9 | 25.8 | ROOT CAUSE |
| Финал (шиппед) | −5.7 | −6.0 | 0.3 | ЗАКРЫТО |
| RT readback | — | `[72,74,80]`→`[54,50,47]` | — | тинт в RT |

### 18.11. Открытый реестр v2 (приоритет!)

1. **Слои ламп** — первым делом при живой игре (`dump_scene
   component=Light` → имена → `reflect_get layer`): решает, делит ли 2-проходная
   схема свет (маски `{0,2,13}`/`{17}` vs full). Если лампы вне масок проходов —
   глаза без прямого света ламп вообще!
2. Interior pass[1] (слой 17 = ?).
3. Бинарник шейдера бандла (RenderDoc-дизассемблер draw вида).
4. Тюнинги томов; `CP_SSSSS/TOD/FogOfWar` внутренности; `StreamingController` bias.
5. `_SurfaceType` downstream; контент decals; 512-vs-1024 цена (`perf_sample`).
6. `CompartmentUnderwaterIME` `[0]`-ветка при воде в отсеке (принято, перепроверить).

## 19. Дополнение 3: RenderCheck, скоуп, деревья, триаж (аудит)

### 19.1. RenderCheck-контракт (`SextantRenderCheck`)

Стреляет 2 раза за открытие (1 с после open + 1 с после алидады >20°),
single-pixel центр-колонка t/m/b + ALPHA (`#020101(0.50)` — оттуда старый
alpha-анализ); `ResetForOpen` per-open; owned-текстура в `Destroy`. Per-frame
вызова бояться не надо (early-return когда done). `manualRenderDelegate` —
только heartbeat-лог (`MANUALRENDER frame=N arc=X`, строка сохранена ради
grep), двойного рендера нет.

### 19.2. Граница скоупа гайда

Рендер — да (камеры, проходы, свет, пост, дисплей, поза incl. алидада-питч,
резолвер, FilterDim-аудит). НЕ покрыто: идентификация/worksheet/readout/
FPPBody/UI-логика (только точки касания: `canSight`, blockade). `CanOpen`
дословно:
`IsLiveViewMode` (SectionView/FPP **строкой** — хрупко к переименованиям!) +
живой резолв камеры + `GameInput` (иначе `cannot-open` / `Z ignored` в логе).
Мод НЕ реализует `IImageEffect` — своих эффект-стейджей у PIP нет вообще
(только тома + `WaterCamera`).

### 19.3. Per-pass свет и RAW-разрыв (фиксация)

`B_pp2off` (+3.3) содержит PIP-SSMS (~0 измерен) + moot bloom-cut.
`A_mainpp2off` (−33.3) содержит main-SSMS (~0) + CP (~0) + inert PPv1.
Разрыв RAW 36 единиц — факт, не артефакт сравнения. Делитель света:
`((1<<light.layer) & passMask)` — лампы вне масок проходов (`{0,2,13}`/`{17}`)
глаза не светят вообще (проверяется слоями ламп — реестр п.1!).
`_StencilComp` 7-vs-3 и `_VolumetricBlendFactor` 1-vs-8 — по индексу прохода.

### 19.4. Перископ-добор (к §10)

`ResetHistory` через `FrameDelay(1)`; `main.cullingMask=0` в fullscreen;
`MotionVectorsTarget=null` (`Periscope.cs:602`); `depthTextureMode |=` при
флаге main (:1291); `AttachedCameras` Add/Remove (:581/:607/:950/:1261/:1615).

### 19.5. Валидационная матрица (открыта!)

Все A/B — один пресет качества (post 3/bloom/AA-off/FSR2/shadows 3).
Не проверено: Low/Ultra (SSMS гейтится Extreme! CP — VeryGood! таблица теней
меняется!), SectionView (`IsLiveViewMode` разрешает, тестов ноль!),
перископ/UZO параллельно, карта при открытом окуляре, bloom-cut OFF
(deferred X), Interior-PPv1-профиль на PIP (закрыто как moot-при-inert).

### 19.6. Триаж-инфра (файлы и ловушки моста)

Логи: `output_log.txt` (текущий), `output_log (previous).txt` (археология!),
`boot.log` (рестарты/мост), `Player.log`, `settings.ubt` (графика одной
строкой — читать скриптом). Ловушки `reflect_*`: двойная строка типа
(`...RadioNavigation.RadioNavigation...`) → `type-not-found`, правильная —
одинарная; приватные поля читаются (`contactShadowsLength` проверен);
`LayerMask` сериализуется непрозрачно; `wait_for` падает в окне рестарта.

### 19.7. GUI/вода/туман/декалы одной строкой

`GUITargetOverride`, `RenderPostprocessedGUI`, `PostprocessGUIRenderer`
получает массив камер, паттерн `interiorCanvas.worldCamera`. Вода:
`GetWaterCamera` + `Type==Normal`, `EffectsLight`-override (`_LightPos`-хак!),
`_WaterlessDepthTexture` вне воды. Туман-тома: `_FogTex`,
`TransparencyFogTex`, хуки GlobalEffects — точки расширения. Декалы:
`R8G8_UNorm`, pass 3, контент неизвестен.

### 19.8. Деревья решений (кратко; сигнатуры — §14)

- Чёрный: ноль писателей? → VSR после первого Render? → keyword-транзиент
  (settle!) → load-fade (15 с)?
- Синий: `BRIGHT b_r` + RT-ридбэк (`RtMean`) → `UseCustomAmbient` на писателе?
  → тома/пресеты (ночь? тревога? глубина?).
- Stuck: `MANUALRENDER` идёт? → `VEC-rotate` при повороте? → VECV vs позиции
  камер (`<0.5°` невидим!). Плюс `rt=True`-строки резолвера.
- Split: дивергенция поз при `arc=0` → разные пути/моменты половин.
- NaN: `max RT>1.0` → bloom на 512 → bloom-cut; мягкие ореолы — мимо.

## 20. Статус приёмки и внедрения (что шиппнуто, что предложено)

**Закрыто (шиппед, live-проверено):** single-writer + прямое позиционирование
(§16.22); `SyncContactShadows` (паритет, визуально нейтрален); `SyncPostProfile`
(паритет, SRP PPv1 не исполняет — нейтрален); `SyncAmbientMode` (тинт Δ25.8→0.3,
подтверждено пользователем); guard самоследования (молчание в норме =
здоров); bloom-cut; scissor+`RenderScene` миррор; bundle-материал.

**Предложено, НЕ шиппнуто** (не принимать за внедрённое!): `ShadowDistance`
30 с Interior (замерено нейтрально — низкий приоритет); warming volume
(не тестировался — фолбэк при регрессе тинта); 1024² RT (цена не мерялась);
Interior-PPv1-профиль на PIP (moot-при-inert).

**Открытая валидация:** сложный вид (рубка, возврат коррупции); ночь
(экспозиция); Low/Ultra пресеты; SectionView; перископ/UZO параллельно;
карта при открытом окуляре; bloom-cut OFF (deferred X).

### 20.1. Teardown-чеклист (против утечек долгоживущих сессий)

`OnDestroy`: `Instance=null`, `RemoveBlockade`, `pipFactory.Destroy`,
`SextantPipPose.Reset`, `ClearTestAim`, `IsOpen=false`, `Destroy(canvasRoot)`,
`bloomCut.Destroy` (тома + owned-профили `ScriptableObject`!), текстуры
(`ring/circleMask/eyecup`), owned-спрайты (все!), `renderCheck.Destroy`
(probe-текстура). `SextantPipFactory.Destroy`: `Detach` (убрать из
`AttachedCameras`!), `interiorMirror.Destroy` (VSR + клоны), RT
`Release()+Destroy`, занулить камеры/данные/воду/кэши. Гейт
`SextantOwnedResources_AreDestroyed` следит; новое owned — сразу в оба
`Destroy` + тест.
