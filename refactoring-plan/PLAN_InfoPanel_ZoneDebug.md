# План: зона/эквисигнал + счётчики в InformationPanel

Активный план реализации. История ревизий, сверка номеров строк (snapshot 2026-08-03) и обоснования решений — в **`AUDIT_InfoPanel_ZoneDebug.md`**.

> **game-model ≠ real system.** `documents-readonly/CONSOL_SONNE_досье.md` описывает реальную CONSOL/SONNE; план реализует внутреннюю модель игры (`GenerateConsolPattern`/`CalculateSector`). Расхождения (число лучей, ±1 счёт, поправка наблюдателя) — `game-упрощения`, intentional, в критерии не вносятся (см. D2/D6).

## Ветвление

- Перед первым изменением кода: `git checkout -b feature/panel-eq-zone-recv`
- Коммитим только `Source/`. `Bundles/testui`, `documents/` и сами планы в коммиты ветки не включаем.

## Что писать в коде (сводка)

| Файл | Что добавить / изменить | Точка соприкосновения |
|---|---|---|
| `SonneSignalGenerator.cs` | `SectorResult.SectorNumber`; заполнить в `CalculateSector` | `sectorNumber` (чёт = dot, нечёт = dash) |
| `StationState.cs` | `IsDashSector`, `SectorNumber` | — |
| `SonneController.cs` | `UpdateStationNavigation`: `IsDashSector`/`SectorNumber` из `sector`; `StationDiagnostics.IsDashSector/SectorNumber/EqSignIndex/DashCount`; новые геттеры `GetReceivedNavigationDotCount`/`GetReceivedNavigationDashCount` | `StationDiagnostics` (GetStationDiagnostics) |
| `InformationPanelHandler.cs` | `UpdatePatterns`: убрать гейт `if (diag.InBeam)` для счётчиков; добавить строку `Recv: X• Y▬` только для `i == dominantStationIndex` | `diag` из `GetStationDiagnostics` |

> Номера строк на момент аудита — в `AUDIT_InfoPanel_ZoneDebug.md` (snapshot 2026-08-03).

## Инварианты

- **I1** `Dots + Dashes == MaxNavigationCount` (60) — тавтология формулы, контроль конфига.
- **I2** `RecvDots + RecvDashes <= 20` — гарантируется `TrimEntries` (MaxMorseChars = 20).
- **I3** `Recv` считает **только** `SignalCharType.Dot`/`.Dash` — `.Callsign` и `.Equisignal` исключаются по типу.
- **I4** `EqSignIndex == TransitionIndex` **только** в dot-зоне (в dash-зоне EqSignIndex = MaxNavigationCount − DotCount).
- **I5** все мутации `signalEntries` — на main-потоке; Recv-геттеры читают безопасно (UI вызывает только при `i == dominantStationIndex`, где `signalEntries` заполнен).

## Риски реализации

- [ ] забыть заполнить `SectorResult.SectorNumber` в `CalculateSector`
- [ ] забыть добавить `IsDashSector`/`SectorNumber` в `StationState`
- [ ] вывести Recv для всех станций (нужен гейт `i == dominantStationIndex`)
- [ ] использовать `EqSignIndex` вместо `TransitionIndex` в dash-зоне (они не равны там)
- [ ] посчитать Callsign/Equisignal в Recv-геттерах (их нужно исключать по `SignalCharType`)
- [ ] забыть убрать `#if DEBUG` вокруг новых строк (игра компилирует `Source` без `DEBUG` — блоки вырезаются в рантайме)
- [ ] забыть `graphify update .` после правок
- [ ] забыть `powershell -File build.ps1` (A7)
- [ ] убрать `if (diag.InBeam)` вокруг строки Zone/EQ/Dots/Dashes (line 379), но **сохранить** `InBeam` для цвета (line 384) и для строки `[IN BEAM]/[OUTSIDE]` (line 370) — см. Фикс #3

## Фиксы ревизии (2026-08-03)

