# F101. InformationPanelHandler — "[Pause]" shown when no dominant station (domIdx=-1)

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | UI · InformationPanelHandler · Logic Bug |
| **Файл** | `Source/UI/InformationPanelHandler.cs` |
| **Строки** | 309-315 |

---

## Описание

Когда `dominantStationIndex` равен -1 (ни одна станция не имеет достаточного усиления), `GetStationDiagnostics(-1)` возвращает дефолтный `StationDiagnostics` с `SchedulerPhase = Pause`. UI-код на строках 311-315 безусловно добавляет `" [" + phase + "]"` к тексту станции:

```csharp
// Строка 309-315:
int domIdx = sonneController.DominantStationIndex;
var diag = sonneController.GetStationDiagnostics(domIdx);
string phase = diag.SchedulerPhase.ToString();
string suffix = " [" + phase + "]";
if (diag.NavigationElapsed.IsPositive())
    suffix += " " + FormatTime(diag.NavigationElapsed);
stationText.text = stationBaseText + suffix;
```

Результат: дисплей показывает `"[Pause]"` хотя ни одна станция не активна. Пользователь видит фазу несуществующей станции.

## Корневая причина

Нет проверки `domIdx == -1` перед формированием суффикса.

## Предлагаемое исправление

```csharp
int domIdx = sonneController.DominantStationIndex;
if (domIdx >= 0)
{
    var diag = sonneController.GetStationDiagnostics(domIdx);
    string phase = diag.SchedulerPhase.ToString();
    string suffix = " [" + phase + "]";
    if (diag.NavigationElapsed.IsPositive())
        suffix += " " + FormatTime(diag.NavigationElapsed);
    stationText.text = stationBaseText + suffix;
}
else
{
    stationText.text = stationBaseText;
}
```

## Зависимости

- Независим от других F-проблем
