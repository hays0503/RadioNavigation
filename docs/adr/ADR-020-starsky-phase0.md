# ADR-020: StarSky Phase0 — только лог, 0 прод кода

> Статус: **Proposed** (2026-08-19); исходный набор дат не подтверждён полностью.
>
> Связи: `docs/plans/STAR_SKY_PLAN.md`, `Source/Logic/StarSkyPhase0Probe.cs`, ADR-021, ADR-024

## Уточнение 2026-10-04

Production-каталог, StarsMesh, Meeus/топоцентрическая Луна и игровые часы уже
реализованы; они не являются «будущими» и не заблокированы исполнением этого
старого probe. Но их наличие не подтверждает **именно протокол двух дат** ниже,
поэтому дата/статус ADR не переписаны ретроактивно в Accepted. Прежнее намерение
«не писать AstroMath до Phase0» осталось историей решения.

Настоящие shader/pass/far-clip контракты изменились: fallback `Sprites/Default`
не доказывает draw в Deep-SRP. Читать текущие ADR-021/024,
`docs/sky-vs-clouds.md`, `docs/CAMERA_GUIDE.md`. Инструкция включения probe ниже
— диагностический исторический протокол, не задание менять дату/погоду живой игры.

## Исходный протокол (2026-08-19)

## Контекст

- `docs/plans/STAR_SKY_PLAN.md:10` Phase0 требует 1 вечер логов: `weather.Sky.MoonAltitude/MoonAzimuth` vs Stellarium, `SunAltitude`, `farClipPlane` мостик/перископ/бинокль, `Shader.Find("Sprites/Default")`, вода `ZTest`, `GeoPositionConverter` север.
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

- [x] Лог-патч создан — `Source/Logic/StarSkyPhase0Probe.cs` (PlayerPrefs `RadioNav.Phase0Probe=1`, throttle 2с, лог SunZenith/SunAlt/MoonAlt/MoonAz/farClip/shader/visibility/pos/north)
- [ ] 2 даты замерены (1942-06-15, 1943-01-10 00:00/06:00 UT), `farClip` лог снят в игре vs Stellarium → вердикт ELP2000 Луна
- [ ] ADR-020 → Accepted (после живого замера), `STAR_SKY_PLAN` шаг 4 закрыт
- Инструкция: `PlayerPrefs.SetInt("RadioNav.Phase0Probe",1)` в консоли игры → смотреть `[RadioNav][Phase0]` в `Player.log` каждые 2с; сравнить `MoonAlt/MoonAz` со Stellarium на тех же UTC
- Следующий: живые замеры → `ADR-021` уже выполнен, далее `STAR_SKY_PLAN:10` п.11 полировка

```json
{"id":"ADR-020","decision":"Phase0 только лог (farClip/Shader/Sun/Moon) — 0 прод кода","reason":"Без живого farClip нельзя выбрать R=3900, без Stellarium — Луна","alternatives":["Сразу генерить каталог","Пропустить Phase0"],"tradeoff":"1 вечер логов vs риск клипа/Луны 0.3°"}
```
