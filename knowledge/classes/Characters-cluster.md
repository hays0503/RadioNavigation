# UBOAT Characters Cluster — Crew, Roles, Station Occupancy

Sources: `UBOAT.Game.Scene.Characters\` (PlayerCrew, CharacterRole, ICharacterRole, CharacterRolesDatabase,
PlayableCharacterData), `UBOAT.Game.Scene.Characters.Activators\Activator.cs`,
`UBOAT.Game.Scene.Characters.Actions\RadioUsageAction.cs`, `UBOAT.Game.Scene.Characters.Statuses\` (CharacterStatus, Skipper).

## 1. Crew model overview

- **PlayerCrew** (`PlayerCrew.cs`) — `[Inject]` singleton `DeepMonoBehaviour`, implements
  `IReadOnlyList<PlayableCharacter>` / `ISingleton`. Holds parallel arrays: `characters`, `officers`
  (populated when `character.IsOfficer`), `sailorsSortedByStress`, `sailorGroups`.
- Membership: `AddCharacter(PlayableCharacter)` wires per-character listeners incl.
  `Data.StatusAdded/StatusRemoved += Data_StatusAdded` (PlayerCrew.cs:925). `Skipper` status added → cached in
  `skipper` field (PlayerCrew.cs:1370–1390); `playerCrew.Skipper` lazily calls `ValidateSkipper()`
  which scans officers for `Data.GetStatus<Skipper>()`.
- **CommandingOfficer** fallback chain (PlayerCrew.cs:249): Skipper (not injured) → officer with
  `Data.characterClass == "Leader"` → any uninjured officer. `characterClass` is a plain public string on
  **PlayableCharacterData** (`PlayableCharacterData.cs:324`; values seen: Leader, Engineer, Radioman,
  War Correspondent). Class assigned via `PromoteToClass(CharacterClass)` which copies skills as statuses.
- Statuses live in `List<CharacterStatus> statuses` on PlayableCharacterData; query API:
  `HasStatus<T>()`, `HasStatus(Type)`, `HasStatus(string name)`, `HasStatus(string[] names)` (any match),
  `GetStatus<T>()`. Per-data events `StatusAdded` / `StatusRemoved` fire from `AddStatus*` / `RemoveStatus*`.
- **Skipper** status class (`Statuses\Skipper.cs`): `CharacterStatus` + `ISkillTokenOwner`, grants a
  "Persuasion" SkillToken on AttachTo. Static `Instance` property is obsolete — use `playerCrew.Skipper`.

## 2. Station occupancy query

- **Activator.Users** (`Activator.cs:198`): `List<PlayableCharacter>`, self-cleaning (null entries removed on get).
  Maintained by `OnActionStarted(character)` → add + instance event `ActionStarted`;
  `OnActionStopped(character)` → remove + event `ActionStopped` (Activator.cs:355–365).
- To find who mans the radio room: resolve the radio's `Activator` and read `Users.Count` /
  `Users.Contains(ch)`. RadioUsageAction itself does exactly this for profit penalties:
  non-officer loses −3.05 if station occupied by someone else; officer penalized only if another *officer*
  is present (RadioUsageAction.cs:248–265); `ActionProfit` variant halves profit ×0.5 when occupied (line 298),
  ×2 while receiving a transmission (line 302).
- Instance events to subscribe: `ActionStarted`, `ActionStopped`, plus tag events
  `ActivatorTagAdded/Removed` (RadioUsageAction adds/removes tag `"Radio Operator"` on its activator at
  start/stop, RadioUsageAction.cs:384,609 — usable as occupancy signal without patching).
- Static events `Activator.ActivatorEnabled` / `ActivatorDisabled` (static, Activator.cs:225–227) fire on
  Start/OnEnable and OnDisable/OnDestroy — enumerate all live activators by subscribing early.

## 3. Role & skill gating

- Roles are data-driven: `CharacterRolesDatabase.Start()` reads GameData rows `"Crew/Roles"`
  (col 0 = role name, col 1 = requirements array of status names); actions come from child rows
  `"Crew/Roles/<name>/Actions"` resolved via `Type.GetType(...)` + `AddJob(type,row)`
  (CharacterRolesDatabase.cs:21–45). Only direct children of "Crew/Roles" become roles.
- Requirement check: `CharacterRole.IsRoleAllowedForCharacter` loops `requirements[]` and requires
  `playableCharacter.Data.HasStatus(requirements[i])` for **every** entry (CharacterRole.cs:326–337).
  So role eligibility == character owns skill-status with that exact name (e.g. Radioman role gated on the
  "Radioman" skill status).
- Job selection cadence: each participant updated round-robin via timed listener returning
  `5f / participants.Count` seconds (LowFrequencyUpdate, CharacterRole.cs:433–446; const `UpdateInterval=5f`),
  i.e. one character re-evaluated every ~5 s, full role cycle ≈ 5 s. Idle/idle-profit characters get timeout
  11 s, selected 75 s, impersonated skipper in CaptainOnly 300 s (UpdateCharacter, line 409–431).
  `GetBestAction(characters, allowInterruptable)` picks highest-profit job action above current profit;
  player-forced orders suppress interrupts (HasPlayerOrders).
- **Class priority** in `RadioUsageAction.GetSortingPriority` (RadioUsageAction.cs:309–324):
  `"Radioman"`→99, `"Engineer"`→96, `"Leader"`→45, default 45. Higher wins slot assignment among candidates.
- **"Radio Usage Practice" status**: checked in `StartMainCharacter` via
  `character.Data.HasStatus("Radio Usage Practice")` → sets `performanceReceiveMultiplier = 1.33f`,
  `performanceSendMultiplier = 0.67f` (RadioUsageAction.cs:377–381). Receive progress scales with
  `Performance × performance × encryptionFactor / ReceiveDuration × receiveMultiplier`; send duration =
  `messages × 11s × sendMultiplier`.

## 4. Global static status events

- `CharacterStatus.GlobalStatusAttach` / `GlobalStatusDetach` (static, `Statuses\CharacterStatus.cs:12–14`)
  raised inside `AttachTo(PlayableCharacter)` / `Detach()` with the status itself as argument;
  `status.Character` gives the owner. Use these to react to ANY skill/status appearing or disappearing crew-wide
  (e.g. grant/remove role membership when "Radioman" skill attaches).

## 5. Extending: custom roles / relaxed requirements

- Add at runtime: implement `ICharacterRole` (or construct `new CharacterRole(name, string[] requirements)`
  then `AddJob(ICharacterRoleJob)`), then call injected `ICharacterRolesDatabase.AddRole(role)`
  (public API, CharacterRolesDatabase.cs:49). Also `RemoveRole`, `GetRole(name)` (searches newest-first),
  `GetAvailableRoles` currently returns ALL roles unfiltered. Timing caveat: database builds once in its
  background `Start()` — register after game scene load (e.g. on your mod init / OnSceneAwake).
- Relax vanilla gating without touching GameData: Harmony postfix on
  `CharacterRole.IsRoleAllowedForCharacter(IPlayableCharacter)` — set `__result = true` when
  `(string)__instance.Name == "<role>" && <your condition>` (e.g. mod-trained radio skill). Prefix alternative
  for full override. Requirements array itself is readonly-backed private field; postfix is the clean path.
- Job-side filtering: `GenericCharacterRoleJob` matches character tags against job config tags where
  `"AI"`, `"*"`, or any matching status name passes (GenericCharacterRoleJob.cs:100) — alternate lever.

## 6. Patching hooks

| Target | Kind | Purpose | Notes |
|---|---|---|---|
| `CharacterRole.IsRoleAllowedForCharacter` | postfix | relax/add role requirement | pure predicate; safe, returns bool |
| `CharacterRolesDatabase.AddRole/GetRole` | direct call | inject custom role | no patch needed; call post-load |
| `RadioUsageAction.GetSortingPriority` | postfix | change class priority (Radioman 99 etc.) | static-ish override; switch on `character.Data.characterClass` |
| `RadioUsageAction.StartMainCharacter` | prefix/postfix | read/alter practice multipliers, "Radio Operator" tag | multipliers are private fields — transpile or reflect if changing |
| `Activator.OnActionStarted/OnActionStopped` | postfix | global occupancy tracking | pair with `Users` read; events also available unpatched |
| `Activator.ActivatorEnabled/Disabled` (static events) | subscribe | discover all stations | subscribe once, unsubscribe on destroy |
| `CharacterStatus.GlobalStatusAttach/Detach` | subscribe | react to any skill/status | fires for every status attach/detach crew-wide |
| `PlayableCharacterData.StatusAdded/Removed` | subscribe | per-officer status watch | already wired by PlayerCrew; additive subscription OK |
