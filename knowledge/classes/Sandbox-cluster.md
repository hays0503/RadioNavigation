# Sandbox cluster — game source dossier (`C:\code\projects\uboat\UBOAT.Game.Sandbox\`)

Namespace `UBOAT.Game.Sandbox`. All positions in **encounter space**: Vector2 in km, X=lon-axis, Y=lat-axis.
Scene space: Unity meters; 1 encounter unit = 1000 scene units. Earth circumference constant `40075.16` km.

## Sandbox (Sandbox.cs) — world singleton
Sealed MonoBehaviour, `[DefaultExecutionOrder(-600)]`, ISingleton. `public static Sandbox Instance` set in Awake, null in OnDestroy.

Groups registry: private `List<SandboxGroup> groups` + `HashSet groupsHashSet` + `List<SandboxGroup> publicGroups`. Player wolfpack always member of `groups`.
- `public List<SandboxGroup> GetGroupsDirect()` → live internal list (never mutate). Same for `GetPublicGroupsDirect()`.
- `public int EncountersCount => groups.Count`.
- `public void AddGroup(SandboxGroup group)` — reparents under sandbox, subscribes EntityAdded/EntityRemoved, calls `group.StartGroup()`, fires GroupAdded. `RemoveGroup` also calls `wolfpack.RemoveObservation(group)`.

Coordinate conversions (origin = injected `sandboxSceneOrigin.Position`, Vector2):
- `public Vector3 EncounterToSceneSpace(Vector2 encounterPos)` → `(pos.x - origin.x)*1000, 0, (pos.y - origin.y)*1000`
- `public Vector2 EncounterToSceneSpace2D(Vector2 encounterPos)` → same ×1000, XY. Double overloads too.
- `public Vector2 SceneToEncounterSpace(Vector2 scenePos)` → `scenePos*0.001 + origin`; Vector3 overload takes XZ; `void SceneToEncounterSpace(Vector3, out double x, out double z)`.

Events:
- `event Action<SandboxGroup> GroupAdded` / `GroupPublicityChanged`; `event Action<SandboxEntity> EntityEvacuated` (crew==0).
- `public static event Action<Sandbox> SandboxStarted` — after static groups/entities/spawners load on entering Gameplay.

Lookups (all Physics2D on layer mask 134217728 = layer 27): `FindGroup(string)`, `FindPublicGroup(string)`, `FindPublicEntity(string)`,
`GetRandomGroupInRange(point, range, [excludedCollider,] [detectabilityFunc,] updateCandidates=false)`, `GetRandomGroupInRangeWithHorizonOcclusion(...)`,
`GetGroupsInRange(point, range|{min,max}[, square/rect], updateCandidates=false)`, `GetPublicGroupsInRange(point[, min,max])`, `GetGroupsAt(point)`, `IsAnyGroupAt(point)`.
Region lookup: `GetRegionAt(x,y)` samples `regionsMap` texture pixel at `w/2 + x * w/40075.16`.

## Coordinates (Coordinates.cs) — static geo math
MonoBehaviour but everything static. Scale k = 40075.16/360 = 111.3199 km per degree.
- `public static Vector2 ToLatLong(float x, float z)` → `(z*360/40075.16f, x*360/40075.16f)` — result .x=lat(from z), .y=lon(from x).
- `public static Vector2 ToLatLong(Vector2 worldPos)` → lat from worldPos.y, lon from worldPos.x.
- `public static Vector2 ToWorldPosition(float lat, float lon)` → `(lon*40075.16f/360, lat*40075.16f/360)` i.e. world.x=lon*k, world.y=lat*k.
- `ToWorldPosition(double lat, double lon)` → `(float)(lon*111.3198888888889), (float)(lat*111.3198888888889)` (same scale).
- `ToWorldPosition(Vector2 latlong)` → swaps components like ToLatLong inverse.
- Instance props: `Latitude = transform.position.y/40075.16*360`, `Longitude = position.x/...*360`.
- `GetCoordinatesString(lat,lon)` / `WithSymbols` — DMS ("52° 06' N") vs decimal per `UserSettings.GameplaySettings.coordinatesType`.

## SandboxGroup (SandboxGroup.cs, 2678 lines) — abstract MonoBehaviour
One contact cluster (convoy/patrol/port/wreck...): entity container, aggregated detection params, observation memory, spawn lifecycle.

