# F111. MorseKeyer — IsActive property lacks volatile, read from main and audio threads

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Sound · Threading · Memory Visibility |
| **Файл** | `Source/Logic/MorseKeyer.cs` |
| **Строки** | 24 |

---

## Описание

`IsActive` — auto-property с `{ get; private set; }`. Записывается из audio thread (через `Load`, `LoadConsol`, `AdvanceElement`) и читается из main thread (через `SonneController.IsKeyerAvailableForPatternUpdate`, строка 464-466):

```csharp
// MorseKeyer.cs, строка 24:
public bool IsActive { get; private set; }

// SonneController.cs, строка 464-466 (main thread):
private bool IsKeyerAvailableForPatternUpdate(int i) =>
    !keyers[i].IsActive                          // <-- чтение из main thread
    || schedulers[i].CurrentPhase != StationScheduler.Phase.Navigation;
```

Без `volatile` основной поток может видеть устаревшее значение: keyer уже начал воспроизведение, но `IsActive` всё ещё `false` (или наоборот).

## Корневая причина

Нет `volatile` на поле, читаемом/записываемом из разных потоков.

## Предлагаемое исправление

Заменить auto-property на поле с volatile:

```csharp
private volatile bool isActive;
public bool IsActive => isActive;
```

Или использовать `Interlocked` для чтения/записи.

## Зависимости

- Часть F01/F29 (audio race condition / no synchronization)
- Аналог F110 (dominantStationIndex)
