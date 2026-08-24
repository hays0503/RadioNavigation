# Story Cluster — Story / StoryPlayer / GenericStory (game source dossier)

Sources: `UBOAT.Game.Scene\Story.cs` (556 L), `UBOAT.Game.Scene.Stories\StoryPlayer.cs` (79 L), `GenericStory.cs` (~1900 L). All line refs verified 2026-08-24.

## 1. Story state machine (`Story.StoryState`, Story.cs:112–123)

| State | Meaning | Set by |
|---|---|---|
| `Disabled` | Inactive/finished; terminal resting state | `GenericStory.ContinueStory` when no Result fragments (:531), `End` (:1456), `EndWithCallback` (:1486) |
| `Normal` | Awaiting player decision | `ContinueStory` :570 (unless Cutscene/Dialogue*) |
| `Wait` | Timed wait | `Story.Wait(float)` :417–423 |
| `Movement` | Wait while crew moves | `Story.MovementWait(float)` :425–431 |
| `GroupSelection` | Crew picker open | `ContinueStory` :562 (GroupSelect fragment) |
| `Cutscene`, `DialogueSegment`, `DialoguePreChoices`, `DialogueChoices` | Script-driven camera/dialogue states | scripts via `State` property setter |

- `State` has public get / `internal` set (Story.cs:198–208). Mods cannot assign it without reflection or transpiler.
- `Wait`/`MovementWait`: stamp `waitStartTicks/waitEndTicks`, set State, register `executionQueue.AddTimedUpdateListener(OnWaitEnd, delay)`; `OnWaitEnd` (:456–460) fires `ContinueStory()` and returns NaN (one-shot listener).

## 2. Decision anatomy (`Story.Decision`, nested class, Story.cs:21–92)

All fields readonly; immutable once built:

| Field | Type | Line |
|---|---|---|
| `LocalizationKey` | string | 24 |
| `FallbackText` / `FallbackTooltip` | string (nullable) | 26 / 28 |
| `Icon` | string | 30 |
| `Enabled` | bool | 32 |
| `Parameters` | object[] (params capture) | 34 |
| `Index` | int (auto = decisions.Count) | 36 |
| `Costs` | IRewardInfo[] | 38 |
| `callback` | Action&lt;Decision&gt; (private) | 40 |
| `callbackAlt` | Action (private) | 42 |

Constructors (overload resolution matters for patchers):
- `(string localizationKey, string fallbackText, string fallbackTooltip, string icon, bool enabled, Action callback, int index, IRewardInfo[] costs, params object[] parameters)` — :44
- `(string localizationKey, string icon, bool enabled, Action callback, int index, IRewardInfo[] costs, params object[] parameters)` — :57 (fallbacks null)
- `(string localizationKey, string icon, bool enabled, Action<Decision> callback, int index, IRewardInfo[] costs, params object[] parameters)` — :70

`CallCallback()` :81–91: prefers `callback` (Action&lt;Decision&gt;, receives `this`); falls back to `callbackAlt`. Exactly one is non-null per ctor used. **Note**: game decompiler names differ — `LocalizationKey` is a readonly *field*, not property; access accordingly in Harmony patches.

## 3. Decision lifecycle

```
StoryPlayer.SelectDecision(index)          StoryPlayer.cs:64–68
  └─ currentStory.SelectDecision(decision) Story.cs:320–325
       ├─ ApplyCost(decision.Costs)        Story.cs:488–490 — EMPTY BODY (stub!)
       ├─ OnDecisionSelected(decision)     protected virtual :332–334; GenericStory override :591–596
       │    stores lastSelectedDecision + AddTempTag(tail of LocalizationKey)
       └─ decision.CallCallback()          :81–91
            GenericStory path → DecisionSelectedListener.OnDecisionSelected (GenericStory.cs:46–74)
              executes fragment script; if script didn't advance → OnUpdated();
              stops finished character-group tasks
```

## 4. Decision list management (Story.cs)

| Member | Signature | Line |
|---|---|---|
| `AddDecision` | `(Decision)` | 372 |
| `InsertDecision` | `(int index, Decision)` — List.Insert, shifts others; Index field NOT renumbered | 377 |
| `AddDecision` | `(string localizationKey, string icon, bool enabled)` — null callback, no costs | 382 |
| `AddDecision` | `(key, icon, enabled, Action callback, params object[])` | 387 |
| `AddDecision` | `(key, icon, enabled, Action<Decision> callback, params object[])` | 392 |
| `AddDecision` | `(key, icon, enabled, IRewardInfo[] costs)` — null callback | 397 |
| `AddDecision` | `(key, icon, fallbackLoc, fallbackTooltip, enabled, Action, IRewardInfo[] costs, params object[])` | 402 |
| `RemoveDecision` / `ClearDecisions` | | 407 / 412 |

All protected — only reachable inside Story subclasses or via `AccessTools`/reflection. Public read: `Decisions` => `decisions.AsReadOnly()` (:186).

## 5. Updated event & refresh

- `event Action<Story> Updated` (:246), raised only by `protected void OnUpdated()` (:438–441). GenericStory raises it at end of every `ContinueStory` (:587), from `DecisionSelectedListener` (:60/:65), and indirectly through scripts. UI listens here to re-render text/choices.
- Other events: `Started` (:248, fired once in `Start()` then nulled :267–271), `CharacterGroupAdded/Removed` (:240–242), `CharacterSlotChanged` (:244).
- `ContinueOnUIEnable` flag (:174–184): if set before `storyPlayerUI.Opened`, `StoryPlayerUIOnOpened` consumes it and calls `ContinueStory()` (:492–503).

