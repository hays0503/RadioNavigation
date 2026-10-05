# F68: RadioStoryIntegration — рефлексия Type.GetType не кешируется

**Приоритет:** P1 (High)  
**Область:** Logic · Integration · Performance  
**Файл:** `Source/Logic/RadioStoryIntegration.cs:308-332`

---

## Проблема

`GetCurrentLanguage()` вызывает `Type.GetType` с assembly-qualified именами **каждый раз**, хотя типы уже были разрешены в `Init()`:

```csharp
private string GetCurrentLanguage()
{
    var singletonType = Type.GetType(                              // ← каждый вызов!
        "DWS.Localization.LocaleManager, Assembly-CSharp");
    var localeType = Type.GetType(                                 // ← каждый вызов!
        "DWS.Localization.Locale, Assembly-CSharp");
    // ...
}
```

Только `languageProp` (PropertyInfo) закеширован в `Init()`. Сами `Type` объекты не сохраняются.

## Влияние

- `GetCurrentLanguage()` может вызываться многократно через радиоисторию
- `Type.GetType` с assembly-qualified именем делает поиск по всем загруженным сборкам
- На игровом билде с 50+ сборками это не бесплатно

## Сравнение с другим кодом

`GetLanguageProperty()` (строка 295-306) тоже вызывает `Type.GetType`, хотя тип уже зарезолвлен в том же методе.

## Исправление

Кешировать результаты `Type.GetType` в статические поля:

```csharp
private static Type _cachedSingletonType;
private static Type _cachedLocaleType;

// в Init() или при первом вызове:
_cachedSingletonType ??= Type.GetType("DWS.Localization.LocaleManager, Assembly-CSharp");
_cachedLocaleType ??= Type.GetType("DWS.Localization.Locale, Assembly-CSharp");
```

Либо вынести все Type.GetType в `Init()` и сохранить в поля класса.

## Риски

- Если сборка выгружается и перезагружается (редко в Unity), кеш может стать stale
- Для AppDomain-стабильных ссылок это безопасно
