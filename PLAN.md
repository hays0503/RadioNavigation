# План рефакторинга TestUI Radio Panel

Цель: довести код до уровня production-ready мода, который масштабируется и не ломается при обновлениях игры.

---

## Файлы фаз

| # | Файл | Приоритет | Сложность | Шагов | Статус |
|---|------|-----------|-----------|-------|--------|
| 1 | [Phase1-Bugs.md](Plan/Phase1-Bugs.md) | Критично | Низкая | 24 из 25 | ✅ |
| 2 | [Phase2-Fragile.md](Plan/Phase2-Fragile.md) | Критично | Средняя | 16 из 18 | ✅ |
| 3 | [Phase3-Reflection.md](Plan/Phase3-Reflection.md) | Важно | Средняя | 11 из 11 | ✅ |
| 4 | [Phase4-Performance.md](Plan/Phase4-Performance.md) | Важно | Низкая | 6 из 7 | ✅ |
| 5 | [Phase5-Style.md](Plan/Phase5-Style.md) | Красота | Низкая | 8 из 12 | ✅ |
| 6 | [Phase6-Architecture.md](Plan/Phase6-Architecture.md) | Красота | Высокая | 8 из 9 | ✅ |
| 7 | [Phase7-Verification.md](Plan/Phase7-Verification.md) | Критично | Низкая | 5 из 5 | ✅ |
| 8 | [Phase8-Testing.md](Plan/Phase8-Testing.md) | Важно | Средняя | 5 из 5 | ✅ |
| 9 | [Phase9-SonneSignal.md](Plan/Phase9-SonneSignal.md) | Функция | Высокая | 6 из 6 | ✅ |

---

## Порядок выполнения

| Приоритет | Фаза | Результат | Статус |
|---|---|---|---|
| Критично | 1 | Устранены все баги (включая race condition и Escape conflict) | ✅ |
| Критично | 2 | Устранена хрупкость (включая Start race) | ✅ |
| Важно | 3 | Рефлексия безопасна | ✅ |
| Важно | 4 | Нет GC pressure | ✅ |
| Красота | 6 | Архитектура чистая | ✅ |
| Красота | 5 | Единый стиль (после архитектуры) | ✅ |
| Критично | 7 | Мод работает, ничего не сломано | ✅ |
| Важно | 8 | Покрытие тестами / diagnostics | ✅ |
| Функция | 9 | Sonne/Consol навигационный сигнал | ✅ |

> **Изменён порядок**: Phase 3 (Reflection) теперь **перед** Phase 5 (Style). Phase 5.7 (InjectionFramework null-check) **удалён** — покрыто в Phase 1.19 (consolidated guard + try-catch). Phase 7 получила новый шаг 7.4 (AssetBundle lifecycle). **v3**: Phase 1 получила 5 новых шагов (1.21-1.25) — идемпотентность, дубликаты, атомарность, fail-open, reset state. Phase 2 — 3 новых (2.16-2.18) — валидация prefab, null-checks, touch guard. Phase 6 — 2 новых (6.11-6.12) — UI state machine, AssetBundle ownership.

---

## Итоговая сводка

| Статус | Шаги |
|--------|------|
| **✅ Выполнен** | 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.10, 1.11, 1.12, 1.13, 1.14, 1.15, 1.16, 1.17, 1.18, 1.19, 1.20, **1.21, 1.22, 1.23, 1.24, 1.25**, 2.2, 2.3, 2.4, 2.5, 2.7, 2.8, 2.9, 2.10, 2.11, 2.12, 2.13, 2.14, 2.15, **2.16, 2.17, 2.18**, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9, 3.10, 3.11, 4.1, 4.2, 4.4, 4.5, 4.6, 4.7, 5.1, 5.2, 5.3, 5.8, 5.10, 5.12, 5.13, 5.14, 6.1, 6.2, 6.3, 6.6, 6.7, 6.10, **6.11, 6.12**, 7.1, 7.2, 7.3, 7.4, **7.5**, 8.1, 8.2, 8.3, **8.4, 8.5** |
| **❌ Не нужен** | 1.9, 2.1, 2.6, 4.3, 5.4, 5.5, 5.7, 5.9, 5.10 Ground/Expander, 6.4 |

