# F112. SonneController — BeatFrequency write/read without synchronization

| Поле | Значение |
|------|----------|
| **Приоритет** | P3 (Low) |
| **Область** | Sound · Threading · Thread Safety |
| **Файл** | `Source/Logic/SonneController.cs` |
| **Строки** | 369 (write), 137 (read) |

---

## Описание

`BeatFrequency` записывается из main thread в `UpdateStationGains` (строка 369) и читается из audio thread в `ProcessAudio` (строка 137):

```csharp
// Строка 369 (main thread):
stations[i].BeatFrequency = Math.Max(300f, Math.Min(1800f, BfoBase + offset * 1000f));

// Строка 137 (audio thread):
sample += stations[i].Gain * carriers[s].NextSample();
```

`BeatFrequency` — `float` поле в `StationState`. На x86/x64 запись float атомарна, но без `volatile` audio thread может видеть старое значение.

Это часть более широкой проблемы F29 (no synchronization), но конкретное поле не документировано отдельно.

## Корневая причина

Нет синхронизации доступа к `BeatFrequency` между потоками.

## Предлагаемое исправление

Добавить `volatile` в `StationState.BeatFrequency` или использовать `Volatile.Read`/`Volatile.Write`.

## Зависимости

- Часть F01/F29 (audio race condition / no synchronization)
- Аналог F110, F111
