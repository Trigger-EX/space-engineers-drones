# Audit: sensing, IGC and AI-block stand-in (key `sensors_igc_ai`)

Scope: `sim/src/Api/{CameraApi,SensorApi,TurretApi,Igc,AntennaApi,Detection,TerminalBlock,TerminalProps,CubeGridApi,GridTerminalSystemApi,BlockApiFactory}.cs`,
`sim/src/Core/Raycast.cs`, `sim/src/Core/World.cs` (TeamTable), `sim/src/Combat/{VanillaAi,TurretAi}.cs`, against the real game. Read-only audit. Date 2026-10-13.
No simulation was run (everything below is a code and source comparison; CLAUDE.md seed rule not triggered).

## 0. Evidence and its limits

| Tag | Source | Weight |
|---|---|---|
| [GD] | `defs_stats.json` raw block fields (camera, sensor, antenna, AI blocks, turrets) | highest |
| [KS] | Keen public source snapshot, commit 54f2f0f (6 Jan 2022), read with `git show` from a blobless clone of `KeenSoftwareHouse/SpaceEngineers`: `MyCameraBlock.cs`, `MyDetectedEntityInfoHelper_ModAPI.cs`, `MySensorBlock.cs`, `MyLargeTurretBase.cs`, `MyGridTargeting.cs`, `MyIDModule.cs`, `MyRadioAntenna.cs`, `MyDataReceiver.cs`, `MyRadioReceiver.cs`, `MyAntennaSystem.cs`, `MyUserControllableGun.cs`, `MyCameraBlockDefinition(+ObjectBuilder)`. **2022 build: it predates the AI blocks, the Custom Turret Controller and the IGC implementation (no IGC code in the snapshot).** Good for design, medium for current numbers. |
| [API] | Keen ModAPI doc mirror `keensoftwarehouse.github.io/SpaceEngineersModAPI/api/Sandbox.ModAPI.Ingame.*` (IMyCameraBlock, IMyOffensiveCombatBlock, IMyFlightMovementBlock, IMyOffensiveCombatStayAtRange, IMySearchEnemyComponent, IMyAttackPatternComponent) | high (current API surface) |
| [wiki] | spaceengineers.wiki.gg: Scripting/Detecting_Things, Scripting/Antenna_Communication_(IGC), AI_Offensive, Automaton, Modding/Reference/Blocks/OffensiveCombatBlock; Keen forum "Antenna Communication (update 1.189)"; Keen support bug "Multiple IGC messages at once on different tags drops all messages" (marked Outdated) | medium |
| [rec] | my recollection, labelled with confidence | low |

Not reachable: MDK-SE wiki (github.com/malware-dev), IGC implementation source, AI-block source, CTC source. Everything about those is therefore documentation-level.

## 1. Coverage: checked and found CORRECT

