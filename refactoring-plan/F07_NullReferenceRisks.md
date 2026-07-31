# F07: NullReferenceException Риски — 4 подтверждённых NRE-бага

**Приоритет:** CRITICAL
**Область:** UI + Logic
**Тип:** Баги (реальные падения)

---

## Проблема

Четыре места в коде гарантированно выбрасывают NullReferenceException при определённых условиях. Три из них — на пользовательском вводе (скролл, клик), одно — в audio callback.

---

## Evidence

### 1. RadioHandler.Instance разыменован до null-проверки

`CursorHandler.cs:108-114`:

```csharp
float newAngle = Mathf.Clamp(
    RadioHandler.Instance.CursorAngle + delta * step,   // ← NRE если Instance == null
    MinAngle, MaxAngle);

cursorRect.localRotation = Quaternion.Euler(0, 0, -newAngle);

if (RadioHandler.Instance.IsNotNull())                    // ← null check ПОСЛЕ доступа
    RadioHandler.Instance.CursorAngle = newAngle;
```

`RadioHandler.Instance` сеттится в `Awake()` (строка 32) и обнуляется в `OnDestroy()` (строка 71). Если `RadioHandler` уничтожен (scene transition, релоад мода), `OnScroll` упадёт. В `OnDrag` (строка 75) этот же доступ правильно загардован.

**Воспроизведение:** Уничтожить RadioHandler (через DestroyImmediate в редакторе) и прокрутить колесо мыши над курсором.

---

### 2. Missing return после transform.parent == null

`ToggleHandler.cs:77-82`:

```csharp
if (transform.parent.IsNull())
{
    Debug.LogError("[RadioNav][ToggleHandler] transform.parent is null");
    return;   // ← ОТСУТСТВУЕТ! Код падает на следующей строке
}
Transform dial = transform.parent.Find("ciferbllat");  // ← NRE: parent is null
```

Визуально `return` есть, но он находится в блоке `if`. В текущем коде `return` отсутствует — управление передаётся на строку 82, где `transform.parent` — null. Ошибка компилятора? Нет, потому что `IsNull()` — extension method, и C# не знает, что после неё `transform.parent` гарантированно null.

---

### 3. MorseKeyer infinite loop при scheduler == null

`MorseKeyer.cs:168-172, 193-197`:

```csharp
// sequence path (строка 193-197):
else
{
    elementIndex = 0;         // сброс в начало
}
// → StartElement();         // рестарт навсегда
```

При отсутствии `scheduler` (создание MorseKeyer без него), после завершения sequence `elementIndex` сбрасывается на 0 и `StartElement()` рестартует. `OnSequenceFinished` срабатывает каждый цикл, `IsActive` никогда не становится `false`. Аудио-треугольник бесконечно крутится в `ProcessAudio`:

```csharp
while (tickAccumulator >= 1f) { for (int s... keyers[s].Advance(); } // ∞
```

---

### 4. MorseKeyer.StartElement() игнорирует sequence

`MorseKeyer.cs:119-134`:

```csharp
private void StartElement()
{
    if (elementIndex < consolPattern?.Length)       // ТОЛЬКО consolPattern!
    {
        var elem = consolPattern[elementIndex];
        if (elem == NavigationElementType.Equisignal) { ... return; }
    }
    state = State.ElementOn;
    ticksInState = 0;
}
```

Когда keyer загружен через `Load(keyer, sequence, ...)` (без consolPattern), `StartElement` не проверяет `sequence`. Если оба `null`, keyer входит в `ElementOn` без элементов — молча жрёт CPU, циклируя `Advance → StartElementSilence → AdvanceElement → StartElement` на каждом тике.

---

### 5. History без Length check

`InformationPanelHandler.cs:446`:

```csharp
float? stationFreq = stations[i].History?[0].FrequencyKHz;
```

Null-conditional `?.` работает только для null, НЕ для пустого массива. Если `History = new BeaconHistoryEntry[0]`, то `[0]` кидает `IndexOutOfRangeException`. Тот же паттерн в `BeaconData.cs:38`:

```csharp
(Bearing = (float)stations[i].History?[0].Bearing)
```

Сравните с защищённым вариантом в `SonneController.cs:470-472`:

```csharp
station.History.IsNotNull()
    && station.History.Length.IsPositive()    // ← правильно
    && !string.IsNullOrEmpty(station.History[0].Callsign)
```

---

## Acceptance Criteria

- [ ] `RadioHandler.Instance` никогда не разыменовывается без null guard
- [ ] `ToggleHandler.Start()` имеет `return` после `parent == null`
- [ ] `MorseKeyer` корректно завершается при `scheduler == null`
- [ ] `StartElement` проверяет и consolPattern, и sequence
- [ ] Все обращения к `History[0]` защищены проверкой длины
