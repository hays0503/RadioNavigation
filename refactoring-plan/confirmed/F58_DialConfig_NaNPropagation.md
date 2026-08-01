# F58: DialConfig.AngleToFrequency() — float.NaN молча пропускается

**Приоритет:** P1 (High)  
**Область:** Data · DialConfig · Correctness  
**Файл:** `Source/Data/DialConfig.cs:106-107`, `Source/UI/RadioHandler.cs:112-118`

---

## Проблема

`DialConfig.AngleToFrequency()` возвращает `float.NaN` при невалидном bandIndex:

```csharp
public static float AngleToFrequency(float angle, int bandIndex)
{
    if (bandIndex.IsNegative() || bandIndex >= Bands.Length)
        return float.NaN; // ← caller не проверяет
    ...
}
```

Каллеры НЕ проверяют на NaN:
- `RadioHandler.EmitFrequency()` (строка 115): `float freq = DialConfig.AngleToFrequency(...)` → передаёт NaN в событие
- `SonneController.UpdateTuning()` (строка 255): `currentFrequencyKHz = frequencyKHz;` → NaN сохраняется в поле
- `InformationPanelHandler.OnFrequencyChanged()` (строка 275): `currentFreqKHz = freqKHz;` → NaN

NaN в `currentFrequencyKHz` приводит к:
1. `UpdateStationGains()`: `float f = currentFrequencyKHz * 1000f;` → NaN → все gain = NaN
2. `Math.Sqrt(1.0 + qf * qf * ratio * ratio)` → NaN → audio тишина
3. `DialConfig.FormatFrequency(float.NaN, _)` → проверяет NaN, возвращает `"---"` (единственный защищённый код)
4. `FindClosestStation(float.NaN, _)` → `Math.Abs(NaN - freq)` = NaN → `NaN < bestDelta` = false → пустая строка

## Воспроизведение

Любой вызов `AngleToFrequency` с bandIndex < 0 или >= 5 выдаёт NaN. На практике zoneIndex приходит от ZoneHandler (0-4), так что прямое воспроизведение маловероятно. Но если ZoneHandler не инициализирован (баг загрузки), GetZoneIndex() возвращает 0... хотя нет, он тоже 0-4.

**Проблема скорее в Design by Contract:** публичный метод документированно возвращает NaN, но никто это не проверяет. Любая будущая модификация ZoneHandler может сломать цепочку.

## Исправление

```csharp
// В RadioHandler.EmitFrequency():
float freq = DialConfig.AngleToFrequency(currentAngle, currentZoneIndex);
if (float.IsNaN(freq)) return; // ← guard
```

Или изменить AngleToFrequency на `float.TryAngleToFrequency(out float freq, ...)`.
