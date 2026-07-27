## graphify

This project has a knowledge graph at `graphify-out/` with god nodes, community structure, and cross-file relationships.

- For codebase questions, run `graphify query "<question>"` first (uses `graphify-out/graph.json`). Use `path` for relationships, `explain` for focused concepts.
- Dirty `graphify-out/` files are expected after incremental updates — do not skip graphify because of them.
- If `graphify-out/wiki/index.md` exists, use it for broad navigation instead of raw source browsing.
- Read `graphify-out/GRAPH_REPORT.md` only for broad architecture review or when query/path/explain lack context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).

## Project identity

C# .NET Standard 2.0 Unity mod for [UBOAT](https://www.deepwaterstudio.com/uboot) (WW2 submarine simulator). Adds a Consol/Sonne radio navigation beacon panel with audio synthesis and position fixing. Namespace: `UBOAT.Mods.RadioNavigation`. Author: testui, Version: 2.0.

## Build system

Uses raw `csc.exe` via PowerShell — **not** `dotnet build` or MSBuild SDK workflow. Requires a local UBOAT game install for Unity/game DLL references.

```powershell
powershell -File build.ps1 [-GamePath <path>] [-Configuration Debug|Release] [-OutputDir .]
```

Default game path: `C:\Program Files (x86)\Steam\steamapps\common\UBOAT`. Output: `radionavigation.dll` in project root.

## Architecture

- **Entry point**: `Source/Logic/Main.cs` — implements `IUserMod`, Harmony patching, DI injection, prefab instantiation
- **Harmony patches** target: `RadioStory.ContinueStory`, `Locale.RetrieveString` (2 overloads), `RadioStory.OnDecisionSelected`, `RadioStory.OnStopped`
- **DI framework**: UBOAT's custom `DWS.Common.InjectionFramework` — requires Reflection permission in Manifest.json. Injects into assembly on `OnSceneAwake`.
- **UI**: Unity uGUI prefabs loaded from AssetBundle (`Bundles/testui`). Prefab hierarchy is validated against hardcoded paths at runtime (Main.cs:165). Destroyed and skipped if validation fails.
- **Source layout**: `Data/` (static configs — 14 beacons, 5 radio bands), `Logic/` (core simulation — morse keyer, navigation patterns, position fixing), `Sound/` (DSP audio synthesis via `OnAudioFilterRead`), `UI/` (MonoBehaviour handlers)

## Gotchas

- No test suite exists. SAD chapter 11 proposes tests but none are implemented yet.
- No lint, formatter, or `.editorconfig`. Refactoring plans propose adding one.
- Harmony patches silently fail if UBOAT game version changes — target methods may be renamed/removed. Check pre-flight log (`target methods not found`) on mod load.
- AssetBundle must exist at `Bundles/testui` relative to mod root. Unity editor build process creates it; the mod loader expects it at runtime.
- `#if DEBUG` blocks in Main.cs dump prefab hierarchy but are stripped from Release builds.

## Documentation sources

- `SAD/` — 16-chapter Software Architecture Document (architecture decisions, code smells, refactoring roadmap)
- `refactoring-plan/` — 20 incremental refactoring plans (`REFACTORING_PLAN.md` through `REFACTORING_PLAN20.md`)
- `.opencode/opencode.json` — configures graphify.js plugin for OpenCode
