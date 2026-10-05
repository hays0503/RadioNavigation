# F132. AudioSynthesisEngine.ProcessAudio — keyer/scheduler работают при выключенном радио

| Поле | Значение |
|------|----------|
| **Приоритет** | P1 (High) |
| **Область** | Sound · AudioSynthesisEngine · RadioOff |
| **Файл** | `Source/Sound/AudioSynthesisEngine.cs` |
| **Строки** | 73-128 |

---

## Описание

Проверка `radioOn` (строка 128) расположена **после** цикла тиков (строки 73-126):

```csharp
// AudioSynthesisEngine.cs:73-128
for (int i = 0; i < data.Length; i += channels)
{
    tickAccumulator += ticksPerSample;
    while (tickAccumulator >= 1f)
    {
        // ... keyer/scheduler advances (строки 78-124) ...
        // keyers[s].Advance(), sched.Tick(), OnElementCompleted, OnSequenceFinished
        tickAccumulator -= 1f;
    }

    if (!radioOn)        // <-- проверка ПОСЛЕ цикла тиков
        continue;        //     аудио пропускается, но тики уже прошли

    // ... audio sample generation ...
}
```

Когда радио выключено (`radioOn == false`):
- `keyers[s].Advance()` продолжает продвигать state machine
- `sched.Tick()` переводит scheduler phases (Pause→Callsign, PreNavPause→Navigation и т.д.)
- `OnElementCompleted` / `OnSequenceFinished` events срабатывают
- `stations[s].NavigationStartTime = Time.time` присваивается (строка 96)

**Результат:** при включении радио игрок слышит середину цикла — equisignal, навигацию или паузу — вместо начала с callsign. Реальный приёмник не декодирует сигнал, который не принимал.

## Корневая причина

Цикл тиков (keyer/scheduler) и генерация аудио — два отдельных блока, но `radioOn` проверяется только для второго.

## Предлагаемое исправление

**Вариант 1 (полная остановка):** Вынести проверку `radioOn` перед циклом тиков:

```csharp
if (!radioOn)
{
    Array.Clear(data, 0, data.Length);
    return;
}
```

**Вариант 2 (парциальная остановка):** Пропускать advance keyers/schedulers при `radioOn == false`, но оставить тики:

```csharp
while (tickAccumulator >= 1f)
{
    for (int s = 0; s < stations.Length; s++)
    {
        if (radioOn) // <-- только при включённом радио
        {
            // ... keyer/scheduler advances ...
        }
    }
    tickAccumulator -= 1f;
}
```

## Зависимости

- Связан с F98 (scheduler double tick) — оба в ProcessAudio, разные аспекты timing
- Связан с F89 (OnDestroy no audio stop) — оба о radio state management
- Связан с F01 (audio race condition) — оба в audio thread
