# F44: SAD Appendix B — расхождения класс-диаграмм с реальным кодом (10+)

**Приоритет:** P2 (Medium)
**Область:** Documentation · SAD
**Тип:** Documentation Drift

---

## Проблема

SAD Appendix B (Class Diagrams) содержит 10+ расхождений с реальным кодом. Секция «Current Class Diagram» должна отражать актуальную архитектуру, но многие классы, поля и методы не соответствуют действительности.

---

## Evidence

| Элемент SAD | В SAD | В реальном коде |
|-------------|-------|-----------------|
| `BeaconStation.BearingTable` | Поле `int[]` | Метод `GetDotCountToBearingTable()` |
| `BeaconStation.Callsign` | Поле `string` | В `History[0].Callsign`, не прямое поле |
| `BeaconStation.Color` | Поле `string` | Не существует |
| `BeaconData.GetDotCountToBearingTable` | Static метод | Instance метод на `BeaconStation` |
| `ReceiverDsp` | 3 метода: `ApplyAGC`, `ApplyFilter`, `ApplySaturation` | 1 метод: `Process(sample, sampleRate)` |
| `Atmosphere.GenerateNoise` | `GenerateNoise(data: float[])` | `Apply(sample, sampleRate)` — sample-by-sample |
| `DialConfig.referencePoints` | `Dictionary<string, float[]>` | `SortedList<float, float>` в `BandConfig` |
| `SpinnerHandlerBase` | `OnPointerDown`, `OnDrag`, `UpdateValue` | `OnAngleUpdated`, `GetAngleOffset`, `ClampTargetAngle`, `HandleDragDirection` |
| `MorseKeyer.IsComplete()` | Существует | Нет такого метода |
| `RadioStoryIntegration` | Отсутствует | Ключевой класс с Reflection (F23) |
| `SonneSignalGenerator` | Отсутствует | Весь Consol-сигнал генерируется здесь |
| `SignalRichTextFormatter` | Отсутствует | UI-форматирование сигнала |

---

## Impact

- Документация вводит в заблуждение новых разработчиков
- SAD — единственный архитектурный документ, его расхождения снижают доверие ко всем разделам
- Целевая архитектура (Section 2) не соответствует коду, миграция непрозрачна

## Fix

Обновить Appendix B: актуализировать поля, методы, добавить пропущенные классы (RadioStoryIntegration, SonneSignalGenerator, SignalRichTextFormatter, DebugAngleHandler, ExpanderHandler, CursorHandler, PanelDragHandler).
