# F107. RadioStoryIntegration.GetCurrentLanguage — uncached Type.GetType on every locale string lookup

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Logic · RadioStoryIntegration · Performance |
| **Файл** | `Source/Logic/RadioStoryIntegration.cs` |
| **Строки** | 308-332 |

---

## Описание

`GetCurrentLanguage()` вызывается из `GetLocalizedLabel()`, который вызывается из `OverrideLocaleKey()` — Harmony prefix на `Locale.RetrieveString`. Этот prefix выполняется при **каждом** обращении к локализации в игре.

Каждый вызов `GetCurrentLanguage()` выполняет:
- До 4 `Type.GetType()` (строки 314-315, 319-320)
- 2 `GetMethod()` (строка 317)
- 1 `GetProperty()` (строка 305 —虽然 cached в `languageProp`, но промежуточные типы ищутся заново)

```csharp
// Строка 314-321:
var singletonType = Type.GetType("DWS.Common.InjectionFramework.ScriptableObjectSingleton, com.uboat.game")
    ?? Type.GetType("DWS.Common.InjectionFramework.ScriptableObjectSingleton, Assembly-CSharp");
// ...
var loadSingleton = singletonType.GetMethod("LoadSingleton", new[] { typeof(Type) });
// ...
var localeType = Type.GetType("UBOAT.Game.Core.Data.Locale, com.uboat.game")
    ?? Type.GetType("UBOAT.Game.Core.Data.Locale, Assembly-CSharp");
```

F68 документирует некешированную рефлексию для `decisionType` etc. в `Init()`, но эти lookup'ы в `GetCurrentLanguage()` — отдельный путь.

## Корневая причина

Промежуточные типы (`singletonType`, `localeType`) и методы (`loadSingleton`) кешируются только частично (сам `languageProp` закеширован).

## Предлагаемое исправление

Вынести `singletonType`, `loadSingleton`, `localeType` в кешированные static поля, как это сделано для `languageProp`:

```csharp
private static Type cachedSingletonType;
private static MethodInfo cachedLoadSingleton;
private static Type cachedLocaleType;
```

## Зависимости

- Связан с F23 (Reflection Fragility), F68 (uncached reflection)
