# PlayerShip cluster (PlayerShip / PlayerShipProxy / Ship / Entity / SandboxEntity)

Game source: `UBOAT.Game.Scene.Entities\*.cs`, `UBOAT.Game.Sandbox\SandboxEntity.cs`. Verified 2026-08-24.

## Identity & lifecycle

- `PlayerShip : PlayableSubmarine, IPlayerEntity, ISingleton` — sealed, ~4870 LOC, one instance
  representing the player submarine's physical GameObject.
- `PlayerShipProxy : MonoBehaviour, IPlayerShipProxy, ISingleton` — `[DefaultExecutionOrder(-450)]`,
  survives ship-type swaps; owns the interior scene load (`LoadShipType`) and the current
  `PlayerShip` reference. `CurrentShip` property returns it (`PlayerShipProxy.cs:60`); `null`
  while loading/switching.
- Proxy keeps a stable `SandboxEntity` across swaps: `SandboxEntity => wolfpack.PlayerShipSandboxEntity`
  (`PlayerShipProxy.cs:94`), whereas the Unity-side `PlayerShip` component is destroyed/recreated.

## Getting player world position every frame

1. `[Inject] private PlayerShip playerShip;` (DWS InjectionFramework injects singletons by type)
   or resolve via proxy. Cache once; re-resolve on change callback (see below) — never per frame.
2. Per frame: `playerShip.RigidBody.position` (world-space Rigidbody position;
   `Ship.cs:334 public Rigidbody RigidBody => rigidBody;`). `transform.position` also works but
   during kinematic setup frames rigidbody and transform can differ briefly; game logic itself
   reads `rigidBody.position/velocity` throughout `PlayerShip.cs` (e.g. L2403-2432).
3. Map coordinates: `playerShip.SandboxEntity.ScenePosition2D` → `Vector2` XZ in world units
   (`SandboxEntity.cs:518-528`; falls back to `sandbox.EncounterToSceneSpace2D(Position)` when not
   spawned). `ScenePosition` (Vector3) is the same for full Y. Note: `SandboxEntity.Position`
   (encounter space, used by mod's `PlayerPositionProvider`) differs from scene space — do not mix.
4. Guard: before first spawn the sandbox position can be NaN — check finiteness before use.

## Lat/lon conversion

No `GeoPositionConverter` exists in game source. The mod ships its own:
`Source/Logic/GeoPositionConverter.cs` — pure static, `DegreesPerWorldUnit = 0.008983120716174308`
(= 1/111.3198888888889, mirrors game `WorldPosition.Latitude/Longitude`).
`ToLatLong(worldX, worldY)` → lat = Y·k, lon = X·k. Use with scene-space XZ from step 3.
Mod's existing consumer: `Source/Logic/PlayerPositionProvider.cs` (reflection path via
`Entity.playerShip` static field + `SandboxPlayerShip.Position`, NaN-guarded).

## Ship-swap detection (submarine type change / save reload)

`PlayerShipProxy.AddChangeCallback(PlayerShipChangeCallback)` (`PlayerShipProxy.cs:295-302`):
register once at startup; fires immediately with current ship if present, then on every swap as
`PlayerShipChangeEvent(previous, next)` (`next == null` during teardown, non-null when loaded).
Callbacks are serialized into saves (`CustomSerialization/CustomDeserialization`,
L512-555) — static lambdas survive reloads; instance delegates of serializable types are
re-bound after load. Always unregister via `RemoveChangeCallback`. Alternative: Harmony-patch
`PlayerShipProxy.LoadShipType` / `OnSceneLoaded` (private) if event semantics insufficient.

## Key public events

On `PlayerShip`: `AlarmStarted/Stopped`, `CollisionEnter/Stay`, `CrewAlertnessChanged`,
`DetectedStateChanged`, `DockChanged`, `EmergencyStateChanged`, `EngineGearOrdered`,
`FlawAdded/Removed`, `LeakStarted/Stopped`, `MineTriggered`, `QuickTravelTargetChanged`,
`NPCObservationUpdate`, `PreferredEngineOnSurfaceChanged`.

On `PlayerShipProxy` (re-raised from PlayerShip, so safe across swaps): same alarm/dock/emergency/
leak/mine set plus `AlertStarted(Observator)/AlertStopped`, `FiredTorpedo(Torpedo)`,
`FiringTargetHit(MissionFiringTarget)`, `ObservationAdded/Removed`,
`SubmergeStateChanged`, `TargetDepthChanged(float prev, float new)`.
Prefer proxy events over patching `PlayerShip.Update` (private `new void Update()`,
`PlayerShip.cs:2156` — heavy, fragile to recompile, runs every frame).

## Other useful API surface

`Docked`, `CurrentDockArea`, `Radio` (PlayerShipRadio), `NavigationTable`,
`Acceleration`, `BowOffset/SternOffset`, `DepthMetersValueOffset`,
`CurrentColliderBottomDepth`, `Fuel/Energy/Oxygen/Stress/Discipline/Water` resources,
`InitializationComplete` (UniTask — await before touching subsystems),
`Entity.SandboxEntity` back-reference property (`Entity.cs:380-423`) links ship ↔ sandbox entity.

## Patching hooks guidance

- Avoid: `PlayerShip.Update` (private new, per-frame, huge method body).
- Prefer: proxy events / `AddChangeCallback` for state changes; Harmony prefix on
  `PlayerShipProxy.OnSceneLoaded` or `SetPlayerShip` for swap interception;
  postfix on specific named methods (e.g. `UpdateCareerStatistics`) only when unavoidable.
