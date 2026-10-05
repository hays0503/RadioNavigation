# ADR-018: CameraResolver — общий резолвер камеры для маяков и звезды

> Статус: Accepted (2026-08-19)  
> Связи: `LighthouseBeamGlow.cs:361` `ResolveCamera`, `Source/Logic/CameraResolver.cs`, `docs/plans/STAR_SKY_PLAN.md:76`, `LighthouseBeamGlow.cs:74` статика, `ADR-017`

## Контекст

- `LighthouseBeamGlow.cs:361-517` 135 строк `ResolveCamera` + `ResolveDeviceCamera:417` (Periscope) + `FindScreenCamera:457` + `IsUtilityCamera:444` + hook `onPreCull:476` — копипаст для звезды `STAR_SKY_PLAN:76` требовал вынести хелпер.
- Два пайплайна (`LighthouseBeamGlow` и будущий `StarSkyRenderer`) писали в разные `static renderingCamera` + два `onPreCull` → гонка, дубль лога, разный `farClip` (`BeamGlow:150` `0.9*farClip` vs `STAR_SKY R=3900`).
- Принцип: код ради кода не генерим — выносим 1 файл, маяк делегирует.

## Решение

- Новый `Source/Logic/CameraResolver.cs` — `TryResolve(out Camera)`, `SafeRadius(cam)=Min(3900, farClip*0.85)`, `IsMapMode()`, `InstallOnce()`, `ResetForReload()`.
- Приоритет как у `BeamGlow:361`: `Camera.main → Periscope device → hook → FindScreenCamera` + `IsUtility` по имени.
- `CameraMode` сравниваем строкой `ToString()=="Generic"/"Map"` чтобы не зависеть от точного enum в `com.uboat.game`.
- `LighthouseBeamGlow.ResolveCamera:361` делегирует в `CameraResolver.TryResolve` + `renderingCamera = cam`; `InstallRenderHook:476` → `Resolver.InstallOnce()`; `ResetStatics:490` → `Resolver.ResetForReload()` + destroy tex.

## Причина

1 подписчик `onPreCull`, 1 `current` камера — перископ/бинокль/FPP видят одинаково маяк и звезду, `R=3900` внутри `farClip` не клипается.

## Альтернативы

- A. Дублировать `ResolveCamera` в `StarSkyRenderer` — 135 строк копипасты, дрейф приоритета — отвергнуто.
- B. Singleton `CameraService` с DI — лишняя сложность, нужен `Inject` в static — отвергнуто.

## Tradeoff

+ `LighthouseBeamGlow` 897→~820 строк, 1 хук, `StarSkyRenderer` переиспользует без копипасты  
- `BeamGlow` хранит `renderingCamera` как алиас `Resolver` — две статики на переходный период (совместимость)

## Статус

- [x] `CameraResolver.cs` + `BeamGlow` делегация
- [x] `dotnet build Release` 0 errors, `192 PASS`
- Следующий: `ADR-019 Panel Hardening` (Ф5) + `ADR-020 StarSky log` (Ф6)

```json
{"id":"ADR-018","decision":"Вынести CameraResolver (TryResolve/SafeRadius/IsMapMode) и делегировать BeamGlow","reason":"1 хук, 1 камера для маяка и звезды, без дублирования 135 строк","alternatives":["Дубль в StarSky","Singleton DI"],"tradeoff":"Две статики на переходный период vs 1 хук"}
```
