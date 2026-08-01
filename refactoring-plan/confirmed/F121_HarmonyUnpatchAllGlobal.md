# F121. Harmony.UnpatchAll() глобально откатывает патчи всех модов

| Поле | Значение |
|------|----------|
| **Приоритет** | P1 (High) |
| **Область** | Logic · Main · Harmony |
| **Файл** | `Source/Logic/Main.cs` |
| **Строки** | 78 |

---

## Описание

При ошибке применения одного из патчей `Main.OnLoaded()` вызывает `harmony.UnpatchAll()`:

```csharp
// Main.cs:75-81
catch (Exception ex)
{
    Debug.LogError("[RadioNav] Patch failed for " + t.name + ": " + ex.Message);
    harmony.UnpatchAll();
    Debug.Log("[RadioNav] All patches rolled back due to partial failure");
    break;
}
```

`Harmony.UnpatchAll()` **без аргументов** удаляет ВСЕ Harmony-патчи из текущего `AppDomain` — включая патчи **других модов** (например, other UBOAT mods, которые используют Harmony). Это не ограничивается патчами этого мода.

## Корневая причина

Используется `UnpatchAll()` (global) вместо `UnpatchSelf()` (только этот mod ID) или `Unpatch(method)` (только конкретный target).

## Предлагаемое исправление

Заменить на `UnpatchSelf()`:

```csharp
catch (Exception ex)
{
    Debug.LogError("[RadioNav] Patch failed for " + t.name + ": " + ex.Message);
    harmony.UnpatchSelf();
    Debug.Log("[RadioNav] All RadioNavigation patches rolled back due to partial failure");
    break;
}
```

Или, для более точного отката — откатить только уже применённые патчи:

```csharp
// Сохранять список успешно применённых target'ов и откатывать только их:
catch (Exception ex)
{
    Debug.LogError("[RadioNav] Patch failed for " + t.name + ": " + ex.Message);
    foreach (var patched in appliedTargets)
        harmony.Unpatch(patched, HarmonyPatchType.All, harmony.Id);
    Debug.Log("[RadioNav] Rolled back " + appliedTargets.Count + " patches");
    break;
}
```

## Риск

Текущее поведение может ломать **другие моды** при любой ошибке патчинга RadioNavigation. В multi-mod среде это критическая проблема совместимости.

## Зависимости

- Независим от других F-проблем
