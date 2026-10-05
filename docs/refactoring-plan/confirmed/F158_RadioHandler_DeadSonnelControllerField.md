# F158. RadioHandler — мёртвое поле sonneController

| Поле | Значение |
|------|----------|
| **Приоритет** | P3 (Low) |
| **Область** | UI · RadioHandler · Dead Code |
| **Файл** | `Source/UI/RadioHandler.cs` |
| **Строки** | 17, 80 |
| **Дата обнаружения** | 2026-07-31 |

---

## Описание

```csharp
// RadioHandler.cs:17
private SonneController sonneController;

// RadioHandler.cs:80 (Start)
sonneController = GetComponent<SonneController>();
```

Поле `sonneController` присваивается в `Start()`, но **нигде не читается** в классе. Это чистый dead code.

## Корневая причина

Поле было добавлено для будущего использования, но так и не было подключено.

## Предлагаемое исправление

Удалить поле и присваивание:

```csharp
// Удалить строку 17:
// private SonneController sonneController;

// Удалить строку 80:
// sonneController = GetComponent<SonneController>();
```

## Зависимости

- Связан с F06 (Dead Code & Mistakes) — общая проблема мёртвого кода
- Связан с F54 (AudioSynthesisEngine dead code) — та же категория
