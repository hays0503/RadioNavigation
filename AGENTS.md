# UBOAT Mod: TestUI Radio Panel

**Stack**: C# / Unity / Harmony. Mod for UBOAT 2026.1.

## Structure
- `Manifest.json` — mod descriptor. `assemblyName: "testui"`, requires `"Reflection"` permission.
- `Source/*.cs` — 8 source files, namespace `UBOAT.Mods.TestUI`.
- `Bundles/testui` — pre-built Unity AssetBundle (46 MB). Contains `Radio.prefab` and textures.

## Entry point
`Main.OnLoaded()` (`IUserMod`). Called by game mod loader.
- Applies 5 Harmony patches: `RadioStory.ContinueStory` (postfix), `Locale.RetrieveString` x2 (prefix), `RadioStory.OnDecisionSelected` (prefix), `RadioStory.OnStopped` (postfix)
- `OnSceneAwake` triggers DI injection (`InjectionFramework.InjectIntoAssembly`)
- `OnSceneStart` spawns `resourceManager.InstantiatePrefab("Radio")` into `/GUI/Viewport` (Main Scene only)
- All MonoBehaviours attached dynamically (not on prefab)

## Component tree
```
Radio (prefab)                    → RadioHandler (singleton), CanvasGroup, PanelDragHandler
├── Cursor (or first child)       → CursorHandler (extends BlockInputUI)
├── Toggle                        → ToggleHandler
├── ciferbllat                    — dial face (referenced from ToggleHandler)
├── Ground                        → GroundHandler
│   └── Background
│       └── GroundCheckmark
└── Expander                      → ExpanderHandler
```

## Key classes
- **RadioHandler** — singleton (`Instance`), extends `BlockInputUI`. Manages visibility via `CanvasGroup`. Escape key hides panel. `CursorAngle` tracks dial rotation.
- **RadioStoryIntegration** — core mod logic via reflection:
  - Injects custom `T3PLL38` decision into RadioStory decision menu at stage=1
  - Overrides `Locale.RetrieveString` to provide RU/EN labels
  - Handles decision selection (T3PLL38 toggles panel, LeaveStation hides it)
  - Hides radio on story stop
  - Decision constructor resolved by parameter count + 4th param `Action` type (fragile)
- **PanelDragHandler** — generates 32x32 crosshair cursor texture programmatically. Blocks drag if originating from `ExpanderHandler`.
- **GroundHandler** — toggles ground state via alpha (`DimAlpha=0.4` / `MaxAlpha=1.0`). Disables Toggle when ground is dimmed.

## Build
No build system in repo. Compile externally with game assemblies + Harmony references. Output: `testui.dll`.

## Deploy
`%LOCALAPPDATA%\Deep Water Studio\UBOAT\Mods\testui\` — place `testui.dll`, `Manifest.json`, `Bundles/testui`.

## Debug
Logs with `[TestUI]` prefix. Check Player.log or in-game mod console.
- `GroundHandler.StateLog()` — full ground/background/checkmark/toggle state
- `ToggleHandler.DebugImg()` — per-image debug (active, sprite, alpha, size, pos)

## Key quirks
- `Documents.prefab` in AssetBundle is unused (planned feature)
- Decision constructor resolved by parameter count (fragile reflection)
- `[NonSerializedInGameState]` on `Main` — prevents save serialization
- `ciferbllat` = "циферблат" (dial face)
- `PanelDragHandler` generates cursor texture in code (no asset)
- No tests, no .gitignore, no CI
- Russian commit messages
