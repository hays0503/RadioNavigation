# Аудит: зона/эквисигнал + счётчики в InformationPanel

Архив истории ревизий, сверок с кодом и обоснований решений для `PLAN_InfoPanel_ZoneDebug.md`. Номера строк — **snapshot на момент аудита (2026-08-03)**; при росте файлов см. `git blame`/snapshot, а не пересчитывайте по текущим номерам.

## Замечания ревизии (2026-08-02, проверено против Source/)

> Тело плана переписано по этим замечаниям — см. «Вердикт». P1–P25 ниже — историческая запись аудита.

### P1 — R2 арифметически неверен для dash-зоны (исправлено до ветвления)

`EqSignIndex` (dash) = `60 − DotCount` = `DashCount`. R2 требовало `Dashes == 60 − N` — не сходится (Dots:38, Dashes:22, EQ@22 → старое R2 ждало Dashes==38). Исправлено: dot `Dots == N`; dash `Dashes == N`.

### P2 — Пример `Recv: 38• 22▬` невозможен: буфер сигнала триммится

`signalEntries` урезается до `MaxMorseChars = 20`. Recv — малое колеблющееся число (напр. `7• 13▬`), **не равное** `Dots/Dashes` той же строки. Решение: **(а)** задокументировать оконную семантику, пример → `Recv: 7• 13▬`; **(б)** поднять лимит. Выбрано **(а)** (см. Вердикт).

### P3 — В dash-зоне `EQ@N` ≠ визуальной позиции ◆◆

`GenerateConsolPattern` всегда эмитит точки первыми → ◆◆ в рич-тексте на позиции `DotCount`, панель покажет `60 − DotCount`. Решение: **(б)** оставить C.A.P.-семантику `EqSignIndex`, расхождение с ◆◆ зафиксировать в R5.

### P4 — Предпосылка строки 3 устарела

«незакоммиченные Bundles/testui и documents/» — неактуально: закоммичено в `04857d8`, рабочее дерево чистое. Правило «коммитить только Source/» оставить как гигиену ветки.

### P5 — Пример `LEC [IN BEAM] ... Zone:S3(DASH)` физически невозможен

InBeam = |отклонение от BaseAngle| ≤ 2.5°; сектор 3 = 15–20°. InBeam-станция — только сектор 0 (dot) / 71 (dash). Пример → `OUTSIDE + S3(DASH)`.

### P6 — R3: уточнить исключение callsign при визуальном подсчёте

Callsign тоже рисуется в рич-тексте (зелёным). R3 → «считать только навигационные •/▬, не зелёные callsign-знаки».

### P7 — R4 фактически неверен: EQ@ — пила, а не монотонный 0↔60

`EqSignIndex = localAngle/5 × 60` в обеих зонах → растёт 0→60, **сбрасывается 60→0 на границе сектора**. «Монотонно без скачков» → «пила: растёт 0→60 от ◆-линии к дальнему краю; на границе сброс 60→0; DOT↔DASH на границе».

### P8 — P2/P3 неполны: ◆ почти всегда вытриммирован из окна

`MaxMorseChars = 20`; ◆ стоит на позиции `callsignLen + DotCount` — выталкивается после старта цикла. ◆ виден только в первые ~20 знаков цикла.

### P9 — R3 неполон: исключать и ◆, не только callsign

◆ = `SignalCharType.Equisignal` (пурпур ◆◆) в Recv-счётчики не входит. R3 → «считать только красные • (Dot) и синие ▬ (Dash), исключая зелёные callsign и пурпурные ◆◆».

### P10 — R2 «Dots + Dashes == 60» — тавтология

`DashCount ≡ MaxNavigationCount − DotCount` → сумма = MaxNavigationCount всегда. Проверка тривиальна; писать `Config.MaxNavigationCount`, а не хардкод `60`.

### P11 — Номера строк устарели в Шаге 4

`UpdatePatterns` — строка 318 (не 362); гейт `if (diag.InBeam)` — 379-380 (InformationPanelHandler.cs).

### P12 — Мелочи

Полный цикл = callsign + **61** знак (60 навигационных + ◆), не «60 + callsign». Имя `EqSignIndex` — мисномер: позиция чтения C.A.P., не индекс ◆.

### P13 — P2/P3 неполны: эмиссия фиксируется на старте цикла, расхождение больше, чем P3 моделирует

`CurrentConsolPattern` обновляется только при `IsKeyerAvailableForPatternUpdate`; во время Navigation паттерн заморожен, `DotCount` живой. Расхождение растёт в течение цикла. R3-сравнение — в PreNavPause / на стоянке.

