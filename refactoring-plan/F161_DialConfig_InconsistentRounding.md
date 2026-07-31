# F161. DialConfig.FormatFrequency — несогласованное округление для не-MHz диапазонов

| Поле | Значение |
|------|----------|
| **Приоритет** | P3 (Low) |
| **Область** | Data · DialConfig · Formatting |
| **Файл** | `Source/Data/DialConfig.cs` |
| **Строки** | 155 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

```csharp
// DialConfig.cs:152-155
if (band.Unit == "MHz")
    return frequency.ToString("F1") + " MHz";  // 1 decimal: "100.5 MHz"

return Mathf.RoundToInt(frequency) + " kHz";   // integer: "333 kHz"
```

Band I (MHz) показывает 1 знак после запятой. Остальные bands (kHz) округляются до целого. Это теряет sub-kHz точность: `332.5 kHz` → `"333 kHz"`.

## Корневая причина

Единый формат для всех не-MHz bands.

## Предлагаемое исправление

Для bands с дробными частотами (например, Band II-V с 0.5 kHz шагом):

```csharp
if (band.Unit == "MHz")
    return frequency.ToString("F1") + " MHz";

return frequency.ToString("F1") + " kHz";
```

Или добавить параметр decimal places в BandConfig.

## Зависимости

- Связан с F20 (DialConfig band inconsistencies) — тот же файл
- Связан с F28 (Magic Numbers) — форматирование — часть magic numbers проблемы
