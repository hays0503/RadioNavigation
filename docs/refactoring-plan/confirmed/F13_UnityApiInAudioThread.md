# F13: Unity Engine API из Audio Thread

**Приоритет:** P0 (Critical)
**Область:** Sound · AudioSynthesisEngine
**Тип:** Threading violation

---

## Проблема

`OnAudioFilterRead` вызывается Unity в выделенном audio thread. docs/DESIGN.md:120-121 прямо запрещает любые UnityEngine API в этом контексте. Нарушение может вызвать deadlock, краш или артефакты звука на любом билде.

---

## Evidence

### 1. Debug.Log() внутри ProcessAudio()

AudioSynthesisEngine.cs:96-98:

```csharp
stations[s].NavigationStartTime = Time.time;
Debug.Log("[RadioNav][NAV] PreNavPause->Navigation st=" + s +
    " NavStartTime=" + Time.time.ToString("F2"));
```

Вызов цепочки: `OnAudioFilterRead` [SonneController.cs:443] → `ProcessAudio` [AudioSynthesisEngine.cs:65] → строка 96-98.

`Debug.Log()` — UnityEngine API. В audio thread вызывает:

- Блокировку консоли Unity (может дедлокнуть main thread, если тот тоже пишет в лог)
- Аллокацию строки (GC压力 на audio thread)
- Нарушение контракта docs/DESIGN.md

### 2. Time.time внутри ProcessAudio()

Там же, строка 96:

```csharp
stations[s].NavigationStartTime = Time.time;
```

`Time.time` — UnityEngine API. В audio thread:

- Читает глобальное состояние Unity из не-main thread (UBER-запрещено)
- Unity может вернуть некорректное значение или упасть

### 3. Вдобавок: запись в mutable struct из audio thread

`stations[s].NavigationStartTime = Time.time` пишет поле `StationState.NavigationStartTime` из audio thread. `StationState` — `struct`. Через массив `stations[]` это мутация элемента. Main thread читает `NavigationStartTime` в `Update()` (SonneController.cs:274). Это дополнительная гонка к F01.

---

## Предлагаемое решение

1. Убрать `Debug.Log()` из ProcessAudio. Вынести в отдельный буфер, сбрасываемый из Update() при наличии флага.
2. Сохранять `NavigationStartTime` через отдельное поле в SonneController (обновлять на main thread при смене фазы).
3. Проверить docs/DESIGN.md compliance: ни одного UnityEngine API вызова в Sound/ директории.

---

## Acceptance Criteria

- [ ] `rg "Debug\.|UnityEngine|Time\.|Mathf\." Source/Sound/` не выдаёт совпадений
- [ ] Звук стабилен при длительной работе (нет дедлоков)
- [ ] NavigationStartTime корректно отражает время начала навигации