### P14 — R3 тавтология (та же болезнь, что R2/P10)

`TrimEntries` гарантирует не-Separator записей ≤ 20; Recv-геттеры читают то же окно. «Совпадение с визуальным счётом» верно по построению. Следствие: `X + Y ≤ 20` всегда.

### P15 — Сброс буфера при смене доминантной станции/частоты

`OnFrequencyChanged` / смена `dominantStationIndex` → `signalEntries.Clear()` → `Recv: 0• 0▬` при живых `Dots/Dashes`. В критерии R1/R3 — как ожидаемое поведение.

### P16 — П4 устарел повторно: файл плана сам модифицирован

На момент P4 дерево чистое; теперь `git status` показывает `M PLAN_InfoPanel_ZoneDebug.md` (эта ревизия). Правило «коммитить только Source/» — в силе; план в коммиты ветки не включать.

### P17 — Структурное: тело плана не переписано после ревизии

P1–P12 — отдельный список, но Шаги 1–6 и R1–R5 всё ещё содержали ошибки. До ветвления переписать тело по P1–P22 и зафиксировать выбор (а)/(б).

### P18 — Окно 20 включает callsign-глифы: Recv ≈ 0 в фазе Callsign

`OnStationElementCompleted` инкрементит `morseCharCount` для любого не-Separator типа, включая `Callsign` → окно заполнено зелёными callsign-глифами → `Recv ≈ 0• 0▬` почти всю фазу Callsign. Нулевые Recv в начале цикла — **ожидаемое поведение, не баг** (в R1/R3).

### P19 — P13/P3 серьёзность переоценена для неподвижной лодки

`DotCount` пересчитывается каждые 0.2 с из текущего пеленга. Лодка на месте → `DotCount` константа → расхождение P13 = 0. Дивергенция только при движении внутри сектора во время цикла.

### P20 — `TransitionIndex` уже хранит `DotCount`: EqSignIndex дублирует его в dot-зоне

`stations[i].TransitionIndex = sector.DotCount` — в dot-зоне новый `EqSignIndex ≡ TransitionIndex`. Зафиксировать эквивалентность в коде/критериях.

### P21 — `Max(0, ·)` в формуле DashCount — мёртвый код

`DotCount` уже клампится в [0, 60] в `CalculateSector`. `Max(0, ·)` убрать.

### P22 — Recv-геттеры не гейтятся по доминантной станции

`signalEntries` заполняется только для `dominantStationIndex` (гейт `IsProcessingDominantStation`), но сами геттеры — сырые проходы без проверки индекса. UI вызывает их только при `i == domIdx` — иначе недоминантная строка покажет чужой буфер. Зафиксировать в Шаге 3 и A5.

### P23 — Путь `SignalRichTextFormatter.cs`: `Sound/` → `UI/`

Файл в **`Source/UI/SignalRichTextFormatter.cs`**, не `Source/Sound/`. При реализации использовать `Source/UI/`. Номера строк внутри файла верны (:9-10, :32-35, :44-48, :70-84).

### P24 — `CalculateSector`: сигнатура на :20, `sectorNumber` вычисляется на :24

Шаг 1: «`CalculateSector` (строка 24)». Сигнатура — :20; `sectорNumber = (int)(delta / SectorWidthDeg)` — :24. При реализации ссылаться на :20 (сигнатура) и :24 (sectorNumber) раздельно.

### P25 — Диапазон `OnFrequencyChanged`: :281-285, не :281-284

Внутреннее противоречие: строка 149 указывала `281-285`, строка 324 (E2) — `:281-284` (потеряна закрывающая скобка :285). Правильно — **:281-285**.

## Подтверждено верным (2026-08-02)

