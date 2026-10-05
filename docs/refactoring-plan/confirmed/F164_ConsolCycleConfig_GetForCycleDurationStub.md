# F164. ConsolCycleConfig.GetForCycleDuration — stub, игнорирует входной параметр

| Поле | Значение |
|------|----------|
| **Приоритет** | P3 (Low) |
| **Область** | Data · ConsolCycleConfig · Stub |
| **Файл** | `Source/Data/ConsolCycleConfig.cs` |
| **Строки** | 36-39 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

```csharp
// ConsolCycleConfig.cs:36-39
public static ConsolCycleConfig GetForCycleDuration(int cycleDurationSeconds)
{
    return Cycle120s;  // ← всегда возвращает Cycle120s, параметр игнорируется
}
```

Принимает `cycleDurationSeconds`, но всегда возвращает `Cycle120s`. Это stub, который был начат, но не завершён.

Это отдельный файл от F55/F86: F55 описывает проблему в `BeaconData.GetDotCountToBearingTable`, F86 — в `SonneSignalGenerator`. F164 описывает сам stub.

## Корневая причина

Incomplete implementation — planned support for multiple cycle durations never materialized.

## Предлагаемое исправление

Добавить switch или оставить только одну конфигурацию с документированным ограничением:

```csharp
public static ConsolCycleConfig GetForCycleDuration(int cycleDurationSeconds)
{
    // Only 120s cycle is supported
    if (cycleDurationSeconds != 120)
        Debug.LogWarning($"[ConsolCycleConfig] Only 120s cycle supported, got {cycleDurationSeconds}s");
    return Cycle120s;
}
```

## Зависимости

- Связан с F55 (ConsolCycleConfig ignored parameter) — тот же stub
- Сязан с F86 (ConsolCycleConfig stub) — тот же stub
- Связан с F92 (ConsolCycleConfig unused fields) — часть неиспользуемых полей
- Связан с F126 (ConsolCycleConfig no validation) — нет валидации
