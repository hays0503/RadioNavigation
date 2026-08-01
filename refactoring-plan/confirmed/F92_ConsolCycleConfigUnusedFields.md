# F92: ConsolCycleConfig — неиспользуемые поля

**Приоритет:** P3 (Low)
**Область:** Data · ConsolCycleConfig
**Файл:** `ConsolCycleConfig.cs:8-10`
**Дата обнаружения:** 2026-07-31

---

## Проблема

`CallsignDotMs`, `CallsignDurationMs`, `NavigationDurationMs` определены, но нигде не читаются. Callsign timing вычисляется из `DotSoundMs`/`DashSoundMs`.

---

## Исправление

Удалить неиспользуемые поля.

---

## Acceptance Criteria

- [ ] Поля удалены, код компилируется
