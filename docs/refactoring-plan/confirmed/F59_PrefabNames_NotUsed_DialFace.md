# F59: PrefabNames.DialFace — константа определена, но не используется

**Приоритет:** P2 (Medium)  
**Область:** Data · PrefabNames · Code Quality  
**Файл:** `Source/Data/PrefabNames.cs:12`, `Source/UI/ToggleHandler.cs:82`

---

## Проблема

В `PrefabNames.DialFace` (строка 12) определена константа со странным значением:

```csharp
public static class PrefabNames
{
    public const string DialFace = "ciferbllat"; // ← определена
    ...
}
```

В `ToggleHandler.cs:82` используется **строковый литерал** вместо константы:

```csharp
var dialFace = parent.Find("ciferbllat");  // ← hardcoded string
```

Что демонстрирует:
1. Константа не используется (dead API surface)
2. Если переименовать префаб, константа разойдётся с реальностью
3. Имя `"ciferbllat"` — странное; возможно опечатка "ciferblat" (нем. Zifferblatt — циферблат). Если да, константа должна называться явно.

## Исправление

1. Заменить литерал на `PrefabNames.DialFace`
2. (Опционально) Переименовать константу для ясности

5 минут.
