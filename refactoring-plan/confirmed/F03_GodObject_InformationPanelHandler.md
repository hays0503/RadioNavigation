# F03: God-Object — InformationPanelHandler (474 LOC)

**Приоритет:** P1 (Major)
**Область:** UI · Panel Management
**Блокирует:** Любые изменения в UI панели информации, добавление новых виджетов, смена макета

---

## Проблема

InformationPanelHandler совмещает **9 ответственностей**: от создания UI-иерархии в коде до форматирования навигационных данных и подписки на события. Как и SonneController, любой change request требует правки одного гигантского файла.

---

## Evidence

### 9 ответственностей

| # | Ответственность | Строки | Описание |
|---|----------------|--------|----------|
| R1 | UI widget creation / layout | 37-80 (Start), 99-151 | `EnsureLayout`, `CreateLabel`, `CreateSeparator` — строит UI в коде |
| R2 | Handler discovery / event wiring | 153-218 | `FindHandlers` сканирует детей, `Subscribe`/`Unsubscribe` — 6 событий |
| R3 | UI state callbacks | 243-271 | `OnToggleChanged`, `OnGroundChanged`, `OnFrequencyChanged` — обновление текста |
| R4 | Frequency + station display | 273-290, 433-467 | `OnFrequencyChanged` обновляет частоту + `FindClosestStation` |
| R5 | Signal pattern display | 292-359 | `UpdatePatterns` — форматирование 6 строк диагностики |
| R6 | Position fix display | 361-386 | `UpdateFixDisplay` — чтение `SonneController.Fix*` свойств |
| R7 | Signal/decoded text display | 388-423 | `UpdateSignalDisplay` — форматирование morse сигнала |
| R8 | Timer-driven update loop | 82-97 (Update) | Два таймера: pattern (0.5s) + signal (0.15s) |
| R9 | UI state inspection (reflection-like) | 220-241 | `IsGroundActive` читает alpha канала Image, `GetZoneIndex` ищет активные Image |

### Coupling #1: Частотный event триггерит всё остальное

`OnFrequencyChanged` [L273] вызывает `UpdatePatterns()` [L289], который вызывает `UpdateFixDisplay()` [L358]:

```
OnFrequencyChanged (line 273)
  └─ UpdatePatterns() (line 292)              # R5
       └─ UpdateFixDisplay() (line 361)        # R6
            ├─ sonneController.HasFix
            ├─ sonneController.FixLatitude
            ├─ sonneController.FixLongitude
            └─ sonneController.FixErrorKm
```

Изменение частоты — это R4 (частота + станция). Но оно триггерит обновление паттернов (R5) и позиции (R6). Если нужно изменить цвет ошибки фикса (R6), приходится лезть в метод `UpdatePatterns`, где цвет выбирается в строках 346-349.

### Coupling #2: UI строится в коде, а не в префабах

Строки 37-80, 99-151 — вся UI-иерархия создаётся через `EnsureLayout`, `CreateLabel`, `CreateSeparator`:

```csharp
private static Text CreateLabel(Transform parent, string name, string text)
{
    var go = new GameObject(name);
    go.transform.SetParent(parent, false);
    var textComp = go.AddComponent<Text>();
    // ... шрифты, размеры, цвета, alignment, layout...
    return textComp;
}
```

11+ Text/Image GameObjects создаются в рантайме. Это означает:
- UI нельзя поправить без перекомпиляции
- Layout-баги требуют rebuild мода
- Нельзя использовать Unity Editor UI tools

### Coupling #3: Время тиков хардкожено в Update

Строки 32-35 + 82-97 — два таймера с хардкожеными интервалами:

```csharp
private const float PatternUpdateInterval = 0.5f;
private const float SignalUpdateInterval = 0.15f;
```

Эти интервалы — магические числа, которые влияют на производительность. Если кто-то захочет их менять, придётся перекомпилировать мод.

---

## Предлагаемое решение

| Новый класс | Ответственность |
|-------------|-----------------|
| `InformationPanelBuilder` | Программное создание UI (один раз в Start) |
| `FrequencyDisplayViewModel` | Данные частоты → текст |
| `PatternDisplayViewModel` | Данные паттернов → текст |
| `FixDisplayViewModel` | Данные фикса → текст (цвет, пороги) |
| `SignalDisplayViewModel` | Данные сигнала → текст |

InformationPanelHandler остаётся как координатор: `Update()` → обновление ViewModel → рендер.

---

## Acceptance Criteria

- [ ] InformationPanelHandler < 100 LOC
- [ ] ViewModel-классы тестируемы без Unity
- [ ] UI-создание вынесено из handler-а
