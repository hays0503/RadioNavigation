# ADR-017: Lighthouse GPU/hook утечки — ResetStatics

> Статус: Accepted (2026-08-19)  
> Связи: `Source/Logic/LighthouseBeamGlow.cs:74` `dotTexture/streakTexture/renderingCamera`, `Source/Logic/Main.cs:38`, `refactoring-plan/hypothesis/INDEX.md F42`, `SAD/03`

## Контекст

- `LighthouseBeamGlow.cs:74-77` `static Texture2D dotTexture/streakTexture` `renderHookInstalled` `renderingCamera` — создавались в `GetDotTexture:781` 64×64 и `GetStreakTexture:817` 128×16 с `HideFlags.DontSave`, но `OnDestroy:324` чистил только `dotCanvas`, текстуры и `Camera.onPreCull` hook (`InstallRenderHook:476`) жили весь `AppDomain`.
- На hot-reload `Main.OnLoaded:34` `LighthousePlacer.Reset()` сбрасывал только `instance`, hook оставался → дубль `onPreCull` после 3× сейв/лоад.
- Принцип: код ради кода не генерим — правим 1 точку входа reload, не весь пайплайн.

## Решение

- `LighthouseBeamGlow.ResetStatics()` — `Destroy(dotTexture/streakTexture) → null`, `Camera.onPreCull -= OnAnyCameraPreCull` если `renderHookInstalled`, `renderingCamera=null`. Вызывается только на main thread из `Main.OnLoaded:38` при `loaded==true` (все `glow-GO` уже уничтожены, shared текстуры orphaned — безопасно).
- `Main.cs:38` добавить `LighthouseBeamGlow.ResetStatics()` рядом с `LighthousePlacer.Reset()`.

## Причина

Без сброса — GPU leak 64×64+128×16 на reload + дубль `onPreCull` → двойной `renderingCamera` запись.

## Альтернативы

- A. Destroy в `OnDestroy` каждого инстанса — сломает shared текстуру пока 1 маяк жив (Wangerooge). Отвергнуто.
- B. RefCount `instanceCount` — лишняя сложность, reload и так уничтожает все. Отвергнуто — оставляем simple reset на reload.

## Tradeoff

+ Leak закрыт, 1 подписчик `onPreCull`, 192 PASS  
- `ResetStatics` чистит даже если 1 glow ещё жив в тесте — приемлемо, вызывается только на reload

## Статус

- [x] `LighthouseBeamGlow.cs:491` `ResetStatics`
- [x] `Main.cs:38` вызов
- [x] `dotnet build Release` 0 errors, `192 PASS`
- Следующий: `ADR-018 CameraResolver` (Ф4) + `ADR-020 StarSky log`

```json
{"id":"ADR-017","decision":"ResetStatics Destroy tex + unsubscribe hook на reload","reason":"GPU leak + дубль onPreCull после hot-reload","alternatives":["Destroy в OnDestroy (ломает shared)","RefCount (лишняя сложность)"],"tradeoff":"Чистит только на reload, когда все glow мертвы — безопасно"}
```
