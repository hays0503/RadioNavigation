# F124. GetCurrentLanguage() повторно резолвит Type.GetType на каждый вызов

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | Logic · RadioStoryIntegration · Performance |
| **Файл** | `Source/Logic/RadioStoryIntegration.cs` |
| **Строки** | 308-332 |

---

## Описание

`GetCurrentLanguage()` заново вызывает `Type.GetType()` и `GetMethod()` при **каждом** вызове, хотя `GetLanguageProperty()` уже кешировал `languageProp`:

```csharp
// RadioStoryIntegration.cs:308-332
private static string GetCurrentLanguage()
{
    try
    {
        if (languageProp.IsNull())
            return "english";
        // <-- Эти 3 строки повторяют работу GetLanguageProperty():
        var singletonType = Type.GetType("DWS.Common.InjectionFramework.ScriptableObjectSingleton, com.uboat.game")
            ?? Type.GetType("DWS.Common.InjectionFramework.ScriptableObjectSingleton, Assembly-CSharp");
        if (singletonType.IsNull()) return "english";
        var loadSingleton = singletonType.GetMethod("LoadSingleton", new[] { typeof(Type) });
        if (loadSingleton.IsNull()) return "english";
        var localeType = Type.GetType("UBOAT.Game.Core.Data.Locale, com.uboat.game")
            ?? Type.GetType("UBOAT.Game.Core.Data.Locale, Assembly-CSharp");
        if (localeType.IsNull()) return "english";
        object locale = loadSingleton.Invoke(null, new object[] { localeType });
        // ...
    }
}
```

`GetLanguageProperty()` (строки 295-306) уже разрезолвил `singletonType`, `localeType` и `languageProp` при инициализации. Но `GetCurrentLanguage()` **не переиспользует** эти результаты — заново ищет `singletonType`, `localeType`, `loadSingleton` метод.

## Корневая причина

Отсутствие кеширования промежуточных reflection объектов (`singletonType`, `loadSingleton`, `localeType`) в статических полях.

## Предлагаемое исправление

Кешировать все reflection объекты:

```csharp
private static Type cachedSingletonType;
private static MethodInfo cachedLoadSingleton;
private static Type cachedLocaleType;

private static PropertyInfo GetLanguageProperty()
{
    cachedSingletonType = Type.GetType("DWS.Common.InjectionFramework.ScriptableObjectSingleton, com.uboat.game")
        ?? Type.GetType("DWS.Common.InjectionFramework.ScriptableObjectSingleton, Assembly-CSharp");
    if (cachedSingletonType.IsNull()) return null;
    cachedLoadSingleton = cachedSingletonType.GetMethod("LoadSingleton", new[] { typeof(Type) });
    if (cachedLoadSingleton.IsNull()) return null;
    cachedLocaleType = Type.GetType("UBOAT.Game.Core.Data.Locale, com.uboat.game")
        ?? Type.GetType("UBOAT.Game.Core.Data.Locale, Assembly-CSharp");
    if (cachedLocaleType.IsNull()) return null;
    return cachedLocaleType.GetProperty("Language", BindingFlags.Public | BindingFlags.Instance);
}

private static string GetCurrentLanguage()
{
    try
    {
        if (languageProp.IsNull() || cachedLoadSingleton.IsNull() || cachedLocaleType.IsNull())
            return "english";
        object locale = cachedLoadSingleton.Invoke(null, new object[] { cachedLocaleType });
        if (locale.IsNull()) return "english";
        string lang = languageProp.GetValue(locale) as string;
        return lang?.ToLowerInvariant() ?? "english";
    }
    catch (Exception ex)
    {
        Debug.LogWarning("[RadioNav] GetCurrentLanguage failed: " + ex);
        return "english";
    }
}
```

## Зависимости

- Связан с F68 (RadioStoryIntegration uncached Type.GetType)
- Связан с F107 (Uncached Type.GetType per locale lookup)
- Связан с F23 (Reflection Fragility)
