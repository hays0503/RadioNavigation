# F173. InformationPanelHandler.CreateLabel — хрупкая загрузка шрифта через OS font name

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | UI · InformationPanelHandler · Portability |
| **Файл** | `Source/UI/InformationPanelHandler.cs` |
| **Строки** | 98-107 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

```csharp
// InformationPanelHandler.cs:98-107
private static Text CreateLabel(Transform parent, string name, string defaultText)
{
    // ...
    var text = go.AddComponent<Text>();
    text.font = Font.CreateDynamicFontFromOSFont("Arial", 14);
    // ...
}
```

`Font.CreateDynamicFontFromOSFont("Arial", 14)` запрашивает системный шрифт "Arial". Проблемы:

1. На Linux/macOS шрифт может называться иначе (например, "Liberation Sans" или "DejaVu Sans")
2. Если шрифт не найден, Unity возвращает пустой шрифт — текст не отображается
3. Динамическое создание шрифта при каждом вызове `CreateLabel` (12+ раз) — расточительно

Дополнительно: `CreateLabel` вызывается из `Start()` для каждого label, что создаёт 12+ отдельных шрифтовых объектов.

## Корневая причина

Зависимость от конкретного системного шрифта без fallback.

## Предлагаемое исправление

Использовать встроенный шрифт Unity или кешировать:

```csharp
private static Font cachedFont;

private static Text CreateLabel(Transform parent, string name, string defaultText)
{
    // ...
    if (cachedFont.IsNull())
        cachedFont = Font.CreateDynamicFontFromOSFont("Arial", 14);
    text.font = cachedFont;
    // ...
}
```

## Зависимости

- Связан с F103 (FontCreatePerLabel)
