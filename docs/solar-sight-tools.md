# Стенд нижнего края Солнца — 2026-10-08

**Диагностические инструменты, не новый режим навигации.** Не меняют небо,
`SUNDRIFT`/`SolveDelta`, production-приведение высоты, frozen observations или
сейвы. Совместимость «видимый нижний край + игровой горизонт → F VIII» ещё
не принята. Числовой отчёт не заменяет её проверку.

В production-исходниках теперь отдельно реализован SH5/F VIII путь:
[sh5-solar-navigation.md](sh5-solar-navigation.md). Утверждения ниже «worksheet
не меняется» относятся **к командам этого стенда**, не к более поздней реализации.
Python `reduce` намеренно сохраняет проверенный поднабор 30–60°; полный fixture
`fviii-full-verified.json` отдельно проверяет все 810 production-клеток.

## Что подготовлено

| Инструмент | Назначение |
|---|---|
| `Tools/solar-sight/audit.py tables` | Солнечные GHA/δ и однозначно читаемый SD из пользовательского альманаха; оригинальные tokens, строки, заголовки времени и SHA-256 |
| `audit.py merge` | Объединение непересекающихся страниц; сохраняет page/input receipts, не интерполирует через пропуски |
| `Dev/SolarSightAudit/` | Фактический C# `AstroMath`/`SunDiscScale`: нулевой и явно сдвинутый Δt рядом, без записи глобальной калибровки |
| `audit.py reference` | Сверка печати с локальным JPL DE421 и C#; раздельные невязки GHA/δ/SD, сводки по странице и часу |
| `audit.py reduce` | Внешняя арифметика `Hs → ИП → h → F VIII + месяц → Ho центра → Ho−Hc`, не пишет I/карточку |
| `audit.py snapshot` | Только GET моста; состояние времени, прибора, позиции и источников, старый probe — `incomplete` |
| `audit.py residual` | Snapshot и сырой отсчёт с совпадающими GMT/arc/ИП; отдельно AP, текущая позиция, таблица, калибровка и направления источников |
| `audit.py projection` | Угловой диаметр ↔ пиксели при известных FOV и высоте PIP RT; ручной замер, не поиск границы по яркости |

Выходы разрешены **только в `artifacts/`**, не поверх входного файла. Оригиналы,
исходники и сейвы выходом назначить нельзя. Коды завершения: успех — 0, отказ /
недоступный или неполный снимок — 2.

## 1. Быстрый запуск

Команды — из корня мода. Python **3.10+**, .NET SDK **10** для C#-инструмента.
`tables` требует pypdf 6.15.0, `reference` — Skyfield 1.55; остальные команды
используют стандартную библиотеку Python. Версии зафиксированы в
`Tools/solar-sight/requirements.txt`. Инструменты ничего не устанавливают и не
скачивают; при необходимости зависимости можно установить в своём окружении:

```powershell
python -m pip install -r Tools/solar-sight/requirements.txt
```

Готовая исходная страница: PDF с.57, **28 августа–4 сентября**, шесть часов
0/4/8/12/16/20, семь лет, 336 строк:

```powershell
python -X utf8 Tools/solar-sight/audit.py tables
dotnet run --project Dev/SolarSightAudit -c Release -- artifacts/reports/solar-sight/tables.json artifacts/reports/solar-sight/csharp.json
python -X utf8 Tools/solar-sight/audit.py reduce --input Tools/solar-sight/sight.example.json --tables artifacts/reports/solar-sight/tables.json
```

Последняя команда — **иллюстрация**, не реконструкция старого наблюдения. ИП=0
задана условно; исходная ИП неизвестна. При raw `37°59.8′`, 5.5 м и сентябре
получаются F VIII ≈ **+10.60′**, Ho ≈ **38°10.40′**. Линейно интерполированные
строки при AP `55°N, 5°45′E` дают Hc ≈ **39°34.50′**, а ≈ **−84.10 NM**.
Это проверка арифметики на указанных входах, не атрибуция старого дефекта и не фикс.

Для JPL нужен **уже имеющийся** локальный `de421.bsp`:

```powershell
$kernel = "$env:LOCALAPPDATA\Temp\opencode\sun-audit-20261008\ephemeris\de421.bsp"
python -X utf8 Tools/solar-sight/audit.py reference --tables artifacts/reports/solar-sight/tables.json --kernel "$kernel" --csharp artifacts/reports/solar-sight/csharp.json
```

