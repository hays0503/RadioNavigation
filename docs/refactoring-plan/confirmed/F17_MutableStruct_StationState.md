# F17: StationState — mutable struct

**Приоритет:** P2 (Medium)
**Область:** Data · StationState
**Тип:** Correctness · Fragile pattern

---

## Проблема

`StationState` — struct с 10 settable полями. При копировании через `var s = stations[i]` создаётся независимая копия. Если не записать её обратно, мутации теряются. Хрупкий паттерн.

---

## Evidence

StationState.cs:3-15:

```csharp
public struct StationState
{
    public bool InBeam;
    public int TransitionIndex;
    public float Gain;
    public float BeatFrequency;
    public int DotCount;
    public int CurrentBearing;
    public int CallsignLength;
    public float NavigationStartTime;
    public ConsolCycleConfig Config;
    public NavigationPattern Pattern;
}
```

Все 10 полей — публичные settable. Структура хранится в массиве `stations[]` (SonneController.cs:76).

### Где копируется без записи обратно

SonneController.cs:268:

```csharp
var s = stations[index];    // копия
// ... только чтение полей
```

В текущем коде это безопасно — копия только для чтения. Но:

1. AudioSynthesisEngine.cs:96 пишет `stations[s].NavigationStartTime = Time.time` напрямую через индексатор
2. PositionFixCalculator.cs:61-64 явно пишет обратно (`measurements[i] = m`) — правильный паттерн
3. Если кто-то добавит `var s = stations[i]; s.Gain = 5f;` без `stations[i] = s` — баг

### Почему это опасно

- `struct` передаётся по значению. Любая копия — независимый объект
- Нет readonly полей, нет инкапсуляции
- При добавлении нового кода легко забыть про write-back

---

## Предлагаемое решение

1. Заменить `StationState` на `class` (reference type) — мутации по ссылке
2. Или сделать readonly struct + builder pattern
3. Или оставить как есть, но добавить `[Obsolete]` на settable поля

---

## Acceptance Criteria

- [ ] StationState не может быть случайно скопирован без намерения
- [ ] Все мутации гарантированно применяются к исходному экземпляру
