# CPlath: состояние убранного прибора

## Назначение Stowed

`CPlathAdaptiveAnimationPlayer.Phase.Stowed` — начальное и конечное скрытое
состояние, не отдельный анимационный клип. Оно появилось вместе с проигрывателем
в коммите `b00b96e` (2026-11-03). В сообщении коммита нет отдельного объяснения,
почему первый вариант `Evaluate()` не содержал явного `case Stowed`.

- `Initialize()` устанавливает Stowed и вызывает Evaluate.
- `StepStowed()` по запросу equipped начинает Raising с clock=0.
- Отмена Raising возвращает в Stowed после обратного хода до нуля.
- Lowering остаётся видимым до конца, затем переходит в Stowed.
- `IsVisible` равен false именно в Stowed. Actor по этому признаку возвращает
  штатное тело и near plane камеры; `NeedsUpdate` прекращает обновления после
  завершения убирания. Это связь по коду, не результат теста реальной камеры.

## Evaluate и отключение — разные контракты

При **инициализированном** Evaluate состояние Stowed выбирает первый клип на
времени 0 (active=0, time=0), затем обновляет graph, renderer flags и событие
VisibilityChanged. Ранний return запрещён: он пропускает эти действия.

`OnDisable()` уничтожает graph, скрывает модель и устанавливает Stowed, но не
обязан сбрасывать CurrentClip/CurrentTime. После отключения Advance отвергается;
следующий Initialize вновь задаёт нулевую скрытую позу. Нельзя требовать нулевое
время только на основании значения свойства CurrentPhase у отключённого player.

## Уточнение 2026-11-12

После восстановления назначения состояния и офлайн-проверки в Evaluate добавлен
явный `case Phase.Stowed: break;` с комментарием. Он сохраняет существующий
fallback active/time, не меняет переходы, OnDisable, клипы, управление или
камеры и не подавляет PVS. Цель — обозначить намеренную обработку для V3002,
не исправлять выдуманный дефект анимации. Отдельные V3125/V3130 в HeldModel этим
изменением не исправляются.

## Штатная регрессия

`Dev/RadioNavigation.SourceCheck/CPlathAnimationSourceTests.cs` динамически
компилирует **полный реальный исходник** player с GameParityParseOptions (C# 7.0).
Fixtures из `Fixtures/CPlathAnimation*.cs` встроены как ресурсы и не компилируются
в основной SourceCheck. Только Unity boundary заменена doubles; state-machine
методы не копируются. Camera math явно отвергается, чтобы не создавать ложную
уверенность в незамоделированной геометрии.

Команда: `dotnet run --project Dev/RadioNavigation.SourceCheck -c Release -- CPlathAnimation`.

10 сценариев проверяют initial/idle hidden state, первое доставание, отмену
подъёма, видимость Lowering до завершения, повторное доставание, ветку чтения,
реверс Lowering, disable/reinitialize и 100 циклов. Проверяются clip/time,
сырое время, mixer weights, флаги обоих renderer и события видимости.
Числа длин и scale=1024 — фиксированные входные данные, прочитанные из
установленного `cplath_animated` 2026-11-12, не тест свежести бандла.

Ещё 2 проверки компилируют мутации только в памяти: ранний return в Stowed и
замену terminal-перехода Lowering→Stowed на Aiming. Обе должны падать при
исполнении сценария; компиляционная ошибка не считается пойманной мутацией.
До уточнения рабочего кода все **12 PASS / 0 FAIL**: исходное поведение было
работоспособным. После уточнения должен сохраняться тот же результат.

Это не пиксельная приёмка реальных поз, не проверка нативного PlayableGraph
и не доказательство восстановления камеры/тела в игре. Игру в рамках этой
правки не перезапускать; исходные диагностические артефакты сохранены в
`artifacts/reports/cplath-stowed/`.

## Прогон внедрения 2026-11-12

- Штатные 12 проверок до изменения: PASS; после явного case: **12 PASS / 0 FAIL**
  (`maintained-before.txt`, `maintained-after.txt`). Мутации выполняются в памяти,
  исходник игрока на диске ими не меняется.
- Сборка C# 7.0 и behavioral suite: **1698 PASS / 0 FAIL** (`fast-gates.txt`).
- Quick-анализ: прежние 16 групп CPlath, новых диагностик нет (`analyze.txt`).
- Полный SourceCheck: **160 PASS / 0 FAIL** (`sourcecheck-final.txt`), включая
  12 новых проверок. PVS завершён (`pvs.txt`): **V3002 отсутствует**, остались
  только V3125/V3130 в CPlathHeldModel.cs:158/149. Общий PVS-гейт остаётся FAIL;
  подавления диагностик не добавлялись.
- Graph update отказался уменьшать граф (10257 против 11053); --force не применялся.
- Runtime diff: только явный case и комментарий. OnDisable, transitions,
  CPlathHeldModel, живая игра и сейвы не менялись. Коммит не создавался.

## Отдельное уточнение shade-input перед коммитом, 2026-11-14

Пользователь отдельно разрешил устранить V3125/V3130 перед commit/push,
не включая остальной долг Roslynator. В PollIndependentShades сохранён контракт:
нет held-прибора или обоих backend — false/changed=false; animated имеет приоритет,
rig — запасной вариант; Alt выбирает этот обработчик, Ctrl — левый банк;
четвёртая шторка левого банка исключена, удержанная цифра не повторяет переключение.

Скобки сделали исходный guard явным. Ветка rig теперь положительно проверяет
наличие rig, а changed=true находится внутри реально выполненной ветки backend.
Это уточнение существующей логики, не доказательство прежнего падения.
Анимационные переходы, OnDisable, материал и игровой процесс не менялись.

`Dev/RadioNavigation.SourceCheck/CPlathShadeInputSourceTests.cs` исполняет
настоящий PollIndependentShades, декларации клавиш/edges и HeldKeyEdge;
заменены только физический input и backend ToggleShade. Четыре сценария
проверяют routing обоих банков и приоритет animated, отсутствие backend/held,
правые модификаторы и Ctrl+4, потребление фронта без Alt и повторные нажатия.
До и после правки: **4 PASS / 0 FAIL** (`artifacts/reports/precommit-shades-*.txt`).

Заключительный предкоммитный прогон 2026-11-14:

- `Tools/build.ps1` со штатным обязательным PVS — PASS;
  `pvs-report.txt`: **No Messages Generated**, V3125/V3130 сняты без подавлений.
- Behavioral suite: **1701 PASS / 0 FAIL**; полный SourceCheck:
  **171 PASS / 0 FAIL**, включая четыре новые проверки shade-input.
- Quick Roslynator по-прежнему блокируют прежние 16 групп CPlath; они отдельно
  исключены пользователем из этой правки, baseline не обновлялся. Весь набор
  гейтов не объявляется зелёным, несмотря на чистый PVS.
- Graph update отказался уменьшать граф (10334 против 11053), --force не применялся.
- Отчёты: `artifacts/reports/precommit-build-pvs.txt`, `precommit-tests.txt`,
  `precommit-sourcecheck.txt`, `precommit-analyze.txt`, `precommit-graph.txt`.
