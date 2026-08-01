# F64: PositionFixCalculator — модификация struct в List через копию (fragile pattern)

**Приоритет:** P2 (Medium)  
**Область:** Logic · PositionFixCalculator · Correctness  
**Файл:** `Source/Logic/PositionFixCalculator.cs:59-64`

---

## Проблема

```csharp
for (int i = 0; i < tentativeFixes.Count; i++)
{
    var fix = tentativeFixes[i];           // копия struct
    fix.Weight = ComputeWeight(fix, ...);   // модификация копии
    tentativeFixes[i] = fix;               // запись обратно
}
```

Код **корректен** (for + indexer), но паттерн хрупок:

1. Замена на `foreach` (будущий рефакторинг) **сломает** логику — struct копируется read-only, Weight не сохранится
2. Любой добавленный `continue;` между строками 2-3 приведёт к потере изменений
3. Аллокации: каждая итерация копирует struct дважды (get + set)

## Рекомендация

Рефакторинг в:

```csharp
for (int i = 0; i < tentativeFixes.Count; i++)
{
    tentativeFixes[i] = tentativeFixes[i] with { 
        Weight = ComputeWeight(tentativeFixes[i], ...) 
    };
}
```

Или добавить комментарий-предупреждение (если C# 7- не поддерживает `with`).

На практике: UBOAT использует .NET 4.x / Standard 2.0. `with` expression доступен с C# 9 / .NET 5. Если проект на C# 7.x — оставить `for` и добавить предупреждение.

30 минут.
