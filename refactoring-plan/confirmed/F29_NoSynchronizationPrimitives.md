# F29: Нет примитивов синхронизации в многопоточном проекте

**Приоритет:** P0 (Critical)
**Область:** Sound · Threading
**Тип:** Thread Safety · Memory Model

---

## Проблема

В проекте **ноль примитивов синхронизации** — ни одного `lock`, `volatile`, `Monitor`, `Interlocked`, `MemoryBarrier`. При этом audio thread (ProcessAudio) и main thread (SonneController, UI handlers) читают/пишут одни и те же поля без барьеров памяти.

---

## Evidence

### Grep результатов: 0 вхождений

| Примитив | Использований |
|----------|:------:|
| `lock` | 0 |
| `volatile` | 0 |
| `Monitor` | 0 |
| `Interlocked` | 0 |
| `Thread.MemoryBarrier` | 0 |
| `Mutex` / `Semaphore` | 0 |

### Shared state без синхронизации

Audio thread (Read) и Main thread (Write) обращаются к:

| Поле | Audio thread | Main thread |
|------|-------------|-------------|
| `stations[s].NavigationStartTime` | Write (AES:96) | Read (SC:274,460) |
| `stations[s].Gain` | Read (AES:56,80,134) | Write (SC:342,357,366) |
| `stations[s].BeatFrequency` | Read (AES:59,60) | Write (SC:369) |
| `stations[s].InBeam` | Read | Write (SC:409) |
| `schedulers[s].CurrentPhase` | Read (AES:92) | Write (SC via Tick) |

Без memory barrier JIT может:
1. Переупорядочить чтения/записи
2. Кэшировать значения в регистрах процессора
3. Не видеть записи из другого потока

---

## Предлагаемое решение

1. Добавить `lock(_syncRoot)` вокруг доступа к shared state в audio thread
2. Или перейти на double-buffer: snapshots читаются audio thread, мутации пишут во второй buffer с `Interlocked.Exchange`
3. Или вынести все мутации из audio thread (F01)

---

## Acceptance Criteria

- [ ] Все обращения к StationState из audio thread защищены барьером памяти
- [ ] Нет race condition при одновременном чтении/записи
- [ ] Нет performance regression (lock-free где возможно)
