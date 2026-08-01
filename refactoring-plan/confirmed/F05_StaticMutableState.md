# F05: Static Mutable State — Неявная связанность через ToggleHandler

**Приоритет:** P1 (Major)
**Область:** UI · Cross-Cutting
**Блокирует:** Тестирование, изоляцию компонентов, создание второго экземпляра UI

---

## Проблема

Три статических поля в `ToggleHandler` формируют невидимый граф зависимостей между 5 файлами. Любой `ToggleHandler` в сцене меняет глобальное состояние для всех. Нельзя запустить два экземпляра UI для теста.

Дополнительно: `IsRadioOn` читается из audio thread без `volatile` — потенциальный data race (см. F01).

---

## Evidence

### Декларации

`ToggleHandler.cs`:

```csharp
public static bool IsRadioOn { get; private set; }      // строка 13
public static bool IsInteractable { get; private set; }  // строка 14
private static bool groundActive;                         // строка 15
```

### Два места мутации

1. `ToggleHandler.OnToggleChanged()` [L119-122] — вызывается при переключении toggle:
```csharp
IsRadioOn = isOn;
IsInteractable = isOn && groundActive;
```

2. `ToggleHandler.SetGroundActive()` [L17-21] — вызывается из GroundHandler:
```csharp
groundActive = active;
IsInteractable = IsRadioOn && active;
```

### Восемь мест чтения в пяти файлах

| Файл | Строка | Код | Контекст |
|------|--------|-----|----------|
| `SpinnerHandlerBase.cs` | 67 | `if (!ToggleHandler.IsInteractable) return;` | OnBeginDrag — блок драга |
| `SpinnerHandlerBase.cs` | 84 | `if (!ToggleHandler.IsInteractable) return;` | OnDrag — блок драга |
| `CursorHandler.cs` | 50 | `if (!ToggleHandler.IsInteractable) return;` | OnBeginDrag — блок драга курсора |
| `CursorHandler.cs` | 67 | `if (!ToggleHandler.IsInteractable) return;` | OnDrag — блок драга курсора |
| `CursorHandler.cs` | 98 | `if (!ToggleHandler.IsInteractable) return;` | OnScroll — блок скролла |
| `CursorHandler.cs` | 122 | `if (!ToggleHandler.IsInteractable) return;` | Update — блок hover-скролла |
| `ZoneHandler.cs` | 114 | `return ToggleHandler.IsInteractable;` | CanSwitchZone — гард смены зоны |
| `SonneController.cs` | 445 | `audioEngine.ProcessAudio(..., ToggleHandler.IsRadioOn, ...)` | OnAudioFilterRead — без volatile |

### Граф зависимостей

```
ToggleHandler.OnToggleChanged()
  ├── mutates IsRadioOn (static) ─────────────→ SonneController.OnAudioFilterRead [445]
  └── mutates IsInteractable (static) ──────┐
                                            │
ToggleHandler.SetGroundActive()             │
  ├── mutates groundActive (static) ────────┤
  └── mutates IsInteractable (static) ──────┤
                                            │
GroundHandler.Start() [96]                  │
GroundHandler.OnPointerClick [133]          │
  └── calls SetGroundActive() ──────────────┘
                                            │
                                            ▼
                                    IsInteractable (static)
                                            │
                          ┌─────────────────┼────────────────┐
                          ▼                 ▼                ▼
                   SpinnerBase         CursorHandler    ZoneHandler
                   OnBeginDrag [67]    OnBeginDrag [50] CanSwitch [114]
                   OnDrag [84]         OnDrag [67]
                                       OnScroll [98]
                                       Update [122]
```

### Проблема #1: Нет изоляции экземпляров

Если в сцене два `ToggleHandler` (даже временно, при релоаде UI), оба пишут в одни и те же `static` поля. Последний вызванный `OnToggleChanged` побеждает. Остальные компоненты видят «случайное» состояние.

### Проблема #2: `IsInteractable` — формула в двух местах

Формула `IsRadioOn && groundActive` повторена на строках 20 и 122. Если формула изменится в одном месте, но не в другом — поведение станет inconsistent.

### Проблема #3: Data race (F01 пересечение)

`ToggleHandler.IsRadioOn` читается в `SonneController.OnAudioFilterRead` [445] из audio thread. Поле не `volatile`, нет барьера памяти. Main thread пишет его в `OnToggleChanged` [121]. Это недокументированная гонка.

---

## Предлагаемое решение

Создать `RadioState` — централизованный контейнер состояния:

```csharp
public class RadioState
{
    public bool IsRadioOn { get; set; }
    public bool IsInteractable { get; set; }
    public bool GroundActive { get; set; }
    public event Action<bool> OnRadioStateChanged;
}
```

- `ToggleHandler` пишет в `RadioState`
- Все потребители читают из `RadioState`
- В audio thread — использовать `volatile` на флаге `IsRadioOn` или отдельный `AudioState`

---

## Acceptance Criteria

- [ ] Нет статических mutable-полей с состоянием в UI-компонентах
- [ ] Все флаги проходят через `RadioState`
- [ ] `IsRadioOn` читается из audio thread через volatile