Путь выше существует на текущей машине, но временный. На другой нужен свой путь
к проверенному DE421. Проверяется SHA-256, отсутствующий kernel **не скачивается**.
`--hour-shift -1` / `1` — контрольные эксперименты, не калибровка игры.

Явный offline Δt=−6.47 min, без изменения игры:

```powershell
dotnet run --project Dev/SolarSightAudit -c Release -- artifacts/reports/solar-sight/tables.json artifacts/reports/solar-sight/csharp-shift.json 55 5.75 -6.47
```

Без последних трёх аргументов используются lat=55, lon=5.75, Δt=0. Оба варианта
пишут `uncalibrated`/`calibrated` и input/source/assembly hashes.
Это offline-сборка инструмента, **не `dotnet build` мода** и не деплой.

## 2. Альманах и шкала времени

Первоисточник — `documents-readonly/celestian-navigation-sh5/[1] Nautical Almanac SH5 1939-1945.pdf`.
Обычную восьмидневную страницу выбирают `--page N --first-date YYYY-MM-DD`.
Anchor — дата **первого числового блока**, не первый rotated label от pypdf.
Проверяются восемь дат, порядок семи лет, 48 часов. Leap-day / переход года
не поддерживаются: отказ, а не неявное смещение дат. Печатные `60.0′` переносятся
в градус арифметически; оригинальные tokens остаются в отчёте.

Проверенные окна для более широкой сверки:

| PDF страница | `--first-date` | Окно |
|---|---|---|
| 27 | `1940-01-01` | 1–8 января |
| 38 | `1940-03-29` | 29 марта–5 апреля |
| 50 | `1940-07-03` | 3–10 июля |
| 57 | `1940-08-28` | 28 августа–4 сентября |
| 61 | `1940-09-29` | 29 сентября–6 октября |

Для каждого выполните `tables` с отдельным `--out`, затем:

```powershell
python -X utf8 Tools/solar-sight/audit.py merge --inputs artifacts/reports/solar-tools-20261008/tables-p27.json artifacts/reports/solar-tools-20261008/tables-p38.json artifacts/reports/solar-tools-20261008/tables-p50.json artifacts/reports/solar-tools-20261008/tables-p57.json artifacts/reports/solar-tools-20261008/tables-p61.json --out artifacts/reports/solar-sight/tables-campaign.json
```

Эти пять входов уже сохранены в текущем отчёте; на другой машине сначала извлеките
их из оригинала. Совместная выборка — **1680 строк**, не весь альманах.

SD читается только из однозначных соседних SD/date tokens. На с.57 он получен
для **294 строк**, у 28 августа `null`. В пяти окнах — **1344 значения SD**;
пропуски не заполняются сезонным расчётом. Другой SHA-256 PDF приводит к отказу
до пересмотра данных/парсера. Печатные port/time headers сохранены в JSON.

Часы строк **предварительно трактуются как GMT/UT1**; портовая GMT−1-поправка
автоматически не применяется. C# использует GMST и TT=UT+24 s; JPL — apparent
RA/δ и GAST. Отличия видны раздельно, не спрятаны в «поправке таблицы».
Линейная четырёхчасовая интерполяция — **не полная печатная процедура инкрементов**.
Точный результат бумажной процедуры можно внести через `sight.table` ниже.

## 3. Внешняя цепочка Hs → Ho

Создайте рабочий JSON по образцу `Tools/solar-sight/sight.example.json` в
`artifacts/reports/solar-sight/`. Обязательны:

- `gmt` с `Z`, кампания 1939–1945; не местное/портовое время;
- `raw_hs_deg` — **сырой барабан**, не старое `HoDeg` с неизвестными поправками;
- `index_correction_arcmin` — **знаковая прибавляемая** ИП, ±5′, и её источник;
- `limb: "lower"` — верхний край этот стенд не поддерживает;
- `eye_height_m` — над выбранным морским горизонтом, описание в `horizon_reference`;
- `assumed_lat_deg`, `assumed_lon_deg` — AP, восточная долгота положительна.