## 6. StoryPlayer flow (StoryPlayer.cs — `BackgroundTaskBase, IStoryPlayer, ISingleton`)

- `Play(Story)` :35–42 — sets `CurrentStory`; fires `Played` unless state==Disabled AND `ContinueStory()` returned false. `ContinueStory` may itself transition state first (GenericStory auto-Starts :502–505).
- `Continue(Story)` :44–51 — fires `Played` if (state ∈ {Wait, Movement, Cutscene, DialoguePreChoices, DialogueSegment} AND `gameTime.Ticks < WaitEndTicks`) OR `ContinueStory()` true.
- `Wait(Story, float)` :53–58 — registers timed listener; fires `WaitStarted`.
- Internal `OnWaitEnd` :70–78 — after timer: `ContinueStory()` true ⇒ fire `Played`; returns NaN (unregisters).
- `Stop()` :60–62 — empty body. `OnFinished` :26–33 — removes listener, clears CurrentStory and both events.
- Events: `Played` (:22), `WaitStarted` (:24).

## 7. GenericStory essentials

- Tag/scripting env: tags are keys in `contextEntity.Variables` (Dictionary<string,object>). `AddTag` :1061, `HasTag` :1095 (presence check, value ignored), `SetValue` :1089, `RemoveTag` :1079 (+ removedVariables tracking), `AddTempTag` :1067 — temp tags auto-purged at start of next `ContinueStory` (:572–576) and in `End` (:1457–1461). `IsTempTag` :1074.
- `StoryScriptingEnvironment` created in ctor (:441–449), disposed & nulled in `OnStopped` (:476–479); restarting a stopped story logs error :454–457. All story logic lives in data-driven fragments executed by this env.
- `ContinueStory()` override :500–589 pipeline: auto-Start → close crewListUI → execute Script-type fragments → resolve Result fragments (GroupSelect fallback) → none ⇒ State=Disabled + `Stop()` + return false → execute Result scripts → AddTempTag(result ids) → build Choice/ChoiceHidden list sorted by SortingPriority → ClearDecisions → AddDecision per choice with `DecisionSelectedListener` callback + fragment Costs → disable time compression if UI open → `OnUpdated()` → true.
- `AllowTimeCompression` :247–280 — GroupSelection: false; Wait/Movement: true; Normal: true only while a group has an unfinished task AND TaskPerformanceMultiplier > 0; else false.
- `End()` :1447–1479 — guards Disabled, purges temp tags, `storyPlayerUI.DelayedClose()`, fade-to-black → `ContinueEnd` :1499 (runs endCallback, Stop(), closes UI, restores camera/sun). `EndWithCallback(Action)` :1481–1497 same with stored callback.
- Decisions surfaced by GenericStory always have `Costs` from `storyFragment.Costs` and `Enabled: true`.

## 8. Auto-stop on save load / state change

`Start()` (:255–272) subscribes: `storyPlayerUI.Opened/Closed`, `gameState.StateChanged`, `savesManager.LoadingStarted`. Handlers `SavesManagerOnLoadingStarted` (:510–520) and `GameStateOnStateChanged` (:522–532) call `Stop()` inside try/catch (exceptions logged, not fatal). `Stop()` (:274–286) unsubscribes all four and calls `OnStopped()` **only if it was running** (idempotent). Serialization re-hookup: `OnAfterDeserialize()` (:542–555) — if deserialized while `running`, re-subscribes the same four handlers (delegates don't survive domain/save round-trip). `OnBeforeSerialize`/`OnBeforeDeserialize` empty.

## 9. Patching hooks table (mod relevance)

| Target | Kind | Notes for Harmony |
|---|---|---|
| `Story.SelectDecision` | instance, public, void | Prefix can veto/capture chosen Decision; lifecycle entry point |
| `Story.OnDecisionSelected` | protected virtual | Mod already prefixes `RadioStory.OnDecisionSelected` — declare override-matching signature |
| `Story.ContinueStory` | public virtual bool | Mod patches `RadioStory.ContinueStory`; return value gates `Played` firing |
| `Story.OnStopped` | protected virtual | Mod patches `RadioStory.OnStopped`; cleanup point (also runs on save-load stop) |
| `Story.Start` / `Story.Stop` | public | Stop idempotent (running flag); good for subscribe/unsubscribe symmetry |
| `Story.Wait` / `MovementWait` | public | Capture wait windows via `WaitStartTicks/WaitEndTicks` |
| `Story.AddDecision*` / `InsertDecision` | protected | Needs `AccessTools`/`HarmonyPatch` with nonpublic flag; inject custom choices |
| `StoryPlayer.Play/Continue/Wait` | public, ISingleton instance | Get instance via DI framework (`[Inject]` static pattern) |
| `StoryPlayer.Played` / `WaitStarted` | events | Subscribe for UI sync instead of patching |
| `GenericStory.End` / `EndWithCallback` | public | Terminal transitions; ContinueEnd is private (skip) |
| `ApplyCost` | private, empty | No-op stub — patching pointless today; may gain body in future versions |
