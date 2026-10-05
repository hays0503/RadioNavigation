# F195: `RadioHandler.NotifyCursorChanged()` — мёртвый код

**Приоритет:** P3 (Low)  
**Область:** UI · RadioHandler · Dead Code  
**Файл:** `Source/UI/RadioHandler.cs:120-123`  
**Дата обнаружения:** 2026-08-01  

---

## Проблема

Метод `NotifyCursorChanged()` определён, но **никогда не вызывается**:

```csharp
// RadioHandler.cs:120-123
public void NotifyCursorChanged()
{
    EmitFrequency();
}
```

**Подтверждение отсутствия вызовов:** grep `NotifyCursorChanged` по всем `.cs` файлам возвращает только определение (RadioHandler.cs:120). Ни один caller не существует.

Метод, очевидно, предназначался для внешнего уведомления об изменении угла курсора (например, из `DebugAngleHandler`, если бы `OnAngleChanged` был подписан). Но:
- `DebugAngleHandler.OnAngleChanged` никогда не подписан (F194).
- `CursorHandler` напрямую устанавливает `RadioHandler.Instance.CursorAngle` (CursorHandler.cs:76, 115), что автоматически вызывает `EmitFrequency()` через сеттер.

---

## Влияние

- Публичный метод в API, который ничего не делает. Может ввести в заблуждение сторонних разработчиков модов.
- F158 документирует мёртвое поле `sonneController` в том же файле; F192 — мёртвое событие `OnCursorAngleChanged`. Но этот метод не документирован.

---

## Исправление

Удалить метод `NotifyCursorChanged()` (RadioHandler.cs:120-123).

---

## Related Issues

- F06 (Dead Code & Mistakes)
- F158 (RadioHandler dead sonneController field)
- F192 (RadioHandler OnCursorAngleChanged dead event)
- F194 (DebugAngleHandler OnAngleChanged dead event)

---

## Acceptance Criteria

- [ ] Метод `NotifyCursorChanged()` удалён.
- [ ] grep `NotifyCursorChanged` по `.cs` файлам возвращает 0 результатов.
