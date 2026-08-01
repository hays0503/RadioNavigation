# F42: Static Texture2D курсора не уничтожается при выгрузке сцены

**Приоритет:** P2 (Medium)
**Область:** UI · PanelDragHandler
**Тип:** Memory Leak · GPU Resource

---

## Проблема

`PanelDragHandler.GenerateMoveCursor()` (PanelDragHandler.cs:40-87) создаёт `Texture2D(32, 32, RGBA32)` и кэширует в статическом поле `cachedCursorTexture`. Регистрирует очистку через `Application.quitting` (срабатывает только при выходе из игры). При перезагрузке сцены или выгрузке мода текстура не уничтожается — утечка GPU-памяти.

---

## Evidence

PanelDragHandler.cs:34-36:
```csharp
if (cachedCursorTexture.IsNull())
    cachedCursorTexture = GenerateMoveCursor();
moveCursorTexture = cachedCursorTexture;
```

PanelDragHandler.cs:80-84:
```csharp
Application.quitting += () =>
{
    if (tex.IsNotNull())
        Object.Destroy(tex);     // ← только при выходе из игры
};
```

---

## Impact

- 4 KB (32×32×4) GPU-памяти на текстуру — не критично объёмом
- Но если мод перезагружается многократно (тестирование), каждая новая текстура висит до рестарта игры
- Паттерн не соответствует Unity Best Practices (cleanup в OnDestroy/OnDisable)

## Fix

Добавить статический деструктор или очистку в `OnDestroy` на любом экземпляре PanelDragHandler. Использовать `Object.DontDestroyOnLoad` или явный cleanup при выгрузке.