Три замечания по аудиту кода (верификация `TransitionIndex` / `SignalCharType` / `InBeam`).

### 1. Dash-зона: `DotCount` ≠ символы до ◆

В dot-зоне эмиссия: точки → ◆ → тире. `DotCount` = точки до ◆ — совпадает с CAP observer `Count`.

В dash-зоне эмиссия: тире → ◆ → точки (`GenerateConsolPattern` line 104: `for (i = dotCount; i < MaxNavigationCount; i++)` добавляет тире). Здесь `DotCount` = точки **после** ◆, а тире **до** ◆ = `MaxNavigationCount − DotCount`.

→ `EqSignIndex = MaxNavigationCount − DotCount` в dash-зоне = `DashCount` — **верно** (CAP: символы до эквисигнала). Но **в коде нет явной семантики**: `StationState.DotCount` в dash-зоне не равен количеству символов до ◆. Нужен **комментарий** в `SonneController.cs:505`:

```csharp
// DotCount = точки в эмиссии; в dot-зоне = до ◆, в dash-зоне = после ◆.
// EqSignIndex (CAP observer Count) = DotCount (dot) / MaxNavigationCount − DotCount (dash).
```

### 2. `Recv` ≠ CAP-count: нужен явный guard/комментарий

Recv-геттеры (`GetReceivedNavigationDotCount`/`GetReceivedNavigationDashCount`) считают по `signalEntries` (окно ≤20, `TrimEntries`). CAP-счётчик (`DotCount`/`DashCount`) — из 60-символьного цикла. Значения **всегда различаются** (P13/P19).

Уже задокументировано (I2, P2 вариант **(а)**), но:
- Recv = 0• 0▬ после смены доминанты и почти всю фазу Callsign — **ожидаемое** (P15/P18).
- При визуальном подсчёте ◆◆ — только в PreNavPause; при движении паттерн заморожен, `DotCount` живёт — сравнение провалится.

→ В коде геттеров нужен XML-документационный комментарий:
```csharp
/// <summary>
/// Count of dots in the emission window (≤20 chars).
/// This is NOT the CAP observer count (DotCount/DashCount from 60-cycle).
/// Recv ≪ CAP-count; Recv = 0 during callsign phase is expected.
/// </summary>
```

### 3. `InBeam` gate: 3 использования в `InformationPanelHandler`

Plan Step 4 убирает гейт `if (diag.InBeam)` для строк Zone/EQ/Dots/Dashes. Но `InBeam` используется в `UpdatePatterns` **в трёх местах** (line 370, 379, 384):

1. Line 370: `string beam = diag.InBeam ? "IN BEAM" : "OUTSIDE";` — **оставить** (как есть; просто строка, gate не нужен).
2. Line 379: `if (diag.InBeam) line += "\n  Dots:" + diag.DotCount + ...` — **убрать** `if`, всегда добавлять строку.
3. Line 384: `if (diag.InBeam) patternTexts[displayIndex].color = Color.green;` — **оставить** (цвет, не gate).

→ При правке нужно трогать **только** line 379 (строку Dots/Bearing). Line 370 и 384 не трогать. Plan Step 4 должен уточнять: убрать `if` **только** вокруг строки Zone/EQ/Dots/Dashes, сохранить `InBeam` для цвета и для строки `[IN BEAM]/[OUTSIDE]`.

## Шаг 1 — `Source/Logic/SonneSignalGenerator.cs`

- `SectorResult`: добавить **только** `public int SectorNumber;` (`IsDashSector` уже есть в структуре — line 15).
- `CalculateSector`: `SectorNumber = sectorNumber` (вычисляется внутри метода: `sectorNumber = (int)(delta / SectorWidthDeg)`; чётный = dot, нечётный = dash).
- `RestoreBearingFromDotCount`, `GenerateBearingTable` — не трогать.

## Шаг 2 — `Source/Data/StationState.cs`

- Добавить: `public bool IsDashSector; public int SectorNumber;`