F VIII — **общая** поправка, SD/dip/refraction нельзя прибавлять ещё раз.
Неизвестные поля (`sd_arcmin`, `dip_arcmin`, `sundrift_minutes`, `ho_deg` и т.п.)
отвергаются. Таблица применяется к высоте **после ИП**. Внешний Ho не квантуется.

`fviii-verified.json` — проверенные вручную строки **30, 32, 34, 36, 38, 40, 45,
50, 55, 60°**, глаза **0…14 м**, 12 месячных добавок нижнего края. Источник —
F VIII, печатная с.72 / PDF с.98. Билинейная интерполяция возвращает клетки/веса,
**без экстраполяции**. Вне поднабора можно явно передать проверенную поправку:

```json
"manual_fviii": {
  "base_arcmin": 9.5,
  "monthly_arcmin": -0.1,
  "source": "F VIII PDF p.98, row 20 degrees, eye 5m; verify your own sight",
  "includes_sd_dip_refraction": true
}
```

Это пример формы, не поправка для любой высоты. Месячный член сверяется с месяцем
GMT. Для точного бумажного GHA/δ **вместо** `--tables` добавьте:

```json
"table": {
  "gha_deg": 330.0,
  "declination_deg": 8.0,
  "gmt": "1939-09-02T10:03:07Z",
  "source": "Example only; replace with your page, row and increment calculation"
}
```

Числа иллюстративные. GMT должен совпадать; нельзя одновременно задать `table`
и `--tables`. Выход: Hs, ИП, h, F VIII, Ho/Hc **центра**, азимут и
а=60·(Ho−Hc): плюс — к светилу, минус — от него. Один отсчёт даёт LOP, не фикс.
Running-fix и перенос LOP здесь не реализованы. Production worksheet не меняется.

## 4. Read-only снимок и разложение невязки

```powershell
python -X utf8 Tools/solar-sight/audit.py snapshot --out artifacts/reports/solar-sight/before.json
```

Только GET `/time → /sextant → /sky → /state?fields=position,time → /time`.
Нет `/set`, `/record`, `/reset`, prefs, save/load, pause/camera commands. Base URL
ограничен localhost, proxy/redirect отключены. Нет связи — `unavailable`, код 2,
**без перезапуска**. Старый probe или некампанийное время — `incomplete`, код 2.

В исходнике `SextantProbe.Snapshot` добавлен `solarAudit`:

- однократные GMT/position для обоих C#-вариантов;
- raw arc, текущая ИП, неквантованное h и квантованный вход worksheet;
- native направление и configured measuring направление/диаметр;
- ready/active, доступность hooks, recent scope entry обеих PIP;
- mode/insideBoat, camera forward, FOV, target height, mask и worldY.

Open/filters/идентификация остаются в соседних полях того же snapshot.
`limbAssumption=LowerLimb`, но **`observedLimb=null`** до проверки контакта;
`eyeHeightAboveSeaM=null`: worldY не доказывает высоту над морем.
Readiness/recent scope — **configuration-not-pixels**, не GPU submission.
Направление renderer может быть от предыдущего LateUpdate, не fresh presented
frame. HTTP-запросы не атомарны; оба `/time` сохраняются для оценки движения часов.

Блок появится в игре только после **отдельно разрешённой** игровой компиляции/
перезапуска. Здесь он не деплоился. Стенд не доустанавливает его сам в live.

Создайте JSON сырого отсчёта с точными GMT/arc/ИП из снимка, внешними AP, высотой и
описанием горизонта, затем:

```powershell
python -X utf8 Tools/solar-sight/audit.py residual --snapshot artifacts/reports/solar-sight/before.json --input artifacts/reports/solar-sight/raw.json --tables artifacts/reports/solar-sight/tables.json
```

Отчёт отдельно показывает табличный Hc в **текущей позиции**, AP-редукцию,
сдвиг высоты из-за калибровки, отличия native/configured направлений от таблицы
и C#. Если источник ready/active, дополнительно `configured alt − radius`
относительно геометрической горизонтали. Разность raw−limb **не** объявляется
дипом или готовой поправкой: в ней смешаны неизвестные горизонт, наведение,
контакт и рефракция. Другое время/arc/ИП вызывает отказ, не привязку к позднему
снимку. Исторические observations не читаются как будто это новый сырой отсчёт.

