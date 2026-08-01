# F02: God-Object — SonneController (508 LOC)

**Приоритет:** P1 (Major)
**Область:** Logic · Audio/Control
**Блокирует:** Любые изменения в звуке, навигации, сигналах — всё завязано на один класс

---

## Проблема

SonneController содержит **9+ ответственностей** в одном MonoBehaviour. Добавление любой новой фичи требует правки этого класса, даже если фича относится только к одной подсистеме.

Стандартный порог God-класса: >300 LOC. Здесь — 508 LOC, 36 методов, 27 полей.

---

## Evidence

### 9 ответственностей

| # | Ответственность | Строки | Поля/Методы |
|---|----------------|--------|-------------|
| R1 | Audio DSP / Synthesis | 14, 20-21, 242-251, 443-446 | `audioEngine`, `carriers`, `silentClip`, `OnAudioFilterRead` |
| R2 | Radio tuning / station gain | 23, 237-258, 337-394 | `currentFrequencyKHz`, `currentZoneIndex`, `UpdateStationGains` |
| R3 | Navigation pattern / bearing | 25-26, 307-312, 396-441 | `playerLatitude`, `playerLongitude`, `CalculateBearing` |
| R4 | Position fixing | 40-42, 314-318, 436-440, 462 | `measurements`, `lastFix`, `fixValid`, `PositionFixCalculator.Calculate` |
| R5 | Morse decoding / signal buffer | 30-33, 121-212, 214-235 | `signalEntries`, `morseDecoderBuffer`, `DecodeCallsignBuffer` |
| R6 | UI handler binding | 27-28, 44-110 (Awake), 242-251 | `volumeSpinner`, `feedbackSpinner`, event subscription |
| R7 | Runtime lifecycle / timers | 35-38, 44-119, 320-335 | `navUpdateTimer`, `tuningUpdateTimer`, `Update()` |
| R8 | Station scheduler management | 17-19, 56-92, 260-305 | `stations`, `schedulers`, `keyers`, `GetStationDiagnostics` |
| R9 | Geographic math utilities | 481-506 | `CalculateBearing`, `NormalizeAngle`, `ToRadians` |

### Coupling #1: UpdateStationGains() мутирует signalEntries

Строки 379-387 — тюнинг (R2) напрямую чистит буфер декодирования (R5):

```csharp
if (maxIndex != dominantStationIndex)
{
    signalEntries.Clear();       // R5: signal buffer cleared
    morseCharCount = 0;          // R5: morse counter reset
    morseDecoderBuffer.Clear();  // R5: morse decoder cleared
    decodedText.Clear();         // R5: decoded text cleared
}
```

Если разработчик меняет логику определения доминантной станции (например, добавляет гистерезис), он должен понимать, что это затронет сигнальный буфер. Если он меняет структуру `signalEntries` — должен проверить, не сломается ли `UpdateStationGains`. Это две независимые подсистемы, связанные через один метод.

### Coupling #2: UpdateStationNavigation() вычисляет fix inline

Строки 436-440 — навигация (R3) сама вызывает `PositionFixCalculator.Calculate` (R4):

```csharp
if (HasEnoughMeasurementsForFix())
{
    lastFix = PositionFixCalculator.Calculate(measurements);
    fixValid = lastFix.Valid;
}
```

Навигация и позиционирование — разные домены. Первая вычисляет, что игрок *должен слышать*. Вторая — где игрок *находится*. Они должны быть разделены.

### Coupling #3: OnAudioFilterRead() дёргает 7 подсистем за один вызов

Строка 445 — одна строка кода обращается к 6 внутренним массивам + внешнему статическому полю:

```csharp
audioEngine.ProcessAudio(data, channels, stations, schedulers, keyers, carriers,
    ToggleHandler.IsRadioOn, currentZoneIndex, SonneZoneIndex);
```

### Статистика

| Метрика | Значение |
|---------|----------|
| Общее LOC | 508 |
| Методы | 36 (включая 8 property) |
| Поля | 27 |
| Ответственности | **9** |
| Внешних зависимостей (прямых) | AudioSynthesisEngine, StationScheduler, MorseKeyer, CarrierGenerator, PositionFixCalculator, ToggleHandler, BeaconData, SonneSignalGenerator |

---

## Предлагаемое решение

Декомпозиция на 3 класса:

| Новый класс | Ответственность | Источник из SonneController |
|-------------|----------------|-----------------------------|
| `StationManager` | gain, beat frequency, dominant station, schedulers | `stations[]`, `schedulers[]`, `keyers[]`, `carriers[]`, `UpdateStationGains()` |
| `SignalDecoder` | buffer, morse decode, callsign | `signalEntries`, `morseCharCount`, `DecodeCallsignBuffer()`, `GetSignalRichText()` |
| `NavigationCalculator` | bearing, fix, multilateration | `UpdatePosition()`, `CalculateBearing()`, `PositionFixCalculator.Calculate()` |

SonneController остаётся как оркестратор — Unity lifecycle + передача событий между подсистемами.

---

## Acceptance Criteria

- [ ] SonneController < 200 LOC
- [ ] Все подсистемы можно тестировать изолированно (без Unity场景)
- [ ] Нет cross-responsibility field mutation (R2 не пишет в R5)
