# Solar lower-limb diagnostics

Команды, формат входа и границы проверки:
[docs/solar-sight-tools.md](../../docs/solar-sight-tools.md).

```powershell
python -X utf8 Tools/solar-sight/audit.py --help
python -X utf8 Tools/solar-sight/tests/test_audit.py
```

Внешние отчёты в `artifacts/`; нет записи наблюдений, калибровки или сейвов.

## Установленный водный шейдер (без запуска игры)

```powershell
python -X utf8 Tools/solar-sight/audit_water_shader.py --output artifacts/reports/water-shader-new
python -X utf8 Tools/solar-sight/tests/test_water_shader.py
python -X utf8 Tools/solar-sight/tests/test_sea_horizon.py
```

Первый — кривизна из DXBC, второй — парсер/привязки, третий — поиск кандидата кромки
и сверка модели. Полный float32 fixture — `water-curvature-verified.json`.
Изменение installed bundle требует повторной сверки; наличие таблицы не доказывает
активный mesh/горизонт в presented-кадре.

## Кандидат кромки и раздельные измерения/прогнозы

```powershell
python -X utf8 Tools/solar-sight/sea_horizon.py --run <папка с *-on.png> --analysis <pixel-analysis.json> --frame-model installed --output artifacts/reports/horizon-new
```

Ищет в горизонтном (левом) луче участок с сильным перепадом, ограниченной вариацией
яркости сверху и текстурой снизу. Это **кандидат**, не доказательство sea/fog/land/UI:
текстурный тёмный силуэт может иметь тот же профиль. Порог ≥120 квалифицированных
колонок не ослаблен ради прежних кадров. При отказе нет измеренной разницы краёв
и нет подстановки отозванного ROI.

`--frame-model` обязателен: `editor` только для сохранённых кадров старой модели,
`installed` — для исправленной. Старый→новый сдвиг применяется **только** к отдельному
прогнозу `editor`; исправленные кадры повторно не сдвигаются. `--frames` задаёт
отдельную папку кадров. Выходной каталог должен быть новым, receipt не перезаписывается.

Schema 2 разделяет `measured_limb_minus_candidate_*` (только пригодный RGB8 fit),
`configured_limb_minus_candidate_*` (конфигурация, не видимый край) и
`predicted_installed_residual_*` (только прогноз по старому кадру). Несошедшийся
радиус/центр, неуспешный fit или большой RMSE запрещают измерение; fallback к
configured-геометрии не превращает её в наблюдение. Physical alpha, same-presented
pose, касание и причинная связь с волнами не сертифицированы.

Корректирующий протокол, сохраняющий прежние отчёты:
`artifacts/reports/solar-table-20261008/continuation-20261009-lower-limb/FINAL.md`.
