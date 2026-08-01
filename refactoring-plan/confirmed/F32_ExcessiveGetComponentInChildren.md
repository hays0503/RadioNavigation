# F32: InformationPanelHandler — 5 GetComponentInChildren подряд

**Приоритет:** P3 (Low)
**Область:** UI · InformationPanelHandler
**Тип:** Performance

---

## Проблема

InformationPanelHandler.Start() делает 5 последовательных вызовов `GetComponentInChildren<T>(true)`. Каждый вызов — отдельный обход иерархии Unity Transform.

---

## Evidence

InformationPanelHandler.cs:155-159:

```csharp
toggleHandler = GetComponentInChildren<ToggleHandler>(true);
groundHandler = GetComponentInChildren<GroundHandler>(true);
zoneHandler = GetComponentInChildren<ZoneHandler>(true);
volumeSpinner = GetComponentInChildren<VolumeSpinnerHandler>(true);
feedbackSpinner = GetComponentInChildren<FeedbackSpinnerHandler>(true);
```

### Почему это проблема

- `GetComponentInChildren<T>(true)` обходит всё поддерево Transform-ов для каждого типа
- 5 обходов вместо одного `GetComponentsInChildren<MonoBehaviour>` + фильтрация по типам
- На prefab с ~15-20 игровыми объектами — overhead незначительный, но это code smell

---

## Предлагаемое решение

```csharp
var handlers = GetComponentsInChildren<MonoBehaviour>(true);
foreach (var h in handlers)
{
    if (h is ToggleHandler th) toggleHandler = th;
    else if (h is GroundHandler gh) groundHandler = gh;
    // ...
}
```

Или просто public поля, присваиваемые через Inspector.

---

## Acceptance Criteria

- [ ] Только 1 вызов GetComponentsInChildren вместо 5
- [ ] Поведение идентично
