# ADR-022: Созвездия — линии из Stellarium index.json

> Статус: Accepted (2026-08-19)  
> Связи: `docs/plans/STAR_SKY_PLAN.md`, `Source/Data/ConstellationLines.cs`, `Source/Logic/ConstellationMesh.cs`, `ADR-021`

## Контекст

- Для полной навигационной системы нужны не только звёзды, но и линии созвездий — помогают ориентироваться на небе.
- Stellarium имеет 88 современных созвездий в `skycultures/modern/index.json` с полилиниями HIP-номеров.
- Нужно конвертировать HIP-номера в индексы `StarCatalog.Stars` и рендерить как тонкие квады.

## Решение

### 1. ConstellationLines.cs — статические данные
- Python скрипт парсит `index.json` → 215 line segments (4 пропущены из-за отсутствующих HIP)
- Каждая линия = массив индексов в `StarCatalog.Stars`
- Генерируется как `int[][] Polylines` — конвенция мода (`BeaconData.cs`)

### 2. ConstellationMesh.cs — MonoBehaviour
- `LateUpdate()` — перестраивает mesh при движении камеры > порога
- Для каждой линии: конвертирует RA/Dec → world position через `AstroMath.RaDecToEnu`
- Рендерит тонкие квады (ширина 2' угловых) между последовательными точками
- Цвет: reddish-brown `(0.55, 0.22, 0.15, 0.4)` — как в Stellarium по умолчанию

## Причина

- Без линий созвездий небо выглядит "голым" — сложно ориентироваться.
- 215 сегментов × 4 вершины = 860 вершин — тривиально для GPU.
- Тонкие квады вместо `GL.Lines` — совместимость с URP/HDRP.

## Альтернативы

- **A. GL.Lines** — отвергнуто: deprecated в новых Unity, проблемы с URP.
- **B. LineRenderer** — отвергнуто: один компонент на линию, 215 GO — overhead.
- **C. Шейдер с geometry shader** — отвергнуто: сложность, не нужна для 215 сегментов.

## Tradeoff

+ 215 линий, цвет как в Stellarium, mesh rebuild только при движении камеры  
- 4 линии пропущены (HIP не в каталоге mag<6) — приемлемо  
- Ширина фиксированная 2' — можно сделать зависящей от расстояния позже

## Статус

- [x] `ConstellationLines.cs` 215 line segments (Python из Stellarium index.json)
- [x] `ConstellationMesh.cs` mesh рендер тонких квадов
- [x] Main.cs интеграция — создаёт `ConstellationRoot` с `ConstellationMesh`
- [ ] Тумблер в GUI (показывать/скрывать созвездия)
- [ ] Зависимость ширины от расстояния до звезды

```json
{"id":"ADR-022","decision":"ConstellationLines (215 сегментов) + ConstellationMesh (тонкие квады)","reason":"Линии созвездий для навигации, конвенция BeaconData.cs","alternatives":["GL.Lines","LineRenderer","Geometry shader"],"tradeoff":"215 сегментов vs 4 пропущены, фиксированная ширина"}
```
