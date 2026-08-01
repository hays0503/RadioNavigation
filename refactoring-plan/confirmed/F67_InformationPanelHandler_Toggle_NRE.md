# F67: InformationPanelHandler — Toggle component без null-чека

**Приоритет:** P1 (High)  
**Область:** UI · InformationPanelHandler  
**Файл:** `Source/UI/InformationPanelHandler.cs:206-207`

---

## Проблема

`RefreshAll()` проверяет `toggleHandler` (GameObject) на null, но не проверяет `GetComponent<Toggle>()`:

```csharp
if (toggleHandler.IsNotNull())                                          // строка 206
    OnToggleChanged(toggleHandler.GetComponent<Toggle>().isOn);         // строка 207
```

Если `toggleHandler` существует, но `Toggle` компонент отсутствует/уничтожен, `GetComponent<Toggle>()` возвращает null и `.isOn` выбрасывает NRE.

## Сценарий

- `ToggleHandler.Start()` может отключить себя (ранний return при отсутствии parent)
- При этом GameObject существует, но Toggle мог быть удалён
- `RefreshAll()` вызывается из `OnZoneChanged` и `OnFrequencyChanged` — оба триггерятся в произвольные моменты

## Воспроизведение

```csharp
// Если ToggleHandler.Start() вернул раньше, Toggle может отсутствовать
informationPanelHandler.RefreshAll(); // NRE на GetComponent<Toggle>().isOn
```

## Исправление

```csharp
if (toggleHandler.IsNotNull())
{
    var toggle = toggleHandler.GetComponent<Toggle>();
    if (toggle.IsNotNull())
        OnToggleChanged(toggle.isOn);
}
```

## Риски

- Низкий — Toggle обычно есть на prefab-е
- Но защита от NRE стоит одной строки
