# F12: Copy-Paste Дублирование Кода и Констант

**Приоритет:** MEDIUM
**Область:** Codebase · Hygiene
**Тип:** Maintainability

---

## Проблема

Код содержит множественные копипасты: от идентичных методов до дублированных констант. Каждая копипаста — это два места, которые нужно править при изменении. Рано или поздно они разойдутся.

---

## Evidence

### 1. `ToRadians(float)` — идентичный метод в 2 файлах

| Файл | Строка | Код |
|------|--------|-----|
| `SonneController.cs` | 503-506 | `private static double ToRadians(float degrees) { return degrees * Math.PI / 180.0; }` |
| `PositionFixCalculator.cs` | 219-222 | `private static double ToRadians(float degrees) { return degrees * Math.PI / 180.0; }` |

Идентичный метод. Должен быть вынесен в `MathHelper` или extension.

---

### 2. `BfoBase = 1000f` — константа определена независимо в 2 файлах

| Файл | Строка | Значение |
|------|--------|----------|
| `SonneController.cs` | 12 | `private const float BfoBase = 1000f;` |
| `AudioSynthesisEngine.cs` | 9 | `private const float BfoBase = 1000f;` |

Обе приватные, обе с одинаковым именем и значением. Если поменять в одном месте, второе останется старым. В одном файле используется как BFO, в другом — как kHz-to-Hz коэффициент.

---

### 3. `MorseKeyer.GetCurrentSoundTicks()` и `GetCurrentSilenceTicks()` — одинаковый isDash логика

`MorseKeyer.cs:214-224, 226-236`:

Обе начинаются с одного и того же:

```csharp
bool isDash = false;
if (consolPattern.IsNotNull() && elementIndex < consolPattern.Length)
    isDash = consolPattern[elementIndex] == NavigationElementType.Dash;
else if (sequence.IsNotNull() && elementIndex < sequence.ElementCount)
    isDash = sequence.GetElement(elementIndex);
```

Только финальный return разный: одна вызывает `GetDotSoundTicks`/`GetDashSoundTicks`, другая — `GetDotSilenceTicks`/`GetDashSilenceTicks`.

---

### 4. `ZoneHandler.NextZone()` / `PreviousZone()` — почти идентичны

`ZoneHandler.cs:84-110`:

Структура обоих:
1. `CanSwitchZone()` guard
2. `SetAlpha(currentIndex, hiddenAlpha)`
3. Инкремент/декремент `currentIndex` с wrap
4. `SetAlpha(currentIndex, visibleAlpha)`
5. Fire `OnZoneChanged`

Отличаются только шагом (+1 vs -1). Можно объединить в `SwitchZone(int direction)`.

---

### 5. `RadioInstaller.cs` — 10 hardcoded string path вместо `PrefabNames`

`RadioInstaller.cs:24-65` — все строки захардкожены:

| Строка | Значение | PrefabNames константа |
|--------|----------|----------------------|
| 24 | `"CursorSpinner"` | `PrefabNames.CursorSpinner` |
| 26 | `"Cursor"` | `PrefabNames.Cursor` |
| 32 | `"Toggle"` | `PrefabNames.Toggle` |
| 36 | `"Ground"` | `PrefabNames.Ground` |
| 37 | `"Background"` | `PrefabNames.Background` |
| 38 | `"GroundCheckmark"` | `PrefabNames.GroundCheckmark` |
| 53 | `"Expander"` | `PrefabNames.Expander` |
| 57 | `"RadioZone"` | `PrefabNames.RadioZone` |
| 61 | `"VolumeSpinner"` | `PrefabNames.VolumeSpinner` |
| 65 | `"FeedbackSpinner"` | `PrefabNames.FeedbackSpinner` |

`PrefabNames.cs` определяет все 10 констант, но `RadioInstaller` их не использует.

---

### 6. `"ciferbllat"` — одна и та же опечатка в 2 местах

| Файл | Строка | Значение |
|------|--------|----------|
| `ToggleHandler.cs` | 82 | `transform.parent.Find("ciferbllat")` |
| `PrefabNames.cs` | 16 | `public const string DialFace = "ciferbllat";` |

