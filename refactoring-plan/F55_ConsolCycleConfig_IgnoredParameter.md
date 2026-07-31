# F55: ConsolCycleConfig.GetForCycleDuration() игнорирует параметр

**Приоритет:** P1 (High)  
**Область:** Data · ConsolCycleConfig  
**Файл:** `Source/Data/ConsolCycleConfig.cs:36-39`

---

## Проблема

```csharp
public static ConsolCycleConfig GetForCycleDuration(int cycleDurationSeconds)
{
    return Cycle120s; // ← параметр cycleDurationSeconds не используется
}
```

Метод всегда возвращает `Cycle120s` независимо от переданной длительности цикла. Работает только потому, что все 14 станций в `BeaconData.Stations` имеют `CycleDurationSeconds = 120`.

Если добавить станцию с другой длительностью (например, 60s или 180s — исторически такие существовали), код **молча использует неверный конфиг**: неверное количество сигналов, длительности элементов, паузы.

## Зависимости

Вызывается в:
- `SonneController.Awake()` (строка 60)
- `BeaconStation.GetDotCountToBearingTable()` (строка 30) — dead code per F34
- `SonneSignalGenerator.GenerateBearingTable()` (строка 57)

## Исправление

Вариант A: Добавить `switch` или `Dictionary<int, ConsolCycleConfig>` с поддержкой разных длительностей.
Вариант B: Если других длительностей никогда не будет — `throw new NotSupportedException()` для не-120s, чтобы не было молчаливого неправильного поведения.

## Риски

- После исправления нужно проверить, какие исторические длительности циклов реально существовали у Consol/Sonne станций
- Изменение может затронуть тайминги в `StationScheduler` и `MorseKeyer`
