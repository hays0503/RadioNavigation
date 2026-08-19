# ADR-026: Видимость STAR_SKY по состоянию неба

> Статус: Accepted (2026-08-19)  
> Связи: `Source/Logic/SkyVisibilityResolver.cs`, `Source/Logic/StarsMesh.cs`, `Source/Logic/ConstellationMesh.cs`, `Source/Logic/PlanetRenderer.cs`

## Решение

- Получать injected `Weather` из UBOAT.
- Использовать игровую формулу видимости:
  `visibility = (1 - weather.Sky.Atmosphere.Fogginess) * (1 - weather.Sky.LerpValue)`.
- Применять visibility к alpha material для звёзд, созвездий, планет и Луны.
- Не перестраивать mesh при изменении visibility.
- Во время scene transition или до готовности injection возвращать `1`, чтобы не скрывать слой аварийно.

## Причина

- STAR_SKY должен исчезать днём и в тумане синхронно с игрой.
- Material alpha дешевле полного rebuild тысяч звёзд.

## Проверка

- UBOAT API подтверждён по `Weather.Sky`, `TOD_Sky.Atmosphere.Fogginess` и `TOD_Sky.LerpValue`.
- Release/Debug build: 0 ошибок.
- Полный тестовый набор: 208 PASS.

```json
{"id":"ADR-026","decision":"Общий sky visibility fade через Weather.Sky","reason":"Синхронизация STAR_SKY с днём и туманом игры","fallback":"visibility=1 при недоступном injected Weather"}
```
