# F192. RadioHandler.OnCursorAngleChanged — событие объявлено и вызывается, но никогда не подписывается

| Поле | Значение |
|------|----------|
| **Приоритет** | P3 (Low) |
| **Область** | UI · RadioHandler · Events · Dead Code |
| **Файл** | `Source/UI/RadioHandler.cs:13, 31-32` |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

Событие `OnCursorAngleChanged` объявлено, проверяется на null и вызывается при изменении `CursorAngle`, но **нигде не подписывается**:

```csharp
// RadioHandler.cs:13
public event Action<float> OnCursorAngleChanged;

// RadioHandler.cs:31-32
if (OnCursorAngleChanged.IsNotNull())
    OnCursorAngleChanged(value);
```

Поиск по всему `Source/` не выдаёт ни одной подписки (`+=`) на `OnCursorAngleChanged`. Вызов — всегда no-op.

---

## Что проверено

| Файл | Подписка на OnCursorAngleChanged? |
|------|:---------------------------------:|
| `CursorHandler.cs` | Нет — использует `RadioHandler.Instance.CursorAngle` напрямую (строки 76, 109, 115) |
| `SonneController.cs` | Нет |
| `InformationPanelHandler.cs` | Нет |
| `ZoneHandler.cs` | Нет |
| `AudioSynthesisEngine.cs` | Нет |

---

## Аналогия с F90

**F90** покрывает аналогичную проблему для статического события `OnVisibilityChanged` (тоже dead event). F90 предлагает удалить событие. F192 — та же проблема, но для нестатического `OnCursorAngleChanged`.

---

## Почему это проблема

1. **Введение в заблуждение**: наличие вызова события создаёт ложное впечатие, что кто-то слушает изменения угла курсора. Новый разработчик может ожидать, что `InformationPanelHandler` или `CursorHandler` подписаны сюда.
2. **Накладные расходы**: проверка `OnCursorAngleChanged.IsNotNull()` + `Action<float>` delegate invocation на каждый `set` `CursorAngle` — хотя сейчас это no-op (всегда `false`), это всё равно выполнение кода.
3. **Несогласованность с архитектурой**: если событие задумано для использования, его нужно либо реализовать (подписать на него нужного потребителя), либо удалить.

---

## Возможные причины

- Событие задумывалось для подписки `InformationPanelHandler` или `CursorHandler`, но подписка так и не была реализована.
- Событие осталось от рефакторинга, когда логика была перенесена на прямые обращения к `RadioHandler.Instance.CursorAngle` (как делает `CursorHandler`).

---

## Исправление

Либо:
1. **Удалить** событие и вызов (если никто не использует).
2. **Подписать** нужного потребителя (если функционал задуман).
3. **Перенести** на статическое событие (как `OnVisibilityChanged` / `OnFrequencyChanged`), если подписка должна быть снаружи.

---

## Зависимости

- **F90** — аналогичный dead event `OnVisibilityChanged`.
- **F158** — `sonneController` field в `RadioHandler` тоже мёртвый (не используется). F158 уже удаляет это поле.
- **F06** — общий dead code: удаление неиспользуемых методов и событий.

---

## Acceptance Criteria

- [ ] `OnCursorAngleChanged` подписан хотя бы на одном потребителе, **либо** событие и вызов удалены
- [ ] Ни одна подписка на `OnCursorAngleChanged` не ожидается в `CursorHandler`, `SonneController`, `InformationPanelHandler`
