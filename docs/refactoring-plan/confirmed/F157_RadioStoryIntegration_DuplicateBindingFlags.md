# F157. RadioStoryIntegration.Init — дублированный BindingFlags.Instance

| Поле | Значение |
|------|----------|
| **Приоритет** | P3 (Low) |
| **Область** | Logic · RadioStoryIntegration · Code Hygiene |
| **Файл** | `Source/Logic/RadioStoryIntegration.cs` |
| **Строки** | 63-64 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

```csharp
// RadioStoryIntegration.cs:63-64
insertMethod = storyType.GetMethod("InsertDecision",
    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public | BindingFlags.Instance);
//                                               ^^^^^^^^                            ^^^^^^^^
//                                               дубликат
```

`BindingFlags.Instance` указан дважды. OR-ing одинаковых флагов безвреден, но это copy-paste ошибка из объединения двух поисковых попыток.

## Корневая причина

Copy-paste при объединении флагов.

## Предлагаемое исправление

```csharp
insertMethod = storyType.GetMethod("InsertDecision",
    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
```

## Зависимости

- Связан с F23 (Reflection Fragility) — та же проблема reflection
- Связан с F68 (RadioStoryIntegration reflection uncached) — тот же файл
- Связан с F104 (RadioStoryIntegration AmbiguousMatchException) — тот же файл, другой reflection aspect
