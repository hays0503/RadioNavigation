# ADR-025: Игровое время для STAR_SKY

> Статус: Accepted (2026-08-19)  
> Связи: `Source/Logic/GameTimeResolver.cs`, `Source/Logic/StarsMesh.cs`, `Source/Logic/ConstellationMesh.cs`, `Source/Logic/PlanetRenderer.cs`

## Контекст

- Кампания UBOAT использует историческое время и ускорение времени.
- `DateTime.UtcNow` делал положение звёзд, планет и Луны независимым от даты кампании.
- Подтверждённый API игры: `UBOAT.Game.Core.Time.GameTime.GmtDateTime`.

## Решение

- Добавить `GameTimeResolver`, который получает `GameTime` через `InjectionFramework`.
- Нормализовать `GmtDateTime` как `DateTimeKind.Utc`, потому что игра возвращает календарное GMT-время без UTC kind.
- Использовать resolver в `StarSkyRenderer`, `StarsMesh`, `ConstellationMesh` и `PlanetRenderer`.
- Оставить `DateTime.UtcNow` fallback только для раннего старта, когда DI ещё не предоставил `GameTime`.
- Сбрасывать cached reference при reload мода.

## Проверка

- Release build: 0 ошибок.
- Debug build: 0 ошибок.
- Полный тестовый набор: 208 PASS.

```json
{"id":"ADR-025","decision":"STAR_SKY использует GameTime.GmtDateTime","reason":"Историческая дата кампании и time compression должны влиять на небо","fallback":"DateTime.UtcNow только до готовности DI"}
```
