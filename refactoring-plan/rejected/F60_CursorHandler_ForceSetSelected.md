# F60: CursorHandler.Update() форсирует SetSelectedGameObject каждый фрейм

**Приоритет:** P2 (Medium)  
**Область:** UI · CursorHandler · Event System  
**Файл:** `Source/UI/CursorHandler.cs:37-43`

---

## Проблема

```csharp
private void Update()
{
    if (IsPointerOverGameObject())
    {
        EventSystem.current.SetSelectedGameObject(gameObject); // ← каждый фрейм
        ...
    }
}
```

`EventSystem.current.SetSelectedGameObject(gameObject)` вызывается **каждый фрейм** при наведении. Это форсирует selection на радио панели, вмешиваясь в:
- Остальной UI игры (другие кнопки теряют focus)
- События Navigation контроллера (геймпад)
- Возможно, блокирует ввод в других модальных окнах

## Рекомендация

Вызывать `SetSelectedGameObject` **один раз при входе** (OnPointerEnter) или проверять, не выбран ли уже gameObject:

```csharp
if (EventSystem.current.currentSelectedGameObject != gameObject)
    EventSystem.current.SetSelectedGameObject(gameObject);
```

10 минут.
