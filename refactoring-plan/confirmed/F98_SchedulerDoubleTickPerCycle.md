# F98. Scheduler ticked 2-3x per audio cycle — pause durations halved/thirded

| Поле | Значение |
|------|----------|
| **Приоритет** | P1 (High) |
| **Область** | Sound · Logic · Timing |
| **Файлы** | `Source/Sound/AudioSynthesisEngine.cs` (87-114), `Source/Logic/MorseKeyer.cs` (78-84) |

---

## Описание

Внутри одной логической тики (одна итерация цикла `while (tickAccumulator >= 1f)`) планировщик станции (`StationScheduler`) может быть тикнут до 3 раз:

1. **Строка 92** `AudioSynthesisEngine`: явный тик для фаз `Pause`/`PreNavPause`/`PostNavSilence`
2. **Строка 114** `AudioSynthesisEngine`: fallback-тик когда consol pattern или sequence недоступны
3. **Строка 83** `MorseKeyer.Advance`: `scheduler.Tick()` когда keyer неактивен

**Пример:** Планировщик в фазе `PostNavSilence` с `pauseTicksRemaining=1`:
- Тик на строке 92: переход в `Pause` (`pauseTicksRemaining=1`)
- Тик на строке 114: переход в `Callsign`
- Тик в `keyer.Advance` (line 83): ничего не делает (Callsign не обрабатывается в Tick)

**Результат:** 2 тика за один цикл вместо 1 → пауза между циклами в 2 раза короче задуманной.

Тройной тик происходит когда оба тика в audio engine срабатывают И keyer неактивен.

## Корневая причина

Два отдельных места в `ProcessAudio` тикают планировщик без координации. Плюс `MorseKeyer.Advance` тикает планировщик при неактивном keyer.

## Предлагаемое исправление

Убрать дублирующий тик на строке 114. Оставить только:
- Тик на строке 92 (для pause фаз)
- Тик в `MorseKeyer.Advance` (для inactive keyer)
- Не тикать планировщик дважды в одном цикле

Либо ввести флаг `hasTickedThisCycle` чтобы гарантировать один тик за итерацию.

## Зависимости

- Связан с F01 (audio race condition) — оба в audio thread