`ciferbllat` — опечатка (правильно: `циферблат` → `ciferblat`). Закреплена в двух файлах. Если когда-нибудь исправлять в префабе, нужно менять в обоих.

---

### 7. `InformationPanelHandler.Subscribe()` / `Unsubscribe()` — зеркальные

`InformationPanelHandler.cs:172-186, 189-202`:

```csharp
private void Subscribe()
{
    toggleHandler.OnValueChanged += OnToggleChanged;
    groundHandler.OnValueChanged += OnGroundChanged;
    // ... 5 одинаковых подписок
}
private void Unsubscribe()
{
    toggleHandler.OnValueChanged -= OnToggleChanged;
    groundHandler.OnValueChanged -= OnGroundChanged;
    // ... те же 5, но с -=
}
```

Boilerplate × 5. При добавлении нового handler-а нужно не забыть добавить в оба метода.

**Фикс:** Использовать `IDisposable` pattern или список `(event, handler)` пар.

---

### 8. `MorseTiming.Get*Ticks` — 4 одинаковые структуры

`MorseTiming.cs` — все 4 метода (`GetDotSoundTicks`, `GetDashSoundTicks`, `GetDotSilenceTicks`, `GetDashSilenceTicks`) следуют:

```csharp
float ms = config.XXX.IsPositive() ? config.XXX : DEFAULT;
return Math.Max(1, (int)(ms / TickMs));
```

Дублируются значения `125f`, `875f`, `1000f` как fallback дефолты, когда `ConsolCycleConfig.Cycle120s` уже определяет те же числа.

---

### 9. `PositionFixCalculator` — два вложенных цикла с одинаковой структурой

`PositionFixCalculator.cs:32-44` (raw) и `69-89` (weighted):

```csharp
for (int i = 0; i < measurements.Count; i++)
{
    for (int j = i + 1; j < measurements.Count; j++)
    {
        var intersection = FindIntersection(
            measurements[i].Latitude, measurements[i].Longitude, measurements[i].Bearing,
            measurements[j].Latitude, measurements[j].Longitude, measurements[j].Bearing);
        if (intersection.Valid)
            rawList.Add(new GeoPoint { ... });
    }
}
```

Оба цикла итерируют пары точек и вызывают `FindIntersection`. Отличаются только типом результата (GeoPoint vs WeightedPoint) и тем, добавляют ли Weight.

---

### 10. `SectorWidthDeg = 5f` / `TotalSignals = 60` — в двух местах

| Файл | Строка | Значение |
|------|--------|----------|
| `SonneSignalGenerator.cs` | 8-9 | `public const float SectorWidthDeg = 5f; public const int TotalSignals = 60;` |
| `ConsolCycleConfig.cs` | 30-33 | `MaxNavigationCount = 60; SectorWidthDeg = 5f; TotalSignals = 60;` |

Вынесены в public const в одном файле, но `ConsolCycleConfig` не ссылается на них — дублирует значения.

---

### 11. `360f` — 8+ вхождений без именованной константы

| Файл | Строка | Контекст |
|------|--------|----------|
| `SonneController.cs` | 411 | `(bearing + 360f) % 360f` |
| `SonneController.cs` | 492 | `(bearing + 360f) % 360f` |
| `SonneController.cs` | 497 | `angle = angle % 360f` |
| `SonneController.cs` | 498 | `angle += 360f` |
| `SonneController.cs` | 499 | `angle -= 360f` |
| `BeaconData.cs` | 38 | `(bearing + 360f) % 360f` |
| `DebugAngleHandler.cs` | 21 | `Mathf.Clamp(value, 0f, 360f)` |

Нигде нет `private const float FullCircleDeg = 360f` или подобного.

---

## Acceptance Criteria

- [ ] `ToRadians` определён ровно один раз
- [ ] `BfoBase` определён ровно один раз
- [ ] `RadioInstaller` использует `PrefabNames` для всех Find-запросов
- [ ] `MorseKeyer` не имеет дублированной isDash-логики
- [ ] `ZoneHandler` имеет один метод `SwitchZone(int direction)`
- [ ] `Subscribe`/`Unsubscribe` не дублируют список handler-ов вручную
- [ ] `360f` заменена на именованную константу
- [ ] `SectorWidthDeg`/`TotalSignals` определены в одном месте
