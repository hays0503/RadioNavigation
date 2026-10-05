# F62: StationState — struct содержит ссылочный массив → алиасинг при копировании

**Приоритет:** P2 (Medium)  
**Область:** Logic · NavigationPattern · Memory Model  
**Файл:** `Source/Logic/StationState.cs`, `Source/Logic/NavigationPattern.cs`

---

## Проблема

```csharp
public struct StationState
{
    public string Name;
    public NavigationPattern Pattern; // ← struct, OK
    ...
}

public struct NavigationPattern
{
    public Group[] Groups; // ← ссылочный массив внутри struct
    ...
}
```

Проблема: `NavigationPattern` — struct, но содержит `Group[] Groups` (массив — ссылочный тип). При копировании `StationState` копируется struct целиком, но `Groups` копирует **ссылку на массив**.

```csharp
StationState a = stations[i];
StationState b = a;
b.Pattern.Groups[0] = default; // ← мутирует a.Pattern.Groups[0]!
```

Где это может проявиться:
- `Array.Sort(stations, ...)` — перемещает элементы, не копируя массивы
- `GetStationByCallsign()` возвращает `StationState` → каллер может мутировать массив
- `RemoveStation()` копирует все элементы, но массивы остаются общими

## Исправление

Вариант А: Сделать `Groups` иммутабельным `IReadOnlyList<Group>` (минимальная вставка).
Вариант Б: Заменить на `ImmutableArray<Group>` (требует reference).

## Альтернатива

Убедиться, что существующий код никогда не мутирует `Groups` через копию, и задокументировать это ограничение.

1 час.
