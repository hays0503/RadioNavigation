## graphify

Knowledge graph at graphify-out/: god nodes, communities, cross-file relationships.

User types `/graphify` → use graphify skill/instructions first.

Rules:
- Codebase questions: run `graphify query "<question>"` first when graphify-out/graph.json exists. Relationships: `graphify path "<A>" "<B>"`. Concepts: `graphify explain "<concept>"`. Returns scoped subgraph — smaller than GRAPH_REPORT.md or raw grep.
- Dirty graphify-out/ files expected after hooks/incremental updates; not a reason to skip. Skip only if task is about stale/incorrect graph output or user opts out.
- `graphify-out/wiki/` is NOT generated in this project — do not reference it. Navigation entry points are `graphify query/path/explain` (preferred) and `graphify-out/GRAPH_REPORT.md`.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain insufficient. This project has no `graph.html` or generated wiki. Graph snapshot 2026-11-03: 11052 nodes / 19679 edges / 813 communities; hooks can change these numbers.
- After code changes, run `graphify update .` (AST-only, no API cost).
- MCP server gotcha (2026-08-24): server default project path resolves to sibling `testui` mod dir — always pass explicit `project_path` = this project root to query/stats/god-nodes tools.
- **Game-source graph** (2026-08-24): second graph at `C:\code\projects\uboat\graphify-out\` (32140 nodes / 78914 edges / 814 communities, full inference). For game-code questions pass `project_path = C:\code\projects\uboat`. Game digests (Stories/Mods/DI/Time+Weather/Map/Camera/AI/Characters/Sandbox/Serialization/DWS infra) — in supermemory.
- **Cross-repo graph** (2026-08-24): merged mod+game graph at `C:\code\projects\cross-graph\graphify-out\` (36360 nodes / 84570 edges) — use `project_path = C:\code\projects\cross-graph` for questions spanning both codebases (mod↔game patch-target links). Rebuild after either source graph changes: `graphify merge-graphs <mod graph.json> <game graph.json> --out C:\code\projects\cross-graph\graphify-out\graph.json`.

## supermemory

Persistent memory (MCP tool `supermemory`), project scope tagged radio-navigation. Server: WSL distro `Ubuntu-26.04`, two systemd units — `supermemory.service` (:6767) + `smproxy.service` (:6768, injects `x-opencode-session`/User-Agent headers and fails over OpenRouter free (×3) → Go paid (`muse-spark-1.3-contributor` → `glm-5.3-flash` → `qwen3.8-flash` → `mimo-v2.5`); Zen free dropped 2026-09-20 — it 403s outside OpenCode). Control from Windows: `powershell -File "C:\Users\Sergey Pimenov\.supermemory\smctl.ps1" status|start|stop|restart|logs|doctor|health`, or Desktop `Supermemory\` shortcuts. Auto-start: Startup `SupermemoryBoot.vbs` boots WSL → systemd starts the units; `SupermemoryHealth` task every 5 min (toast + restart on failure). Logs `~/.supermemory/logs/{sm,smproxy}.log`; keys `~/.supermemory/keys/{or,zen,go}.txt`. Plugin: `opencode-supermemory` (global opencode.jsonc), config `~/.config/opencode/supermemory.json`.

Do NOT start the server with `npx supermemory local` — it lacks the LLM-proxy env and the memory agent fails with `No model provider configured`. smproxy skips HTTP 200 responses whose assistant `content` is null (reasoning-only models) and moves to the next provider. **OpenCode Go endpoint split (verified 2026-09-21):** each model family lives on its own path — `/v1/chat/completions` (glm/deepseek/kimi/mimo/longcat/hy), `/v1/messages` (minimax/qwen, anthropic auth = `x-api-key`, not Bearer), `/v1/responses` (muse-spark/grok-4.6/gpt-5.6-luna). Calling a Responses-only model on chat/completions returns HTTP 500 and looks like a dead provider; `smproxy.py` routes via `MODEL_ENDPOINTS` + translates both ways (`_chat_to_responses`/`_responses_to_chat`/`_chat_sse`) with `max_output_tokens` floored at 4096 (else reasoning burns the budget and emits nothing). Two more live-verified traps (2026-09-21): assistant turns must use `output_text`, not `input_text` (Responses API rejects it → 400); and `_has_content` must treat a reply whose `content` is null but which carries `tool_calls` as VALID — otherwise every tool-call turn is discarded and the walk cascades through the whole chain. `deepseek-v4.1-flash` was dropped from the go chain: its thinking mode demands the prior assistant turn echo back `reasoning_content`, so any OpenAI-shaped multi-turn tool client gets HTTP 400. muse-spark trains Meta models (not ZDR) — reorder the go chain if that matters. The Zen gateway also needs a real `User-Agent` (python urllib default → 403 on `/v1/models`).

Rules:
- Auto-capture ON: `captureEveryNTurns: 5`. Verified configuration 2026-10-04: `autoRecallEveryPrompt=false`; manual recall remains available. Verify loaded routing with `supermemory(mode:"help")`: `runtime.memoryDestinationVersion="origin-bound-v1"`, directory, tag, project ID. File hashes or `status=active` alone are not loaded-version proof.
- As of 2026-08-24 memory holds ~58 architecture-type records: SAD digests, all ADRs (001-029), subsystem internals (bearing math, DSP chain, astro math, lighthouse, chronometer, radio stories, morse timing, zones), data files (Leuchtfeuer.json registry, Sonne tables), build/test/UI infrastructure, tooling gotchas. Search before re-deriving facts.
- Manual adds for verified findings: `supermemory(mode:"add", scope:"project", type:"architecture")`; user preferences go to scope:"user".
- Manual tools and recall follow the current session directory; deferred captures/summaries and retries keep their original destination. Historical mixed-project records were not migrated. Details and limits: `docs/live-diagnostics.md`.

## knowledge/

Compiled game/modding knowledge base (built 2026-08-24 during free-token window; **26 файлов** на 2026-11-03):
- `knowledge/inventory/patch-surface.md` — все virtual-методы игрового кода (2562 файла, цели Harmony)
- `knowledge/inventory/events.md` — все события игры (наблюдение без патчей)
- `knowledge/inventory/reflection-contracts.md` — рефлексия мода vs декларации игры
- `knowledge/catalogs/locale-keys.md` — 236 кандидатов locale-ключей
- `knowledge/classes/` — досье: Sandbox, Story, PlayerShip, Characters, DI+Resources
- `knowledge/playbooks/` — рецепты: маркеры карты, сейв-персистентность, решения RadioStory, гейтинг UI по камере, погода/время через события
Подключено как opencode reference `uboat-kb` + skill `uboat-game-api`. Проверяй KB до чтения исходников игры.
- `docs/plans/SEXTANT_PLAN.md` — реализация и журнал приёмки, не новый чеклист на исполнение. Начинать с актуальной шапки, `docs/CAMERA_GUIDE.md` и §16.23–16.25; исходный §7 исторический.

## Project identity

C# .NET Standard 2.0 Unity mod for [UBOAT](https://www.deepwaterstudio.com/uboot) (WW2 submarine sim). Consol/Sonne radio nav beacon panel, audio synthesis, bearing diagnostics, accurate star sky. Namespace: `UBOAT.Mods.RadioNavigation`. Author: testui, Version: 2.1 (= Manifest.json).

## Build system

**Layout 2026-10-05:** scripts are in `Tools/`; developer projects are in `Dev/`
(`Mod/`, `Analysis/`, `Tests/`, `RadioNavigation.Analyzers/`,
`RadioNavigation.Analyzers.Tests/`, `RadioNavigation.SourceCheck/`). SAD, audit and
archives are in `docs/SAD/`, `docs/refactoring-plan/`, `docs/archive/`.
Older journal paths below are historical: translate them using
`docs/PROJECT_LAYOUT.md`. Runtime paths, knowledge, graph and memory stay at root.
Run the full gates with `powershell -ExecutionPolicy Bypass -File Tools/gates.ps1`.

Raw `csc.exe` via PowerShell — **not** `dotnet build`/MSBuild SDK. Needs local UBOAT install for Unity/game DLL refs.

```powershell
powershell -File Tools/build.ps1 [-GamePath <path>] [-Configuration Debug|Release] [-OutputDir .]
```

Default game path: `C:\Program Files (x86)\Steam\steamapps\common\UBOAT`. Output: `artifacts/build/<Configuration>/radionavigation.dll` (default Release); an explicit `-OutputDir` overrides it. Reports live under `artifacts/reports/`; folder layout: `docs/PROJECT_LAYOUT.md`.

**PVS-Studio gate (always runs, blocking):** after a successful csc compile, `Tools/build.ps1` invokes `Tools/pvs.ps1`, which analyzes `Dev/Analysis/RadioNavigation.Analysis.csproj` (net48 + real game DLLs) with `PVS-Studio_Cmd.exe` and converts the report to `artifacts/reports/pvs/pvs-report.txt`. A non-zero PVS result fails the build. Run standalone via `powershell -File Tools/pvs.ps1 [-Configuration Debug|Release] [-Json] [-Sarif]`. Suppressions live in `Dev/Analysis/RadioNavigation.pvsconfig` (referenced by Analysis.csproj) — disabled codes are Unity/extension-guard false positives documented there; do NOT re-add inline `//-V:` comments (C++-only, ignored for C#). Keep PVS clean before committing.

