# F46: SAD Harmony patch targets completely wrong

**Severity:** P0 (Critical)

**Тип:** Документация — неверное описание архитектуры

**Файлы:** `SAD/02_System_Context.md`, `SAD/12_Architecture_Decisions.md`

---

## Описание

SAD описывает точки входа Harmony-патчей неверно. Это критично, потому что читатель SAD не сможет понять, как мод интегрируется с игрой.

### SAD утверждает (SAD/02: System Context):

```
Game.OnLoaded()          → postfix (entry point)
StoryManager.InjectDecision() → prefix
LocaleManager.GetCurrentLanguage() → postfix (с запасным prefix)
```

Также SAD/12 (ADR-001) показывает пример кода с атрибутами:
```csharp
[HarmonyPatch(typeof(Game), "OnLoaded")]
```

### Реальность (Main.cs):

```csharp
typeof(RadioStory).GetMethod("ContinueStory")          // prefix (Main.cs:39)
typeof(Locale).GetMethod("RetrieveString", new[] {...}) // postfix (Main.cs:40)
typeof(Locale).GetMethod("RetrieveString", new[] {...}) // prefix (Main.cs:41)
typeof(RadioStory).GetMethod("OnDecisionSelected")     // postfix (Main.cs:42)
typeof(RadioStory).GetMethod("OnStopped")               // postfix (Main.cs:43)
```

| Аспект | SAD | Реальность |
|--------|-----|-----------|
| **Target class** | `Game`, `StoryManager` | `RadioStory`, `Locale` |
| **Target methods** | `OnLoaded`, `InjectDecision`, `GetCurrentLanguage` | `ContinueStory`, `RetrieveString` (2 overloads), `OnDecisionSelected`, `OnStopped` |
| **Patch count** | 3 | 5 (2 prefix, 3 postfix) |
| **Patch style** | Attribute `[HarmonyPatch]` | Imperative `harmony.Patch()` with `MethodInfo` |
| **Entry point** | Method `Game.OnLoaded` is «entry point» | `Game.OnLoaded` is `IUserMod` interface method, NOT a patch |

---

## Влияние

1. Разработчик, читающий SAD, будет искать несуществующие патчи
2. При обновлении UBOAT версии, SAD не поможет найти правильные target methods
3. F23 (Reflection Fragility) уже описывает риски, но SAD усугубляет их неверной документацией

---

## Рекомендация

Переписать Sec 2 (System Context) и ADR-001 (12_Architecture_Decisions.md) с корректным списком патчей. Использовать imperative API в примерах. Указать, что `RadioStory` и `Locale` — классы из `com.uboat.game.dll`.