- P1–P17 (повторная сверка по Source/): окно/тримминг `TrimEntries` (SignalRichTextFormatter.cs:70-84), порог `CalculateInBeam` (SonneSignalGenerator.cs:144-150), dot-first эмиссия (:97-107), заморозка паттерна `IsKeyerAvailableForPatternUpdate` (SonneController.cs:670-672), сброс буфера (:470-476, :281-285), пила EQ@ (формула `CalculateSector`, :24-33), сброс при смене доминанты (:470-476).
- Потокобезопасность Recv-геттеров: вся мутация `signalEntries` на main-потоке — верно.
- A1: чёт/нечет = `isDashSector = sectorNumber % 2 == 1` (SonneSignalGenerator.cs:25).
- `signalEntries` — только доминантная станция (гейт `IsProcessingDominantStation`, SonneController.cs:659-662).
- Номера строк ~верны: `DotCount =` на :505; `StationDiagnostics` на :338.
- P5: порог `CalculateInBeam` = `SectorWidthDeg/2` = 2.5° → InBeam достижим только в секторе 0/71.
- P2: `MaxMorseChars = 20` (SignalRichTextFormatter.cs:10); `MaxSignalEntries = 60` (:9); тримминг в `TrimEntries` (:70-84).
- P1/P3/P6: чёт/нечет — :25; dot-first эмиссия — :97-107; callsign в рич-тексте — SignalRichTextFormatter.cs:32-35.
- `SignalCharType.Dot/Dash/Callsign/Equisignal/Separator` существуют (SignalTypes.cs:3-10).
- `StationDiagnostics` — единственный потребитель: InformationPanelHandler.cs:344,365.
- P14: не-Separator записей ≤ 20 — инвариант `TrimEntries`, а не «обычно».
- P13: `IsKeyerAvailableForPatternUpdate` :670-672; `UpdateStationNavigation` — 5 Гц (NavUpdateInterval = 0.2, :56).

## Сводка по приоритетам и конкретные исправления

### Блокеры — исправлено до ветвления

- **P1 (R2)**: dot `Dots == N`; dash `Dashes == N`, где `N = EqSignIndex`.
- **P2 (Recv)**: выбрано **(а)** — оконная семантика, пример `Recv: 7• 13▬`.
- **P5 (пример)**: `OUTSIDE` + `S3(DASH)` (реалистичный).
- **P7 (R4)**: EQ@ = пила (растёт 0→60, сброс 60→0 на границе сектора).
- **P17**: тело плана переписано; выбор (а)/(б) по P2/P3 зафиксирован.

### Важные — зафиксировано в Шаге 4 и критериях

- **P3/P8/P13**: `EQ@N` = позиция чтения C.A.P.; ◆ в рич-тексте = `callsignLen + DotCount` (обычно вытриммирован, P8). R3-сравнение — в PreNavPause/на стоянке; при движении внутри сектора — расхождение (P13).
- **P22**: Recv-геттеры только при `i == domIdx`; строка Recv — только доминантная (закреплено в Шаг 3 и A5).
- **P6/P9**: R3 — считать только • (Dot) и ▬ (Dash), исключая callsign и ◆◆.

### Умеренные — учтены при приёмке

- **P10/P14**: R2 «Dots+Dashes==60» и R3 «совпадение с визуальным счётом» тавтологичны; вынесены в инварианты I1/I2 в плане. Следствие: `X + Y ≤ 20` всегда.
- **P15/P18**: `Recv: 0• 0▬` после смены доминанты/частоты и в фазе Callsign — норма (в R1/R3).

### Минорные — правки по ходу

- P4/P16: git-статус — модифицирован только план; `Bundles/testui` и `documents/` закоммичены в `04857d8`.
- P11: `UpdatePatterns` = 318; гейт `if (diag.InBeam)` = 379-380.
- P12: полный цикл = callsign + **61** знак (60 навигационных + ◆).
- P20: в dot-зоне `EqSignIndex ≡ TransitionIndex` (SonneController.cs:508).
- P21: `Max(0, ·)` — мёртвый код, убран из формулы `DashCount`.
- P19: расхождение P13 = 0 на стоянке; дивергенция только при движении внутри сектора во время цикла.

## Сверка с CONSOL/SONNE досье (2026-08-02)

Источник: `documents-readonly/CONSOL_SONNE_досье.md`. Досье = **реальная** система; план = внутренняя модель игры.

### D1 — Подтверждено: базовые параметры совпадают с досье

- 60 знаков/с → `MaxNavigationCount = 60` (досье:82, ConsolCycleConfig.cs:27).
- Полный цикл 120 с → `Cycle120s` (досье:85).
- Чередующиеся dot/dash-секторы → `IsDashSector = sectorNumber % 2 == 1` (досье:66, SonneSignalGenerator.cs:25).
- Метод наблюдателя: dot-сектор считаются точки, dash-сектор — тире (досье:98-101) → подтверждает исправленный R2.
- Эквизигнал = линия нуля внешних антенн → `EQ@N` = позиция чтения C.A.P. (вариант (б) P3) согласована с досье (досье:69).

### D2 — Расхождения (game-упрощения, не ошибки плана)

