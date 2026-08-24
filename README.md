# RadioNavigation

Мод для UBOAT: панель радионавигационных маяков CONSOLE/Sonne с синтезом звука.

- **Версия:** 2.1
- **Автор:** testui
- **Namespace:** `UBOAT.Mods.RadioNavigation`

## Возможности

- Панель радио: включение приёмника, переключение наземного/секторного режима (ground/zone), настройка громкости и обратной связи
- 14 маяков Consol/Sonne (5 диапазонов), сигналы синтезируются DSP-кодом (`OnAudioFilterRead`, без аудиофайлов)
- Декодирование позывных азбукой Морзе, анализ сигнальных паттернов

## Установка

Скопировать папку мода в каталог модов UBOAT (мод загружается игрой; исходники `Source/*.cs` компилируются самой игрой при загрузке — заранее собранная DLL не требуется).

Требуется AssetBundle `Bundles/testui` (префабы панели). Если префаб не пройдёт валидацию, панель отключается без падения игры.

## Сборка

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1 [-GamePath <path>]  # локальная DLL (для IDE-проверок)
dotnet build radionavigation.csproj -c Release -c Debug        # Release+Debug, эталонный билд (0 errors, MSB3277 warn ожидаем)
powershell -ExecutionPolicy Bypass -File tests.ps1             # тесты (263 PASS; baseline-история ADR-014)
```

## Структура репозитория

- `Source/` — код мода: `Data/` (конфиги маяков/диапазонов), `Logic/` (ядро: морзе, паттерны, пеленги), `Sound/` (DSP-синтез), `UI/` (обработчики uGUI)
- `Tests/` — юнит-тесты (без Unity)
- `SAD/` — документ архитектуры (11 глав; приложения A–C и старый roadmap — в `archive/`)
- `refactoring-plan/` — аудит: 207 гипотез, статусы в `hypothesis/INDEX.md`, неисправленное в `DEFERRED.md`
- `DESIGN.md` — спецификация дизайна

## Статус качества

- Аудит рефакторинга: **207 гипотез** — ~160 закрыто (доказательства построчно в `refactoring-plan/RESOLVED_LEDGER.md`), 22 открыто (`DEFERRED.md` + `PLAN_Phase5.md`), ~10 частично, 13 отклонено
- Тесты: **263 PASS** (baseline `baseline-182-20260819` на `8b3021fa`, ADR-014; рост 232→253→255→257→260→263 — 2026-08-22/23), сборка Release+Debug без ошибок
- Отложено (live-проверка в игре): характер шума (F71); архитектурные F163/F179–F181 (см. `refactoring-plan/DEFERRED.md`)

## История

- **2.1** — PlayerPrefs-персистентность панели (F15), GeoMath/Constants и обновление SAD
- **2.0** — большой рефакторинг по аудиту (F01-F207), мерж `fix/audit-and-issue-files` (42 коммита)
- **1.x** — исходная реализация панели, сборка AssetBundle Unity 2021.3.33f1 (совместим с 2021.3.45f2 игры)
