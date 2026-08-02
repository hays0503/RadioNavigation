# План: зона/эквисигнал + счётчики в InformationPanel

Ветка по умолчанию на момент планирования: **main** (незакоммиченные `Bundles/testui` (M) и `documents/` (??) — не трогать).

## Ветвление

- **Перед первым изменением кода** — `git checkout -b feature/panel-eq-zone-recv`
- Коммитим только `Source/`. `Bundles/testui` и `documents/` в коммиты ветки не включаем.

## Шаг 1 — `Source/Logic/SonneSignalGenerator.cs`

- `SectorResult`: добавить `public int SectorNumber;`
- `CalculateSector` (строка 24): `SectorNumber = sectorNumber` (значение уже вычислено локально).
- `RestoreBearingFromDotCount`, `GenerateBearingTable` — не трогать.

## Шаг 2 — `Source/Data/StationState.cs`

- Добавить: `public bool IsDashSector; public int SectorNumber;`

## Шаг 3 — `Source/Logic/SonneController.cs`

- `UpdateStationNavigation` (строка ~502): после `stations[i].DotCount = sector.DotCount;` добавить:
  - `stations[i].IsDashSector = sector.IsDashSector;`
  - `stations[i].SectorNumber = sector.SectorNumber;`
- `StationDiagnostics` (строка 338): поля `IsDashSector`, `SectorNumber`, `EqSignIndex`, `DashCount`; заполнение в `GetStationDiagnostics`:
  - `DashCount = Max(0, s.Config.MaxNavigationCount − s.DotCount)`
  - `EqSignIndex = s.IsDashSector ? s.Config.MaxNavigationCount − s.DotCount : s.DotCount`
- Новые геттеры после `GetDecodedText`: `GetReceivedNavigationDotCount()` / `GetReceivedNavigationDashCount()` — проход по `signalEntries`, подсчёт `SignalCharType.Dot` / `.Dash` (callsign исключён по типу `.Callsign`; мутация буфера только на main-потоке — чтение из UI безопасно).

## Шаг 4 — `Source/UI/InformationPanelHandler.cs`, `UpdatePatterns` (строка 362)

- Убрать гейт `if (diag.InBeam)` для счётчиков — показывать для всех станций с gain:

```
LEC [IN BEAM] 97% BFO:319Hz Navigation ToBoat:214.3° 00:12
  Zone:S3(DASH) EQ@22/60 Dots:38 Dashes:22 Bearing:067.0°
```

- Доминантная станция (`domIdx >= 0`): доп. строка `Recv: 38• 22▬` из новых геттеров.
- Цвета прежние (зелёный InBeam / серый Outside).
- **Без `#if DEBUG`** — игра компилирует Source без `DEBUG`, блоки вырезаются в рантайме (AGENTS.md).

## Шаг 5 — Проверка сборки

- `powershell -File build.ps1` — компиляция без ошибок (статическая; рантайм-DLL не используется, игра компилирует `Source\*.cs` сама).

## Шаг 6 — Обслуживание графа

- `graphify update .` (по AGENTS.md после правок кода).

# Критерии приёмки

**Код (проверяется по диффу/сборке):**

- A1. `SectorResult.SectorNumber` заполняется; чётный = dot-зона, нечётный = dash-зона.
- A2. `IsDashSector`/`SectorNumber` в `StationState` заполняются из `CalculateSector` и читаются в `GetStationDiagnostics`.
- A3. `DashCount == 60 − DotCount`; `EqSignIndex == DotCount` (dot-зона) / `60 − DotCount` (dash-зона).
- A4. Recv-геттеры не считают callsign-элементы (тип `.Callsign`).
- A5. В `UpdatePatterns` строка Zone/EQ/Dots/Dashes у **всех** станций с gain; Recv — только доминантная.
- A6. Новые строки не под `#if DEBUG`.
- A7. `build.ps1` проходит без ошибок/предупреждений компиляции.

**Рантайм (в игре, зона Sonne, станция слышна):**

- R1. У каждой станции в списке видно `Zone:S<n>(DOT|DASH)`; у доминантной — `Recv: X• Y▬`.
- R2. `Dots + Dashes == 60`; `EQ@N` согласован со зоной (dot: `Dots == N`; dash: `Dashes == 60 − N`).
- R3. Recv-счётчики совпадают с визуальным подсчётом знаков в rich-text строке Signal (◆◆ = эквисигнал).
- R4. При переходе через эквисигнал `EQ@N` меняется монотонно 0↔60 без скачков, зона DOT↔DASH сменяется на границе сектора.
- R5. Известное ограничение зафиксировано наблюдением: в dash-зоне Recv показывает точки до ◆◆ (модель `GenerateConsolPattern` всегда эмитит точки первыми — расхождение с C.A.P. 59, если обнаружено — отдельной задачей).

**Откат:** `git checkout main` (ветка не сливается, пока критерии не пройдены).
