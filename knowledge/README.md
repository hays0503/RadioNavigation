# knowledge/
Compiled UBOAT/mod knowledge base (free-token window 2026-08-24).
- inventory/patch-surface.md - all virtual methods of game code (Harmony targets)
- inventory/events.md - game event declarations (observable without patching)
- inventory/reflection-contracts.md - mod reflection lookups vs game declarations
- catalogs/locale-keys.md - candidate locale keys from game source
- classes/ - class dossiers (agents)
- playbooks/ - integration recipes (agents)

## Актуальность

Инвентари и досье — снимок игрового API от 2026-08-24, не гарантия для любой
новой сборки игры. `playbooks/gate-ui-by-camera-mode.md` содержит более поздние
уточнения по ручному FPP и завершению переходов.

Для действующих правил **мода**, а не исходного API:

- `../docs/CAMERA_GUIDE.md` — текущий PIP, интерьерный culling, поза и деплой;
- `../SEXTANT_PLAN.md` — контракт секстанта и журнал приёмки (§16.24–16.25);
- `../docs/live-diagnostics.md` — MCP, память, восстановление настроек и границы
  доказательства.

Сначала используйте эту базу знаний; затем, если факт изменился, проверяйте
соответствующий исходник/живой постфакт. Наличие метода или камеры в снимке
не доказывает, что она отправила геометрию на GPU.
