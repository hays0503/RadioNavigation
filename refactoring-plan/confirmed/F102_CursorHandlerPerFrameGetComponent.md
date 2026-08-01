# F102. CursorHandler.Update — per-frame GetComponentInParent<ScrollRect>() not cached

| Поле | Значение |
|------|----------|
| **Приоритет** | P2 (Medium) |
| **Область** | UI · CursorHandler · Performance |
| **Файл** | `Source/UI/CursorHandler.cs` |
| **Строки** | 130 |

---

## Описание

`GetComponentInParent<ScrollRect>()` вызывается каждый кадр пока курсор наведён и пользователь скроллит:

```csharp
// Строка 130:
ScrollRect scrollRect = GetComponentInParent<ScrollRect>();
```

Это выполняет обход иерархии каждый кадр. `ScrollRect` статичен после настройки сцены и должен быть закеширован в `Start()`.

## Корневая причина

Отсутствие кеширования компонента.

## Предлагаемое исправление

Добавить поле `private ScrollRect cachedScrollRect;` и инициализировать в `Start()`:

```csharp
private ScrollRect cachedScrollRect;

private void Start()
{
    // ... существующий код ...
    cachedScrollRect = GetComponentInParent<ScrollRect>();
}
```

В `Update()` использовать `cachedScrollRect`.

## Зависимости

- Частично пересекается с F95 (CursorHandler unnecessary ScrollRect)
