# F194: `DebugAngleHandler.OnAngleChanged` — dead event

**Приоритет:** P3 (Low)  
**Область:** UI · DebugAngleHandler · Events · Dead Code  
**Файл:** `Source/UI/DebugAngleHandler.cs:25,30`  
**Дата обнаружения:** 2026-08-01  

---

## Проблема

Event `OnAngleChanged` объявлен и firing в сеттере `Angle`, но **никто не подписывается**:

```csharp
// DebugAngleHandler.cs:25
OnAngleChanged?.Invoke(angle);

// DebugAngleHandler.cs:30
public event Action<float> OnAngleChanged;
```

**Подтверждение отсутствия подписок:** grep `OnAngleChanged` по всем `.cs` файлам возвращает только 2 совпадения — оба в `DebugAngleHandler.cs` (invoke на строке 25, declaration на строке 30). Ни один `+=` не найден.

---

## Влияние

- Event invocation — постоянный no-op. Проверка `OnAngleChanged?.Invoke` каждый раз в сеттере — чистой выносливости расход.
- F63 (TmpDependency) и F159 (SerializeField overwrite) документируют другие проблемы DebugAngleHandler, но не эту dead event.
- F90 и F192 документируют аналогичные dead events в RadioHandler, но не для DebugAngleHandler.

---

## Исправление

- [ ] Удалить event `OnAngleChanged` и его invocation, если он не нужен для будущей отладки.
- [ ] Либо добавить subscriber (например, `SonneController` для тестирования bearing-вычислений через UI).

---

## Related Issues

- F63 (DebugAngleHandler TMPro dependency)
- F159 (DebugAngleHandler SerializeField overwrite)
- F90 (RadioHandler OnVisibilityChanged dead event)
- F192 (RadioHandler OnCursorAngleChanged dead event)

---

## Acceptance Criteria

- [ ] Event `OnAngleChanged` удалён или имеет хотя бы одного subscriber.
