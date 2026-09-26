# Находки фазы 5 — Game source verification (G1–G4)

Обнаружены при сверке мода с исходниками игры (`C:\code\projects\uboat`, сборка `com.uboat.game`) 2026-08-01. Все четыре — звенья одного мёртвого контура интеграции с `RadioStory`: панель мода в текущей версии игры не открывается.

| ID | Поломка | Приоритет | Мод (RadioStoryIntegration.cs) | Игра | Следствие |
|----|---------|-----------|-------------------------------|------|-----------|
| **G1** | `stage` читался через `(int)`-каст boxed-перечисления → InvalidCastException (поле ЕСТЬ: RadioStory.cs:83, тип RadioStoryStage, MainChoices=1) — **находка уточнена, см. G1_stageField_dead.md** | P0 | :13, :95, :113-116, :151-156 | `private RadioStoryStage stage` (RadioStory.cs:83) | Инжекция T3PLL38 падает на любой стадии |
| **G2** | `insertMethod` = null | P0 | :56-71 | `InsertDecision(int, Decision)` в `Story.cs:377`, GetMethod не трассирует базы | `Init()` падает: «Decision type not found» — всё мертво |
| **G3** | `onUpdatedMethod` = null | P1 | :100-102, :244-262 | `OnUpdated()` в `Story.cs:438` | UI не обновится после инжекции |
| **G4** | `localizationKeyProp` = null | P0 | :74, :183-199, :334-367 | `LocalizationKey` — readonly поле (`Story.cs:24`), не свойство | Перехват решений fail-open, нет дедупликации |

**Порядок починки:** G2 → G1 → G4 → G3 (без G2 остальные бессмысленны; G1 блокирует вход в инжекцию; G4 блокирует обработку; G3 — обновление UI).

Детали каждого — в файлах `G1_stageField_dead.md` … `G4_localizationKeyProp_null.md`. Связи с гипотезами: F23 (подтверждена усиленно), F104/F186 (→ rejected, см. `../rejected/`).

Справочник API игры (мод ↔ игра, file:line) — `GAME_SOURCE_REFERENCE.md`. Общий отчёт — `../VERIFICATION_REPORT.md`, секция 5.


**Дополнение 2026-08-13:** размещение 3D-префаба мода на географической точке + точка с подписью на карте (проверенные API песочницы, 8 находок-граблей, верификация) — LIGHTHOUSE_PLACEMENT.md.
