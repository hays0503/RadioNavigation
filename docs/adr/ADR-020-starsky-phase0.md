# ADR-020: StarSky Phase0 — только лог, 0 прод кода

> Статус: Proposed (2026-08-19) → Accepted после живого замера  
> Связи: `STAR_SKY_PLAN.md:10`, `Source/Logic/CameraResolver.cs`, `Source/Logic/LighthouseBeamGlow.cs:549`, `Source/Data/StarCatalog.cs` (будущий)

## Контекст

- `STAR_SKY_PLAN.md:10` Phase0 требует 1 вечер логов: `weather.Sky.MoonAltitude/MoonAzimuth` vs Stellarium, `SunAltitude`, `farClipPlane` мостик/перископ/бинокль, `Shader.Find("Sprites/Default")`, вода `ZTest`, `GeoPositionConverter` север.
- `ADR-018` `CameraResolver.SafeRadius 3900<0.9*farClip:150` — без живого `farClip` нельзя выбрать `R`.
- Принцип: код ради кода не генерим — Phase0 только диагностика, без `StarCatalog.cs`/`AstroMath.cs`.

## Решение

- Phase0 — временный `MonoBehaviour` лог-патч (не в `Source/` прод, в `Tools/Phase0Probe/` или ветке `phase0-log`), пишет каждые 2с:
  - `SunZenith/SunAltitude/MoonAltitude 0/06:00 UT` 1942-06-15 etc vs Stellarium → вердикт Луна `ELP2000` нужна?
  - `farClip` мостик/перископ/бинокль → `SafeRadius` 3900 или `0.85*farClip`
  - `Shader.Find("Sprites/Default") != null`, `water depth` (quad под горизонтом скрыт?)
  - `LerpValue 86.5→97` монотонно
- Результат — `docs/adr/ADR-020` обновить `Proposed→Accepted` с вердиктами + `STAR_SKY_PLAN:11` чеклисты.

## Причина

Без замера `R` может вылезти за `farClip` → звёзды клип, маяк-dot конкурирует за глубину.

## Альтернативы

- A. Сразу генерить `StarCatalog` 700 звёзд — 2ч, риск `farClip` — отвергнуто.
- B. Пропустить Phase0 — риск Луна ошибка 0.3° — отвергнуто.

## Tradeoff

+ 0 прод кода, 1 вечер логов → решение Луна/R  
- Откладывает `AstroMath` тесты на 1 день

## Статус

- [ ] Лог-патч создан (ветка `phase0-log`)
- [ ] 2 даты замерены, `farClip` лог снят
- [ ] ADR-020 → Accepted, `STAR_SKY_PLAN` шаг 4 закрыт
- Следующий: `ADR-021` `AstroMath` + `StarCatalog` конвертер (Ф7)

```json
{"id":"ADR-020","decision":"Phase0 только лог (farClip/Shader/Sun/Moon) — 0 прод кода","reason":"Без живого farClip нельзя выбрать R=3900, без Stellarium — Луна","alternatives":["Сразу генерить каталог","Пропустить Phase0"],"tradeoff":"1 вечер логов vs риск клипа/Луны 0.3°"}
```