- **Число лучей**: досье «7 лучей вперёд/назад» (~3.86λ). План: `SectorWidthDeg = 5°` → 72 сектора (36 на сторону). Число секторов ≠ число лучей; план описывает игровую модель — `S<n>` не интерпретировать как физический луч.
- **Поправка потерянных знаков**: досье (99-101): потерянные = 60 − насчитанные, половина к каждой группе. План не реализует — intentional (цель — отладка модели, а не наблюдательская процедура).
- **Сдвиг линии в dash-секторе**: досье (108): линия через середину луча, `Ts + L/2`. План не учитывает — intentional (идеальная линия без ширины эквисигнала). Не путать с P3: P3 — расхождение EQ@ с ◆◆ из-за dot-first эмиссии; D2 — наблюдательская поправка.

### D3 — Границы интерпретации для приёмки

- R2 «Dots+Dashes==60» — проверка модели, не счёт наблюдателя: поправку D2 не применять.
- R3-визуальный подсчёт сверять с Recv (окно рич-текста), а не с `Dots/Dashes`.
- Запрос «зона эквизигнала + символ наступления + точки/тире для отладки» — покрывается полями плана без изменений.

### D4 — Запрос пользователя → поля панели

- «зона эквисигнала» → `Zone:S<n>(DOT|DASH)` (досье:66);
- «символ наступления» → `EQ@N/60` = позиция чтения C.A.P. (`RestoreBearingFromDotCount`, досье:69,82);
- «кол-во точек/тире» → `Dots/Dashes` + `Recv: X• Y▬` (окно ≤ 20, только доминантная).
Покрытие полное — изменений плана не требуется.

### D5 — Путь к досье: `documents-readonly/`, не `documents/`

`documents/` в ветку не коммитится (P16); ссылки держать на `documents-readonly/`.

### D6 — Начало отсчёта наблюдателя: 0 в dot, 1 в dash

FIAT 1105: dot-сектор счёт с **0**, dash — с **1**. Внутренняя модель: `EqSignIndex` стартует с 0 на границе сектора в **обеих** зонах (P7). Сдвиг ±1 не перенесён — при отладке не ожидать и за баг не считать (intentional, как D2).

### D7 — ◆◆ = «сплошной тон» на эквизигнале

«На эквизигнале точки и тире сливаются в сплошной тон» → пурпурные ◆◆ (`SignalCharType.Equisignal`, SignalRichTextFormatter.cs:44-48). ◆ вытриммирован из окна после ~20 знаков цикла (P8).

### D8 — Выравнивание с CAP 59 observer procedure

Согласование внутренней модели игры с исторической процедурой наблюдателя (official CONSOL/SONNE navigation manual, CAP 59):

- **1 equisignal/цикл; порядок** `точки → ◆ → тире` (или обратный) — `GenerateConsolPattern` эмитит dot-first: `:97-100` точки → `:102` ◆ → `:104-107` тире.
- **Считаем до эквисигнала, а не после:** в dot-зоне `◆` идёт **после** точек → `DotCount` = число точек перед ◆ = `EqSignIndex`; в dash-зоне — порядок `тире → ◆`, `DashCount` = число тире перед ◆ = `EqSignIndex`. То есть `EqSignIndex` = `Count` доминирующего символа, который слышал радист.
- **`Dots + Dashes == 60` всегда:** задаётся на уровне модели (`DashCount ≡ MaxNavigationCount − DotCount`, I1), а не правилом redistribution 38/18→40/20 — поправка D2 не реализована, intentional (цель — отладка модели, а не наблюдательская процедура).
- **Только доминирующий символ используется:** `EqSignIndex` в dot = `DotCount`; в dash = `DashCount` — ровно тот `Count = N`, по которому радист открывает таблицу. Вторая величина избыточна (`60 − Count`, I1).
- **Номер сектора `S<n>` радисту не нужен** — выводим его только как служебный тюнинговый индекс (см. D4/R1).
- **`Recv` ≠ CAP-count:** `Recv: X• Y▬` = число •/▬ в эмиссионном окне `signalEntries` (≤20, P13/P19), а не `Count` до эквисигнала. Оба выводятся намеренно: `Dots/Dashes` + `EQ@` = счёт CAP 59; `Recv` = «что игра сейчас играет в эфир».

## Вердикт

**ОБНОВЛЕНО (2026-08-03): тело плана ПЕРЕПИСАНО по P1–P25 с зафиксированными решениями — план готов к ветвлению.**

