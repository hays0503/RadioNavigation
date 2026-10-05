# F86: ConsolCycleConfig.GetForCycleDuration — stub игнорирует параметр

**Приоритет:** P2 (Medium)
**Область:** Data · ConsolCycleConfig
**Файл:** `ConsolCycleConfig.cs:36-39`
**Дата обнаружения:** 2026-07-31

---

## Проблема

```csharp
public static ConsolCycleConfig GetForCycleDuration(int cycleDurationSeconds)
{
    return Cycle120s;  // Игнорирует cycleDurationSeconds!
}
```

Работает только потому что все 14 станций = 120s. При добавлении станции с другим cycle duration → **silent wrong results**.

---

## Исправление

Добавить switch или dictionary для разных durations.

---

## Acceptance Criteria

- [ ] Метод возвращает корректный config для каждого поддерживаемого duration