**Important:** game **recompiles mod from `Source\*.cs`** at load (Roslyn, `Mod.cs Compile()`) — Tools/build.ps1 DLL unused at runtime. Game compile defines: `UBOAT`, `UBOAT_2024_1`, `UNITY_STANDALONE_WIN` — **no `DEBUG`**, so `#if DEBUG` blocks stripped at runtime regardless of Tools/build.ps1 config. Local builds = IDE/static checks only.

**C# language version = 7.0 (game RoslynCSharp contract, verified in-game 2026-09-03):** the game compiler rejects anything newer (e.g. `default;` literal = C# 7.1 → CS8107) and then loads a broken assembly (`BadImageFormatException`). Mod sources must stay C# 7.0. Enforced statically in three places: (1) `build.ps1` Roslyn branch passes `/langversion:7`; (2) `ModCompiler.GameParityParseOptions` (C#7 + game defines, no DEBUG) pins every mod-source parse in SourceCheck, so `Mod_CompilesAgainstCleanGameApi` fails offline on any newer-feature use; (3) the same test also fails on CS0169 (never-used field — dead code after extractions). CS0649 (never-assigned `[Inject]` fields) is the DI pattern, allowed. Consequence: never use `const bool` as a feature gate — const folds away and produces CS0162 unreachable-code in the game compiler; use `static readonly bool`.

