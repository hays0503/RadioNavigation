# F103. InformationPanelHandler.CreateLabel — Font.CreateDynamicFontFromOSFont called per label

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | UI · InformationPanelHandler · GC Pressure |
| **Файл** | `Source/UI/InformationPanelHandler.cs` |
| **Строки** | 134 |

---

## Описание

`Font.CreateDynamicFontFromOSFont("Arial", 14)` вызывается для каждого Text-label'а (approximately 15+ labels):

```csharp
// Строка 134:
text.font = Font.CreateDynamicFontFromOSFont("Arial", 14);
```

В зависимости от внутреннего кешинга Unity это может создавать новый объект `Font` при каждом вызове. Шрифт должен быть создан один раз и переиспользован.

## Корневая причина

Нет кеширования объекта Font.

## Предлагаемое исправление

```csharp
private static Font cachedArialFont;

private static Text CreateLabel(Transform parent, string name, string defaultText)
{
    // ...
    if (cachedArialFont.IsNull())
        cachedArialFont = Font.CreateDynamicFontFromOSFont("Arial", 14);
    text.font = cachedArialFont;
    // ...
}
```

## Зависимости

- Независим от других F-проблем
