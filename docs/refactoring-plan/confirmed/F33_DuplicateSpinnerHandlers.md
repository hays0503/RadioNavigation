# F33: VolumeSpinnerHandler и FeedbackSpinnerHandler — дубликаты

**Приоритет:** P3 (Low)
**Область:** UI · Spinner Handlers
**Тип:** Duplication

---

## Проблема

`VolumeSpinnerHandler.cs` и `FeedbackSpinnerHandler.cs` — почти идентичные классы. Разница только в `DebugTag`. Можно объединить в `SpinnerHandlerBase` с параметром.

---

## Evidence

VolumeSpinnerHandler.cs (17 строк):

```csharp
public class VolumeSpinnerHandler : SpinnerHandlerBase
{
    #if DEBUG
    private const string DebugTag = "VolumeSpinner";
    protected override void OnAngleChanged(float angle)
    {
        Debug.Log("[RadioNav][" + DebugTag + "] angle=" + angle);
    }
    #endif
}
```

FeedbackSpinnerHandler.cs (17 строк):

```csharp
public class FeedbackSpinnerHandler : SpinnerHandlerBase
{
    #if DEBUG
    private const string DebugTag = "FeedbackSpinner";
    protected override void OnAngleChanged(float angle)
    {
        Debug.Log("[RadioNav][" + DebugTag + "] angle=" + angle);
    }
    #endif
}
```

Идентичны, разница только в строке `"VolumeSpinner"` vs `"FeedbackSpinner"`.

---

## Предлагаемое решение

1. Добавить в SpinnerHandlerBase protected-метод или свойство DebugTag
2. Volume/Feedback — удалить, использовать SpinnerHandlerBase с инъекцией тега
3. Или полностью убрать override, если Debug.Log не нужен

---

## Acceptance Criteria

- [ ] VolumeSpinnerHandler и FeedbackSpinnerHandler удалены
- [ ] SpinnerHandlerBase работает с обоими spin-контролами
- [ ] DebugTag различается
