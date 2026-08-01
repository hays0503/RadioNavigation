# F171. GroundHandler.StateLog — аллокация строк в DEBUG каждый кадр

| Поле | Значение |
|------|----------|
| **Приоритет** | P3 (Low) |
| **Область** | UI · GroundHandler · Performance |
| **Файл** | `Source/UI/GroundHandler.cs` |
| **Строки** | 48-73 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

```csharp
// GroundHandler.cs:48-73
private string StateLog()
{
    // ... 20+ строк string concatenation ...
    return "[Ground] active=" + gameObject.activeSelf
        + " grPos=" + (grRt.IsNotNull() ? grRt.anchoredPosition.ToString() : "no-rt")
        + " grSize=" + (grRt.IsNotNull() ? grRt.sizeDelta.ToString() : "no-rt")
        // ... ещё ~15 конкатенаций ...
}
```

Метод `StateLog()` вызывается в `#if DEBUG` блоках (строка 117 и 147). Каждый вызов аллокит ~20 промежуточных строк через `+` конкатенацию. В DEBUG сборке это происходит каждый кадр при активном GroundHandler, создавая GC pressure.

## Корневая причина

Отладочный метод не оптимизирован для частых вызовов.

## Предлагаемое исправление

Использовать `StringBuilder` или `string.Format`:

```csharp
#if DEBUG
private string StateLog()
{
    var sb = new System.Text.StringBuilder(256);
    sb.Append("[Ground] active=").Append(gameObject.activeSelf);
    // ... остальные поля через Append ...
    return sb.ToString();
}
#endif
```

Или лучше: кешировать результат и обновлять только при изменении состояния.

## Зависимости

- Аналогичная проблема в F31 (ExcessiveDebugLog)
