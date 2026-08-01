# F82: Main.cs — exception swallow при panel construction

**Приоритет:** P1 (High)
**Область:** Logic · Main
**Файл:** `Main.cs:190-193`
**Дата обнаружения:** 2026-07-31

---

## Проблема

```csharp
catch (Exception ex) {
    Debug.LogError("[RadioNav] " + ex);
    // нет rethrow → mod продолжает работу
}
```

Если panel construction упадёт на середине (часть компонентов создана, часть нет), mod продолжает с **поломанным panel** → каскадные NullReferenceException в других handler'ах.

---

## Исправление

Либо rethrow (mod не должен работать с поломанным panel), либо cleanup частично созданных объектов.

---

## Acceptance Criteria

- [ ] При ошибке panel construction mod либо работает корректно, либо полностью отключён
- [ ] Нет partial panel state
