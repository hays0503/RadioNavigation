# F106. AudioSynthesisEngine — Debug.Log + Time.time in audio thread, no #if DEBUG

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Sound · Audio Thread · Debug Logging |
| **Файл** | `Source/Sound/AudioSynthesisEngine.cs` |
| **Строки** | 97-98 |

---

## Описание

`Debug.Log` с строковой конкатенацией и `Time.time` вызываются в audio thread БЕЗ обёртки `#if DEBUG`:

```csharp
// Строка 97-98:
Debug.Log("[RadioNav][NAV] PreNavPause->Navigation st=" + s +
    " NavStartTime=" + Time.time.ToString("F2"));
```

Два отдельных нарушения:
1. **Debug.Log без #if DEBUG** — выполняется в каждом Release build (F31/F93)
2. **Time.time в audio thread** — Unity API, не безопасен для вызова из audio callback (F13)

Строковая конкатенация аллоцирует память в audio thread (GC pressure).

## Корневая причина

Отсутствие `#if DEBUG` и обращение к Unity main-thread API из audio callback.

## Предлагаемое исправление

```csharp
#if DEBUG
Debug.Log("[RadioNav][NAV] PreNavPause->Navigation st=" + s +
    " NavStartTime=" + Time.time.ToString("F2"));
#endif
```

Лучше: вынести логирование в main thread через флаг/очередь.

## Зависимости

- Часть F13 (Unity API in audio thread)
- Часть F31 (Debug.Log в Release)
