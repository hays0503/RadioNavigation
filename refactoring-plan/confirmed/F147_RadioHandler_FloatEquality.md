# F147. RadioHandler — float equality `==` в CursorAngle setter

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | UI · RadioHandler · Floating-Point |
| **Файл** | `Source/UI/RadioHandler.cs` |
| **Строки** | 28-29 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

Сравнение float через `==`:

```csharp
// RadioHandler.cs:28-29
if (cursorAngle == value)
    return;
```

После Lerp smoothing в `SpinnerHandlerBase.OnDrag` угол может прийти с tiny rounding differences (например, `89.99999` vs `90.0`). Это вызывает:
1. **Пропуск** обновления: если два различных угла rounding'ятся до одного `float`, изменение пропускается
2. **Дублирование** вызовов: если `EmitFrequency()` вызывается при каждом微量ном изменении, это создаёт лишние string allocations в `DialConfig.FormatFrequency`

## Корневая причина

Float equality ненадёжна для сравнения значений, полученных через Lerp/interpolation.

## Предлагаемое исправление

Использовать `Mathf.Approximately`:

```csharp
if (Mathf.Approximately(cursorAngle, value))
    return;
```

Или пороговое сравнение:

```csharp
if (Math.Abs(cursorAngle - value) < 0.001f)
    return;
```

## Зависимости

- Связан с F72 (SpinnerHandler FPS-dependent Lerp) — Lerp создаёт неточные float значения
- Связан с F74 (ComparisonExtensions threshold mismatch) — inconsistencies в пороговых сравнениях
