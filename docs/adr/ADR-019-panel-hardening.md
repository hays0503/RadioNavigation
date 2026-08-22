# ADR-019: Panel hardening — PrefabNames + SkipValidation + clamps

> Статус: Accepted (2026-08-19)  
> Связи: `Source/Logic/Main.cs:198` `requiredChildren`, `Source/UI/PanelStateStore.cs:17`, `Source/UI/PrefabNames.cs:5`, `Tests/PanelStateStoreTests.cs`

## Контекст

- `Main.cs:198` `requiredChildren` 9 литералов дублировали `PrefabNames.cs:5-13` — best practice DRY нарушен, расхождение → `Destroy(radio)` молча.
- `PanelStateStore.cs:17` `Save/Load` без `Clamp` — битый вызов `Save(999, -5, 99, 999)` затёр Prefs мусором, `Load` вернул `zone 99` → `IndexOutOfRange` в `ZoneHandler`.
- Нужен degraded open для бандл-расхождения без краша.

## Решение

- `Main.cs:198` `requiredChildren` через `PrefabNames.Cursor/Ground/...` (`Ground/Background` как `PrefabNames.Ground+"/"+PrefabNames.Background`), лог `missing` + `PlayerPrefs RadioNav.SkipValidation=1` → `Warning` вместо `Destroy` (best practice — флаг).
- `PanelStateStore.cs:17` `Save/Load` `Clamp` `volume 0-140`, `feedback 0-140`, `zone 0-4` (int ternary), `cursor -90..90`. `Load` clamp после чтения.

## Причина

Без централизации — 1 опечатка в `Main` vs `AssetBundle` = панель не откроется. Без clamp — сохранёнка бьёт UI.

## Альтернативы

- A. Оставить литералы — отвергнуто (F180).
- B. `try/catch` вокруг `Find` — скрывает баг, отвергнуто.

## Tradeoff

+ DRY, degraded open флаг, защита Prefs  
- `SkipValidation` флаг живёт в Prefs — сброс `PlayerPrefs.DeleteKey` нужен для возврата strict

## Статус

- [x] `Main.cs:198` PrefabNames + SkipValidation
- [x] `PanelStateStore.cs:17` clamps
- [x] `192 PASS`, `dotnet build 0 errors`
- Следующий: `ADR-020 StarSky Phase0 log`

```json
{"id":"ADR-019","decision":"Main PrefabNames + SkipValidation + PanelStateStore clamps","reason":"DRY + degraded open + защита Prefs от мусора","alternatives":["Оставить литералы","try/catch"],"tradeoff":"Флаг в Prefs требует ручного сброса"}
```
