# F45: SAD ADRs «Accepted» but 0% implemented

**Severity:** P0 (Critical)

**Тип:** Документация / Architecture Decision Records

**Обнаружено:** 2026-07-30, SAD vs code cross-check

---

## Описание

Пять Architecture Decision Records (ADR) в `SAD/12_Architecture_Decisions.md` имеют статус **«Accepted»**, но ни один из них **не реализован** в коде. SAD подаёт их как принятые решения, однако в проекте нет ни строчки кода, соответствующей этим ADR.

| ADR | Статус в SAD | Факт | Где должно быть |
|-----|-------------|------|-----------------|
| **ADR-002** | «Accepted: Implement double-buffer with lock-free ring buffer» | **0 synchronization primitives** в проекте. Нет double-buffer, нет lock-free ring buffer. F01/F29 документируют полное отсутствие thread safety. | `SAD/12_Architecture_Decisions.md` |
| **ADR-003** | «Accepted: Decompose God Classes» | `SonneController` — 508 строк (было 506). `InformationPanelHandler` — 474 строки. Никакой декомпозиции. | `SAD/12_Architecture_Decisions.md` |
| **ADR-004** | «Accepted: Create interfaces for all game integrations» | **Ни одного интерфейса** в проекте. `RadioStoryIntegration` всё ещё использует raw reflection. | `SAD/12_Architecture_Decisions.md` |
| **ADR-005** | «Accepted: Centralize all constants in Data/Constants.cs» | **Файл `Constants.cs` не существует**. Магические числа раскиданы по 10+ файлам (F28). | `SAD/12_Architecture_Decisions.md` |
| **ADR-007** | «Accepted: Implement unit tests for all pure C# logic» | **0 unit tests, 0% coverage**. Нет тестового проекта. `dotnet test` не работает (проект использует csc, не SDK). | `SAD/11_Testing_Strategy.md` |

---

## Влияние

1. **Документация вводит в заблуждение:** Читатель SAD думает, что эти решения приняты и реализованы
2. **Невозможно отследить прогресс:** Если ADR «Accepted», ожидается код. Кода нет — ADR должен быть «Rejected» или «Deferred»
3. **Риск для новых контрибьюторов:** Разработчик, следующий SAD, будет искать несуществующие классы и интерфейсы

---

## Рекомендация

Изменить статус ADR-002/003/004/005/007 с **«Accepted»** на **«Deferred»** или **«Proposed — not started»**. Добавить пояснение: решение принято архитектурно, но реализация отложена до рефакторинга. Ссылаться на соответствующие F-проблемы (F01/F29 для ADR-002, F02/F03/F04 для ADR-003, F23 для ADR-004, F28 для ADR-005, F22 для ADR-007).
