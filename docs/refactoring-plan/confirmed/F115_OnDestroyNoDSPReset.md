# F115. SonneController.OnDestroy — doesn't reset audioEngine internal state (DSP/Atmosphere)

| Поле | Значение |
|------|----------|
| **Приоритет** | P3 (Low) |
| **Область** | Logic · SonneController · Resource Management |
| **Файл** | `Source/Logic/SonneController.cs` |
| **Строки** | 112-119 |

---

## Описание

`OnDestroy` отписывается от событий, но не вызывает `audioEngine.Dsp.Reset()` и не очищает внутреннее состояние `Atmosphere`/`ReceiverDsp`:

```csharp
// Строка 112-119:
private void OnDestroy()
{
    RadioHandler.OnFrequencyChanged -= OnFrequencyChanged;
    if (volumeSpinner.IsNotNull())
        volumeSpinner.OnValueChanged -= OnVolumeChanged;
    if (feedbackSpinner.IsNotNull())
        feedbackSpinner.OnValueChanged -= OnFeedbackChanged;
    // <-- нет audioEngine.Dsp.Reset() или audioEngine.Atmosphere cleanup
}
```

Если `SonneController` уничтожается и пересоздаётся (например, при перезагрузке сцены), старый `AudioSynthesisEngine` просто удаляется GC. Объекты `Atmosphere` и `ReceiverDsp` с накопленным состоянием (AGC gain, filter state, QSB phase, RNG state) теряются.

Более того, seed RNG в `Atmosphere` основан на `Environment.TickCount`, который меняется медленно — при быстром пересоздании контроллера может воспроизвестись та же последовательность шума.

## Корневая причина

Нет явного сброса состояния DSP при уничтожении.

## Предлагаемое исправление

```csharp
private void OnDestroy()
{
    RadioHandler.OnFrequencyChanged -= OnFrequencyChanged;
    if (volumeSpinner.IsNotNull())
        volumeSpinner.OnValueChanged -= OnVolumeChanged;
    if (feedbackSpinner.IsNotNull())
        feedbackSpinner.OnValueChanged -= OnFeedbackChanged;

    audioEngine?.Dsp?.Reset();
}
```

## Зависимости

- Независим от других F-проблем
