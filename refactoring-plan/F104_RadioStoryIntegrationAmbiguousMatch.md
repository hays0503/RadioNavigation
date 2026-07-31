# F104. RadioStoryIntegration.Init — AmbiguousMatchException risk from combined NonPublic+Public BindingFlags

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Logic · RadioStoryIntegration · Reflection |
| **Файл** | `Source/Logic/RadioStoryIntegration.cs` |
| **Строки** | 63-64 |

---

## Описание

`GetMethod("InsertDecision", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public | BindingFlags.Instance)` ищет публичные и непубличные методы без указания типов параметров:

```csharp
// Строка 63-64:
insertMethod = storyType.GetMethod("InsertDecision",
    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public | BindingFlags.Instance);
```

Если базовый тип игры имеет несколько перегрузок `InsertDecision`, `GetMethod` выбросит `AmbiguousMatchException`. То же самое для строк 97-98 (`decisionsProp`) и 100-101 (`onUpdatedMethod`).

## Корневая причина

Смешивание `BindingFlags.Public` и `BindingFlags.NonPublic` без указания типов параметров.

## Предлагаемое исправление

Либо указать типы параметров, либо использовать `GetMethods()` с ручным выбором:

```csharp
insertMethod = storyType.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
    .FirstOrDefault(m => m.Name == "InsertDecision" && m.GetParameters().Length >= 2);
```

## Зависимости

- Связан с F23 (Reflection Fragility) и F68 (uncached reflection)