Position/state:
- `Vector2 Position { get; set; }` (setter virtual `SetPosition(Vector2, bool isWarpToTarget=false)`); NaN until StartGroup syncs with transform.
- `List<SandboxEntity> Entities`; `SpawnState SpawnState` (NotSpawned/Spawning/Spawned/Unspawning/SpawningImpostors/SpawnedImpostors).
- `bool IsGroupPublic` — public groups are visible on map; toggling registers in `sandbox.publicGroups`.
- `bool DetectedByPlayer`, `Spotted`, `Country Country`, `string Type` ("Convoy","Patrol","Wreck",...), `float AreaRadius`, computed `DefaultAreaRadius` (AreaSize+margins; ×0.65 ally-private, ×1.25 spawned, ×0.75 dynamic-undetected-unspawned; + occupied radius; ×AreaRadiusScale).

Detection params via `GroupParameterListener` (max/min/log aggregation over entities): HydrophoneRange/Detectability, RadarRange/Wavelength,
RadarDetectorRange/Wavelength, SightRange, DirectionFinderRange, DirectionFinderInaccuracy, SignatureRadius, VisualDetectability.

Key members: `static T Create<T>(string name, string type, Vector2 position, Country country)` (GameObject "Name"/"Name #N");
`AddEntity/RemoveEntity/FindEntity(name)/GetEntity<T>()`; `HasAnyTransmissions` (any entity Transmission != null); `GetRandomEntityWithRadio()`;
`UpdateGroup()` (pulls spawned entity positions), `UpdateGroupLowFrequency(bool allowObservationUpdates)` → `UpdateObservations()` (private):
random rolls visual/hydrophone/radar/radar-detector using `sandbox.GetRandomGroupInRange*` then `AddObservation(...).AddKnownData(...)`.

Observations API: `GroupObservation AddObservation(SandboxGroup observed, GroupDetectionMethod method)` (merges flags if exists, evicts oldest past limit `observationsLimit`=5);
`GetObservation(group)`, `GetObservations()`, `RemoveObservation(...)` → `observation.SetLost()` + ObservationRemoved;
`ValidateObservation(observation, considerTimeout=true)` re-checks every sensor band and prunes DetectionMethod bits.

Events: `Changed`, `Destroyed`, `EntityAdded(SandboxEntity)`, `EntityRemoved`, `HoverOrSelectionChanged`,
`ObservationAdded(SandboxGroup, GroupObservation)`, `ObservationRemoved(SandboxGroup, GroupObservation)`, `Spawned`, `VisibilityChanged`.

## SandboxEntity (SandboxEntity.cs, 3134 lines)
Serializable unit record (ship/aircraft/port/minefield); bridges to scene `Entity` via spawn lifecycle `EntitySpawnState {Unspawned, Spawning, Spawned, Virtual, SpawnError, Destroyed}`.

Position semantics (important):
- Backing field `position` + `CoordinatesModeType CoordinatesMode {World, Local}` (auto-switched by SpawnState: Spawned→World, else Local).
- `Vector2 Position`: get → Local ? `position + group.Position` : `position`; setter stores minus group offset when Local. Getter always returns encounter-world coords.
- `ScenePosition`/`ScenePosition2D`: spawned → `spawnedTransform.position`; else `sandbox.EncounterToSceneSpace(Position)`.
- `UpdatePosition()` (per-frame from group when Spawned): writes back `Position = sandbox.SceneToEncounterSpace(rigidbody.position)`.
- `SandboxGroup Group { get; internal set; }` — setter fires `GroupChanged(GroupChangedEventData)`.

Radio transmission flow:
- `bool CanTransmit()` — Category Empty always true; needs crew>0, not pacified, healthy equipment in slot tagged "Antenna"/"U-Boat Antenna".
- `bool StartTransmission(RadioTransmission transmission)` — rejects if !CanTransmit/null/same; sets `transmission.Source=this`, stamps `LastUpdateTicks`
  (`gameTime.Ticks` for Dynamic travel system, else StoryTicks); non-RadioStation types get timed listener `UpdateTransmission` (every ~2 s),
  player ship additionally per-frame `UpdateTransmission2`; fires `TransmissionChanged`; `radioTransmissionsRegister.RegisterTransmission(...)`.
- `UpdateTransmission()`: re-checks CanTransmit; `sandbox.GetGroupsInRange(Position, transmission.SignalStrength*100)`; NPC groups within their
  DirectionFinderRange roll `gameSettings.RadioDetectionChance` → `group.AddObservation(this.Group, GroupDetectionMethod.DirectionFinder)`
  `.AddKnownData(GroupData.TransportType, dist*0.5f*DirectionFinderInaccuracy)` + messages OnReadMessage + search clue. Duration drains by ticks
  (max 40 s per step); on expiry HQ.ParseReport + `transmission.OnSent()` + StopTransmission (unless ContinuousBroadcast).