## Шаг 3 — `Source/Logic/SonneController.cs`

- `UpdateStationNavigation`: после `stations[i].DotCount = sector.DotCount;` добавить **комментарий** (см. Фикс #1):
  ```csharp
  // DotCount = точки в эмиссии; в dot-зоне = до ◆, в dash-зоне = после ◆.
  // EqSignIndex (CAP observer Count) = DotCount (dot) / MaxNavigationCount − DotCount (dash).
  ```
  Затем:
  - `stations[i].IsDashSector = sector.IsDashSector;`
  - `stations[i].SectorNumber = sector.SectorNumber;`
  - **Не трогать** строку `stations[i].TransitionIndex = sector.DotCount;` (line 508) — она остаётся как есть.
- `StationDiagnostics`: поля `IsDashSector`, `SectorNumber`, `EqSignIndex`, `DashCount`; заполнение в `GetStationDiagnostics`:
  - `DashCount = s.Config.MaxNavigationCount − s.DotCount` (без `Max(0,·)` — DotCount уже клампится в [0, 60], P21);
  - `EqSignIndex = s.IsDashSector ? s.Config.MaxNavigationCount − s.DotCount : s.DotCount` — семантика C.A.P. (позиция чтения из `RestoreBearingFromDotCount`); в dot-зоне `EqSignIndex ≡ TransitionIndex` (I4) — зафиксировать в коде (P20).
- Новые геттеры после `GetDecodedText`: `GetReceivedNavigationDotCount()` / `GetReceivedNavigationDashCount()` — проход по `signalEntries`, подсчёт **только** `SignalCharType.Dot`/`.Dash` (Callsign исключён по типу `.Callsign`, Equisignal — по `.Equisignal`; P6/P9). Мутация буфера только на main-потоке (I5) — чтение из UI безопасно. **XML-документация** на геттерах: `/// <summary> Count of dots/dashes in the emission window (≤20 chars). This is NOT the CAP observer count (DotCount/DashCount from 60-cycle). Recv ≪ CAP-count; Recv = 0 during callsign phase is expected. </summary>` (см. Фикс #2).
- **UI — только при `i == dominantStationIndex`**: `signalEntries` заполняется только для доминантной станции (гейт `IsProcessingDominantStation`) — иначе недоминантная строка покажет чужой буфер (P22).

## Шаг 4 — `Source/UI/InformationPanelHandler.cs`, `UpdatePatterns`

- Убрать гейт `if (diag.InBeam)` для счётчиков — показывать для **всех** станций с gain:
  ```
  LEC [OUTSIDE] 97% BFO:319Hz Navigation ToBoat:214.3° 00:12
    Zone:S3(DASH) EQ@22/60 Dots:38 Dashes:22 Bearing:067.0°
  ```
  (InBeam достижим только в секторе 0 (dot) / 71 (dash) — порог `CalculateInBeam` ≈ 2.5°; пример с OUTSIDE — реалистичный, P5.)
- Доминантная станция (строго `i == domIdx`): доп. строка `Recv: X• Y▬` из новых геттеров. Пример: `Recv: 7• 13▬` — оконная семантика: `signalEntries` триммится до `MaxMorseChars = 20`; Recv — малое колеблющееся число ≤ 20, **не равное** `Dots/Dashes` той же строки (таргеты из `StationState`, цикл 60 знаков; P2 вариант **(а)**). `X + Y ≤ 20` всегда (I2). `Recv: 0• 0▬` после смены доминанты/частоты (буфер чистится) и почти всю фазу Callsign (окно заполнено callsign-глифами) — ожидаемое поведение, не баг (P15/P18).
- Цвета прежние (зелёный InBeam / серый Outside).
- **Без `#if DEBUG`** — игра компилирует `Source/*.cs` без `DEBUG`.

## Шаг 5 — Проверка сборки

- `powershell -File build.ps1` — компиляция без ошибок (статическая; рантайм-DLL не используется, игра компилирует Source/*.cs сама).

## Шаг 6 — Обслуживание графа

- `graphify update .` (AGENTS.md — после правок кода).

# Критерии приёмки

**Код:**

- A1. `SectorResult.SectorNumber` заполняется; чётный = dot-зона, нечётный = dash-зона.
- A2. `IsDashSector`/`SectorNumber` в `StationState` заполняются из `CalculateSector` и читаются в `GetStationDiagnostics`.
- A3. `DashCount == MaxNavigationCount − DotCount`; `EqSignIndex == DotCount` (dot) / `MaxNavigationCount − DotCount` (dash). В dot-зоне `EqSignIndex ≡ TransitionIndex` (I4).

  > **Соответствие CAP 59 (observer procedure):** `EqSignIndex` = число символов доминирующего типа, услышанных **до** эквисигнала (observer `Count`): в dot-зоне = `DotCount` (игра эмитит точки → ◆ → тире, `GenerateConsolPattern`); в dash-зоне = `DashCount = 60 − DotCount` (тире → ◆). `Dots + Dashes == 60` задаётся моделью, а не observer-redistribution (`38/18 → 40/20`) — поправка D2 не применяется, intentional. Номер сектора `S<n>` выводится **только для дебага**, радисту не нужен (см. D4/D7). `Recv` ≠ CAP-count: Recv = окно эмиссии ≤ 20 (P13/P19). Подробнее — в `AUDIT` → D8.
- A4. Recv-геттеры считают только `Dot`/`Dash`; callsign (`.Callsign`) и эквизигнал (`.Equisignal`) не считаются (I3).
- A5. В `UpdatePatterns` строка Zone/EQ/Dots/Dashes у **всех** станций с gain; Recv — только доминантная, геттеры вызываются строго при `i == domIdx` (P22).
- A6. Новые строки не под `#if DEBUG`.
- A7. `build.ps1` проходит без ошибок/предупреждений компиляции.

**Рантайм (игра, зона Sonne, станция слышна):**

- R1. У каждой станции с gain видно `Zone:S<n>(DOT|DASH)`; у доминантной — `Recv: X• Y▬`. Нулевые Recv (после смены доминанты/частоты, фаза Callsign) — ожидаемое поведение, не баг.
- R2. `EQ@N` согласован с зоной: dot-зона `Dots == N`; dash-зона `Dashes == N`, где `N = EqSignIndex`. Сумма `Dots + Dashes == MaxNavigationCount` — тавтология формулы (I1), оставлена как контроль конфига.
- R3. Визуальный подсчёт в рич-тексте Signal: считать только красные • (Dot) и синие ▬ (Dash), исключая зелёные callsign-глифы и пурпурные ◆◆ (`SignalCharType.Equisignal`). Сравнение Recv с визуальным счётом — **только** на стоянке / в PreNavPause-окне: при движении лодки внутри сектора паттерн заморожен на старте Navigation (`IsKeyerAvailableForPatternUpdate`), а `DotCount` живёт — сравнение провалится (P13/P19). ◆ виден в окне только в первые ~20 знаков цикла, далее вытриммирован (P8). `X + Y ≤ 20` всегда (I2).
- R4. EQ@ — **пила**: растёт 0→60 от ◆-линии (начало сектора) к дальнему краю; на каждой границе сектора сброс 60→0; зона DOT↔DASH переключается на границе (P7). Не монотонно.
- R5. В dash-зоне `EQ@` = C.A.P.-позиция (`MaxNavigationCount − DotCount`); ◆◆ в рич-тексте = позиция модели `callsignLen + DotCount` (dot-first эмиссия) — расхождение ожидаемо, отладка по `EqSignIndex`, а не по ◆◆ (P3 вариант **(б)**). ◆ — в первые ~20 знаков цикла (P8). Полный цикл = callsign + **61** знак (60 навигационных + ◆) (P12).

**Откат:** `git checkout main` (ветка не сливается, пока критерии не пройдены).
