# F139. Main.cs — UnpatchAll() без harmony ID ломает патчи всех модов

| Поле | Значение |
|------|----------|
| **Приоритет** | P1 (High) |
| **Область** | Logic · Main · Harmony |
| **Файл** | `Source/Logic/Main.cs` |
| **Строки** | 30 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

При hot-reload вызывается `harmony?.UnpatchAll()` без указания ID:

```csharp
// Main.cs:30
harmony?.UnpatchAll();
```

`Harmony.UnpatchAll()` без аргумента откатывает **все** Harmony-патчи в текущей assembly, включая патчи от **других модов**. Это функциональный баг: при повторной загрузке RadioNavigation ломаются патчи любых других Harmony-модов в UBOAT.

**Сравнение:** строка 78 использует `harmony.UnpatchAll()` при откате частичного фейла — там это корректно (патчи ещё не применены к другим модам). Но строка 30 вызывается при повторной загрузке, когда другие моды уже запатчены.

## Корневая причина

Используется глобальный `UnpatchAll()` вместо `UnpatchSelf()` или `UnpatchAll(harmony.Id)`.

## Предлагаемое исправление

```csharp
// Вариант 1: UnpatchSelf (рекомендуется)
harmony?.UnpatchSelf();

// Вариант 2: UnpatchAll с ID
harmony?.UnpatchAll(harmony.Id);
```

Для строки 78 (откат при partial failure) оставить `harmony.UnpatchAll()` — там это корректно, потому что патчи были применены только к текущему harmony.

## Зависимости

- Связан с F121 (Harmony.UnpatchAll global) — F121 описывает тот же баг в общем виде; F139 фиксирует конкретную строку
- Связан с F83 (Zombie decisions) — zombie decisions возникают при hot-reload, UnpatchAll может усугубить
- Связан с F23 (Reflection Fragility) — оба связаны с Harmony lifecycle