| Item | Sim | Game evidence |
|---|---|---|
| Camera charge rate | 2.0 m/ms (2 km/s), `SimSettings.cs:77` | `MyCameraBlock.AvailableScanRange`: `+= dt_ms * RaycastTimeMultiplier`; builder default 2.0 [KS]; wiki "2 km/s or ~33.3 m/tick" |
| Camera cone limit / distance limit | 45 deg / -1 (unlimited) | `MyObjectBuilder_CameraBlockDefinition` defaults `RaycastConeLimit=45f`, `RaycastDistanceLimit=-1`, `RaycastTimeMultiplier=2.0f` [KS]. `SmallCameraBlock` has no fields in `defs_stats.json`, so it inherits these defaults (the open question in docs/research.md 4 is closed) |
| Charge cost = ray length, spent even on a miss; no charge cap; starts at 0 | `CameraApi.cs` Cast | `AvailableScanRange -= distance` before the physics cast; `Math.Min(double.MaxValue, ...)` [KS]; wiki "charge not saved, 0 after load" |
| `CanScan`, `TimeUntilScan` semantics | `CameraApi.cs:71-87` | [KS] `(distance - avail)/mult`, clamp 0 (sim rounds up; trivial) |
| Pitch axis and rotation order (pitch about Right first, then yaw about the original axis) | `DirFromAngles` | `MatrixD.CreateFromAxisAngle(Right, pitch)` then `(Down, yaw)` [KS]. Pitch sign correct (+pitch = up). Yaw sign is wrong, see F-10 |
| Hit data: EntityId = grid, HitPosition = hit point, Orientation = grid orientation, Velocity = grid linear velocity, BoundingBox = grid world AABB, Position = AABB centre, TimeStamp = now | `Detection.cs` | `MyDetectedEntityInfoHelper.Create` [KS] (uses `entity.PositionComp.WorldAABB`, `Physics.LinearVelocity`, `GetTopMostParent` grid) |
| Name hidden for non-owned grids ("Small Grid"/"Large Grid") | `Detection.FromBody` | [KS] reveals the name only for Owner and FactionShare (sim also reveals for Friends; no script parses names) |
| Sensor terminal ids "Left/Right/Top/Bottom/Front/Back", "Detect Players/Floating Objects/Small Ships/Large Ships/Stations/Subgrids/Asteroids/Owner/Friendly/Neutral/Enemy" | `SensorApi.cs` Register | `MySensorBlock` control ids [KS] (exact match). `LeftExtend` style names exist only as properties |
| Sensor field geometry (box in block frame, front = -Z, back = +Z), 10-tick update, 50 m max range | `SensorApi.cs` Poll | `EACH_10TH_FRAME`, `GetHkShape` box (max-min)/2, `MaxRange` 50 [KS][GD] |
| Sensor owner exclusion ("own grid never detected") | `b == own` | [KS] `entity == CubeGrid` false; plus whole logical group (see F-08 for subgrids) |
| IGC: queue overflow drops the OLDEST | `Igc.cs` Push | ModAPI doc of `MaxWaitingMessages` ("oldest one will be dropped") [API]; Keen forum |
| IGC: unicast fails when unreachable (`SendUnicastMessage` returns bool) | `EnqueueUnicast` | IGC API comment/gist [wiki-level] |
| IGC: duplicate `RegisterBroadcastListener` returns the existing listener | `Igc.cs` RegisterBroadcastListener | Keen forum (Inflex) |
| IGC: callbacks at most once per tick per provider | `SimMessageQueue` | IGC API comments |
| Relay rule: enemies, neutrals and ownerless broadcasters are skipped; antenna receiver works whenever the block is working (Enable Broadcast not needed to receive) | `Igc.cs` Friendly/BuildNet | `CanIUseIt` (Enemies/Neutral/Owner==0 -> false), `MyRadioAntenna.CheckIsWorking -> RadioReceiver.Enabled = IsWorking`, broadcaster enabled only if working and EnableBroadcasting [KS]. The wiki sentence "Enable Broadcast must be on or messages will not be received" disagrees with the source; the sim follows the source |
| Antenna terminal ids "Radius", "EnableBroadCast"; power = Radius/500 * MAX_REQUIRED_POWER_ANTENNA (sim uses 4 kW/km = 2 kW per 500 m) | `AntennaApi.cs` | ids [KS]; constant `MyEnergyConstants.MAX_REQUIRED_POWER_ANTENNA` not retrievable (file not found in snapshot) - unverified but plausible |
| Gun actions `Shoot_On`, `Shoot_Off`, `ShootOnce` exist (the Wasp "name from memory" worry in packages/unknowns.md is resolved) | `TerminalProps` registry | `MyUserControllableGun`: button id "ShootOnce", switch id "Shoot" with On/Off actions [KS] |
| PB ids `Recompile`, `TerminalRun`, `ConsoleCommand`; sensor/turret "Range" slider id | registry | [KS] `MyProgrammableBlock`, `MyLargeTurretBase` ("Range", "TargetMeteors", ...) |
| Turret search range = ShootingRange + 100 m; tracking kept out to that range; decoys take priority; neutrals/ownerless blocks not targeted | `TurretAi.cs` | `m_searchingRange = m_shootingRange + 100`, `TestTarget` decoy logic, `IsTargetEnemy` [KS] |
| Turret definition numbers (800/600 m, rotation speeds) | `TurretUnit` | [GD] `MaxRangeMeters`, `RotationSpeed` |
| AI Offensive search radius 2500 m, 10 kW power, default search interval 30 s (range 5-60 s) | `SimSettings.cs:130,116` | wiki AI_Offensive + `defs_stats.json` `RequiredPowerInput 0.01` |
| `GetBlocksOfType` clears, filters `is T` then predicate; `GetBlockWithName` exact match | `GridTerminalSystemApi.cs` | [KS] `MyGridTerminalSystem_ModAPI` |
| Camera / team relation base cases (same identity -> Owner, no owner -> NoOwnership, no faction -> Enemies) | `TeamTable.Relation` | `MyIDModule.GetRelation` [KS] |
| Scripts' sensor/camera/IGC API use compiles against real signatures (`DetectedEntities(list)` after the script clears the list, `c.Raycast(distance,pitch,yaw)`, `Raycast(Vector3D)`, `CanScan(Vector3D)` which exists in the current API) | scripts | [API] IMyCameraBlock page lists all three CanScan overloads |

