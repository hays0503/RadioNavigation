---
name: uboat-game-api
description: Use when writing or planning UBOAT mod code that touches game APIs (map, camera modes, saves, stories/decisions, weather/time, crew/stations, DI/resources) — points to the compiled knowledge base instead of re-exploring decompiled source.
---

# UBOAT Game API Knowledge Base

Compiled knowledge lives in `knowledge/` at the mod root. ALWAYS check it before exploring `C:\code\projects\uboat` directly.

## Lookup order

1. `knowledge/playbooks/` — ready recipes:
   - add-custom-map-marker.md — geo→map coordinates, marker lifecycle, save survival
   - gate-ui-by-camera-mode.md — MainCamera.CameraMode + GameUI.Mode gating, device-manual path
   - persist-mod-state-across-saves.md — three persistence strategies + pitfalls
   - radio-story-custom-decision.md — full Harmony cycle for RadioStory decisions
   - subscribe-weather-time-events.md — event-based observation without polling
2. `knowledge/classes/` — dossiers: Sandbox-cluster, Story-cluster, PlayerShip-cluster, Characters-cluster, DI-and-resources
3. `knowledge/inventory/patch-surface.md` — every virtual method in game source (grep it for Harmony targets)
4. `knowledge/inventory/events.md` — every game event (observe without patching)
5. `knowledge/inventory/reflection-contracts.md` — what the mod resolves via reflection today
6. `knowledge/catalogs/locale-keys.md` — candidate locale keys

## Graphs

- Mod questions: graphify MCP with project_path = this mod root
- Game-code questions: project_path = C:\code\projects\uboat
- Cross mod↔game links: project_path = C:\code\projects\cross-graph

## Rules

- Verify a signature in knowledge/ before using it; if missing, read game source once and ADD the finding to the relevant KB file.
- Never re-derive facts already written here (see STAR_SKY_PLAN.md §3 for sky/camera verified facts).