- `StopTransmission()` — clears listeners, unregisters, fires TransmissionChanged.

Other: static factories `Create(string name, Country country)` / `Create(name, blueprint, ...)` / `Create<T>` variants;
events `Changed`, `Removed`, `StateChanged`, `SpawnStateChanged`, `GroupChanged`, `TransmissionChanged`, `DamagedBeyondSalvage`, `Pacified`.

## GroupObservation (GroupObservation.cs) — perceived contact
`[Serializable][Inject] class GroupObservation : IGroupIntelligence, IIntelligence`. One observer's belief about one group; stored in observer's observations list.

Perception model: `lastKnownPosition/lastKnownVelocity/lastKnownSpeed`, uncertainty `lastKnownRadius`, random error vector `offset` (unit×≤0.9),
bitmask `detectionMethod` (DirectObservation, IndirectObservation, Hydrophone, Radar, RadarDetector, DirectionFinder, Headquarters, Console),
`knownData` bits (GroupData.Name/Category/TransportType/Size), `spottedEntitiesCount` (direct eyes-on count).
- `Vector2 EstimatedPosition` — spotted → real `group.Position`; else dead reckoning `lastKnownPosition + offset*radius + lastKnownVelocity*(StoryTicks-lastKnownPositionTime)*1e-7`. `EstimatedPositionWithoutOffset` drops the offset term.
- `Vector2 GetPerceivedPosition(Vector2 observationOrigin)` — spotted→real; intel only via HeadquartersAndConsole→`group.Position + offset*radius`; else LOS-direction-biased error: along-LOS component scaled by `bestCloseupFactor` (lerp 0.5..1 vs distance/radius), perpendicular scaled 0.5·radius.
- `PositionPerceivedByPlayer` — GameData "Use Real Position For Contacts" ? EstimatedPosition : GetPerceivedPosition(wolfpack.Position).
- `MagicalPosition` — real position + offset*radius (debug-ish). `Deprecation` 0..1 = max(time drift, movement drift)/validity limits.
- `PerceivedContactType` cascade picks icon label: Console/Name→Name, Category→Type, DirectObservation→Ship/Ships/Aircrafts/Wreckage, IndirectObservation→FunnelSmoke/Masts/Contrail/VisualContact, Hydrophone→PropellerNoise/HydrophoneContact, RadarDetector→DetectedRadar, Radar→RadarContact, Headquarters→Coordinates, else UnknownGroup.
- `UpdateKnownCoordinates(radius, updateVelocity)` — smoothing: better data snaps, worse lerps by Deprecation; caps radius at maxDetectionRadius.
- Events: `event Action<GroupObservation> NewData` (DetectionMethod change, known-data updates), `ContactLost` (from `SetLost()`).
Lifecycle: created by AddObservation; removed by RemoveObservation/RemoveLeastImportantObservation/RemoveOutdatedObservations (each calls SetLost first).

## NavalGrid (NavalGrid.cs)
Hierarchical naval squares from sheet "Sandbox/World Grid"; sub-squares built lazily 3×3 (`BuildSubSquares`). Square Min/Max stored as worldPos×1000.
- `NavalGridSquare GetSquare(string name)` ("AM5..." hierarchical), `GetSquare(float x, float z, int depth)` (input ×1000 internally), `Squares`, `TopLevelSquares`.

## Patching hooks (mod relevance)
- Subscribe `Sandbox.Instance.GroupAdded` / `SandboxStarted` after scene load; enumerate via `GetGroupsDirect()` (copy before mutating iteration).
- Public patch targets: `Sandbox.AddGroup/RemoveGroup`, `SandboxGroup.AddObservation` is **private** (transpile or use public ValidateObservation/ObservationAdded event instead), `SandboxEntity.StartTransmission/StopTransmission` (public bool/void — clean prefixes), `GroupObservation.SetLost` (public).
- Bearing-from-radio hook lives in `SandboxEntity.UpdateTransmission` (private) where DirectionFinder observations are added; parameters `DirectionFinderRange`/`DirectionFinderInaccuracy` are readable properties on SandboxGroup.
- All Physics2D group queries use layer 27 (mask 0x08000000); group colliders are CircleCollider2D radius=AreaRadius.
