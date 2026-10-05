# F65: MorseKeyer.Load() не очищает stale consolPattern

**Приоритет:** P0 (Critical)  
**Область:** Logic · MorseKeyer  
**Файл:** `Source/Logic/MorseKeyer.cs:31-44, 203-206`

---

## Проблема

`Load(IKeySequence)` (строка 31-44) устанавливает `this.sequence = sequence`, но **не обнуляет** `consolPattern`.

```csharp
public void Load(IKeySequence sequence, ...)
{
    this.sequence = sequence;
    // ← consolPattern не тронут!
    Reset();
}
```

`LoadConsol(NavigationPattern)` (строка 51-65) делает правильно — устанавливает `sequence = null`.

`GetCurrentElementType()` (строка 203-206) проверяет `consolPattern` **до** `sequence`:

```csharp
public NavigationElementType GetCurrentElementType()
{
    if (consolPattern.IsNotNull() && elementIndex < consolPattern.Length)
        return consolPattern[elementIndex];
    if (sequence.IsNotNull() && elementIndex < sequence.Length)
        return sequence.GetElementType(elementIndex);
    return NavigationElementType.Undefined;
}
```

**Сценарий:** После `LoadConsol(pattern1)` → `Load(pattern2)`, stale `consolPattern` всё ещё не null → возвращается элемент из `pattern1`, а не из `pattern2`.

## Воспроизведение

```csharp
keyer.LoadConsol(consolPattern); // consolPattern = не null, sequence = null
keyer.Load(sequence);            // sequence = new, но consolPattern = не null (STALE)
var type = keyer.GetCurrentElementType();
// Ожидается: тип из sequence
// Реальность: тип из consolPattern
```

## Исправление

Добавить `consolPattern = null` в `Load(IKeySequence)`:

```csharp
public void Load(IKeySequence sequence, ...)
{
    this.sequence = sequence;
    this.consolPattern = null;  // ← добавить
    Reset();
}
```

## Риски

- Все caller-ы `Load()` ожидают, что загружен `sequence`, а не `consolPattern`
- Фикс однострочный, без побочных эффектов
