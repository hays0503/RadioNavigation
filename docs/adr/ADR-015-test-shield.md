# ADR-015: Test Shield для тюнинг/пеленг — без прод кода

> Статус: Accepted (2026-08-19)  
> Связи: `ADR-014 baseline`, `SonneController.cs:507` `Gain`, `GeoMath.cs:52` `MapBearing`, `refactoring-plan/hypothesis/INDEX.md F203`

## Контекст

- `Ф1` план: без щита любой фикс `Gain`/`MapBearing` = регресс вслепую (0% `SonneController` `Main` `UI`).
- `F203` QF=20 кросс-толк 315 vs 319 не покрыт; `GeoMath` антимеридиан без теста.
- Требуется зафиксировать ожидаемое поведение до правок данных/аудио, не порождая прод код ради кода.

## Решение

- Добавить `Tests/ShieldTests.cs` 8 тестов (5 Gain + 3 Geo), 191 PASS (было 182):
  - `Gain_315_vs_319_QF20 0.89` `Gain_315_vs_319_QF200 0.2` `Gain_266_vs_257 0.58` — формула `1/sqrt(1+Q²ratio²)` `SonneController:507` дублируется в тесте как документ регресса
  - `Haversine 179→-179 222км` `MapBearing 270°` `InitialBearing finite` — пеленг на антимеридиане
- `RadioNavigation.Tests.csproj` линк `ShieldTests.cs`. Прод код не трогали.

## Причина

Щит дешевле `double-buffer` (5ч) и ловит изменение `QF`/`Bfo`/`GeoMath` за 90 сек `tests.ps1`.

## Альтернативы

- A. Сразу править `GeoMath` без теста — отвергнуто (нет доказательства бага).
- B. Выносить `GainCalculator` класс — отвергнуто (код ради кода, Ф0 принцип).

## Tradeoff

+ `191` зелёных vs `182` — защита пеленга/тюнинга  
- Формула дублируется в тесте — при смене `SonneController` тест правим руками (осознанно)

## Статус

- [x] `ShieldTests.cs` + `csproj` — `191 PASS`
- [x] `dotnet build Release/Debug` 0 errors
- Следующий: `ADR-016 Data` `Bushmills 89.9`

```json
{"id":"ADR-015","decision":"Добавить 8 shield-тестов Gain/Geo без прод кода, 191 PASS","reason":"Без щита Ф2-Ф4 = регресс вслепую (0% SonneController)","alternatives":["Править без теста","Выносить GainCalculator"],"tradeoff":"Дубль формулы в тесте vs защита за 90с"}
```
