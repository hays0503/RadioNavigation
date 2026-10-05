# F08: GC Pressure — Heap-Аллокации в Per-Frame Hot Paths

**Приоритет:** HIGH
**Область:** Sound + Logic + UI
**Тип:** Performance

---

## Проблема

Восемь мест в коде аллоцируют управляемую память (`new List<>`, `new StringBuilder`, `str +=`, `Debug.Log`) внутри Update-циклов и audio callback-а. Это создаёт GC pressure: сборщик мусора активируется чаще, вызывая микро-фризы (особенно критично на аудио-треде).

---

## Evidence

### 1. `SonneSignalGenerator.GenerateConsolPattern()` — аллокация каждые 0.2с × 14 станций

`SonneSignalGenerator.cs:86, 115`:

```csharp
var elements = new List<NavigationElementType>();    // ← аллокация
// ... заполнение ...
return new NavigationPattern(elements.ToArray());    // ← ещё одна
```

Вызывается из `SonneController.UpdateStationNavigation()` (строка 420) каждые 0.2с, в цикле по всем 14 станциям.

**Итого:** 14 List + 14 ToArray = **28 аллокаций в секунду** (на самом деле ~28/0.2 = 140/сек, но точнее ~28 × 5 = 140/сек).

---

### 2. `PositionFixCalculator.Calculate()` — аллокация каждые 0.2с

`PositionFixCalculator.cs:30, 67`:

```csharp
var rawIntersections = new List<GeoPoint>();           // ← аллокация
var weightedIntersections = new List<WeightedPoint>(); // ← ещё одна
```

Вызывается из `SonneController.UpdateStationNavigation()` (строка 438) каждые 0.2с. Внутри циклов — `.Add()` с ростом внутреннего массива (re-allocation).

**Итого:** 10 аллокаций/сек (2 × 5), плюс внутренние re-allocations при `.Add()`.

---

### 3. `SignalRichTextFormatter.FormatRichText()` — `new StringBuilder` каждые 0.15с

`SignalRichTextFormatter.cs:14`:

```csharp
var sb = new StringBuilder(entries.Count * 20);       // ← аллокация
```

Вызывается из `InformationPanelHandler.UpdateSignalDisplay()` (строка 402) via `SonneController.GetSignalRichText()` каждые 150мс, даже когда сигнал не изменился.

**Итого:** ~6.7 аллокаций/сек.

---

### 4. `Debug.Log` на аудио-треде — строковая аллокация

`AudioSynthesisEngine.cs:97-98`:

```csharp
Debug.Log("[RadioNav][NAV] PreNavPause->Navigation st=" + s +
    " NavStartTime=" + Time.time.ToString("F2"));     // ← 3+ строковых аллокаций
```

Внутри `ProcessAudio()`, вызываемой из `OnAudioFilterRead()` — **на аудио-треде**. Срабатывает при каждом переходе фазы навигации. `Debug.Log` аллоцирует строку + отправляет её через внутреннюю очередь Unity — GC на real-time thread.

---

### 5. `Debug.Log` в `Update` — каждые 0.15с, не под `#if DEBUG`

`InformationPanelHandler.cs:419-423`:

```csharp
Debug.Log("[RadioNav][PANEL] decoded=[" + decoded + "]");  // ← не под #if DEBUG
```

В `UpdateSignalDisplay()`, вызываемом из `Update()` каждые 150мс. Работает **даже в Release** — строчка не обёрнута в `#if DEBUG`. Аллоцирует строку + конкатенацию.

---

### 6. `RemoveAt(0)` в while-loop — CPU spike на аудио-треде

`SignalRichTextFormatter.cs:69-78`:

```csharp
while ((morseCharCount > MaxMorseChars || entries.Count > MaxSignalEntries)
    && entries.Count.IsPositive())
{
    entries.RemoveAt(0);    // ← O(n): сдвиг всех элементов
}
```

