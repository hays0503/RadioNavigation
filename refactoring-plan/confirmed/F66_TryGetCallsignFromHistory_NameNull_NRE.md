# F66: TryGetCallsignFromHistory — NRE при station.Name == null

**Приоритет:** P1 (High)  
**Область:** Logic · SonneController  
**Файл:** `Source/Logic/SonneController.cs:470-477`

---

## Проблема

Метод `TryGetCallsignFromHistory()` проверяет `station.History`, `station.History.Length` и `station.History[0].Callsign`, но **не проверяет `station.Name`** перед вызовом `.Substring()`:

```csharp
if (station.History.IsNotNull()                          // строка 470
    && station.History.Length.IsPositive()                // строка 471
    && !string.IsNullOrEmpty(station.History[0].Callsign) // строка 472
)
{
    callsign = station.History[0].Callsign;
}
else
{
    callsign = station.Name.Substring(0, Math.Min(2, station.Name.Length)).ToUpper(); // ← NRE!
}
```

Если `station.Name` — null, `station.Name.Substring()` выбрасывает NullReferenceException.

## Поражённые станции

`BeaconData.cs` содержит 6 станций с `Callsign = null` (F37), но все имеют `Name`. Проблема возникнет при добавлении новой станции без имени или при повреждении данных.

## Воспроизведение

```csharp
var station = new BeaconStation { Name = null, History = null };
string callsign;
controller.TryGetCallsignFromHistory(station, out callsign); // NRE
```

## Исправление

Добавить проверку `station.Name`:

```csharp
else if (!string.IsNullOrEmpty(station.Name))
{
    callsign = station.Name.Substring(0, Math.Min(2, station.Name.Length)).ToUpper();
}
else
{
    callsign = "??";
}
```

## Риски

- Защита от null в данных — лучшая практика
- Не влияет на текущие станции (все имеют Name)
- Предотвращает падение при future data entry
