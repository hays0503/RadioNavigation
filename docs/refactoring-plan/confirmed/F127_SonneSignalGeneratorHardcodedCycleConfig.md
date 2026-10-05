# F127. SonneSignalGenerator.GenerateBearingTable хардкодит Cycle120s

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Logic · SonneSignalGenerator · Configuration |
| **Файл** | `Source/Logic/SonneSignalGenerator.cs` |
| **Строки** | 57 |

---

## Описание

`GenerateBearingTable()` принимает `BeaconStation`, но игнорирует его `CycleDurationSeconds`:

```csharp
// SonneSignalGenerator.cs:55-57
public static int[] GenerateBearingTable(BeaconStation station)
{
    var config = ConsolCycleConfig.Cycle120s; // хардкод — station не используется
    // ...
}
```

`BeaconStation` содержит поле `CycleDurationSeconds`:

```csharp
// BeaconData.cs — BeaconStation struct
public int CycleDurationSeconds; // 120 для всех текущих станций
```

Все 14 текущих станций используют 120s cycle, поэтому проблема **скрыта**. Но если добавить станцию с другим cycle duration (например, 60s), bearing table будет неверной — количество dots, пауз и DegPerSign рассчитаются для 120s.

## Корневая причина

Метод получает `station` но не обращается к `station.CycleDurationSeconds`. Вместо этого использует глобальную константу.

## Предлагаемое исправление

```csharp
public static int[] GenerateBearingTable(BeaconStation station)
{
    var config = ConsolCycleConfig.GetForCycleDuration(station.CycleDurationSeconds);
    // ...
}
```

(После исправления F55/F86 — `GetForCycleDuration` должен возвращать правильный конфиг для данного duration.)

## Зависимости

- Связан с F55/F86 (ConsolCycleConfig.GetForCycleDuration ignores param)
- Связан с F35 (Duplicate bearing algorithms)