---

## Ключевые изменения v2

### Новые баги (Phase 1)
- **1.18** — Race condition: GroundHandler vs ToggleHandler Start порядок
- **1.19** — InjectionFramework: null-check + try-catch (consolidated guard)
- **1.20** — Escape conflict: мод перехватывает Escape когда открыто другое UI (перенесён из 6.8)
- **2.15** (перенесён из Phase 2) — Toggle value change race

### Консолидация дубликатов
- Старые 1.14 + 5.6 → новый 1.14 (CursorHandler null-check Image)
- Старые 1.16 + 5.11 → новый 1.16 (SpinnerHandlerBase protected Start)
- Старый 2.11 + 6.9 → убран из Phase 6, остался в Phase 2
- Старый 1.9 + 6.5 → убран из Phase 6, остался в Phase 1
- **Старый 1.19 + 5.7 → новый 1.19** (InjectionFramework null-check + try-catch, consolidated guard)

### Переоценка приоритетов
- **4.4** — `Input.mousePosition` → `eventData.position`: Критично (корректность, не производительность)
- **6.8** — Escape conflict: Критично (баг), перенесён в Phase 1
- **5.3** — Magic numbers: Важно (maintenance debt), не Красота

### Новая Phase 8: Тестирование
- 8.1 — Иерархия prefab: data-driven конфиг имён с fallback
- 8.2 — Diagnostic hierarchy dump в `#if DEBUG`
- 8.3 — Smoke test: автоматическая проверка что мод загружается

---

## Ключевые изменения v3

### Новые баги (Phase 1)
- **1.21** — Идемпотентность: T3PLL38 инжектируется повторно при каждом ContinueStory без проверки наличия в Decisions
- **1.22** — Дубликаты панелей: каждый OnSceneStart создаёт новый Radio без guard от повторного создания
- **1.23** — Атомарность патчинга: один try/catch на 5 Harmony патчей, partial patching без отката
- **1.24** — Fail-open prefix: HandleCustomDecisions без catch блокирует оригинальный OnDecisionSelected при reflection errors
- **1.25** — Static state: `_initialized` и кэшированные reflection fields не сбрасываются при unload/reload мода

### Новые зависимости (Phase 2)
- **2.16** — Валидация prefab: нет проверки обязательных детей до частичной установки компонентов → NPE позже
- **2.17** — Unity component null-checks: Toggle, RectTransform, Image не проверяются в handlers → краш при отсутствии
- **2.18** — Touch/multi-pointer: один `isDragging` флаг без pointer ID → некорректный drag на touch

### Архитектурные (Phase 6)
- **6.11** — UI State Machine: GroundHandler и ToggleHandler оба управляют одним Toggle без single source of truth
- **6.12** — AssetBundle ownership: неизвестно кто загружает/выгружает 46 MB bundle

### Верификация (Phase 7)
- **7.5** — Compatibility matrix: нет формального способа зафиксировать среду тестирования

### Тестирование (Phase 8)
- **8.4** — Reproducible build: нет `.csproj` или build script, сборка воспроизводима только на одной машине
- **8.5** — Structured diagnostics: `[DIAG]` тег для machine-readable проверки состояния мода

### Исправления рекомендаций
- **2.9** — ~~`DestroyImmediate()`~~ → static cache с lifecycle ownership (`Application.quitting` callback)

### Статистика v3
- **Всего шагов**: 79 → **92** (+13)
- **Нужен**: 72 → **83** (+11, включая исправление 2.12: был ❌ в сводке, анализ говорит ✅)
- **Не нужен**: 7 → **10** (удалён 2.12 из ❌, добавлено 13 новых ✅)
- **Критично**: Phase1 +5, Phase2 +3 → +8 критических шагов
- **Важно**: Phase6 +2, Phase7 +1, Phase8 +2 → +5 важных шагов
- **Красота**: без изменений (+0)
