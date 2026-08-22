## graphify

Knowledge graph at graphify-out/: god nodes, communities, cross-file relationships.

User types `/graphify` → use graphify skill/instructions first.

Rules:
- Codebase questions: run `graphify query "<question>"` first when graphify-out/graph.json exists. Relationships: `graphify path "<A>" "<B>"`. Concepts: `graphify explain "<concept>"`. Returns scoped subgraph — smaller than GRAPH_REPORT.md or raw grep.
- Dirty graphify-out/ files expected after hooks/incremental updates; not a reason to skip. Skip only if task is about stale/incorrect graph output or user opts out.
- graphify-out/wiki/index.md exists → use it for broad navigation, not raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain insufficient.
- After code changes, run `graphify update .` (AST-only, no API cost).

## Project identity

C# .NET Standard 2.0 Unity mod for [UBOAT](https://www.deepwaterstudio.com/uboot) (WW2 submarine sim). Consol/Sonne radio nav beacon panel, audio synthesis, bearing diagnostics. Namespace: `UBOAT.Mods.RadioNavigation`. Author: testui, Version: 2.0.

## Build system

Raw `csc.exe` via PowerShell — **not** `dotnet build`/MSBuild SDK. Needs local UBOAT install for Unity/game DLL refs.

```powershell
powershell -File build.ps1 [-GamePath <path>] [-Configuration Debug|Release] [-OutputDir .]
```

Default game path: `C:\Program Files (x86)\Steam\steamapps\common\UBOAT`. Output: `radionavigation.dll` in project root.

**Important:** game **recompiles mod from `Source\*.cs`** at load (Roslyn, `Mod.cs Compile()`) — build.ps1 DLL unused at runtime. Game compile defines: `UBOAT`, `UBOAT_2024_1`, `UNITY_STANDALONE_WIN` — **no `DEBUG`**, so `#if DEBUG` blocks stripped at runtime regardless of build.ps1 config. Local builds = IDE/static checks only.

## Architecture

- **Entry point**: `Source/Logic/Main.cs` — `IUserMod`, Harmony patching, DI injection, prefab instantiation
- **Harmony patches** target: `RadioStory.ContinueStory`, `Locale.RetrieveString` (2 overloads), `RadioStory.OnDecisionSelected`, `RadioStory.OnStopped`
- **DI framework**: UBOAT custom `DWS.Common.InjectionFramework` — needs Reflection permission in Manifest.json. Mod assembly injected explicitly via `InjectIntoAssembly` (`Mod.cs InitializeAssembly`), not framework `"uboat"` auto-injection (assembly name `radionavigation` doesn't match). `OnSceneAwake` handler injects scene objects.
- **UI**: Unity uGUI prefabs from AssetBundle (`Bundles/testui`). Hierarchy validated against hardcoded paths at runtime (Main.cs:165); destroyed+skipped on failure.
- **Source layout**: `Data/` (static configs — 14 beacons, 5 radio bands), `Logic/` (core sim — morse keyer, nav patterns, bearing diagnostics), `Sound/` (DSP audio synthesis via `OnAudioFilterRead`), `UI/` (MonoBehaviour handlers)

## Gotchas

- Tests: 253 PASS `powershell -ExecutionPolicy Bypass -File tests.ps1` (xunit 2.9 + Runner, baseline ADR-014 `baseline-182-20260819`; 232 с 2026-08-22, 253 с 2026-08-22 — маркер-сайзинг + диагностика + TransformHelpers). `dotnet build -c Release/Debug` 0 errors. SAD 11 — 253 PASS.
- No lint, formatter, `.editorconfig`. Refactoring plans propose adding one.
- Harmony patches fail silently on game version change — targets renamed/removed. Check pre-flight log (`target methods not found`) on load.
- AssetBundle required at `Bundles/testui` relative to mod root. Unity editor builds it; mod loader expects it at runtime.
- `#if DEBUG` blocks in Main.cs dump prefab hierarchy but stripped from Release builds.
- **Game source**: `C:\code\projects\uboat` (full C# project, assembly `com.uboat.game`, Unity 2021.3.45f2, 0Harmony 2.2.2). All 5 Harmony targets verified statically (2026-08-01). API map: `refactoring-plan/game-source-findings/GAME_SOURCE_REFERENCE.md`.
- **RadioStoryIntegration dead (G1-G4, verified 2026-08-01)**: `stage` field missing (RSI.cs:95 → null); `InsertDecision`/`OnUpdated` looked up on `RadioStory.BaseType` (=GenericStory) but declared in `Story` (GetMethod doesn't traverse bases); `LocalizationKey` readonly *field* in game, not property (RSI.cs:74 → null). Panel can't open via radio story. Details: `refactoring-plan/game-source-findings/`.
- `supportedGameVersions` in Manifest.json **not validated** by game (ModManager parses, never checks).
- AssetBundle built with Unity 2021.3.33f1 — compatible with game's 2021.3.45f2.
- Game does no own Harmony patching (0Harmony.dll ships for mods only); mod's `new Harmony("UBOAT.Mods.RadioNavigation")` → `UnpatchAll()` hits only its own patches.

## Documentation sources

- `SAD/` — 16-chapter architecture doc (decisions, code smells, refactoring roadmap)
- `refactoring-plan/` — 20 incremental refactoring plans (`REFACTORING_PLAN.md` through `REFACTORING_PLAN20.md`)
- `refactoring-plan/hypothesis/INDEX.md` — 207 verified findings (194 confirmed, 13 rejected) with status column
- `refactoring-plan/VERIFICATION_REPORT.md` — verdicts, duplicates, Phase 5 game-source verification
- `refactoring-plan/game-source-findings/` — G1-G4 (dead RadioStoryIntegration chain) + `GAME_SOURCE_REFERENCE.md` (mod↔game API map)
- `.opencode/opencode.json` — graphify.js plugin config for OpenCode