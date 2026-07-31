# F52: SAD missing classes from diagrams

**Severity:** P1 (High)

**Тип:** Документация — пропущенные сущности

**Файлы:** `SAD/Appendix_B_Class_Diagrams.md`

---

## Описание

SAD Appendix B (Current Class Diagram) не включает три существующих класса, которые являются важной частью архитектуры. Вместо них диаграмма показывает несуществующие классы (F47).

### Пропущенные классы

| # | Реальный класс | LOC | Роль | Где должен быть |
|---|---------------|-----|------|-----------------|
| 1 | `RadioStoryIntegration` | 381 | Интеграция с RadioStory через reflection, инъекция решений, локализация | Logic layer |
| 2 | `SonneSignalGenerator` | 146 | Генерация Consol/Navigation сигналов, bearing table | Logic layer |
| 3 | `SignalRichTextFormatter` | 86 | Форматирование сигналов в rich text для UI | UI layer |

### Почему их отсутствие критично

**RadioStoryIntegration (381 строк):**
- Центральный класс для интеграции с игровой RadioStory
- Содержит всю reflection-логику (8 cached members)
- Управляет жизненным циклом радио через OnDecisionSelected/OnStopped патри
- Без него непонятно, как мод взаимодействует с игровым сюжетом
- Вместо него SAD показывает несуществующий `GameReflectionCache`

**SonneSignalGenerator (146 строк):**
- Генерирует Consol сигналы (Dot/Dash/Equisignal patterns)
- Содержит `GenerateBearingTable()` (dead code — F34)
- Содержит `CalculateSector()` — ключевой алгоритм определения сектора
- Содержит `GenerateConsolPattern()` — создание паттерна навигации
- Вместо него SAD ничего не показывает (signal generation «спрятан» в SonneController)

**SignalRichTextFormatter (86 строк):**
- Форматирует буфер сигналов в rich text для TMP
- Содержит `FormatRichText()`, `TrimEntries()`, `TrimDecodedText()`
- Без него непонятно, как UI отображает декодированные сигналы

---

## Влияние

1. Current Class Diagram неполон на ~613 строк кода (14% проекта)
2. Sequence diagrams в Appendix C не могут ссылаться на эти классы
3. Архитектор, изучающий проект по SAD, пропустит важные компоненты
4. Создаётся ложное впечатление, что SonneController делает больше, чем на самом деле (часть логики вынесена)

---

## Рекомендация

Добавить `RadioStoryIntegration`, `SonneSignalGenerator`, `SignalRichTextFormatter` в Appendix B Current Class Diagram. Указать их связи с существующими классами (RadioStoryIntegration → RadioStory (game), SonneSignalGenerator → SonneController, SignalRichTextFormatter → InformationPanelHandler).