## Static analysis (Roslynator + custom Harmony analyzer)

- `powershell -File Tools/analyze.ps1 [-Profile quick|full] [-Severity info|warning|error] [-Fix] [-Json]` — Roslynator CLI over `Dev/Analysis/RadioNavigation.Analysis.csproj` (net48 + real UBOAT/Unity DLLs from `UBOAT_Data\Managed`). Quick=155 RCS, full=3084. Restore-first guards shared-`obj/` assets corruption.
- `RadioNavigation.Analyzers/` — custom Roslyn analyzers validating **imperative** Harmony patching (now in `HarmonyPatchInstaller.cs`; early measurements below refer to Main before extraction). Drives from each `harmony.Patch(...)` call and resolves targets/handlers **backward through data flow** via `HarmonyPatchCandidateResolver` (`HarmonyPatchCandidate` model): direct/named args, arrays of anonymous objects iterated by `foreach`, Type-variable receivers (`patchType.GetMethod(...)` → follows `typeof()` initializer), conditional/HarmonyMethod wrappers. Signature checks: special params (`__instance`/`__result`/`__state`/`__originalMethod`/`__args`), ref/out, and **strict type equality** for named params (deliberately NOT assignability — catches `HandleCustomDecisions(object decision)` vs target `Decision decision`). Wired into Analysis.csproj as `<Analyzer>` item → runs alongside RCS under `analyze.ps1`.
- **Metadata fallback (Step 2, done 2026-08-31):** when Roslyn cannot resolve a target, `HarmonyTargetResolver` consults `GameMetadataIndex` (raw ECMA-335 metadata over `System.Reflection.Metadata`, no file IO — reads the compilation's own `PortableExecutableReference.GetMetadata()`, cache keyed by `pe.FilePath` only; index is Harmony-ignorant and reusable). Result enum `MetadataLookupResult`: Unavailable / TypeNotFound / MethodNotFound / Found / Ambiguous. Resolution: Found→no diag (obfuscation hid it); Absent→RN0001 (any source); Ambiguous→RN0002; Unavailable→RN0001 only for in-source types. Overload-aware `FindMethod(type,name,paramTypes,requireStatic)` disambiguates same-named overloads via explicit `typeof(...)` param list AND BindingFlags static/instance pin (`BindingFlags.Static` parses as member-access syntax). Param-type matching canonicalizes both sides to simple names (`global::System.String`↔`String`) because obfuscation strips namespaces. Files: `Metadata/{GameMetadataIndex,MethodMetadata,ParameterMetadata,MetadataTypeName}.cs`, `HarmonyTargetResolver.cs`. `InternalsVisibleTo("RadioNavigation.Analyzers.Tests")` in `Properties/AssemblyInfo.cs`.
- **Tests**: `dotnet run --project Dev/RadioNavigation.Analyzers.Tests` — **68 PASS / 0 FAIL**, repeated 2026-10-04 (55 was the 2026-08-31 snapshot), via **in-project custom runner** (`Program.cs`, reflection dispatch, no vstest; matches repo convention because vstest testhost crashes on restricted tokens). Direct `CompilationWithAnalyzers` harness (NOT `Microsoft.CodeAnalysis.Testing` — its `ReferenceAssemblies` static ctor throws under the custom runner). `GameMetadataIndexTests.cs` references the REAL `com.uboat.game.dll` (skips gracefully if absent) and pins: ShipControlUI=87 methods, ApplyToolPositionLimits=2 overloads (instance+static), RetrieveString overloads distinct, Roslyn-missing-metadata-found→no RN0001, DefinitelyNotAMethod→RN0001, name-only ambiguous→RN0002.
- **Step 3 (done 2026-08-31): Harmony overloads/constructors/BindingFlags.** (a) BindingFlags held in a local variable (Main.cs `anyStatic = Public|NonPublic|Static`) is followed back to its initializer via `FollowBindingFlagsVariable` so the static/instance pin still disambiguates hidden overloads. (b) `GetConstructor(Type[])` supported → resolves `.ctor` through raw metadata (index already flags constructors); `ResolveTargetMethod` is now signature-aware (filters candidates by parameter count + canonical type names) so a wrong-signature GetConstructor/GetMethod correctly falls through to metadata → RN0001 instead of silently binding the first overload. Param-type matching canonicalizes both sides to simple names (`global::System.String`↔`String`). Real Main.cs full analysis: exactly 1 RN0003, zero RN0001/RN0002 false positives.
- **Step 4 (done 2026-08-31): Unity lifecycle rules** — new `UnityLifecycleAnalyzer.cs` (separate `DiagnosticAnalyzer`, category "Unity"). **RN0006** flags per-frame lookups (`GetComponent*`, `FindObjectOfType*`, `GameObject.Find*`) called directly inside `Update/LateUpdate/FixedUpdate` bodies — cache in Awake/Start instead. Deliberately EXCLUDES `Instantiate`/`Destroy` (legit one-shot spawn/cleanup in Update; LighthousePlacer self-destroy was a false positive). **RN0007** flags allocations inside `OnAudioFilterRead(float[],int)` only: `new` ref-types, LINQ methods, boxing (via invocation args + EqualsValueClause + SimpleAssignment), string concat/interpolation. Real mod: audio path is allocation-clean → zero RN0007; 3 genuine RN0006 hits (NavMapMarkerBase:92/99, StationMapOverlay:36 — all `FindObjectOfType` in Update).
- **Step 5 (done 2026-08-31): Unity main-thread API misuse** — **RN0008** flags Unity main-thread-only API called from an explicit background context: `Task.Run`, `Task.Factory.StartNew`, `ThreadPool.QueueUserWorkItem`, `Parallel.For/ForEach/Invoke`, `new Thread`. Only fires when the background context is PROVABLE (delegate/lambda arg to those executors); ordinary methods are never flagged because Roslyn can't prove they run off the main thread. Detection: member-access receiver type in UnityEngine namespace (or base-type walk) + bare-invocation fallback for known Unity API names when the symbol is unresolved (stub/obfuscation artifact). Receiver-type-name falls back to syntax when semantic resolution is ambiguous (e.g. stub Task colliding with BCL Task). Real mod has NO threading → zero RN0008, purely preventive.
- **Step 6 (done 2026-08-31): Harmony handler return-type safety** — **RN0009** validates the Harmony handler return contract in `ValidateHandlerReturnType` (hooked into `ValidateHandler`): prefix must return `void` or `bool`, postfix must return `void`; a wrong return type breaks the patch at runtime (non-bool prefix can't skip original). Real handlers all valid (`InjectT3PLL38Decision` void, `OverrideLocaleKey` bool, `HandleCustomDecisions` bool, `HideRadioOnStop` void) → zero RN0009.
- **Step 7 (done 2026-08-31): Reflection hazards** — new `ReflectionHazardAnalyzer.cs` (separate `DiagnosticAnalyzer`, category "Harmony"). **RN0010** flags a reflection member lookup by string name (`GetMethod`/`GetField`/`GetProperty`/`GetConstructor`) whose result is dereferenced immediately without a null guard: walks up from the lookup invocation; reports when it's the receiver of a NON-null-conditional member access / element access; safe under `?.` or when assigned-then-checked. Game assemblies are obfuscated so string-name lookups can return null → NRE on rename. Real mod: 3 genuine hits (StoryDecisionGateway:165 `GetProperty("Count").GetValue`, SextantController:2620/2627 `GetProperty(...).SetValue`), zero false positives. Test suite grew to 55 PASS.
- **OBFUSCATION (verified 2026-08-31):** game assemblies are obfuscated — Roslyn's semantic model **under-reports members** vs raw metadata. Probe: `ShipControlUI` raw metadata = 87 methods, Roslyn `GetMembers()` = 30 (missing `ApplyToolPositionLimits`, `UpdateRudder`, `LateUpdate`, etc.). Runtime reflection still finds them, so the mod works — it was purely a static-analysis blind spot, now closed by the metadata fallback above. Don't trust Roslyn member completeness against game DLLs.
- **IDE0051/IDE0052 dead-member audit (done 2026-09-04, risk register iter6):** `analyze.ps1 -IdeAudit` builds Analysis.csproj with `-p:EnforceCodeStyleInBuild=true` and triages `IDE0051/IDE0052/CS0414` into genuine vs Unity-noise (engine-called allowlist in the script). Enforcement is CLI-only — the repo carries just two inert severity lines in `.editorconfig` (raw csc and Roslynator CLI never surface IDE rules). Findings matrix (verified against docs): `private`+no refs → IDE0051 (dup of RCS1213 for consts/methods); write-only field → IDE0052 (NOT 0051 — a write counts as a reference); read-but-unused-value (null-guard) → NO rule, manual audit only; `internal` → invisible to both, covered by RCS1213+behavioral gates. First run fixed 6: `WorldRadiusM` dup ×2 (live copy: `CameraResolver`), `opaqueViewMaterial` + `pipRect` (images already hold the refs — verified in `SextantOcularBuilder`), `toggleRect` (Unity invariant: Graphic implies RectTransform), `LogPipStack` (dead debug method, zero callers). CA1823 deliberately NOT enabled (IDE0052 covers its niche, CA1823 is off-by-default anyway). CS0414 stays in `NoWarn` (comparison run: IDE0052 subsumes it, compiler path adds no unique signal).

## Neural-slop guard (RadioNavigation.SourceCheck)

Compiles the mod's `Source\**\*.cs` against a **clean, non-obfuscated signature-only reference** built from the real game source (`C:\code\projects\uboat`), so a hallucinated/wrong-signature game API call fails the build instead of passing silently against the obfuscated DLL. Run: `dotnet run --project Dev/RadioNavigation.SourceCheck -c Release` → **146 PASS / 0 FAIL** (2026-11-03). Counts elsewhere in dated entries are historical.

- **`GameSource_CompilesClean`**: the dependency-closure subset of the decompiled game source (only files the mod's game-API surface touches) must compile cleanly with bodies stripped. If this fails, the clean reference is unavailable and the guard can't run.
- **`Mod_CompilesAgainstCleanGameApi`**: compiles the mod against the emitted clean reference + real Unity/third-party DLLs. Any CS Error = the mod calls a game API that doesn't exist / has the wrong signature → neural slop → FAIL. Cached at `obj/cleanref/com.uboat.game.clean.dll` (`CleanReferenceCache`), invalidated by game-source/DLL/mod-source content hash.
- **Gate in `Tools/analyze.ps1`**: SourceCheck runs as a **blocking gate** before Roslynator analysis (skip with `-SkipSourceCheck`). A FAIL stops the analysis with exit 1. **Performance (measured 2026-09-26)**: first run after an input change ~294 s (full seed comparison), then ~16 s — `SeedQuality_NewVsOld`/`DllOnlyTypesAreBounded` skip via per-test stamps (`seedquality-<test>.stamp` next to the clean-ref cache) when game sources, Managed DLLs, mod sources and this tool's own sources are unchanged; the stamp is written only on PASS, so a failure always re-verifies. `Tools/analyze.ps1` prints the SourceCheck/Restore/Roslynator split via `Write-PhaseTimings` before every terminal exit.
- **`Probe_MissingTypes`**: reports which referenced types resolve to which Managed DLL (sanity probe).
- **Cyclomatic-complexity gate** (2026-09-02): `MethodComplexity_NoSuperMethods` computes cyclomatic complexity over the mod's own `Source\**\*.cs` (1 + if/else-if, switch case, for/foreach/while/do, `?:`, `&&`/`||`/`??`, conditional-access, catch; nested lambdas/local functions counted as separate methods). Threshold **15**, FAIL on exceed → super-method refactor candidate. All 14 methods that exceeded 15 were refactored into sub-methods (2026-09-02) — gate now PASSes.
- **Seed discovery** (`DependencyClosure.ComputeSeedTypes`): two passes — (1) syntax pass for any simple type name the mod uses that exists in the game source (catches obfuscation-renamed types like `Periscope`); (2) **member-chain expansion** — resolves mod member-access chains (`gameMainCamera.GenericController.ExternalController`) against the game-source member index, iterating to a fixed point, so types reachable only as property return types (e.g. `GenericCameraController` via `MainCamera.GenericController`) become seeds too. (A semantic pass against the obfuscated DLL was removed 2026-09-02 — it was redundant: every type it found also exists in the game source, and it cost ~205s.)
- **Quality regression tests** (2026-09-02): `SeedQuality_NewVsOld` re-runs the old semantic+ syntax+member-chain algorithm and asserts the seed set (names + arity) is identical to the new one — 0 diffs. `SeedQuality_DllOnlyTypesAreBounded` asserts **0** types in `com.uboat.game.dll` are missing from the game source, so the syntax pass cannot miss any DLL type (the "type in DLL but not source" hole is empty). Both PASS.
- **Reference DLLs** (`ModCompiler.ModRefNames`): clean game assembly + BCL (net40 mscorlib/System) + `0Harmony`, `com.uboat.modapi` (InjectAttribute/ISingleton), `com.uboat.rendering` (AdditionalCameraData), `com.uboat.dependencies` (TOD_Sky/LipSync/Blake2), TextMeshPro, postprocessing, water, UnityEngine modules. `com.uboat.game.dll` is deliberately EXCLUDED (that's the obfuscated one the whole guard replaces).
- **Stub machinery** (`DependencyClosure`): non-seed referenced types are stubbed as empty classes/enums/interfaces in the correct namespace, with generic arity, nested types, enum members, and member-property stubs (`<M:T,...>`) resolved from the full-source member index. Broken `using` namespaces stripped only for `UBOAT./DWS.` (never `UnityEngine.`). Diagnostic messages are Russian with double quotes — parsers handle both quote styles.

## Architecture

- **Entry point**: `Source/Logic/Main.cs` — thin `IUserMod` facade → `ModBootstrapper` (DI, lifecycle, bundle); patches → `HarmonyPatchInstaller`, content → `SceneContentBuilder`, validation → `PrefabValidator`.
- **Radio/locale Harmony targets**: `RadioStory.ContinueStory` (postfix), `Locale.RetrieveString` (2 prefix overloads), `RadioStory.OnDecisionSelected` (prefix), `RadioStory.OnStopped` (postfix). Additional render/sky patches are separate; radio preflight 5/5 is not a count of all hooks.
- **DI framework**: UBOAT custom `DWS.Common.InjectionFramework` — needs Reflection permission in Manifest.json. Mod assembly injected explicitly via `InjectIntoAssembly` (`Mod.cs InitializeAssembly`), not framework `"uboat"` auto-injection (assembly name `radionavigation` doesn't match). `OnSceneAwake` handler injects scene objects.
- **UI**: Unity uGUI prefabs from AssetBundle (`Bundles/testui`). Hierarchy validated by `PrefabValidator` against `PrefabNames` paths; destroyed+skipped on failure. Sextant overlay is built programmatically.
- **Source layout**: `Data/` (static configs — 5 beacons CAP 59, 5 radio bands), `Logic/` (core sim — morse keyer, nav patterns, bearing diagnostics), `Sound/` (DSP audio synthesis via `OnAudioFilterRead`), `UI/` (MonoBehaviour handlers)

## Актуальные контракты и приёмка (2026-10-05)

- Документация: `DOCS.md` → `README.md`, `docs/CAMERA_GUIDE.md`, `docs/live-diagnostics.md`. Датированные эксперименты и отозванные гипотезы — в `docs/archive/AGENTS_GOTCHAS_JOURNAL.md`; позднее измерение имеет приоритет над ранней гипотезой. Исторические `PENDING`/счётчики не считать текущей сводкой.
- PIP рендерится **attached либо standalone fallback**, не attached + enabled. Внутри `RefreshMirroredState(viewCamera)` идёт **до** `InteriorMirror.Sync(insideBoat)`; последний выставляет `RenderScene=true` и полный culling rect только внутри. Лампа: причинный RenderDoc A/B/A′ **0→12→0** PIP / **1→1→1** main, продуктовый capture и 12 checkpoints — `docs/plans/SEXTANT_PLAN.md` §16.25. Не трогать pose/алидаду/свет/шейдер ради этого уже доказанного фикса.
- Оба луча внутри переводятся через `SextantPipFrameMath.ToParentFrame` (inverse parent × world); позиция берётся с игровой InteriorCamera. Единое преобразование сохраняет угол алидады. Open-edge очищает только обе PIP-истории (ResetHistory + флаг), не main. Первый кадр: 24/24 записанных B-открытий на 30 fps чистые (§16.24), это не гарантия всех кадров/пресетов.
- Реальный деплой — `Tools/run-game.ps1` / `Tools/deploy-radionav.ps1`: touch Source + restart + игровая компиляция. DLL из Tools/build.ps1 не копировать в Temp. Скрипт требует ровно McpBridge + RadioNavigation, останавливает игру (даже с `-NoRestart`) и при обычном запуске quick-load'ит последний сейв; не запускать без необходимости/разрешения на смену живого состояния.
- Render snapshot — конфигурация, не submission/fresh presented frame. `accepted=true,verified=false` — доставка ввода, не игровое постусловие. Обычный проход через люк и внешняя эстетическая приёмка звёзд/PSF остаются открытыми. Телепорт не заменяет люк. Recovery-журнал нельзя удалять вместо восстановления.
- Оптика: `RadioNav/UIOpaque` использует straight RGB + `SrcAlpha`, coverage только в alpha, рампа внутрь круга. Старое RGB `* coverage` ниже **не возвращать** — это двойное покрытие; отличительный маркер `_ApertureCoverageVersion`, а не legacy `_LuneWallAnchor`. Lune defaults: глубина 97%, ход 0.22 R, полный отклик 55°/с, attack/hold 0.22 с, release 0.65 с. Опрос 1 Гц по unscaled time; `PollSettings` после чтения вызывает `Write`. Исторические 95% / 0.15 R / 70°/с и Bind-only не текущий контракт (`docs/sextant-optical-visuals.md`).
- Карта: native points/save sanitizer реализованы, но `MapShapeEraser.IsOwned` перехватывает и FIX/LOP; физический LMB/drag/динамическое удаление не закрыты (`docs/plans/MAP_MARKERS_PLAN.md`). Phase5 частично реализован; наличие helpers не подтверждает immutable snapshot или весь исходный план.
- Астронавигация сохраняется в конкретный `.save`: `SextantSaveData : SceneObject` с bounded/versioned `Payload` **v3** (чтение v1/v2/v3; три наблюдения, frozen карточка/фикс, калибровка серии, course track, FIX/LOP и mask скрытия); native БД хранит drag-позиции/ссылки фигур. LoadingInitialization сбрасывает модель, хвост Loaded queue восстанавливает без пересчёта; ContentReady блокирует раннюю реконсиляцию. Новое I отменяет pending O-уборку и прежний triangle. Live 2026-10-05: Empty/One/Two/Three, продолжение, drag/hidden/линия и O на закрытой карте проверены; итоговый `RadioNav_AstroSave_20261004.save` записан и загружен после возврата backup. Старые 9 безымянных точек/2 линии и 9 protected hashes сохранены; настройки/Map/пауза возвращены. Это не физический pointer-тест. Подробности/receipt — `docs/astro-save.md`; старое «серия не переживает save/load» отменено.

## Операционные правила (действующие)

Полный исторический журнал экспериментов, отозванных гипотез, датированных
замеров и разборов дефектов перенесён без изменений в
`docs/archive/AGENTS_GOTCHAS_JOURNAL.md` (2026-08-01 … 2026-11-09). Здесь
осталось только то, что действует сейчас. Числа ниже — датированный снимок
прогона, а не постоянное обещание.

### Сборка, деплой, игра

- `Tools/build.ps1` — raw `csc`, C# **7.0**, игровые define
  `UBOAT;UBOAT_2024_1;UNITY_STANDALONE_WIN` (без `DEBUG`), затем блокирующий
  PVS-Studio. `#if DEBUG` в игре вырезан, поэтому им нельзя гейтить поведение.
- Игра компилирует мод из `Source/**/*.cs` в
  `%LOCALAPPDATA%Low\Deep Water Studio\UBOAT\Temp\radionavigation.dll`.
  DLL из `artifacts/build` в игру **не копировать** — это ломает загрузку.
- Запуск и деплой: `Tools/run-game.ps1` (touch Source → kill → прямой
  `UBOAT.exe "<launcher>" -quicklatestgamestate` → ожидание моста).
  Hot-reload сломан (остаётся старая копия сборки) — после правок рестарт.
- Harmony-цели молча пропадают при смене версии игры; проверять preflight
  `target methods not found` в логе загрузки.
- `supportedGameVersions` в `Manifest.json` игра не валидирует.

### Проверки

<!-- gate-snapshot: date=2026-11-03 tests=1653 sourcecheck=146 analyzers=68 -->

- `Tools/gates.ps1` = build → SourceCheck + quick → tests → PVS, fail-fast.
  `-Parallel` — быстрая/медленная линии.
- Снимок **2026-11-03**: tests **1653 PASS / 0 FAIL**,
  SourceCheck **146 PASS / 0 FAIL**, analyzers **68 PASS / 0 FAIL**.
  Историю счётчика не выдавать за текущий прогон.
- Один гейт: `dotnet run --project Dev/RadioNavigation.SourceCheck -c Release -- <подстрока>`.
- Мутация считается пойманной, если упал **любой** гейт. Проверять надо
  **вызов и его аргументы**, а не наличие имени: «объявлено» ≠ «используется».
- Гейт, кодирующий конструкцию, а не измерение, обязан быть обратимым и
  объяснять в комментарии, какое измерение его оправдывает.

### Бандл и шейдеры

- Шейдеры есть в `Source/Shaders`, но Unity-проект собирает **копию** из
  `C:\Users\Sergey Pimenov\Desktop\TestUI\Assets\Packages\testui`. Править обе;
  иначе бандл унесёт старое тело.
- Сборка: меню Unity `Build/Build TestUI Bundle`; проверка
  `python Tools\BundleCheck.py Bundles\testui`.
- Unity вырезает **тело** шейдера из бандла, оставляя блок `Properties`.
  Маркер свежести обязан быть свойством и меняться вместе с правкой.
- `Material` из бандла не наследует несериализованные shader-defaults;
  значения по умолчанию писать из кода.

### Граф и знания

- `graphify update .` после правок кода (AST-only, без API). Снимок
  **2026-11-03**: **11052 узла / 19679 рёбер / 813 сообществ** (сборка от `b00b96e`).
- MCP graphify: всегда передавать `project_path` = корень этого проекта,
  иначе сервер уходит в соседний `testui`.
- `knowledge/` — **26 файлов**, проверять до чтения исходников игры.
  `graphify-out/wiki/` в проекте не генерируется.

### Диагностика

- Мост: порт **28777**. Полный композит с UI — `/screenshot?mode=frame`;
  обычный `/screenshot` канвас не включает.
- Крутилки `PlayerPrefs` читаются при **открытии** прибора: после смены
  переоткрыть секстант. На паузе 1 Гц-поллинг остановлен.
- Чёрный композит: сначала проверить адаптер в логе
  (`Renderer: AMD Radeon(TM) Graphics` = iGPU) и число ненулевых пикселей.
  На iGPU пиксельная приёмка недействительна.
- `RadioNav.DebugAim=1` — строка Δ/в/г; `RadioNav.Lune*`, `RadioNav.Probe*` —
  крутилки оптики и пробы прицела.
- Проба прицела живёт только пока прибор открыт и режим — живой вид
  (не FreeFly). `ProbeRun` надо **увеличивать на 1**, иначе нет свежего запуска.

### Сейвы и карта

- Астрономия: `SextantSaveData`, payload **v3** (чтение v1/v2/v3), см.
  `docs/astro-save.md`. Загрузка восстанавливает серию без пересчёта.
- Маркеры карты — нативные `MapPointUI` (только они видны инструментам и
  ластику). Компонент искать **по имени типа**, а не по своему `Type`:
  в сцене бывают две копии сборки.
- Stale-набор маркеров не удалять целиком — только точные дубли; явные
  удаления идут через `CollectExplicitRemovals`.
- `MapSaveSanitizer` висит на `SavesManager.Loaded/SavingStarted`; перед
  `Destroy` точки — `RemovePointDataBeforeDestroy`.

### Документация

- `DOCS.md` — единственная карта. Новый `.md` = строка в карте; это проверяет
  гейт `Docs_EveryMaintainedFileIsMapped`.
- Счётчики — датированный снимок прогона; строка-маркер
  `<!-- gate-snapshot: ... -->` в `DOCS.md` и `AGENTS.md` должна совпадать.
- Все `.md` — UTF-8 без BOM, с завершающим переводом строки, без mojibake.
  PowerShell 5.1 показывает ложный mojibake — это артефакт консоли.

## Documentation sources

- **`DOCS.md` — карта документации: что читать и для чего, известные пробелы, правила сопровождения. Начинать отсюда.**
- `docs/SAD/` — 11 глав архитектуры (01-08, 10-12; глава 09 = roadmap, ушла в `docs/archive/09_Refactoring_Roadmap.md`, нумерация не сдвигалась; приложения A–C — в `docs/archive/`)
- `docs/adr/` — 21 решение, ID ADR-014…ADR-034, дублей нет; сводная таблица статусов — `docs/SAD/12_Architecture_Decisions.md` § 1
- `docs/refactoring-plan/` — планы: `PLAN_Phase6_LoadGate_Hygiene.md` (живой, фазы 0-5 + live-приёмка), `PLAN_Phase5.md` (ждёт live), `PLAN_Phase4_GodObjects.md` (закрыт), `PLAN_InfoPanel_ZoneDebug.md` (перенесён в чеклист/архив), `LIVE_VALIDATION_CHECKLIST.md`, `DEFERRED.md`, `VERIFICATION_REPORT.md`
- `docs/refactoring-plan/hypothesis/INDEX.md` — реестр находок F001-F243 (241 строка; номера F225/F230 не использованы). Аудит 2026-08-01 дал F01-F207, из них 194 в `docs/refactoring-plan/confirmed/` и 13 в `docs/refactoring-plan/rejected/`; поздние раунды F208-F229 и F231-F243 лежат только строками в таблице
- `docs/refactoring-plan/RESOLVED_LEDGER.md` — закрытие гипотез с доказательствами (теги file:line, коммиты)
- `docs/refactoring-plan/game-source-findings/` — G1-G4 (dead RadioStoryIntegration chain) + `GAME_SOURCE_REFERENCE.md` (mod→game API map)
- `knowledge/` — скомпилированная база по API игры (26 файлов на 2026-11-03: inventory/catalogs/classes/playbooks/qa), подключена как reference `uboat-kb` + skill `uboat-game-api`. Проверять до чтения исходников игры
- `documents-readonly/` — первоисточники по радионавигации, не редактируются
- `.opencode/opencode.json` — graphify.js plugin config for OpenCode
