# F56: RadioHandler — подписка zoneHandler.OnZoneChanged без отписки

**Приоритет:** P1 (High)  
**Область:** UI · RadioHandler · Event Lifecycle  
**Файл:** `Source/UI/RadioHandler.cs:84, 69-73`

---

## Проблема

```csharp
private void Start()
{
    ...
    zoneHandler.OnZoneChanged += OnZoneChanged; // ← подписка
}

private void OnDestroy()
{
    if (Instance == this)
        Instance = null;
    // ← OnZoneChanged не отписан!
}
```

F30 документирует отсутствие отписки в RadioHandler, но описывает её как часть F11/F14 (утечки подписок). Конкретно `zoneHandler.OnZoneChanged` не упомянут.

Это самостоятельная утечка: ZoneHandler переживает RadioHandler → RadioHandler висит в списке подписчиков ZoneHandler → утечка памяти + вызов мёртвого обработчика.

## Зона поражения

- Утечка: RadioHandler не будет собран GC, пока ZoneHandler жив
- Логическая: OnZoneChanged вызовет EmitFrequency после смерти RadioHandler → попытка доступа к полям уничтоженного объекта

## Исправление

```csharp
private void OnDestroy()
{
    if (zoneHandler.IsNotNull())
        zoneHandler.OnZoneChanged -= OnZoneChanged;
    if (Instance == this)
        Instance = null;
}
```

10 минут.
