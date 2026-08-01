# F61: ToggleHandler — статическое состояние не сбрасывается при перезагрузке сцены

**Приоритет:** P2 (Medium)  
**Область:** UI · ToggleHandler · Static State  
**Файл:** `Source/UI/ToggleHandler.cs:16-18`

---

## Проблема

```csharp
public static bool IsRadioOn = false;
public static bool IsInteractable = false;
```

Эти статические поля не сбрасываются в `OnDestroy()` или `SceneManager.sceneUnloaded`.

F05 (Static Mutable State) перечисляет `ReceiverController.IsRadioOn` (частное поле), но пропускает `ToggleHandler` как класс.

## Последствия

- После перезагрузки сцены: `ToggleHandler.IsRadioOn` = `true` (старое значение), радио отображается как включённое, но объекты не пересозданы
- `ToggleHandler.IsInteractable` = старое значение → кнопка может быть заблокирована

## Исправление

```csharp
private void OnDestroy()
{
    IsRadioOn = false;
    IsInteractable = false;
}
```

15 минут.