## 2. Findings

Severity: critical = result wrong or cannot work; high = likely wrong or likely breaks; medium = plausible deviation; low = minor; info = note.

### F-01 (high) `Raycast(double, Vector3D)` and `CanScan(double, Vector3D)` take a CAMERA-LOCAL direction in the game; the sim treats it as WORLD, and `GnatSwarmAI.cs` passes a world vector
- Game: ModAPI doc: "Does a raycast in the given direction (in camera local space)" and "CanScan(double distance, Vector3D direction) ... (in camera local space)" [API]; source: `Raycast(distance, targetDirection)` does `Vector3D.TransformNormal(targetDirection, this.WorldMatrix)` before the cone check [KS] (`MyCameraBlock.cs` ~584-595).
- Sim: `CameraApi.cs` header line 8 and `Raycast(double, Vector3D)` use the vector as WORLD. This makes the sim hide a real script bug.
- Script: `GnatSwarmAI.cs:2109` (`RaidScan`): `c.Raycast(RaidRay, Vector3D.Normalize(axis + u*... + v*...))` where `axis` comes from `HitAxis()` (world). In the game the world vector is rotated by the camera orientation again, so the ray points somewhere else and usually fails the 45 deg cone test (returns empty, no charge used). The v1.9 "hit-bearing raid" sweep therefore finds nothing in game while the sim finds targets.
- Other callers: only that one (`grep "Raycast(.*Vector3D\."`); `Raycast(Vector3D target)` callers pass world positions, which is correct.
- Fix: script: `Vector3D.TransformNormal(worldDir, MatrixD.Transpose(c.WorldMatrix))` or `c.Raycast(c.GetPosition() + worldDir * RaidRay)`. Sim: interpret the direction as camera-local in both overloads so such a bug shows up. Confidence high.

### F-02 (high) Default-mode sim assumes the AI blocks work on pasted drones; the owner's in-game report says they do nothing
- Sim: `SimSettings.cs:123 ScriptAiBlocksInert = false`, `:129 AiBehaviorNeedsSwitch = false`. With these defaults every Full drone's AI Offensive/Flight pair flies the ship (VanillaAi `ScriptShip`) as soon as the script enables the blocks, and docs/RESULTS.md says these stand-ins "dominate default-mode results".
- Reality: in-game report 2026-10-13 (comment at `SimSettings.cs:121`) and `packages/unknowns.md`: AI blocks come out of a blueprint with AI Behavior off and default settings. The "Behavior" property id the scripts hunt for is undocumented (the sim registers `ActivateBehavior`, an unverified guess, `BlockApiFactory.cs`/`TerminalBlock.cs SimGenericFunctional`). Also by wiki the AI Offensive needs a selected weapon ("Use Static Weapons") to aim fixed guns, none is selected by default.
- Impact: every sim number that relies on AI-flown approach (Hornet vs Hornet, the 2.5 km starts, "3 Hornets with working AI blocks won 3 of 4") is an upper bound. The scripted fallback (cameras only) is the lower bound already reported in docs/sim-validation.md.
- Fix: publish default results under `ScriptAiBlocksInert=true` as the baseline until test T-AI confirms the switch (see F-03: the typed API may solve this), keep the current default as the "what-if the owner configures them".

