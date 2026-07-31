# F28: Magic Numbers Scattered Across Codebase

**Приоритет:** P2 (Medium)
**Область:** Project-wide
**Тип:** Hygiene · Maintainability

---

## Проблема

~40 inline numeric literals разбросаны по 8+ файлам. Нет централизованного источника констант. Изменение tuning parameter требует поиска по всем файлам.

---

## Evidence

| Value | Файл:Строка | Значение |
|-------|-------------|----------|
| `0.01f` | SonneController:387 | Порог значимости Gain |
| `0.001f` | SonneController:453 | Порог потери сигнала |
| `300f` | SonneController:369 | BeatFrequency min |
| `1800f` | SonneController:369 | BeatFrequency max |
| `15f` | InformationPanelHandler:384-385 | Порог точности fix (green) |
| `35f` | InformationPanelHandler:384-385 | Порог точности fix (yellow) |
| `90f` | SpinnerHandlerBase:86, CursorHandler:71, IPH:85 | Cursor angle offset |
| `140f` | SpinnerHandlerBase:9 | Spinner max rotation |
| `50f` | MorseKeyer:? | Tick timing resolution ms |
| `120s` | ConsolCycleConfig:36-39 | Cycle duration (всегда!) |
| `1000f` | SonneController:12, AudioSynthesisEngine:9 | BfoBase (дубликат, см. F12) |

### ADR-005 в SAD 08_Target_Architecture.md

Предложен `Constants.cs` / `RadioConstants.cs` — не реализован.

---

## Предлагаемое решение

Создать `Data/RadioConstants.cs`:

```csharp
internal static class RadioConstants
{
    internal const float GainSignificantThreshold = 0.01f;
    internal const float SignalNoiseFloor = 0.001f;
    internal const float BeatFrequencyMinHz = 300f;
    internal const float BeatFrequencyMaxHz = 1800f;
    internal const float FixAccuracyGreenKm = 15f;
    internal const float FixAccuracyYellowKm = 35f;
    internal const float CursorAngleOffsetDeg = 90f;
    internal const float SpinnerMaxRotationDeg = 140f;
    internal const float MorseTickMs = 50f;
    internal const float BfoBaseKHz = 1000f;
}
```

---

## Acceptance Criteria

- [ ] RadioConstants.cs создан, все ~40 magic numbers заменены
- [ ] BfoBase определён ровно один раз
- [ ] ConsolCycleConfig.GetForCycleDuration возвращает константу, а не всегда Cycle120s