Зафиксированные решения:
- P1 (R2): dash-зона `Dashes == N` (не `60−N`)
- P2 (Recv): **(а)** — оконная семантика, пример `Recv: 7• 13▬`
- P3 (EQ@ vs ◆◆): **(б)** — C.A.P.-семантика `EqSignIndex`, расхождение с ◆◆ в R5
- P5 (пример): `OUTSIDE + S3(DASH)`
- P7 (R4): EQ@ = пила
- P11: `UpdatePatterns` = 318; гейт `if (diag.InBeam)` = 379-380
- P20: `EqSignIndex ≡ TransitionIndex` в dot-зоне задокументировано
- P21: `Max(0, ·)` убран из формулы `DashCount`
- P22: Recv-геттеры только при `i == domIdx` — в Шаге 3 и A5
- P23: путь `SignalRichTextFormatter.cs` = `Source/UI/`

Остаточные риски (не блокеры): окно 20; нулевые Recv в Callsign/после смены доминанты; R3-сравнение только на стоянке/в PreNavPause.

Исходный вердикт (до перезаписи): план был **не готов к ветвлению** — блокеры P1/P2/P5/P7/P17 фатальны.

## Подробное пояснение трёх оговорок Recv (E1–E3)

Механизмы с цепочками вызовов. Поведение не меняется.

### E1 — Recv — оконный буфер ≤20 знаков (P2/P8/P14)

Полный цикл = callsign (≈4–6) + 60 навигационных + ◆ = **65–67 записей**; в рич-тексте и Recv — только последние **20** не-Separator.

```
OnStationElementCompleted (SonneController.cs:170)
  → signalEntries.Add(new SignalEntry(c, type))   // :192
  → morseCharCount++                              // :194
  → SignalRichTextFormatter.TrimEntries(...)       // :195
```
`TrimEntries` (SignalRichTextFormatter.cs:70-84): удаляет с начала, пока `morseCharCount > MaxMorseChars (20)` или `entries.Count > MaxSignalEntries (60)`. Recv-геттеры читают тот же `signalEntries` → `X + Y ≤ 20` всегда (I2). `Dots/Dashes` — таргеты всего цикла (60), обновляются из `CalculateSector` независимо. Поэтому `Recv: 7• 13▬` и `Dots:38 Dashes:22` — разные сущности.

### E2 — Recv: 0• 0▬ после смены доминанты/частоты и в фазе Callsign (P15/P18)

**Смена доминантной станции** (`UpdateStationGains`, SonneController.cs:470-477):
```csharp
if (maxIndex != dominantStationIndex)
{
    signalEntries.Clear();       // :472
    morseCharCount = 0;          // :473
    morseDecoderBuffer.Clear();  // :474
    decodedText.Clear();         // :475
}
dominantStationIndex = maxIndex; // :477
```
Буфер обнуляется; новая доминанта заполняет с нуля → `Recv: 0• 0▬` до первого элемента.

**Смена частоты** (`OnFrequencyChanged` :281-285 → `ClearSignalBuffer` :268-274): полный сброс `signalEntries`, `morseCharCount`, `morseDecoderBuffer`, `decodedText`.

**Фаза Callsign** (`OnStationElementCompleted` :193-194): `morseCharCount++` для любого не-Separator типа, включая `.Callsign` → окно 20 заполняется зелёными callsign-глифами (не входят в Recv) → `Recv ≈ 0• 0▬` почти всю фазу Callsign.

### E3 — Визуальный подсчёт совпадает с Recv только на стоянке (P13/P19)

**Паттерн замораживается при старте Navigation** (`UpdateStationGains`, SonneController.cs:517-520):
```csharp
if (IsKeyerAvailableForPatternUpdate(i))
{
    schedulers[i].CurrentConsolPattern = consolPattern;
}
```
`IsKeyerAvailableForPatternUpdate` (:670-672):
```csharp
private bool IsKeyerAvailableForPatternUpdate(int i) =>
    !keyers[i].IsActive
    || schedulers[i].CurrentPhase != StationScheduler.Phase.Navigation;
```
keyer активен && фаза = Navigation → паттерн не обновляется (заморожен на старте цикла). `UpdateStationNavigation` пересчитывает `DotCount` каждые 0.2 с (:505, NavUpdateInterval = 0.2, :56).

- **Стоянка**: `DotCount` константа → замороженный паттерн = живому `EQ@/Dots` → Recv = визуальный подсчёт (R3 проходит).
- **Движение внутри сектора во время цикла**: `DotCount` живой, паттерн заморожен → расхождение растёт. R3 — только в PreNavPause (между циклами, keyer не активен) или на стоянке.

