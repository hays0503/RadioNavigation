# F140. Main.cs — GetMethod() результаты не проверяются на null перед HarmonyMethod

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Logic · Main · Reflection |
| **Файл** | `Source/Logic/Main.cs` |
| **Строки** | 47-51 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

Анонимный тип `targets` создаёт `HarmonyMethod` из результатов `patchType.GetMethod(...)`:

```csharp
// Main.cs:47-51
new { method = continueStory, prefix = (HarmonyMethod)null,
      postfix = new HarmonyMethod(patchType.GetMethod("InjectT3PLL38Decision", anyStatic)),
      name = "RadioStory.ContinueStory" },
new { method = retrieveString1,
      prefix = new HarmonyMethod(patchType.GetMethod("OverrideLocaleKey", anyStatic)),
      postfix = (HarmonyMethod)null, name = "Locale.RetrieveString" },
// ...
```

Если любой из методов `InjectT3PLL38Decision`, `OverrideLocaleKey`, `HandleCustomDecisions`, `HideRadioOnStop` не существует (переименован, удалён, рефакторинг), `GetMethod` возвращает `null`. Передача `null` в `new HarmonyMethod(null)` выбрасывает `ArgumentNullException` **до** проверки pre-flight (строка 54-63), которая проверяет только `t.method` (target methods), но не `t.prefix`/`t.postfix` (patch methods).

## Корневая причина

Pre-flight проверяет только target methods, но не patch methods. `GetMethod` может вернуть null для любого из 4 патч-методов.

## Предлагаемое исправление

Добавить проверку patch methods в pre-flight:

```csharp
string missingPatch = null;
foreach (var t in targets)
{
    if (t.method.IsNull())
        missing = (missing ?? "") + t.name + ", ";
    if (t.prefix.IsNotNull() && t.prefix.method.IsNull())
        missingPatch = (missingPatch ?? "") + t.name + " (prefix), ";
    if (t.postfix.IsNotNull() && t.postfix.method.IsNull())
        missingPatch = (missingPatch ?? "") + t.name + " (postfix), ";
}
```

## Зависимости

- Связан с F23 (Reflection Fragility) — общая проблема хрупкой рефлексии
- Связан с F68 (RadioStoryIntegration reflection uncached) — тот же паттерн
- Связан с F82 (MainCs exception swallow) — если exception произойдёт, она будет swallowed
