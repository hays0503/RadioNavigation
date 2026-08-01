# F152. Main.cs — третий catch block логирует только e.Message, теряет StackTrace

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Logic · Main · Error Handling |
| **Файл** | `Source/Logic/Main.cs` |
| **Строки** | 190-193 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

Третий catch block в `SceneEventsListener_OnSceneStart`:

```csharp
// Main.cs:190-193
catch (Exception e)
{
    Debug.LogError("[RadioNav][Main] Error: " + e.Message);
}
```

Логирует только `e.Message` без `StackTrace`. Это третий такой catch block в файле — F69 (строка 77) и F82 (строка 123) описывают аналогичные проблемы в других catch blocks.

**Сравнение:** строка 124 корректно использует `Debug.LogException(e)`:

```csharp
// Main.cs:123-124
Debug.LogError("[RadioNav][Main] Injection failed (Awake): " + e.Message);
Debug.LogException(e);  // ← полный stack trace
```

## Корневая причина

Непоследовательное использование `Debug.LogError` vs `Debug.LogException` в разных catch blocks.

## Предлагаемое исправление

```csharp
catch (Exception e)
{
    Debug.LogError("[RadioNav][Main] Error: " + e.Message);
    Debug.LogException(e);
}
```

## Зависимости

- Связан с F69 (Main catch swallows stack trace) — тот же паттерн, строка 77
- Связан с F82 (MainCs exception swallow) — тот же паттерн, строка 123
- Связан с F106 (DebugLog in audio thread) — проблема diagnostic logging в разных контекстах