## Сверка номеров строк (snapshot 2026-08-03, 50+ ссылок, 8 файлов)

38 ссылок на номера строк — **38/38 подтверждены точно, 0 расхождений** (семантика кода не затронута). Три зарегистрированных расхождения пути/диапазонов (P23/P24/P25) — точны и зафиксированы выше.

### Подтверждённые ссылки (выборочно)

- P11: `UpdatePatterns` :318; гейт `if (diag.InBeam)` :379-380 (InformationPanelHandler.cs)
- P24: сигнатура `CalculateSector` :20; `sectorNumber` :24 (SonneSignalGenerator.cs)
- P25: `OnFrequencyChanged` :281-285
- P22: `IsProcessingDominantStation` = :659-662 (`IsInSonneZone && !Gain.IsNegligible(0.001f) && stationIndex == dominantStationIndex`)
- P13/P19: `IsKeyerAvailableForPatternUpdate` :670-672; гейт обновления паттерна :517-520
- P15: блок смены доминанты :470-477 (`signalEntries.Clear()` :472, `morseCharCount = 0` :473)
- E1/E2: `OnStationElementCompleted` :170-173; `signalEntries.Add` :192; `morseCharCount++` :194; `TrimEntries` :195; `ClearSignalBuffer` :268-274
- P21: кламп `DotCount` в `CalculateSector` :33 (`Math.Max(0, Math.Min(TotalSignals, dotCount))`)
- P7: порог `CalculateInBeam` :149 (`angleDiff <= sectorWidthDeg / 2f`)
- P8/P14: `MaxSignalEntries = 60` (:9); `MaxMorseChars = 20` (:10); `TrimEntries` :70-84
- P9: `Equisignal` — пурпур `#FF00FF`, `\u25C6\u25C6`, жирный (:44-48)
- P6: callsign — зелёный `#00FF00`, `\u25AC`/`\u25CF` (:32-35)
- P20: `stations[i].TransitionIndex = sector.DotCount` :508
- P3: dot-first эмиссия в `GenerateConsolPattern` :97-100 (точки), :102 (◆), :104-107 (тире)
- A1: `isDashSector = (sectorNumber % 2 == 1)` :25
- `SignalCharType` enum: 5 членов `Callsign/Dot/Dash/Equisignal/Separator` (SignalTypes.cs:3-10)
- `NavUpdateInterval = 0.2f` :56; `MaxNavigationCount = 60` (ConsolCycleConfig.cs:27)

### Новые наблюдения (для справки при реализации)

- **`SignalEntry` struct** (SignalTypes.cs:12-22) — пара `(char Ch, SignalCharType Type)`; Recv-геттеры проходят по ней без препятствий.
- **Два enum'а**: `NavigationElementType` (Dot/Dash/Equisignal — для паттерна) и `SignalCharType` (для `signalEntries`/рич-текста). План корректно использует `SignalCharType` для Recv-геттеров — путать не нужно.
- **`morseCharCount` инвариант** — гарантия кода: `TrimEntries` (:76) удерживает `morseCharCount <= 20` до допуска записей. `X + Y ≤ 20` — инвариант, а не вероятностное наблюдение (I2).
- **Потокобезопасность Recv-геттеров**: все мутации `signalEntries` на main-потоке (`OnStationElementCompleted`, `OnStationSequenceFinished`, `ClearSignalBuffer`, блок смены доминанты в `UpdateStationGains`); `GetSignalRichText()` тоже main-поток.
- **`GetStationDiagnostics` — единственный потребитель** (InformationPanelHandler.cs:344,365); расширение структуры безопасно.
- **Новые поля отсутствуют** в текущем коде — план корректно требует их добавить.
- **Слот в `UpdateStationNavigation`**: после `TransitionIndex` (:508) есть место для `IsDashSector`/`SectorNumber` — структурных препятствий нет.

## Вердикт сверки

План **полностью консистентен** с текущим Source/. Все 38 ссылок совпадают точно; три зарегистрированных расхождения (путь SignalRichTextFormatter → UI, сигнатура CalculateSector :20 vs :24, диапазон OnFrequencyChanged :281-285) задокументированы и точны. Предлагаемые изменения (поля в `SectorResult`/`StationState`/`StationDiagnostics`, Recv-геттеры, правка `UpdatePatterns`) структурно реализуемы без конфликтов. **План готов к ветвлению и реализации.**
