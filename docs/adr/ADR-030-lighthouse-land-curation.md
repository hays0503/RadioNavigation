# ADR-030: Land curation маякового флота — перенос воды в LeuchtfeuerExcluded.json

> Статус: Accepted (2026-09-09)  
> Связи: `Source/Data/Leuchtfeuer.json`, `Source/Data/LeuchtfeuerExcluded.json`, `Source/Data/LeuchtfeuerLandProbe.csv`, `Source/Logic/LighthouseLandGate.cs`, `LighthouseCoastProbe.cs`, `LighthouseLandSweep.cs`, `Tools/Exclude-WaterBeacons.ps1`, ADR-027, ADR-028

## Контекст

- В Leuchtfeuer DB 1184 реальных огней; заметная часть координат — вода (молы, пирсы, портовые огни). Игровой префаб ставится на Y=0 (`SandboxEntity.SetRandomSpawnPosition` не поднимает не-корабли; категория `Structure` не входит в маску `TerrainObjects`) → водные маяки стояли на поверхности моря.
- `CoastQuadTree.GetDistanceToCoast(pointKm)` — знаковое расстояние до векторного берега в км (минус = суша внутри контура), работает глобально без стриминга террейна. DI singleton, готовность через `IsLoading`.
- Решение пользователя (2026-09-09): курирование переносом данных; рантайм-гейт не делать.

## Решение

- Порог `LighthouseLandGate.ShoreBandKm = 0.3` км: вода = `d > +0.3`; полоса ±0.3 = `borderline` (остаётся); NaN/∞ = `unknown` (остаётся — неизвестность никогда не исключает).
- Dev-console команда `rn_lighthouse_landprobe` (`LighthouseLandSweep`) сняла все профили и записала `Source/Data/LeuchtfeuerLandProbe.csv`: 846 строк (841 театральный + 5 курируемых), вердикты **land=396, borderline=161, water=289, unknown=0**.
- Валидация знака: B0983 Campen d=+0.910 при высоте террейна −22.6 м (под водой) — «плюс = вода» подтверждено кросс-чеком; Wangerooge −0.154 borderline (остров), B1054 Norderney −0.351 land.
- `Tools/Exclude-WaterBeacons.ps1` перенёс 287 водных записей из `Leuchtfeuer.json` в `LeuchtfeuerExcluded.json` (архив; рантайм его не читает): 897 + 287 = 1184, пересечение по name|lat|lon = 0.
- Курируемые: B0983 и B1318 (вода) — `enabled:false` в C# + их DB-близнецы ушли в excluded; Wangerooge `enabled:true`; B1054/B1309 (land/borderline) — `enabled:false` в C#, но их DB-близнецы остаются в основном файле и размещаются как обычные registry-огни.
- Рантайм: `LeuchtfeuerRegistry` читает только `Leuchtfeuer.json` → фильтр сработал автоматически; `LighthouseLandGate` остаётся библиотекой для sweep/офлайн-тестов знака.

## Причина

Рантайм-гейт оставлял бы грязные данные в флоте и требовал проверки готовности quad tree на каждом старте; перенос данных решает задачу один раз и заодно ускоряет мод (554 театральных профиля вместо 841).

## Альтернативы

- A. Рантайм-гейт в `EnabledFleet()` через CoastQuadTree — отвергнуто: зависимость от готовности дерева, стоимость на каждый старт, данные всё равно грязные.
- B. Классификатор по `ProceduralTerrainGrid.GetInterpolatedHeight` — отвергнуто: работает только для стриминговых сегментов рядом с игроком (sentinel −2000), глобально бесполезен.
- C. Исключать и borderline — отвергнуто: портовые/моловые огни (B1014, B1026, B1027*, B1086*, B1158) стоят на гидросооружениях, векторный берег до них не дотягивается; визуально корректны.

## Tradeoff

+ Флот 555 (был бы 842); все водные исчезли из карты и 3D; stale-cleanup мигрировал старый сейв (18 групп удалено, включая дубль-набор с суффиксом `#1`).
− Borderline ±0.3 км (161 шт.) — серая зона; вердикты зафиксированы в CSV для ручного пересмотра.
− `LeuchtfeuerExcluded.json` рантаймом не читается — при изменении берегового дерева нужен повторный sweep и перенос.

## Статус

- [x] `LighthouseLandGate` / `LighthouseCoastProbe` / `LighthouseLandSweep` (+ тесты знака офлайн)
- [x] CSV 846 строк, знак подтверждён террейн-кроссчеком (B0983 −22.6 м)
- [x] Перенос 287; 897 + 287 = 1184; пересечение 0
- [x] Живая проверка: registry 554 theater, fleet 555, markers built=555, stale-cleanup 18 групп, вода (B1159 d=+0.356, B1163–B1168, B1137) не размещается
- [x] Гейты: tests 745 PASS / analyze baseline clean / build + PVS OK

```json
{"id":"ADR-030","decision":"водные маяки (>+0.3 км от берега) перенесены из Leuchtfeuer.json в LeuchtfeuerExcluded.json; рантайм-гейта нет","reason":"Y=0 у Structure -> водные стоят на воде; чистка данных один раз лучше проверки на каждый старт","alternatives":["рантайм-гейт EnabledFleet по CoastQuadTree","классификация по GetInterpolatedHeight","исключать borderline тоже"],"tradeoff":"флот 555 вместо 842; borderline 161 остаётся; excluded-файл архивный, при смене берега нужен re-sweep"}
```
