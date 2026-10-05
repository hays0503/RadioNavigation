# Структура папки проекта

Актуально на 2026-10-05. Пути ниже указаны относительно корня RadioNavigation.
Группировка документации, скриптов и проектов проверок не меняет загрузку мода.

## Что находится в корне

- `README.md`, `DOCS.md`, `AGENTS.md` — входные документы и правила проекта.
- `Manifest.json`, `Source/`, `Bundles/` — игровые пути; не переносить ради уборки.
- `.editorconfig`, `.roslynatorconfig`, `.gitignore`, `.gitattributes` — общие
  настройки, которые должны действовать и на `Source/`, и на `Dev/`.
- `.opencode/`, `.supermemory/`, `graphify-out/`, `memory.txt` — инструменты и память;
  их адреса сохранены. Скрипты и проекты разработки теперь находятся в `Tools/` и `Dev/`.

## Каталоги

| Каталог | Назначение |
|---|---|
| `Source/` | Код, данные, локализация и исходники шейдеров мода |
| `Bundles/` | Скомпилированный AssetBundle и его manifest |
| `Dev/Mod/` | IDE-проект `radionavigation.csproj`; не игровой компилятор |
| `Dev/Analysis/` | Analysis.csproj, PVS-конфигурация и версионируемый Roslynator baseline |
| `Dev/Tests/` | Офлайн-тесты, stubs и собственный runner |
| `Dev/RadioNavigation.Analyzers/` | Анализаторы Harmony/Unity |
| `Dev/RadioNavigation.Analyzers.Tests/` | Тесты анализаторов |
| `Dev/RadioNavigation.SourceCheck/` | Проверка чистого игрового API и структурные гейты |
| `Tools/` | Все PowerShell-точки входа и вспомогательные скрипты |
| `docs/` | Действующие справочники и спецификация радиопанели `docs/DESIGN.md` |
| `docs/plans/` | Четыре плана функций и журналы приёмки, ранее лежавшие в корне |
| `docs/adr/` | Архитектурные решения |
| `docs/SAD/` | Главы архитектурного документа |
| `docs/refactoring-plan/` | Реестр находок, доказательства и планы рефакторинга |
| `docs/archive/` | Исторические архитектурные материалы |
| `knowledge/` | Компилированная база знаний об API игры |
| `documents-readonly/` | Первичные исследовательские документы; не редактировать |
| `captures/` | Сохранённые визуальные материалы; не считать мусором по расширению |
| `artifacts/` | Локальные сборки, отчёты и аудиозаписи; исключён из Git |
| `bin/`, `obj/` внутри проектов | Стандартные результаты MSBuild и кеши; остаются на штатных местах |
| `graphify-out/` | Рабочий граф знаний и его история |
| `.opencode/`, `.supermemory/` | Локальные инструменты и память; пути сохранены |
| `artifacts/cache/legacy-root/` | Прежние корневые `.vs/`, `bin/`, `obj/`; сохранены, не удалены |

## Выходные пути скриптов

| Команда | Результат по умолчанию |
|---|---|
| `Tools/build.ps1 -Configuration Release` | `artifacts/build/Release/radionavigation.dll` и `.pdb` |
| `Tools/build.ps1 -Configuration Debug` | `artifacts/build/Debug/radionavigation.dll` и `.pdb` |
| IDE-проект `Dev/Mod/radionavigation.csproj` | `artifacts/ide/<Configuration>/` |
| `Tools/pvs.ps1` | `artifacts/reports/pvs/pvs-report.plog` и `.txt`; `.json`/`.sarif` по флагам |
| `Tools/analyze.ps1 -Json` | `artifacts/reports/roslynator/roslynator-report.sarif` |
| `Tools/coverage.ps1` | `artifacts/reports/coverage/coverage.cobertura.xml` |
| `Tools/pause_check/analyze_pause.py` | Читает `artifacts/audio/pause_check/rec.wav` |

Скрипты создают недостающие выходные каталоги. Пути по умолчанию привязаны к
корню проекта, а не к текущей папке терминала. Явные `Tools/build.ps1 -OutputDir` и
`Tools/coverage.ps1 -Output` сохранены; Python-пробник по-прежнему принимает путь к WAV.
Старые корневые DLL/PDB/deps сохранены в `artifacts/build/legacy-root/`.

**Локальная DLL не является деплоем:** игра компилирует `Source/` сама и пишет
свою сборку в соседний игровой `Temp/`. Уборка не запускает `Tools/run-game.ps1`, не
трогает сейвы, игровые настройки или recovery-журналы.

## Новые команды из корня

```powershell
powershell -ExecutionPolicy Bypass -File Tools/gates.ps1 -Parallel -Coverage
powershell -ExecutionPolicy Bypass -File Tools/tests.ps1 -Configuration Release
dotnet run --project Dev/RadioNavigation.Analyzers.Tests -c Release
dotnet run --project Dev/RadioNavigation.SourceCheck -c Release
```

Корневые обёртки не оставлены: это снова размножило бы служебные файлы.
IDE-проект открывать из `Dev/Mod/`; кеши новых проектов создаются внутри `Dev/`,
а не в корне. Анализ и IDE больше не делят один корневой NuGet `obj/`.

## Пути в исторических журналах

Датированные file:line, команды старых коммитов и схемы архивов не переписываются
как новые доказательства. При работе с текущим checkout применять таблицу:

| Прежний путь | Текущий путь |
|---|---|
| Корневые `*.ps1` | `Tools/<имя>.ps1` |
| `Tests/`, `RadioNavigation.Analyzers/`, `RadioNavigation.Analyzers.Tests/`, `RadioNavigation.SourceCheck/` | Те же каталоги внутри `Dev/` |
| Корневой `radionavigation.csproj` | `Dev/Mod/radionavigation.csproj` |
| Корневые `RadioNavigation.Analysis.csproj`, `RadioNavigation.pvsconfig`, `roslynator-baseline.txt` | `Dev/Analysis/` |
| `SAD/`, `archive/`, `refactoring-plan/` | Те же каталоги внутри `docs/` |

Содержимое реестра и архивов сохранено. Относительные ссылки внутри каждого
перемещённого дерева остаются относительными; актуальные входные документы и
команды проверок используют новые пути. Старые inline-code пути в журналах —
история, не обещание наличия файлов по прежним адресам.

## Правила дальнейшей уборки

Новые планы функций помещать в `docs/plans/`, справочники — в `docs/`, результаты
локальных экспериментов — в `artifacts/`. После перемещения документа обновлять
карту `DOCS.md` и ссылки; Markdown-ссылки считать относительно их документа.
Не удалять записи, захваты и базы данных только потому, что Git их игнорирует.