### F-03 (high, opportunity) The scripts could configure the AI blocks through the typed ModAPI; docs say "a blueprint cannot pre-set" but a script can
- Game API [API]: `IMyFlightMovementBlock { SpeedLimit (get/set), CollisionAvoidance, PrecisionMode, AlignToPGravity, FlightMode, MinimalAltitude, IsAutoPilotEnabled, LookAtPosition, CurrentWaypoint, GetWaypoints }`; `IMyOffensiveCombatBlock { UpdateTargetInterval (seconds, get/set), SelectedAttackPattern (long), TargetPriority, GetAttackPatternIds, TryGetSelectedAttackPattern, SearchEnemyComponent (SubsystemsToDestroy, TargetingLockOptions, FoundEnemyId), LastScan }`; `IMyOffensiveCombatStayAtRange { MinimalDistance, MaximalDistance, EvasiveManeuvers, FacingPriority }`. A keen support thread says `SetSelectedWeapons` was missing on StayAtRange but present on HitAndRun.
- Scripts: `grep SpeedLimit|IMyFlightMovementBlock|IMyOffensiveCombat|UpdateTargetInterval|IsAutoPilotEnabled scripts/*.cs` finds nothing; they only toggle `Enabled` and guess a "Behavior" property. `packages/unknowns.md` ("AI Flight speed limit 10 m/s ... a blueprint cannot pre-set") and `scripts/MidgeNotes.md` are therefore outdated as statements about what is possible.
- Impact: the single biggest lever on the AI-block dominated results: a script that sets `SpeedLimit` (slider max 100 per `EntityComponents.sbc AutopilotSpeedLimit`), `UpdateTargetInterval = 5`, Stay-at-Range 300..600 and enables `IsAutoPilotEnabled`-type behaviour would remove most of the 10 m/s and 30 s delay penalty. Needs an in-game test (property names via `GetProperties` dump to the PB screen, then these typed calls).
- Compile note: `stubs.cs` must gain these interfaces (they are in `Sandbox.ModAPI.Ingame`), and the sim must grow matching `SimGenericFunctional` members.

### F-04 (medium) Real AI blocks require thrust in all six directions; the sim stand-in does not enforce it, and several drones are lopsided
- Wiki "Automaton": "The drone must have thrust in all six directions." The sim's VanillaAi flies with any thruster set (`ShipPilot.SetAcceleration`).
- Hornet-class (12 thrusters, 2 per axis) satisfies it; the Gnat/Midge Lite frames (nose axis only) have no AI blocks by design; Harrier ("12 m/s2 forward only", unknowns.md) must be checked. All new big ships that want AI blocks need six-direction thrust and a gyro.
- Fix: add a six-direction check to `VanillaAiBrain.Active` (config flag) so lopsided designs get an inert AI in the sim; add a validator rule in `tools/sebp.py` for ships carrying AI blocks.

### F-05 (medium) Turret target (re)acquisition runs every 100 ticks in the game, every 10 ticks in the sim
- Game [KS]: `MyLargeTurretBase.UpdateAfterSimulation100` calls `CheckNearTargets()` (the only periodic call; the other is on `ShootingRangeChanged`), and `MyGridTargeting.TargetRoots/TargetBlocks` rescans only when `GameplayFrameCounter - m_lastScan > 100`. So a pirate turret needs up to 1.67 s (mean about 0.8 s) to pick a new target, after every kill and when a drone enters range.
- Sim: `SimSettings.TurretTargetRefreshTicks = 10` (`:89`), staggered by EntityId.
- Impact: pirate and enemy turrets are about 0.7 s faster to react than in game; for fast gatling duels this flatters the pirate by a measurable amount (first-shot latency). Set 100 ticks with random phase for large turrets. Does not apply to the CTC designator (different code; unverified).
- Confidence medium (2022 build, but the update-100 structure is long-standing).

