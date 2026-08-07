## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

When the user types `/graphify`, use the installed graphify skill or instructions before doing anything else.

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- Dirty graphify-out/ files are expected after hooks or incremental updates; dirty graph files are not a reason to skip graphify. Only skip graphify if the task is about stale or incorrect graph output, or the user explicitly says not to use it.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).

## Project identity

C# .NET Standard 2.0 Unity mod for [UBOAT](https://www.deepwaterstudio.com/uboot) (WW2 submarine simulator). Adds a Consol/Sonne radio navigation beacon panel with audio synthesis and bearing diagnostics. Namespace: `UBOAT.Mods.RadioNavigation`. Author: testui, Version: 2.0.

## Build system

Uses raw `csc.exe` via PowerShell — **not** `dotnet build` or MSBuild SDK workflow. Requires a local UBOAT game install for Unity/game DLL references.

```powershell
powershell -File build.ps1 [-GamePath <path>] [-Configuration Debug|Release] [-OutputDir .]
```

Default game path: `C:\Program Files (x86)\Steam\steamapps\common\UBOAT`. Output: `radionavigation.dll` in project root.

**Important:** the game **recompiles the mod from `Source\*.cs` itself** at load (Roslyn, `Mod.cs Compile()`) — the build.ps1 DLL is not used at runtime. The game's compile defines are `UBOAT`, `UBOAT_2024_1`, `UNITY_STANDALONE_WIN` — **no `DEBUG`**, so `#if DEBUG` blocks are stripped at runtime regardless of build.ps1 config. Local builds only matter for IDE/static checks.

## Architecture

- **Entry point**: `Source/Logic/Main.cs` — implements `IUserMod`, Harmony patching, DI injection, prefab instantiation
- **Harmony patches** target: `RadioStory.ContinueStory`, `Locale.RetrieveString` (2 overloads), `RadioStory.OnDecisionSelected`, `RadioStory.OnStopped`
- **DI framework**: UBOAT's custom `DWS.Common.InjectionFramework` — requires Reflection permission in Manifest.json. The mod's assembly is injected explicitly via `InjectIntoAssembly` (`Mod.cs InitializeAssembly`), not by the framework's `"uboat"`-filtered auto-injection (mod assembly name `radionavigation` doesn't match). `OnSceneAwake` handler injects scene objects.
- **UI**: Unity uGUI prefabs loaded from AssetBundle (`Bundles/testui`). Prefab hierarchy is validated against hardcoded paths at runtime (Main.cs:165). Destroyed and skipped if validation fails.
- **Source layout**: `Data/` (static configs — 14 beacons, 5 radio bands), `Logic/` (core simulation — morse keyer, navigation patterns, bearing diagnostics), `Sound/` (DSP audio synthesis via `OnAudioFilterRead`), `UI/` (MonoBehaviour handlers)

## Gotchas

- No test suite exists. SAD chapter 11 proposes tests but none are implemented yet.
- No lint, formatter, or `.editorconfig`. Refactoring plans propose adding one.
- Harmony patches silently fail if UBOAT game version changes — target methods may be renamed/removed. Check pre-flight log (`target methods not found`) on mod load.
- AssetBundle must exist at `Bundles/testui` relative to mod root. Unity editor build process creates it; the mod loader expects it at runtime.
- `#if DEBUG` blocks in Main.cs dump prefab hierarchy but are stripped from Release builds.
- **Game source available**: `C:\code\projects\uboat` (full C# project, assembly `com.uboat.game`, Unity 2021.3.45f2, 0Harmony 2.2.2). All 5 Harmony patch targets verified statically against it (2026-08-01). API map: `refactoring-plan/game-source-findings/GAME_SOURCE_REFERENCE.md`.
- **RadioStoryIntegration is currently dead (G1-G4, verified 2026-08-01)**: `stage` field doesn't exist (RSI.cs:95 → null), `InsertDecision`/`OnUpdated` are looked up on `RadioStory.BaseType` (=GenericStory) but declared in `Story` (GetMethod doesn't traverse bases), `LocalizationKey` is a readonly *field* in the game, not a property (RSI.cs:74 → null). Panel cannot be opened via the radio story. Details: `refactoring-plan/game-source-findings/`.
- `supportedGameVersions` in Manifest.json is **not validated** by the game (ModManager parses but never checks).
- AssetBundle mod is built with Unity 2021.3.33f1 — compatible with the game's 2021.3.45f2.
- The game itself does not Harmony-patch anything (0Harmony.dll ships for mods only); mod's own `new Harmony("UBOAT.Mods.RadioNavigation")` → `UnpatchAll()` affects only its own patches.

## Documentation sources

- `SAD/` — 16-chapter Software Architecture Document (architecture decisions, code smells, refactoring roadmap)
- `refactoring-plan/` — 20 incremental refactoring plans (`REFACTORING_PLAN.md` through `REFACTORING_PLAN20.md`)
- `refactoring-plan/hypothesis/INDEX.md` — 207 verified findings (194 confirmed, 13 rejected) with status column
- `refactoring-plan/VERIFICATION_REPORT.md` — verdicts, duplicates, Phase 5 game-source verification
- `refactoring-plan/game-source-findings/` — G1-G4 (dead RadioStoryIntegration chain) + `GAME_SOURCE_REFERENCE.md` (mod↔game API map)
- `.opencode/opencode.json` — configures graphify.js plugin for OpenCode
