# F88: csproj подавляет все nullable warnings

**Приоритет:** P2 (Medium)
**Область:** Build · Configuration
**Файл:** `radionavigation.csproj:10`
**Дата обнаружения:** 2026-07-31

---

## Проблема

```xml
<NoWarn>CS8600;CS8601;CS8602;CS8603;CS8604;CS8618;CS8625</NoWarn>
```

7 nullable warning'ов подавлены. Компилятор не может поймать genuine NRE risks.

---

## Исправление

Убрать подавление, исправить warnings в коде.

---

## Acceptance Criteria

- [ ] Код компилируется без nullable warnings
- [ ] NoWarn пустой или содержит только подтверждённые safe suppressions
