# F126. ConsolCycleConfig — нет валидации отрицательных timing values

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Data · ConsolCycleConfig · Validation |
| **Файл** | `Source/Data/ConsolCycleConfig.cs` |
| **Строки** | 1-41 |

---

## Описание

`ConsolCycleConfig` — mutable struct с 13 публичными полями без какой-либо валидации:

```csharp
// ConsolCycleConfig.cs
public struct ConsolCycleConfig
{
    public float DotSoundMs;
    public float DashSoundMs;
    public float ElementPeriodMs;
    // ... ещё 10 полей
}
```

`MorseTiming` вычисляет silence ticks через деление:

```csharp
// MorseTiming.cs:~20-25 (внутри методов-конвертаций)
int soundTicks = ToTicks(soundMs);
int periodTicks = ToTicks(periodMs);
int silenceTicks = Math.Max(1, periodTicks - soundTicks);
```

Если `DotSoundMs > ElementPeriodMs` (например, при ручном конфигурировании нового cycle duration), `silenceTicks` вычисляется как отрицательное число, и `Math.Max(1, ...)` маскирует ошибку — тишина становится 1 тик вместо ожидаемого отрицательного значения.

Дополнительно:
- `EquisignalMs` может быть больше `ElementPeriodMs`
- `PostNavSilenceMs` может быть отрицательным
- `NavigationDurationMs = 0` приведёт к station scheduler, который никогда не завершит навигацию

## Корневая причина

Нет контракта на валидность полей. `GetForCycleDuration()` (F55/F86) возвращает единственный захардкоженный конфиг, поэтому проблема скрыта до добавления нового конфига.

## Предлагаемое исправление

Добавить метод валидации:

```csharp
public bool IsValid()
{
    return DotSoundMs > 0
        && DashSoundMs > 0
        && ElementPeriodMs >= DotSoundMs
        && ElementPeriodMs >= DashSoundMs
        && CallsignDotMs > 0
        && EquisignalMs > 0
        && CallsignDurationMs > 0
        && PreNavPauseMs >= 0
        && NavigationDurationMs > 0
        && PostNavSilenceMs >= 0
        && MaxNavigationCount > 0
        && DegPerSign > 0
        && SectorWidthDeg > 0
        && TotalSignals > 0;
}
```

Вызывать `IsValid()` в `GetForCycleDuration()` и логировать предупреждение при невалидном конфиге.

## Зависимости

- Связан с F55/F86 (ConsolCycleConfig.GetForCycleDuration ignores param)
- Связан с F92 (ConsolCycleConfig unused fields)