Вызывается из `SonneController.OnStationElementCompleted()` (строка 146), которая триггерится событием от `MorseKeyer` — в итоге на аудио-треде или сразу после него. При 60 элементах RemoveAt(0) копирует ~1830 элементов за burst. Не heap-аллокация, но CPU spike на real-time треде.

---

### 7. String concatenation в `UpdatePatterns()` — каждые 0.5с

`InformationPanelHandler.cs:338-342`:

```csharp
string line = callsign + " [" + beam + "] " + gain + " BFO:" + bfo + " " + phase;
if (diag.NavigationElapsed.IsPositive())
    line += " " + FormatTime(diag.NavigationElapsed);       // ← временная строка
if (diag.InBeam)
    line += "\n  Dots:" + diag.DotCount + " Bearing:" ...;  // ← временная строка
```

В цикле по станциям (до 6), внутри `UpdatePatterns()`, вызываемого из `Update()` каждые 0.5с.

---

### 8. `new StringBuilder` каждые 0.5с в `SignalRichTextFormatter.cs`

Повторение пункта 3, но также `sb.ToString()` на строках 37-43 создаёт финальную строку:

```csharp
return sb.ToString();  // ← 1 строка каждые 0.15с
```

---

### Сводка GC pressure

| # | Источник | Аллокаций/сек | Размер за раз |
|---|----------|--------------|---------------|
| 1 | `GenerateConsolPattern` List + ToArray | ~140 | ~200-800 bytes |
| 2 | `PositionFixCalculator` 2× List | ~10 | ~100-400 bytes |
| 3 | `FormatRichText` StringBuilder | ~6.7 | ~256+ bytes |
| 4 | `Debug.Log` на audio thread | per event | ~150+ bytes |
| 5 | `Debug.Log` в Update | ~6.7 | ~100+ bytes |
| 7 | string concat в UpdatePatterns | ~6-12 | ~100-200 bytes |
| 8 | `sb.ToString()` | ~6.7 | ~100-500 bytes |
| **Total** | | **~180-200 alloc/sec** | |

**Каждые 5 секунд — мегабайт аллокаций.** При стандартном GC-режиме Unity (incremental) это означает микро-фризы каждые несколько секунд.

---

## Предлагаемое решение

1. **`GenerateConsolPattern`** — переиспользовать `List<NavigationElementType>` как static pool или заменить на `ArrayPool`
2. **`PositionFixCalculator.Calculate`** — передавать List как ref параметр (caller allocates)
3. **`FormatRichText`** — кэшировать `StringBuilder` в статическом/экземплярном поле, `Clear()` вместо `new`
4. **`Debug.Log` в audio callback** — убрать или переписать через `#if DEBUG`
5. **`Debug.Log` в Update** — обернуть в `#if DEBUG`
6. **`RemoveAt(0)`** — заменить на `Queue<SignalEntry>` (см. F06)
7. **string concat** — `StringBuilder` в `UpdatePatterns`

### 9. `foreach (Transform child in t)` — boxing allocation

`Main.cs:103`:

```csharp
foreach (Transform child in t)
```

В старых версиях Unity (до 2020.x) `Transform.GetEnumerator()` возвращает нетипизированный `IEnumerator`, что вызывает boxing на каждой итерации foreach. UBOAT использует Unity 2020+ (судя по модульной структуре), но версия может варьироваться. Boxing = hidden allocation ≈ 24 bytes per frame.

**Фикс:** Заменить на `for (int i = 0; i < t.childCount; i++)`.

---

## Acceptance Criteria

- [ ] `GenerateConsolPattern` не аллоцирует на каждый вызов (pool или pre-alloc)
- [ ] `PositionFixCalculator.Calculate` не создаёт List-ы (receive from caller)
- [ ] `FormatRichText` переиспользует StringBuilder
- [ ] Нет `Debug.Log` на аудио-треде в Release
- [ ] `SignalRichTextFormatter.TrimEntries` не дёргает RemoveAt(0)
