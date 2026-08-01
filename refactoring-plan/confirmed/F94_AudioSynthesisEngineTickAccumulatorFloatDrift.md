# F94: AudioSynthesisEngine — tickAccumulator float drift

**Приоритет:** P3 (Low)
**Область:** Sound · AudioSynthesisEngine
**Файл:** `AudioSynthesisEngine.cs:75-76`
**Дата обнаружения:** 2026-07-31

---

## Проблема

`tickAccumulator` (float) накапливает `ticksPerSample ≈ 0.0000453`. За часы gameplay floating-point drift может сбить tempo audio.

---

## Исправление

Использовать `double` вместо `float` для `tickAccumulator`.

---

## Acceptance Criteria

- [ ] tickAccumulator типа double
- [ ] Tempo стабилен при длительных сессиях