### F-06 (medium) Real turrets target the nearest enemy BLOCK (fat block with an ID module), not the grid centre
- Game [KS]: `TestTarget` iterates `TargetBlocks` of each enemy grid, picks the minimum squared distance from the turret to the block position (`target.PositionComp.GetPosition()`), prefers decoys, checks visibility per block with a ray from the muzzle (`IsTargetVisible`: the first hit must be the target, its parent, or share its parent; any other entity, even another enemy, blocks the shot) and aims at the predicted block position. Armor has no fat block and is never selected.
- Sim: `TurretAi.Acquire` picks the nearest BODY by bound-sphere distance and aims at the body centre (or decoy / targeting-group block). The first-hit rule is looser (any enemy body counts).
- Impact: damage distribution on the target differs: game turrets focus the nearest functional block (usually the outermost gun or thruster), the sim shoots through the middle. For small drones the difference is small; for the large pirate ship and for the new big ships it changes what dies first. Also affects what `GetTargetedEntity()` reports (see F-12).
- Fix: model nearest-functional-block aim for large turrets (reuse `Subsystem()` with "all functional terminal blocks").

### F-07 (medium) Sim cameras, turret line-of-sight and AI search see through the whole own grid; in the game only the camera block itself is skipped
- Game [KS]: `MyCameraBlock.Raycast`: `foreach hit: if (entity == this) continue;` where `this` is the camera BLOCK. The hit entity is the grid, so the first hit on the own grid (a gun, wing or hull block in front of the lens) is returned with EntityId = own grid and Relationship Owner. The ray starts at the block centre.
- Sim: `Raycast.RayWorld(..., ignore: Inst.Body)` skips the entire own body (`CameraApi.cs:99`, `TurretAi.cs` LOS, `VanillaAi.Search`).
- Script side is safe: `Valid()` rejects `e.EntityId == Me.CubeGrid.EntityId` (HornetDroneAI.cs:1424), so the cost is a wasted ray and its charge, not a false target. But side/aft cameras that look through the drone's own structure (the v1.4 "5 extra cameras") would be blocked in game and not in the sim. The camera's own model has "HasPhysics: false" in the definition raw data, so the first own cell may not block; uncertain.
- Fix: in the sim cast against the own body too, skipping only the camera's own cell (and optionally any block with no collision). Check the existing blueprint camera clearance rule (`keep_clear` in the validator) already prevents own-hull hits for the forward cameras.

### F-08 (medium) Sensor model differences: relation mapping, overlap test, min extent, power, subgrids
1. Relation flags [KS `ShouldDetectRelation`]: `NoOwnership` and `FactionShare` -> "Detect Friendly"; `Owner` -> Detect Owner; `Neutral` -> Detect Neutral; `Enemies` -> Detect Enemy; a grid with NO big owners (derelict, wreck) counts as ENEMY. Sim `SensorApi.Passes`: NoOwnership -> Neutral; wrecks (EffectiveOwner 0) go to Neutral. Net effect on scripts that set Detect Friendly=false, Detect Neutral=TargetNeutral: wrecks appear in game (as "enemy") but not in the sim when TargetNeutral is false; `Valid()` then drops them (NoOwnership is not Enemies), so only CPU/queue effects.
2. Overlap test: game uses a Havok shape-shape penetration test of the field box against the grid's collision shape [KS]; sim uses grid AABB corners in sensor axes (conservative, bigger). A drone diagonal to the field may be reported by the sim and missed in game; the 50 m sensor scenarios (Stinger terminal homing) are slightly optimistic.
3. Minimum extent: game sliders `SetLimits(1, MaxRange)` and clamp FieldMin/Max to +-1 minimum [KS]; sim minimum is 0.1 (`SensorApi.cs:20`).
4. Power: game requires `0.0003 * cbrt(field volume)` MW [KS] (a 100 m cube = 30 kW). `defs_stats.json` has no RequiredPowerInput for the sensor so the sim gives it no sink. A maxed sensor costs 30 kW: 30 kW x 15 min = 7.5 kWh = 15 % of a 0.05 MWh (50 kWh) Gnat/Stinger battery. Models for Stinger and Gnat Full endurance are optimistic by that amount.
5. Subgrids: the game excludes everything in the sensor's logical group (rotor/piston/connector linked) unless Detect Subgrids is on [KS `HasSameGroup`]; sim excludes only the own body. No current design has subgrids.
6. `DetectedEntities(list)` in the game does `result.AddRange` WITHOUT clearing [KS]; the sim clears. The scripts clear first (`_sensed.Clear()`), fine; any new script must too.
7. `LastDetectedEntity` is the first hit of a `Parallel.ForEach` (non-deterministic order) in the game; the sim returns the last list element. No script uses it.

