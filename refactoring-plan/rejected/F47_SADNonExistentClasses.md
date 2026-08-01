# F47: 14+ non-existent classes presented as real in SAD

**Severity:** P1 (High)

**Тип:** Документация — несуществующие сущности

**Файлы:** `SAD/03_Current_Architecture.md`, `SAD/04_Component_Architecture.md`, `SAD/05_Dependency_Analysis.md`, `SAD/06_God_Object_Analysis.md`, `SAD/08_Target_Architecture.md`, `SAD/Appendix_B_Class_Diagrams.md`, `SAD/Appendix_C_Sequence_Diagrams.md`

---

## Описание

SAD документы ссылаются на 14+ классов, которые **не существуют** в исходном коде проекта. Они нарисованы в class diagrams, упомянуты в sequence diagrams, и описаны в архитектурных разделах — но ни один из них не имеет соответствующего `.cs` файла.

### Несуществующие классы

| # | Класс | Где упомянут | Вместо чего в реальности |
|---|-------|-------------|------------------------|
| 1 | `TickScheduler` | 03, 04, App B, App C | Нет аналога. `StationScheduler.Tick()` — inline |
| 2 | `GameStrings` | 03, 04, 05 | Нет аналога. Строки в `BeaconData.cs` |
| 3 | `StationManager` | 04, 05, 06, 08, App B, App C | `SonneController` управляет станциями напрямую |
| 4 | `SignalDecoder` | 04, 08, App B, App C | `SonneController.DecodeCallsignBuffer()` |
| 5 | `PositionTracker` | 04, 08, App B, App C | `PositionFixCalculator` |
| 6 | `AudioController` | 04, 08, App B, App C | `SonneController` + `AudioSynthesisEngine` |
| 7 | `AudioPipeline` | 04, 08, App B | `DspPipeline` (embedded 3 classes in 1 file) |
| 8 | `CarrierBank` | 04, App B, App C | `CarrierGenerator[]` внутри `SonneController` |
| 9 | `RadioContext` | 08, App B | DI container? Не реализован |
| 10 | `StationInfoDisplay` | 04, App B | `InformationPanelHandler` |
| 11 | `StationFinder` | 04, App B | `InformationPanelHandler.FindClosestStation()` |
| 12 | `GameReflectionCache` | 08, App C | `RadioStoryIntegration` (field-level cache) |
| 13 | `StationStateBuffer` | 08, App B, App C | Double-buffer? Не реализован |
| 14 | `FormatDiagnosticString` | 03, 06 | `GetStationDiagnostics()` — возвращает struct, не string |

### Статистика по документам

| Документ | Несуществующих классов | Примеры |
|----------|----------------------|---------|
| 03_Current_Architecture.md | 3 | GameStrings, TickScheduler, FormatDiagnosticString |
| 04_Component_Architecture.md | 6 | StationManager, StationInfoDisplay, StationFinder, SignalDecoder, TickScheduler, GameStrings |
| 05_Dependency_Analysis.md | 1 | GameStrings |
| 06_God_Object_Analysis.md | 1 | FormatDiagnosticString |
| 08_Target_Architecture.md | 10 | Все 14, кроме StationInfoDisplay, FormatDiagnosticString, StationFinder |
| Appendix_B_Class_Diagrams.md | 6 | StationManager, SignalDecoder, PositionTracker, AudioPipeline, StationInfoDisplay, StationFinder |
| Appendix_C_Sequence_Diagrams.md | 7 | StationManager, StationStateBuffer, SignalDecoder, PositionTracker, GameReflectionCache, AudioController, CarrierBank |

---

## Влияние

1. **SAD невозможно использовать как reliable reference** для новых разработчиков
2. **Sequence diagrams не отражают реальный flow** данных
3. **Усугубляет F44** (class diagram drift), добавляя классы, которых в природе не существует
4. При попытке `dotnet build` или рефакторинга ссылки на эти классы не найдутся

---

## Рекомендация

1. Удалить все упоминания несуществующих классов из SAD документов (где они описаны как существующие)
2. В `08_Target_Architecture.md` оставить их как «Proposed/Desired» — с пометкой, что они не реализованы
3. Добавить реальные классы в диаграммы: `RadioStoryIntegration`, `SonneSignalGenerator`, `SignalRichTextFormatter`