## 5. Проекция и будущая live-проверка

```powershell
python -X utf8 Tools/solar-sight/audit.py projection --diameter-deg 0.53 --fov-deg 65 --target-height-px 1000
```

`Dpx = Hpx · tan(Ddeg/2) / tan(FOVdeg/2)` на оптической оси. `--observed-px`
обращает формулу для **ручного** измерения края. Фон/halo, центрирование, zoom
и масштаб RT→canvas проверяются отдельно; screen crop не считается RT автоматически.

Будущая процедура: конфигурация → разрешённый filter/source A/B → presented/GPU
кадр с limb и горизонтом → второй снимок → возврат фильтров/вида. **Не нажимать
I/O, не менять calibration и не писать сейв** ради render A/B. Проверка нового
определения места — отдельная приёмка, после согласования модели горизонта/лучей.

## 6. Проверки и результаты

```powershell
python -X utf8 Tools/solar-sight/tests/test_audit.py
python -X utf8 Tools/solar-sight/mutate.py
powershell -ExecutionPolicy Bypass -File Tools/gates.ps1
```

Python-тесты идут **отдельно** от C# gates. Без PDF/pypdf только PDF integration
test явно SKIP, не PASS. C# tests исполняют math/JSON; SourceCheck проверяет
read-only проводку, один clock и явные Δt=0/текущий. Это не Unity/GPU-приёмка.

Итоговый полный прогон: csc, quick Roslynator baseline и PVS clean;
**1482 PASS / 0 FAIL / 0 SKIP**, SourceCheck **106 PASS / 0 FAIL**.
Отдельно Python: **28 PASS**, **14/14 мутаций пойманы** ожидаемыми именованными
тестами. Мутанты Python компилируются в памяти в отдельных процессах; fixture
копируется только в отчёт. Рабочие файлы не редактируются даже временно.

Отчёты — `artifacts/reports/solar-tools-20261008/`: исходная инвентаризация,
`tables-p*.json`, `tables-campaign.json`, `csharp-campaign.json`, reference,
reduction и test/gate logs. `live-readonly.json`: связь отсутствует, игрового
перезапуска/наблюдения/записи сейва не было.
Ранний 336-row набор с.57: printed GHA против JPL максимум **0.08136′**, printed
δ ниже JPL на **1.19618…3.10958′**. Это не причина всего дефекта и не разрешение
на постоянную поправку; более широкая сверка хранится отдельно.

Пять сезонных окон, **1680 строк**: GHA максимум **0.08439′**. Printed δ−JPL:

| PDF страница | Диапазон невязки δ, минуты |
|---|---|
| 27, январь | +34.155…+38.409′ |
| 38, март–апрель | −1.003…−0.351′ |
| 50, июль | −37.055…−33.012′ |
| 57, август–сентябрь | −3.110…−1.196′ |
| 61, сентябрь–октябрь | +0.224…+0.735′ |

Сдвиги JPL на ±1 час оставляют сезонное расхождение δ и создают ошибку GHA около
**900′ = 15°**. Только JPL сдвигается в этих экспериментах; C# остаётся на
явно заданных GMT/Δt. Нельзя объявлять это доказанной моделью составителя или
устранять постоянной поправкой. Receipts — `reference-campaign*.json` и
`time-conventions.json`. Синтетический `synthetic-residual.json` проверяет лишь
выполнимость CLI, не игровое наблюдение.

Обновление графа остановлено shrink-защитой: **9151 candidate / 9758 existing**.
SHA-256 `graph.json` не изменился, принудительного перезаписывания не было.
Это не падение csc/SourceCheck/PVS, но новые связи в графе могут отсутствовать.

`preservation.json` сверяет этот проход с baseline до подготовки инструментов:
**2083 файла первоисточников** и **274 прежних файла `Source/` вне двух probe
изменений** сохранены по SHA-256, длине и mtime. Нет пропавших или неожиданных
изменённых файлов baseline. Это не инвентаризация всех игровых сейвов и не
доказательство восстановления более раннего checkout. `completion.json`
содержит контрольные суммы итоговых инструментов, выбранных отчётов и logs.

См. [celestial-disc-sizes.md](celestial-disc-sizes.md) и
[astro-save.md](astro-save.md) для production-контрактов/frozen snapshots.
