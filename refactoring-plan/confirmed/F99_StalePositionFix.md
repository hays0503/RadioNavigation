# F99. Stale position fix retained when measurements drop below 2

| Поле | Значение |
|------|----------|
| **Приоритет** | P1 (High) |
| **Область** | Logic · SonneController · Position Fix |
| **Файл** | `Source/Logic/SonneController.cs` |
| **Строки** | 41-42, 314-317, 436-440 |

---

## Описание

`fixValid` и `lastFix` обновляются только когда `HasEnoughMeasurementsForFix()` возвращает `true` (>= 2 измерений). Когда условие ложно, предыдущие данные фикса сохраняются:

```csharp
// Строка 436-440:
if (HasEnoughMeasurementsForFix())
{
    lastFix = PositionFixCalculator.Calculate(measurements);
    fixValid = lastFix.Valid;
}
// Если HasEnoughMeasurementsForFix() == false — lastFix и fixValid остаются старыми
```

Свойства `HasFix`, `FixLatitude`, `FixLongitude`, `FixErrorKm` (строки 314-317) возвращают данные `lastFix` без учёта текущего состояния:

```csharp
public bool HasFix => fixValid;
public float FixLatitude => lastFix.Latitude;
public float FixLongitude => lastFix.Longitude;
```

Если игрок выходит из зоны действия станций и количество измерений падает ниже 2, `HasFix` продолжает возвращать `true` с устаревшими координатами. `InformationPanelHandler.UpdateFixDisplay()` отображает эти данные без индикации устаревания.

## Корневая причина

Нет сброса `fixValid` при недостаточном количестве измерений.

## Предлагаемое исправление

```csharp
if (HasEnoughMeasurementsForFix())
{
    lastFix = PositionFixCalculator.Calculate(measurements);
    fixValid = lastFix.Valid;
}
else
{
    fixValid = false;  // <-- сброс при недостаточных данных
}
```

## Зависимости

- Независим от других F-проблем