### F-09 (medium) Relationship semantics and how the user's enemies register
- Game [KS `MyIDModule.GetRelation`]: owner==0 -> NoOwnership; same identity -> Owner; same faction (and share mode Faction) -> FactionShare; if either side has no faction -> Enemies; two different factions: Neutral when their standing is neutral, otherwise Enemies. The 2022 source treats allied factions as Enemies too (newer builds have `Friends`; recollection, low confidence). `MyDetectedEntityInfo.Relationship` uses `grid.BigOwners[0]` (the biggest owner), `BigOwners.Count == 0` -> NoOwnership.
- Sim: `TeamTable.Relation` (all teams Enemies unless set allied). OK as the "pirates are Enemies" model: NPC pirate factions are at war with players by default (wiki AI Offensive: "Factions must be Enemy: very low faction standing or declared war").
- Risk outside the sim: the owner's test enemy `pc/enemies/Tech Hunter/bp.sbc` has all 606 blocks `<Owner>144115188075855920</Owner>` with ShareMode Faction, i.e. a normal player identity. When pasted, the game re-owns blocks to the pasting player [rec, medium]: the "enemy" is then Owner/FactionShare, not Enemies, and neither the scripts, the turrets nor the AI blocks will engage it. In game tests the enemy needs NPC (pirate) ownership or a hostile faction/player.
- Sim: `Detection.EffectiveOwner` zeroes ownership when no functional terminal block is left; the game keys on BigOwners (owners of any remaining owned block), so a half-dead grid keeps its owner longer. Minor.

### F-10 (low) Camera pitch/yaw sign and limit check
- Game [KS `Raycast(distance,pitch,yaw)`]: yaw rotation is `CreateFromAxisAngle(WorldMatrix.Down, yaw)`, so +yaw turns the ray to the RIGHT; +pitch up. Sim `DirFromAngles` turns +yaw LEFT (`CameraApi.cs` header, docs/sim-validation.md item 3 listed it as unconfirmed; the source now settles it: the sim is mirrored).
- The game rejects only `pitch > ConeLimit || yaw > ConeLimit` (no absolute value; -90 deg passes the check and is cast); sim rejects `|x| > 45`. Irrelevant for scripts that stay inside +-45.
- Impact: scripts use symmetric circular sweeps `(r sin th, r cos th)` so no result changes, but any aimed yaw (new ships) would be mirrored in the sim. Fix: negate yaw in `DirFromAngles`.
- Also (info): the game accumulates charge lazily and `m_lastUpdateTime` only advances while `EnableRaycast && IsWorking`, so toggling `EnableRaycast` off for a while and on again grants all the time in between as charge (a free burst). The sim accrues only while enabled. No script toggles it.

