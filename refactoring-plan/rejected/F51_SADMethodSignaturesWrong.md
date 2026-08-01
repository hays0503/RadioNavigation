# F51: SAD method signatures wrong

**Severity:** P2 (Medium)

**Тип:** Документация — неверные API сигнатуры

**Файлы:** `SAD/04_Component_Architecture.md`, `SAD/Appendix_B_Class_Diagrams.md`, `SAD/Appendix_C_Sequence_Diagrams.md`

---

## Описание

SAD содержит неверные сигнатуры методов в class diagrams и component descriptions.

### Таблица расхождений

| Класс | Метод | SAD сигнатура | Реальная сигнатура | Severity |
|-------|-------|--------------|-------------------|----------|
| `AudioSynthesisEngine` | `ProcessAudio` | `(float[] data, int channels, StationState[] stations)` — 3 params | `(float[] data, int channels, StationState[] stations, StationScheduler[] schedulers, MorseKeyer[] keyers, CarrierGenerator[] carriers, bool radioOn, int currentZoneIndex, int sonneZoneIndex)` — **9 params** | P2 |
| `MorseKeyer` | `(none)` | `Configure(pattern, scheduler)` | `Load(pattern, scheduler, config)` и `LoadConsol(consol, scheduler, config, callsignLength)` | P2 |
| `StationScheduler` | `GetCurrentConsolPattern()` | Method | **Property** `CurrentConsolPattern` | P2 |
| `CarrierGenerator` | `phase` | `float phase` | `double phase` | P2 |
| `Atmosphere` | `GenerateNoise` | `GenerateNoise(data: float[])` | `Apply(sample, sampleRate): float` — **совсем другой API** | P2 |
| `Atmosphere` | `GenerateImpulse` | `GenerateImpulse(): float` | **Не существует** | P2 |
| `ReceiverDsp` | (все 3) | `ApplyAGC(data)`, `ApplyFilter(data)`, `ApplySaturation(data)` | `Process(sample, sampleRate): float` — **один метод вместо трёх** | P2 |
| `AudioSynthesisEngine` | `ApplyFeedback` | (not listed) | `ApplyFeedback(float[] data, int channels, StationState[] stations, int currentZoneIndex)` | P2 |
| `MorseAlphabet` (MorseKeyer в SAD) | `DecodeElements` / `EncodeText` | Listed as MorseKeyer methods | Методы находятся на `MorseAlphabet`, не `MorseKeyer` | P2 |
| `InformationPanelHandler` | `(none)` | `- uiElements: Dictionary<string, GameObject>` | Поле **не существует** | P2 |
| `RadioHandler` | `EmitFrequency(freq: float, zone: int)` | Public с 2 params | `private EmitFrequency()` — **0 params**, **private** | P2 |

---

## Влияние

1. Class diagram в Appendix B не может быть использован для code generation или reverse engineering
2. Sequence diagrams в Appendix C рисуют неверный flow данных
3. Разработчик, следующий SAD, будет искать несуществующие API

---

## Рекомендация

Исправить сигнатуры методов во всех SAD документах. Для крупных методов (ProcessAudio с 9 params) добавить пояснение о параметрах. Удалить несуществующие методы (ReceiverDsp.GenerateImpulse, Atmosphere.GenerateImpulse, RadioHandler.EmitFrequency(freq, zone)).
