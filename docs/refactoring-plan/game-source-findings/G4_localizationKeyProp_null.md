# G4. localizationKeyProp null — `LocalizationKey` в игре — поле, мод ищет свойство

**Статус:** ✔ ПОДТВЕРЖДЕНО (исходники игры, 2026-08-01)
**Приоритет:** P0 (Critical) — перехват решений и дедупликация мертвы
**Область:** Logic · RadioStoryIntegration · Reflection
**Источник (мод):** `Source/Logic/RadioStoryIntegration.cs:74, 183-199, 334-367`
**Источник (игра):** `C:\code\projects\uboat` — `UBOAT.Game.Scene\Story.cs:24`

---

## Суть

Мод читает ключ решения через `GetProperty("LocalizationKey")`, а в игре `LocalizationKey` — **public readonly поле**, не свойство. `GetProperty` → null.

## Доказательство

Мод (RadioStoryIntegration.cs:74):

```csharp
localizationKeyProp = decisionType.GetProperty("LocalizationKey");
```

Игра (Story.cs:22-24):

```csharp
public class Decision
{
    public readonly string LocalizationKey;
    ...
}
```

`Type.GetProperty` находит только свойства — поле не будет найдено → `localizationKeyProp == null`.

## Следствие (два независимых эффекта)

1. **`HandleCustomDecisions` (префикс на `RadioStory.OnDecisionSelected`) fail-open:**
   ```csharp
   if (localizationKeyProp.IsNull())
       return AllowOriginal;      // RSI.cs:340-341
   ```
   Перехват «Radio/OpenT3PLL38» (открытие панели) и «Radio/LeaveStation» (скрытие) никогда не срабатывает — даже если решение будет инжектировано.
2. **`DecisionAlreadyExists` не дедуплицирует** (RSI.cs:188 — `localizationKeyProp.IsNotNull()` → нет, ключ не читается) → при повторной инжекции копились бы дубликаты решения.

## Предлагаемый фикс (вне рамок текущей задачи)

```csharp
var field = decisionType.GetField("LocalizationKey", BindingFlags.Public | BindingFlags.Instance);
var prop  = decisionType.GetProperty("LocalizationKey");
localizationKeyProp = (prop ?? field) as MemberInfo;  // или раздельные ветки для field/property
```

Либо читать через `GetField("LocalizationKey").GetValue(item) as string`.

## Связано

- G1 (stageField null) — инжекция не происходит, так что G4 скрыт
- Отчёт: `refactoring-plan/VERIFICATION_REPORT.md`, секция 5.2