### F-11 (medium) VanillaAi (AI Offensive/Flight stand-in): how far to trust it
| Parameter / behaviour | Sim | Documented real behaviour | Trust |
|---|---|---|---|
| Search radius | 2500 m from the grid (`VanillaSearchRange`) | 2500 m, line of sight from the block, ignores own and physically connected grids (wiki AI_Offensive; `SearchEnemyComponent DefaultEnemySearch` data) | good |
| Search interval | 30 s default, first search on activation (`VanillaSearchOnActivate=true` [I]) | 30 s default, 5-60 s (wiki) via `UpdateTargetInterval` | interval good, activation timing unknown |
| Target priority | closest | Closest / Largest / Smallest, default unknown | ok |
| Who counts as target | Enemies only | "Enemies" (default) or "Enemies and Neutrals"; Target Characters option | good |
| Attack pattern | Stay at Range 300..600 m + evasive | patterns Circle Orbit (100-2000 m), Stay at Range (Min 0-1999, Max 1-2000), Hit and Run, Intercept; defaults of pattern and distances not documented | guess [I]; the real default pattern may be Orbit, not Stay at Range |
| Speed | 10 m/s limit, evasive 30 m/s clipped to it | slider max 100 (`AutopilotSpeedLimit`); wiki gives no default; script can set `SpeedLimit` (F-03) | default unverified |
| Firing | auto-fires first fixed gun inside a 5 deg cone | needs a weapon selected under "Use Static Weapons"; none selected by default; mixing weapon types worsens aim; first selected weapon is the aiming reference | optimistic |
| Flight quality | ideal accel controller, 3 rad/s turn cap | real AI Flight has collision avoidance (can be overridden), PID, precision mode; no numbers | unknown |
| Defensive block, Basic Mission | ignored | only one Combat block (Offensive or Defensive) is active at a time | fine for current drones |
| Power | 10 kW per block | 0.01 MW [GD] | good |
Verdict: use the stand-in for qualitative statements (slow, long-delay approach; no fire until aligned) and for A/B between scripts under identical AI assumptions; do not quote absolute win rates from default-mode runs. Items marked [I] in `SimSettings.cs:116-139` have no source at all. The strongest anchored number is the 2500 m search range, which already makes every >2.5 km start "dead on arrival" for AI-only ships (docs/sim-validation.md).

### F-12 (medium, unverified) What `GetTargetedEntity()` returns for a turret/CTC target
- In [KS], `MyDetectedEntityInfoHelper.Create(entity, ...)` fills Velocity and Orientation only `if (entity.Physics != null)` and uses `entity.PositionComp.WorldAABB` as BoundingBox. Large-turret targets are fat BLOCKS (F-06) and blocks have no Physics, so a block target would give Velocity (0,0,0), zero Orientation and a block-sized BoundingBox (Position = block centre), while the grid id is resolved through `GetTopMostParent`. The sim returns the grid's velocity and AABB.
- `GetTargetedEntity` is not in the 2022 snapshot, so I cannot see how the current game builds it, nor how the CTC does. If velocity is zero in game, scripts that seed the lead from the contact's velocity (`AddContact(e,"TURRET"/"CTC")`, e.g. GnatSwarmAI.cs:1196-1201, Aegis/Basilisk 1513-1519) get a stationary first track until the first camera ray corrects it. Add to the in-game test list: print `GetTargetedEntity().Velocity` for a moving enemy.

### F-13 (low) Turrets also fire at ownerless moving grids and only act while rendered
- [KS] `IsTarget` accepts a grid entity only when `TargetMoving` and speed > 3 m/s, and `IsTargetEnemy(MyCubeGrid)` returns true exactly when the grid has no big owners. So drifting wrecks/debris of dead ships keep drawing fire (wasting shots and acquisition time). Sim never targets `EffectiveOwner == 0`.
- `UpdateAfterSimulation100` runs the AI only `if (Render.IsVisible())`: in single player a pirate far from the player camera (draw-distance culled) does not search or shoot. A test with the observer far away underestimates pirate lethality; the sim has no such rule. In-game test caveat only.

