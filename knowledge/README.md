# knowledge/
Compiled UBOAT/mod knowledge base (free-token window 2026-08-24).
- inventory/patch-surface.md - all virtual methods of game code (Harmony targets)
- inventory/events.md - game event declarations (observable without patching)
- inventory/reflection-contracts.md - mod reflection lookups vs game declarations
- inventory/celestial-renderers.md - native/mod Sun/Moon, physical sizes, PIP-scoped isolation and topocentric diagnostic; live acceptance open (2026-10-08)
- inventory/game-hotkeys.md - game hotkey declarations and input paths
- inventory/game-input-semantics.md - input semantics and state transitions
- inventory/navigation-instrument-contracts.md - navigation instrument contracts
- inventory/navigation-projection-contract.md - map/world projection contract
- inventory/solar-flare-postprocess.md - solar flare post-process renderer
- catalogs/locale-keys.md - candidate locale keys from game source
- classes/ - class dossiers (agents)
- playbooks/ - integration recipes (agents)
- qa/qa-classes.md, qa/qa-game-corners.md, qa/qa-inventories.md, qa/qa-mod-internals.md, qa/qa-playbooks.md - QA dossiers

Всего **26** `.md` в Git: 9 inventory, 1 catalogs,
5 classes, 5 playbooks, 5 qa, плюс этот README.
Локальное дополнение (27-й файл, вне поставки): inventory/submarine-motion-and-drift.md.
Результаты и границы стенда дрейфа — в [плане течений](../docs/plans/OCEAN_CURRENTS_PLAN.md) §2.

## Актуальность

Инвентари и досье — снимок игрового API от 2026-08-24, не гарантия для любой
новой сборки игры. `playbooks/gate-ui-by-camera-mode.md` содержит более поздние
уточнения по ручному FPP и завершению переходов.

Для действующих правил **мода**, а не исходного API:

- `../docs/CAMERA_GUIDE.md` — текущий PIP, интерьерный culling, поза и деплой;
- `../docs/plans/SEXTANT_PLAN.md` — контракт секстанта и журнал приёмки (§16.24–16.25);
- `../docs/live-diagnostics.md` — MCP, память, восстановление настроек и границы
  доказательства.

Сначала используйте эту базу знаний; затем, если факт изменился, проверяйте
соответствующий исходник/живой постфакт. Наличие метода или камеры в снимке
не доказывает, что она отправила геометрию на GPU.
