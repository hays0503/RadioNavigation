# F150. DialConfig — перекрытие частотных диапазонов Band III и Band IV

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Data · DialConfig · Band Ranges |
| **Файл** | `Source/Data/DialConfig.cs` |
| **Строки** | 57, 73 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

Band III и Band IV перекрываются:

| Параметр | Band III | Band IV |
|----------|----------|---------|
| RangeMin | 340 | **720** |
| RangeMax | **760** | 1600 |

Диапазон **720-760 kHz** принадлежит обоим диапазонам. Частота 740 kHz может быть интерпретирована как Band III (ReferencePoint `{740, 163.3f}`) или Band IV (ReferencePoint `{740, 18.8f}`) — это **разные углы** (163.3° vs 18.8°).

В `AngleToFrequency` вызов с `bandIndex=2` (III) и `bandIndex=3` (IV) для одного и того же угла вернёт разные частоты.

## Корневая причина

Диапазоны частот определены с перекрытием. Нет уникального маппинга частота→диапазон.

## Предлагаемое исправление

Разделить диапазоны без перекрытия:

```csharp
// Band III: 340-719 kHz
// Band IV: 720-1600 kHz
Bands[2] = new BandConfig { RangeMin = 340, RangeMax = 720, ... };
Bands[3] = new BandConfig { RangeMin = 720, RangeMax = 1600, ... };
```

Или использовать `719.5` как границу.

## Зависимости

- Связан с F20 (DialConfig band inconsistencies) — общая проблема диапазонов
- Связан с F130 (BandConfig mutable in static array) — тот же файл