### F-14 (low) IGC: things the sim assumes without a source (and one reported game bug)
- Queue length 25 (`IgcMaxWaitingMessages`, [I]): the real default is not documented (the API only says oldest dropped). With N drones each rebroadcasting target tuples twice a second plus the mothership, 25 may be too small or too large; an in-game print of `listener.MaxWaitingMessages` would fix it.
- Message size limit and per-tick message limit: none documented, none modelled.
- Sender does not receive its own broadcast in the sim (also the script skips `msg.Source == IGC.Me`); the game behaviour is not documented. Harmless.
- Not modelled: `ConnectedConstructs` (connector-linked grids), laser antennas ("any viable path: physical, radio, laser" per the Keen forum), antenna default radius when none given (game: large 10000 m / small 500 m [KS `MyRadioAntenna`], sim 5000). All current blueprints set BroadcastRadius explicitly (`tools/designs/_common.py:68`).
- Keen support bug (marked Outdated, single player): several broadcasts on DIFFERENT tags in the same tick may all be dropped. The scripts do exactly that (`HornetDroneAI.cs:1163` `Tag_TGT` then `FleetSend` on other tags, plus `_SWARM`, `_HOME`). Cheap mitigation: stagger sends across ticks. Mark as an in-game test item.
- Docked drones: while merged they are one grid, so IGC works without antennas in sim and, per the "physical path" rule, in game. When docked by connector in game, `GetBlocksOfType` also returns the other grid's blocks (shared terminal system) - scripts guard with `IsSameConstructAs(Me)`, the sim has no connector support (sim-validation item 2), so connector docking cannot be tested in the sim.
- Sim delivers messages in phase 2 of the same tick and runs `IGC` callbacks immediately after; documentation says callbacks fire "the next tick". Within one tick of latency; negligible.

### F-15 (low) Terminal API details in the sim stand-ins
- `DefinitionDisplayNameText` returns `Def.Key` ("Type/Subtype") and the default CustomName is the subtype; the game returns the localized display text and "<Name> <NumberInGrid>". No script reads either (`grep`), but `BlockDefinition.ToString()` is used (`AegisAI.cs:2734`, `.Contains("OffensiveCombat")`): the sim builds `MyObjectBuilder_<TypeId>/<Subtype>`, which matches the game.
- `GetBlockGroups` always empty (no group data in blueprints). No script uses groups.
- `IsAccessibleForProgrammableBlock` (game GetBlocks filter by ownership/sharing) is not modelled; blocks welded by a printer belong to the printer owner, same as the PB, so no effect.
- `SimSlimBlock.BuildIntegrity` = MaxHp (blocks always fully built); printer-welded blocks start at the build-progress level in the game. Out of scope for this audit.
- Camera ray results only cover grids; the game also returns missiles, floating objects, characters, asteroids and planets (and a documented far-planet quirk beyond ~10 km). Space duels: no effect.

## 3. Answers to the specific questions

- Camera range / charge / cone / what comes back: see section 1; charge model and definition numbers match the game; direction overload and yaw sign do not (F-01, F-10); own-grid handling differs (F-07).
- Hits friendly grids? Yes in both (sim hits every body except the own one).
- Relationship values for unowned/NPC grids: NoOwnership when BigOwners is empty; NPC pirates are Enemies for a player without a hostile-neutral faction standing; sensors map NoOwnership to the "Friendly" flag (F-08, F-09).
- Sensor: 50 m max, 10-tick update, physics-shape overlap, flags as above (F-08).
- IGC: antenna rule and enemy exclusion match the source; limits unknown (F-14).
- GridTerminalSystem scope: game = logical group (rotor/piston/connector linked), sim = one Body; scripts filter with `IsSameConstructAs`, so no current effect (F-14, F-15).
- AI blocks: F-02, F-03, F-04, F-11.
- Property ids / actions the scripts use: all verified except the AI "Behavior" switch id (F-02) and CTC-specific members (no source).

## 4. Suggested in-game tests (additions to the T-list)
1. PB dump of `GetProperties` of an AI Offensive, AI Flight and CTC block (ids, defaults), plus `IMyFlightMovementBlock.SpeedLimit` read/write and `IsAutoPilotEnabled`.
2. Print `MaxWaitingMessages` of a broadcast listener and test two tags in one tick.
3. `GetTargetedEntity().Velocity/BoundingBox` of a turret and a CTC on a moving enemy.
4. Camera: `Raycast(500, Vector3D.Forward)` vs the same with a rotated camera (local vs world), and a ray through the own hull (does it return the own grid id).
5. Sensor with a derelict (no owner) grid in the field: which Detect flag reports it.
