# F23: Reflection Fragility — RadioStoryIntegration

**Приоритет:** P0 (Critical)
**Область:** Logic · RadioStoryIntegration
**Тип:** Architecture · Integration

---

## Проблема

`RadioStoryIntegration` (381 строк) использует сырой reflection (`Type.GetType`, `GetMethod`, `Activator.CreateInstance`) для интеграции с UBOAT story system. При обновлении игры методы могут переименоваться или изменить сигнатуры — мод молча отключается без уведомления игрока.

---

## Evidence

### 1. Тройная дупликация type resolution

RadioStoryIntegration.cs:297-305 и 314-321:

```csharp
// Копия #1
Type.GetType("DWS.Common.InjectionFramework.ScriptableObjectSingleton, com.uboat.game")
    ?? typeof().Assembly

// Копия #2 — идентичный код в другом методе
Type.GetType("InjectionFramework.ScriptableObjectSingleton, ...")
```

Три копии одного и того же pattern без метода-помощника.

### 2. Хрупкое конструирование (строка 74)

```csharp
param[3].ParameterType == typeof(Action)
```

Если UBOAT добавит ещё один `Action`-параметр в конструктор — сломается.

### 3. Dynamic Construction в CreateDecision() (строки 201-229)

```csharp
Activator.CreateInstance(paramType) ?? null
```

Если конструктор изменит порядок параметров — передаются неверные данные без компиляции-проверки.

### 4. Silent Fail/Open (строки 362-366)

```csharp
catch (Exception ex) {
    Debug.LogWarning("[RadioNav] HandleCustomDecisions failed (fail-open): " + ex);
    return AllowOriginal; // true — оригинальный story продолжается без мода
}
```

Любая внутренняя ошибка молча восстанавливает игру без мода. Игрок не знает, что интеграция сломана.

### 5. DWS.Common.InjectionFramework reflection

Main.cs использует `DWS.Common.InjectionFramework` для DI. При смене версии UBOAT — инъекция не сработает, prefab не загрузится.

---

## Предлагаемое решение

1. Выделить `GameApiCompatibilityChecker` — проверка всех типов/методов при старте (OnLoaded)
2. Fail-fast: если критический тип не найден — показать игроку сообщение об ошибке
3. Вынести triple-duplicated resolution в один helper-метод
4. Заменить `Activator.CreateInstance` на типизированные фабрики

---

## Acceptance Criteria

- [ ] Все Type.GetType и GetMethod проверены при загрузке (не во время игры)
- [ ] При failure — игрок видит сообщение (не silent fail-open)
- [ ] Нет дубликатов type resolution кода
