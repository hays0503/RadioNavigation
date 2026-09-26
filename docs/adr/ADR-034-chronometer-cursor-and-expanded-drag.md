# ADR-034: Курсорные зоны и расширенный драг хронометра

> Статус: Accepted (2026-08-23)  
> Связи: `Source/UI/ChronometerScaleControl.cs`, `Source/UI/ChronometerOverlayHandler.cs`, `Source/UI/ChronometerCursorPolicy.cs`, `Source/UI/ChronometerMapDragMath.cs`, `Source/UI/PanelDragHandler.cs`, `Tests/ChronometerCursorPolicyTests.cs`, `Tests/ChronometerMapDragMathTests.cs`, `PLAN_Phase5.md`

## Контекст

- Нативный драг циферблатов (`ShipControlUI.HandleDialDragging`, :1310) захватывает жест по **кругу фиксированного радиуса вокруг пивота** (√30625 × uiScale/1.45 ≈ 175 px), а не по визуальному ректу.
- Замер `[ChronoDbg]` (2026-08-23): обычный размер — farthest угол 275 px → покрытие 64%; при модовом ×2.5 — 687 px → покрытие 25%. Драг увеличенного хронометра физически возможен только у нижних углов.
- Hover-подсказки через uGUI pointer-events ненадёжны: нативная графика над прозрачным контролом перекрывает raycast.
- Игровой hand-cursor (`CursorManager.SetHoverCursor`) визуально не отличим от обычного — подтверждено логом: цепь вызовов работала, эффекта на экране нет.
- `CursorManager` хранит только режим, не текстуру: чужие `UnsetCustomCursor` (инструменты карты) переводят режим в Default, не трогая текстуру → guarded `SetDefaultCursor` раннего выхода оставляет кастомный курсор залипшим.

## Решение

### 1. Геометрический сенсор вместо uGUI событий
`ChronometerScaleControl.Update` — rect-тесты против мыши каждый кадр:
- центр 52×52 (сам контрол) = зона масштаба;
- остальной рект `chronometerToolDragContainer` = зона перемещения;
- удержание ЛКМ после нажатия внутри тела замораживает смену курсора;
- клик = press+release внутри центра без превышения `EventSystem.pixelDragThreshold`; drag никогда не toggлит масштаб; клик-элигибилити фиксируется зоной нажатия.

### 2. Чистая политика + тонкий исполнитель
`ChronometerCursorPolicy.Evaluate(scale, move, expanded)` → команда {None, ArrowsExpand, ArrowsReduce, Crosshair, GameDefault}; emit-only-on-change; стрелки владеют центром. `RefreshCursor()` в handler'е — единственная точка применения: команды транслируются в бэкенды (CursorManager Custom / fallback override), `GameDefault` всегда через `ApplyGameDefaultCursor()`.

### 3. Возврат дефолта — принудительный
`ApplyGameDefaultCursor()` = `SetDefaultCursor()` (синхронизация режима) + force-apply приватной `normalCursorTex` через кэшированный reflection. Иммунитет к чужим `UnsetCustomCursor`.

### 4. Стрелки expand/reduce — собственная графика
Заметный визуальный hint вместо неотличимого игровой руки; fallback-ветка без синглтона сохранена.

### 5. Расширенный драг на карте — перехват модом
При expanded нажатие в теле: сенсор стримит дельты мыши (`OnMapDragBegin/Delta/End`), handler двигает `container.position`, каждый кадр `CancelNativeDrag()`. Позиция персистится через reflection-мост в нативный формат сейва:
`static uiDialsDataHolder` → `IUserInterfaceDialsDataHolder.ChronometerPosOverride` (нормализация `InverseLerp(20, W−20, x/y)` вынесена в чистый `ChronometerMapDragMath.ToNormalizedOverride`) → private static `ApplyToolPositionLimits` (кламп краёв). Save/load round-trip идентичен игре.

### 6. GameInput blockade
На время перехвата — `gameInput.AddBlockade(this)` … `RemoveBlockade(this)` (как в `HandleDialDragging`): карта и прочие потребители ввода не реагируют на жест.

### 7. Зеркалирование зон и гвард PanelDragHandler
Зоны работают одинаково на карте и в поднятой радиорубке; `PanelDragHandler.OnPointerEnter` получает guard `chronometerPointerInside` — его move-курсор не конфликтует с зонными курсорами при raise.

## Альтернативы (отклонены)

- **Игровой hand как hover-hint** — визуально неотличим от обычного курсора (лог-верификация).
- **Адаптивный кап увеличения внутри нативного кольца** — потребовал бы коэффициент <1.0 (базовый farthest 275 px уже > радиуса 175 px).
- **Центр-компенсация пивота** — не влияет: farthest 687 px >> любого правдоподобного радиуса.
- **uGUI IPointerEnter/Click для зон** — перекрытие raycast нативной графикой.
- **Штатный SwapCursor** — даёт только руку/disabled, крестика нет.
- **Свой драг поверх натива без записи PosOverride** — позиция прыгала бы к последней нативной при рестарте сцены.

## Последствия

+ Полное покрытие лица курсорами и драгом при любом масштабе; сейв/лоад позиции без изменений; карта не панорамируется под жестом.
+ Политика чистая → пиннится тестами без Unity: 8 кейсов `ChronometerCursorPolicyTests` (все симптомы сессии) + 3 `ChronometerMapDragMathTests`; baseline 263 PASS.
− Рефлексия на приватные члены игры (`normalCursorTex`, `uiDialsDataHolder`, `ApplyToolPositionLimits`, `ControlSettings.uiScale`) ломается при их переименовании в апдейтах игры; деградация мягкая — warn once + прежнее поведение.
− Формула нормализации позиции продублирована (натив делегат vs наш `ToNormalizedOverride`) — зафиксирована тестами математики.

## Верификация

`tests.ps1` 263 PASS · `build.ps1` OK · live-подтверждение всех жестов пользователем (2026-08-23).

Refs: `PLAN_Phase5.md`, `docs/adr/ADR-021`, `ShipControlUI.cs` (:1116, :1208, :1310), `CursorManager.cs`, `SwapCursor.cs`.
