# Research notes: Space Engineers facts for the drone project

Written 2026-10-11. Scope: the 8 questions in the research brief. Every claim carries a tag:

- **[V-local]** verified by reading the user's game data in `ref/` (Data/*.sbc, CubeBlocks/*.sbc, Starburst/bp.sbc). Strongest evidence we have; it is the user's installed game version.
- **[V-web]** a page I fetched and read in this session (URL given). Wiki pages can lag the game.
- **[V-src]** read from Keen's old open-source repo (KeenSoftwareHouse/SpaceEngineers, public until ~2019) via its GitHub page. Mechanism is real, but numbers/sign details may have changed since.
- **[I]** inferred or recalled, not confirmed by anything I fetched. Treat as a hypothesis to test.

Tooling note: the GitHub MCP in this session is bound to the project repo only, and `raw.githubusercontent.com` returned 404 for several paths, so I could read Keen source only through `github.com/.../blob/...` pages via WebFetch, and only some of them.

## 0. Corrections to HANDOFF.md (read this first)

| # | HANDOFF says | What the evidence says | Impact |
|---|---|---|---|
| 1 | Small merge block (1x1x1) clamp face is local **Right** (`drones.py` assumed this) | Scripting wiki: the *original two* merge blocks merge toward **Right**; the *newer 1x1x1* block merges toward **Up** [V-web, one source]. | If `drones.py` places the `SmallShipSmallMergeBlock` with Right toward the printer, the merge face points the wrong way. **Highest-priority fix.** Needs one in-game confirmation (test T1). |
| 2 | Gatling: 400 m/s, 600 RPM | `Weapons.sbc`: `RateOfFire="700"`; `Ammos.sbc` LargeCaliber (what the gatling's NATO_25x184mm uses): `DesiredSpeed` **800** m/s, MaxTrajectory 800 [V-local]. Wiki says 600 RPM [V-web] (older). | Sim should use the local data: 700 RPM, 800 m/s. |
| 3 | Railgun "~20 s recharge", fires on trigger | Weapon def has `ShotDelay` **500 ms (small) / 2000 ms (large)** with a "PreShot/Charge" sound [V-local]: there is a charge-up lag between trigger and projectile. | Longbow lead calc should add this delay (inferred meaning, [I]); test T9. |
| 4 | (not in HANDOFF, but the wiki claims the merge block has mount points on 5 of 6 faces) | `SmallShipSmallMergeBlock` in `CubeBlocks_Utility.sbc` lists mount points on **all 6** sides [V-local]; the wiki's "5 of 6" text [V-web] is about the old blocks. | Do not rely on "one face has no mount point". |
| 5 | Event Controller "can't watch blocks that don't exist yet" | True, and additionally it has **no projector event at all** (22 events, list in section 2) [V-local]. | A timer/EC printer cannot know when the projection is finished. |
| 6 | Gyro convention `Pitch=-l.X, Yaw=-l.Y, Roll=-l.Z` | Consistent with user's in-game result. Keen's old getters differ for pitch (section 5.3). | Keep as is; do not "fix"; add test T10 to pin it down. |

## 1. Merge block

### 1.1 Which face merges
- Original 2 merge blocks merge toward their **Right**; the newer 1x1x1 merge block merges toward **Up**. [V-web] https://spaceengineers.wiki.gg/wiki/Scripting/Finding_a_block%27s_orientation (only source found for the 1x1x1 direction; cross-check not possible from SBC because the definition carries no direction field).
- "Only the sides with clamps can merge." [V-web] https://spaceengineers.wiki.gg/wiki/Merge_Block
- In blueprint terms: the merge face normal is the block's **`BlockOrientation Up`** direction, in grid space. For the printer merge block and the drone merge block to face each other, the two `Up` vectors must be opposite in world space. [I, follows from the above]
- The rotation-hints cube in game (red/green/blue faces = +X Right / +Y Up / +Z Back) shows the local axes. [V-web, same page]
- Definition (user's install) [V-local] `SmallShipSmallMergeBlock`: size 1x1x1, `Strength` **0.005**, components SteelPlate x2, Construction x3, Motor, SmallTube, Computer, 6 mount points. Mass 92.2 kg, component HP 346 (computed from `Components.sbc`; matches the wiki's 92.2 kg / 346 HP for the small merge block).

| Merge block | Size | `Strength` [V-local] |
|---|---|---|
| `LargeShipMergeBlock` | 1x1x1 (large) | 0.1 |
| `SmallShipMergeBlock` | 1x3x3 (small) | 0.015 |
| `SmallShipSmallMergeBlock` | 1x1x1 (small) | 0.005 |

`Strength` is the magnetic constraint strength, unitless in the definition; the small-small block is 1/3 of the 1x3x3 and 1/20 of the large one. Absolute holding force is not documented [I]. Practical consequence: a drone with heavy thrusters firing, or a collision, can tear off a 0.005 block; keep thrusters off while merged.

### 1.2 Alignment and behaviour
- Procedure on the wiki: clamp faces facing each other, both blocks on (white), within ~1 m they turn yellow and become magnetic, within ~0.1 m the grids snap and align, then green. [V-web] Merge_Block page.
- Both grids must be the same size class (small with small). Ownership/faction permission needed on both. [V-web]
- **Temporary vs permanent:** if any other full block surfaces of the two grids touch apart from the merge blocks, the bond is permanent and switching off or grinding the merge blocks will not separate them; thin edges (slopes) are tolerated. [V-web] This matches HANDOFF 4.4.
- Required grid-axis alignment: the snap aligns the faces; rotation about the normal is expected to be in 90 degree steps. [I]
- **Static grid trap:** merging a mobile grid to a *static* grid makes the mobile grid static; a ship merged to a station converts its blocks to static station blocks (revert via Info screen). [V-web] Consequence: the **printer must be a dynamic ship** (`<IsStatic>false</IsStatic>` on its grid), otherwise every printed drone may come out static. Test T4.
- Unmerging does not restore the smaller grid's original name. [V-web]
- Merge blocks do not carry conveyors/inventory. [V-web] Power and control do pass (one grid). Drone sharing printer power while merged is consistent with HANDOFF CHARGE phase.
- Switching **either** merge block off releases a temporary merge. The drone's own merge block can therefore be the release trigger (useful for a scriptless drone, section 2.4). [I from wiki; test T3]

### 1.3 Build-and-release printer behaviour
- Projector facts [V-web] https://spaceengineers.wiki.gg/wiki/Projector:
  - Projector and blueprint grid size must match (small projector -> small grid).
  - **Subgrids are not projected**; only the largest grid. A drone must be a single grid (no rotors/pistons/hinges).
  - Welding a projection costs the same components as manual building; you can weld only *supported* blocks. Unsupported ghost blocks hang in mid-air. "Show Only Buildable" hides unsupported ones.
  - Wiki hint for merge blocks: aligning the projection's merge block with a merge block on the projector's grid makes docking blocks count as supported. **So the printer's merge block should coincide with the projected drone merge block**, which also makes the merge block the natural attachment root. [V-web, from a passage about subgrids; applying it to drones is [I]]
  - Each projected block costs 1 PCU. [V-web]
  - Projector Keep Projection: enable to mass-produce or repeatedly repair. [V-web]
- Offsets via API: `IMyProjector.ProjectionOffset` is a `Vector3I`, `ProjectionRotation` a `Vector3I` where 1 = 90 degrees; call `UpdateOffsetAndRotation()` after changing. `RemainingBlocks`, `RemainingArmorBlocks`, `TotalBlocks`, `BuildableBlocksCount`, `IsProjecting`, `ShowOnlyBuildable` exist. [V-web] https://github.com/malware-dev/MDK-SE/wiki/Sandbox.ModAPI.Ingame.IMyProjector (page notes it is deprecated; offsets are in blocks of the projector's grid, [I]).
- Projector block XML [V-local] (Starburst, a small projector with a loaded projection): contains `<ProjectedGrids>` (a full `MyObjectBuilder_CubeGrid` with blocks, each with `BuildPercent`/`IntegrityPercent` tiny, i.e. an unbuilt ghost), then `<ProjectionOffset>`, `<ProjectionRotation>`, `<KeepProjection>`, `<ShowOnlyBuildable>`, `<InstantBuildingEnabled>`, `<MaxNumberOfProjections>`, `<MaxNumberOfBlocks>`, `<ProjectionsRemaining>`, `<GetOwnershipFromProjector>`, `<Scale>`, `<MarkUnfinishedBlocks>`, `<MarkMissingBlocks>`. So pre-loaded printers are possible and the exact shape is known. The Starburst sample's projected grid contained no PB.
- `GetOwnershipFromProjector` field exists [V-local]: decides whether welded blocks take the projector's owner. The drone must end up owned by the player/faction, so set this deliberately (test T5).
- **Known bug, fixed in Update 206:** with Keep Projection **off**, welding a projection that contains a merge block and then unpowering/unmerging gave an unclosable "Cannot find blueprint" softlock. Workaround then: Keep Projection **on** (some reports it still happened, or the projection vanished). [V-web] https://support.keenswh.com/spaceengineers/pc/topic/44353-error-with-projector-and-merge-block . Another report: grinding the projector was the only way to stop it. Use Keep Projection ON, and never turn the projector off during a release.
- Older report: a PB inside a projector-built missile was **not auto-compiled**; the reporter had to press Recompile manually, which implies the code was present. Status Won't Fix, unreproduced, ~8 years old. [V-web] https://support.keenswh.com/spaceengineers/pc/topic/programmable-block-not-compiling-automatically-after-being-constructed-using-a-projector
- **Does a projected PB keep its Program text?** No source states it. Evidence for yes: a blueprint stores PB scripts [V-web https://spaceengineers.wiki.gg/wiki/Blueprint]; the projector stores the full grid object builder (incl. `<Program>` if present) [V-local shape]; the Keen thread above implies the code survived. Evidence for risk: needs compile. Verdict **[I] likely keeps text, may not run until recompiled**. Mitigations: (a) printer script calls `ApplyAction("Recompile")` on the drone PB while merged (one construct, so it is visible to the printer's GridTerminalSystem) [I]; (b) drone constructor/EC-driven recompile. Test T6.
- Welder: `SmallShipWelder` is 3x3x6 cells, `SensorRadius` 1.3 m [V-local]; HANDOFF's "every drone block must be close to a welder" stands. Welder block XML (Starburst, large welder) has `<Enabled>false</Enabled>` as the only block-specific field. [V-local]
- Community printers: sections 8.

## 2. Event Controller

### 2.1 Full event list (22) [V-local]
From `Data/EntityComponents.sbc` (`UniqueSelectionId` = value stored in `<SelectedEvent>`; confirmed by Starburst, whose EC has `SelectedEvent=14` = Block On/Off with a welder selected). Names/thresholds/ranges from the wiki [V-web] https://spaceengineers.wiki.gg/wiki/Event_Controller

| Id | Event | Watches / notes |
|---|---|---|
| 1 | Cargo filled % | inventory blocks, 0-100 |
| 2 | **Block integrity %** | blocks with control panel, 0-100 |
| 3 | Gas tank filled % | O2/H2 tank, H2 engine |
| 4 | **Stored power %** | battery, jump drive |
| 5 | Piston position % | |
| 6 | Cockpit occupied | slot1 occupied, slot2 emptied |
| 7 | Connector connected | |
| 8 | Landing gear locked | |
| 9 | Door opened | |
| 10 | **Block added/removed** | slot1 added, slot2 removed/destroyed |
| 11 | Power output % | generators |
| 12 | Angle changed | rotor/hinge, -360..360 |
| 13 | Altitude (surface height) | 1..50000 m, vs original planet surface |
| 14 | **Block on/off switched** | slot1 on, slot2 off; ship tools do not trigger |
| 15 | Rotor/hinge attached/detached | |
| 16 | **Merge block merged** | slot1 merged, slot2 unmerged |
| 17 | Thrust % | any thruster |
| 18 | Grid speed | 0..100 m/s |
| 19 | Magnetic lock ready | |
| 20 | Connector ready to lock | |
| 21 | Natural gravity | 0..10 g |
| 22 | **Distance to locked target** | cockpit, AI Offensive, AI Defensive; 1..2500 m (`MaxDistance 2500` in data) |

Not watchable: projector remaining blocks, PB state, sensor, weapons/ammo (only through Cargo filled %), warhead, camera, AI block internal state. "Only one event per Event Controller." Watches only its own grid or connected/merged grids. [V-web]

### 2.2 Mechanics
- Conditions are `>=` or `<=` (both include equality). Slot 1 = event true action, slot 2 = false/reset action. Multiple actions per slot via toolbar pages. AND gate (multi-block events): all blocks vs at least one. [V-web]
- XML [V-local] (Starburst): `<Threshold>`, `<ANDGate>`, `<SelectedEvent>`, `<SelectedBlocks><long>id</long>...`, `<ConditionInvert>`, `<Toolbar>` (slot 0 = true action, slot 1 = false action; items are `ToolbarItemTerminalBlock` or `ToolbarItemTerminalGroup` with `Action`, `BlockEntityId`, `GroupName`), plus a `ComponentContainer` holding one `MyEvent*` component per event with its stored parameter (e.g. `<DistanceToLockedTarget>2500</DistanceToLockedTarget>`).
- Block references are by **EntityId**. For a freshly welded drone the ids are new; a printer-side EC cannot pre-select drone blocks. Group-name actions are resolved by name. [I] Keen bug: EC "loses the selected block when copied and pasted" in some cases, reproduced by QA. [V-web] https://support.keenswh.com/spaceengineers/xbox/topic/28047-event-block-settings-wipe-after-paste (Xbox section, may differ).
- Known annoyance: an EC can restart a running timer repeatedly (Won't Fix). [V-web] https://support.keenswh.com/spaceengineers/pc/topic/28178-event-controller-keeps-re-starting-timing-block
- Power: `RequiredPowerInput` 0.0005 MW (small) [V-local].

### 2.3 What a timer/EC-only printer cycle can do
Possible, open loop:
1. EC(Merge block merged, printer merge block) slot1 -> start timer A.
2. Timer A: welders On, projector On/keep, start timer B after `T_weld`.
3. Timer B: welders Off, (optional charge delay timer C), then Merge block Off (release).
4. EC(Merge merged, slot2 = unmerged) -> timer D: wait clearance, Merge block On, restart.
Closed-loop signals that exist: merge state (16), printer cargo filled % (1) to pause when out of components, printer battery stored power % (4), integrity of existing printer blocks (2), grid speed of the drone after release (18). Delay limits: timer delay 1 s to 1 h, sub-second not supported, timers inaccurate and slower at sim speed < 1. [V-web, Timer section]

Cannot do: know that the projection is complete, detect missing components mid-print other than via cargo %, count living drones, choose which bay to use, send IGC.

### 2.4 Scriptless drone (Midge) idea [I, untested]
Put an EC *inside the drone blueprint* watching **drone-side** blocks. Example: EC "Stored power % >= 90" (drone battery) with AND gate -> slot1 toolbar: own merge block Off, AI Flight On, AI Offensive On, thrusters On. This removes printer-side id problems because the drone's own block ids are remapped consistently within its grid. Risk: when the EC is welded before the blocks it references, its `SelectedBlocks` point to ids that do not exist yet. Test T7 decides this.

### 2.5 Comparison
| Need | Timer/EC printer | Script printer (HiveMotherAI) |
|---|---|---|
| Wait until projection done | fixed delay only | `RemainingBlocks == 0` |
| Detect stall (no components) | cargo % only | stall detection, retry |
| Count drones / max drones | no | via `_SWARM` |
| Select bay | no | yes |
| Works with scripts disabled | yes | no |
| Robust to wrong delay | no (cuts the build short or wastes time) | yes |
Conclusion in HANDOFF section 8 stands, with the added finding that the EC has no projector event.

## 3. Timer, Sensor, AI blocks

### 3.1 Timer block
- Delay: 1 s (0:00:01) to 1 h (1:00:00); no sub-second timing. Trigger Now, Start, Stop; a timer can start/stop other timers or itself (repeating loop); stop loops by sending Stop to all looping blocks. 81 actions per timer (9 toolbar pages x 9). Silent = no countdown beeps. "All the actions run inside a timer don't wait for each other." [V-web] https://spaceengineers.wiki.gg/wiki/Timer_Block
- Accuracy: delays can round down (e.g. 14 s typed -> 12 s), run slower when sim speed < 1.0. [V-web] https://support.keenswh.com/spaceengineers/pc/comments/32809/vote/toggle
- Definition `TimerBlockSmall` [V-local]: mass 30.2 kg, 1x1x1, no power field in the definition (uses `Utility` sink group).
- Serialized fields: `sebp.py` writes `Toolbar`, `Delay` (ms, 10000 default), `CurrentTime`, `IsCountingDown`, `Silent`. I could not re-verify these names online [I from Keen source memory, per `sebp.py` comments]. Starburst has no timer block, so no local ground truth. Ask for a saved sample (see section 10).

### 3.2 Sensor
- Definition `SmallBlockSensor` / `LargeBlockSensor` [V-local]: `MaxRange` **50** m both; mass 134.2 kg; mount: Back only (HANDOFF 4.3). Wiki: min 0.1 m, max 50 m per direction (left/right/top/bottom/back/front, relative to sensor facing). [V-web] https://spaceengineers.wiki.gg/wiki/Sensor
- Detect flags (wiki): Players (not seated), Floating objects, Small ships, Large ships, Stations, Subgrids, Asteroids; ownership: Owner, Friendly, Neutral, Enemy. Two toolbar slots (enter/leave), 9 pages. Fires once per first entering/leaving target. [V-web]
- Update every 10 ticks, "not recommended for high speed". [V-web] Detecting_Things page; update rate also in https://spaceengineers.wiki.gg/wiki/Modding/Reference/Blocks/SensorBlock. Power formula `MW = 0.0003 * FieldVolume^(1/3)`. [V-web]
- Serialized fields in `sebp.py`: `FieldMin`, `FieldMax`, `PlaySound`, `DetectPlayers`... `DetectEnemy`, `IsActive`, `Toolbar`. [I; unverified here]. A 2015 community post lists older names `LeftExtend/RightExtend/...` [V-web search snippet, outdated].
- Paste hazard recorded in `sebp.py`: Keen source `Remap()` dereferences `Toolbar` unguarded, so a Sensor without `<Toolbar>` could abort the paste. [I, from the repo comments; not re-checked]. This is the best current explanation for "adding a Sensor made every drone fail to paste".

### 3.3 AI blocks
All four exist for small grid with non-DLC ids (`defs.json` dlc `[]`) [V-local]: `SmallOffensiveCombat`, `SmallFlightMovement`, `SmallDefensiveCombat`, `SmallBasicMission`; 1x1x1, `RequiredPowerInput` 0.01 MW each, 486 HP (computed 486 for Offensive), 156 kg.
- Roles [V-web] (https://spaceengineers.wiki.gg/wiki/AI_Offensive , AI_Defensive , AI_Flight , AI_Basic):
  - **AI Flight (Movement)**: only flies. Needs another AI block to supply waypoints. Needs thrusters in all six directions and at least one gyro. Settings: Collision Avoidance, Precision Mode, **Speed Limit 0-100 m/s default 10**, Align to gravity, Min altitude 0-500 m, max pitch/roll. When activated it switches thruster/gyro overrides off and takes control.
  - **AI Offensive**: detects, locks and attacks enemy grids within **2500 m** with line of sight; ignores own and connected grids. Enemies are decided by faction standing (at war / very low standing). Patterns: Circle/Orbit (100-2000 m), Stay at Range (min 0-1999, max 1-2000), Hit and Run, **Intercept** (ram; for warhead drones; guidance Basic or Target Prediction; no guns in this mode). Target priority Closest/Largest/Smallest; search interval 5-60 s (default 30); threatening subsystem filter; facing modes: Use Static Weapons or Manual Facing Priority; first selected weapon is the aiming reference, mixed weapon types aim poorly. It deactivates "Task" blocks while an enemy is in range.
  - **AI Defensive**: flee logic (Never / Always / when target-locked / when taking damage; away from target or to GPS/beacon), can trigger AI Offensive via toolbar.
  - **AI Basic (Mission)**: Follow Home (1-2500 m range, wander), Follow Player (0-500 m), Autopilot.
- Configuration lives in components, not block attributes: the default container for `OffensiveCombatBlock` creates `SearchEnemy`, `TargetLockingBlock`, `OffensiveCombatCircleOrbit/StayAtRange/HitAndRun/Intercept`, `AiBlockComponent`, `AiBlockPowerComponent` (`EntityContainers.sbc`, `ForceCreate="true"`); Flight uses `AutopilotComponent` [V-local]. So an AI block saved with **no** `ComponentContainer` should be filled with defaults on load [I]; to preset behaviour (e.g. Stay at Range 300-400 m, Speed Limit 100) you need a user-saved sample for the exact component XML (Starburst has an `AutopilotComponent` shape on its Remote Control: `AutoPilotEnabled`, `FlightMode`, `Waypoints`, `CollisionAvoidance`, `AutopilotSpeedLimit` ...) [V-local].
- Small-grid limits: nothing in the pages prohibits small grids; Flight needs six-direction thrust, so the Gnat's two axial thrusters are **not enough** for AI Flight [V-web requirement]. The Midge needs 6-axis thrusters, as HANDOFF says. Power 10 kW per block x4 = 40 kW.
- NPC "pirate" targets are enemies by faction standing; a player-vs-player test target needs an at-war faction or a different faction with enemy standing. [I from "faction standing" text]

## 4. Weapons

Source of truth: user's install [V-local] `Data/Ammos.sbc`, `AmmoMagazines.sbc`, `Weapons.sbc`, `CubeBlocks_Weapons.sbc`, `Warfare2`. Wiki table [V-web] https://spaceengineers.wiki.gg/wiki/Block_Weapons (no projectile speeds there).

| Weapon (small grid) | Mag/ammo | Speed m/s | Range m | Damage (local) | Rate | Spread | Notes |
|---|---|---|---|---|---|---|---|
| Gatling (`SmallGatlingGun`, empty subtype, 1x1x4) | `NATO_25x184mm` -> `LargeCaliber` | **800** | 800 | MassDamage **90** (blocks), HealthDamage 33, ExplosiveMult 0.6 | **700 RPM** (`RateOfFire`) | 0.15 deg | wiki: 600 RPM, 90/33/120 |
| Autocannon (`SmallBlockAutocannon`, 1x1x5) | `AutocannonClip` -> `AutocannonShell` | 800 | 800 | MassDamage **500**, Health 85, ExplMult 0.8 | 150 RPM | 0.15 deg | wiki agrees |
| Rocket launcher (`SmallMissileLauncher`, 1x1x4) | `Missile200mm` -> `Missile` | initial 100, accel 600, cap 200 | 800 | explosion **500**, radius 4 | 60 RPM | 0.1 deg | wiki agrees; rockets do not inherit launcher velocity (HANDOFF link) |
| Small railgun (1x2x8) | `SmallRailgunAmmo` -> `SmallRailgunSlug` | **1000** | **1400** | HealthPool 8000 (penetration), expl 0 | `RateOfFire 20`, `ShotDelay 500 ms`, `ReloadTime 4000 ms`; wiki "3 RPM / ~20 s variable" | 0 | gravity affects slug (`MissileGravityEnabled`) |
| Large railgun (1x2x8, large) | `LargeRailgunAmmo` -> `LargeRailgunSlug` | **2000** | **2000** | HealthPool 50000 | `ShotDelay 2000 ms`; wiki "1 RPM / ~60 s variable" | 0 | large grid only |

Other [V-local]: small warhead `ExplosionRadius` 4.4883 m, `WarheadExplosionDamage` 15000 (HANDOFF's 4.49 radius is right). Flares/fireworks irrelevant.

Discrepancies vs HANDOFF/wiki: gatling speed (400 vs 800), gatling RPM (600 vs 700), railgun charge delay not modelled. HANDOFF's "range 800 m randomized 80-100%" is not in the data files [I, remove or mark as unverified in the sim].

### Ammo the sim must assume
- Inventory capacity (m3, from block defs) / magazine volume gives magazines carried when full [V-local, derived]:
  - Gatling 0.064 m3 / NATO box 16 L = **4 boxes x 140 rounds = 560 rounds** = 48 s at 700 RPM. Box mass 35 kg.
  - Autocannon 0.072 m3 / clip 24 L = 3 clips x 16 = **48 shells** (19 s at 150 RPM). Clip 40 kg.
  - Rocket launcher 0.24 m3 / 60 L = **4 rockets**. 45 kg each.
  - Small railgun 0.048 m3 / 8 L = **6 slugs** (10 kg each). Large railgun 0.24 m3 / 40 L = 6 slugs (60 kg).
  - Auto-fill factor `InventoryFillFactorMin 0.6` on gatling/railgun defs: a pasted gun may start 60% full [I].
- **Creative:** Keen QA: "Since it's creative mode, the weapon does not need any ammo or magazine in order to fire"; in survival with creative tools ammo is required. [V-web] https://support.keenswh.com/spaceengineers/pc/topic/28147-1-202-066-creative-weapon-blocks-do-not-retrieve-ammo . The user plays "creative world" (HANDOFF 1): ambiguity between true Creative mode and Survival+creative tools. Make the sim switch `infinite_ammo` default **true**, plus a finite mode with the capacities above; ask the user which world type (test T8).
- Also, block weapons in a pasted blueprint carry whatever the blueprint inventory had; `sebp.py` should either pre-load magazines (Starburst has 29 `AmmoMagazine` items in inventories, so the XML shape exists [V-local]) or rely on Creative.

## 5. Physics and limits

### 5.1 Speed
`Data/Environment.sbc` [V-local]: `SmallShipMaxSpeed` 100, `LargeShipMaxSpeed` 100 (m/s), `SmallShipMaxAngularSpeed` 360, `LargeShipMaxAngularSpeed` 180. Angular unit is presumably deg/s (gyro slider cap is in `MaxAngularRadiansPerSecond`, [V-src] and the modding wiki: "slider limits are affected by Environment definition's Large/SmallShipMaxAngularSpeed" [V-web https://spaceengineers.wiki.gg/wiki/Modding/Reference/Blocks/Gyro]) [I on unit]. Mods/worlds may change; AI Flight speed limit slider is 0-100 m/s. [V-web]

### 5.2 Thrusters
- `SmallBlockSmallThrust` (ion, 1x1x2) [V-local]: `ForceMagnitude` **14400 N**, `MaxPowerConsumption` **0.2 MW**, `MinPowerConsumption` 0.000002, effectiveness in atmosphere falls to 0.2 at max planetary influence (`EffectivenessAtMaxInfluence` 0.2), mass 121 kg (computed, = wiki), flame damage 0.1. Large-grid small thruster `SmallBlockLargeThrust` (3x2x4): 172800 N, 2.4 MW.
- Thrust per kg of thruster alone: 119 N/kg. A 12-thruster Hornet (2 per axis) has 28.8 kN per axis pair.
- Mass of any block = sum of component masses (verified: merge 92.2, thruster 121.0, gyro 613.6, AI 156.0 all match wiki). Masses I computed: gatling 148.2 kg, autocannon 180.2, rocket launcher 226.2, **small railgun 1364 kg**, small battery 146.4 (0.05 MWh, 0.2 MW out), `SmallBlockBatteryBlock` 1040 kg (1 MWh, 4 MW), sensor 134.2, PB 138.4, camera 40.6, RC 38.2, antenna 84.2, timer 30.2, EC 38.4, warhead 106.2, welder 448.4. Component integrity is in `Components.sbc` (SteelPlate 100, Computer 1, Motor 40, Thrust 30 ...) [V-local].
- Override semantics: since Feb 2018 patch notes scripts' `ThrustOverride` is an **absolute force in Newtons** (not percent); `ThrustOverridePercentage` exists. [V-web via search snippet of Keen patch notes; page not opened]. Known bug thread: setting override by script can break overrides for the grid. [V-web] https://support.keenswh.com/spaceengineers/pc/topic/changing-thruster-override-via-script-breaks-overrides-completely-for-the-grid (status unknown).

### 5.3 Gyros and the sign convention
- `SmallBlockGyro` [V-local]: `ForceMagnitude` **448000** (N*m), `RequiredPowerInput` 0.002 MW, mass 613.6 kg. Large 3.36e7. Wiki lists the same. [V-web]
- Mechanism [V-src] https://github.com/KeenSoftwareHouse/SpaceEngineers/blob/master/Sources/Sandbox.Game/Game/GameSystems/MyGridGyroSystem.cs : override is a **velocity target**, not a torque: all overridden gyros' targets are averaged weighted by `MaxGyroForce`; `velocityDiff = target - localAngularVelocity`; `desiredAcceleration = velocityDiff * 60 / rampFrames`; `desiredTorque = desiredAcceleration / invInertiaDiagonal`; torque is clamped to a sphere of radius `sum(ForceMagnitude of overridden gyros) + unused...`, multiplied by `ResourceSink.SuppliedRatio` (low power = weaker turns), and applied with `AddForce(..., Torque)`. So: more gyros = higher clamp, higher inertia = slower; the achievable rate also hits the angular speed cap.
- Rough scaling [I, computed]: a 3 t drone with ~2 m extent has I ~ 12,000 kg*m2; one small gyro gives ~37 rad/s2 maximum, so the ramp time (`rampFrames`, value not seen) and the 2*pi rad/s cap dominate, not torque, for light drones. A Longbow carrying a 1364 kg railgun is much less agile.
- Override range: terminal slider limits are `0.01 RPM .. MaxAngularRadiansPerSecond`; wiki describes the UI range as +-30 RPM = 3.14 rad/s. [V-src / V-web] A script writing larger values is probably clamped or ignored [I].
- Overriding resets momentum each time the value is set (Keen report; every new yaw/pitch/roll value resets its momentum to zero) [V-web search snippet https://support.keenswh.com/spaceengineers/pc/topic/10813/vote/add/1]; one pitch gyro + one yaw gyro in override can fight ("dominant gyro") [V-web snippet]. Prefer setting all three axes on the same set of gyros every tick, as the scripts already do.
- **Sign convention check.** Keen's old `MyGyro.cs` [V-src]: `IMyGyro.Pitch => +stored.X`, `Yaw => -stored.Y`, `Roll => -stored.Z`, where `stored` is the target velocity in the gyro's local frame (`GyroOverrideVelocityGrid = TransformNormal(stored, Orientation)`). Whiplash's widely used `ApplyGyroOverride` negates the pitch input and writes `Pitch=X, Yaw=Y, Roll=Z` of the transformed vector, with a comment that Keen's conventions are unusual [V-web search snippet of `tank skid steering system v19.cs`; I could not open the file]. Both agree that yaw and roll are negated relative to a right-handed rotation about +Y/+Z; they disagree with HANDOFF's `Pitch = -l.X` unless `l` is defined in the script with a sign flip I have not seen. Given the user's in-game result (aim works), **do not change the scripts**. The old source is years out of date, so this is a flagged ambiguity, not a bug. Test T10 pins it.

### 5.4 Camera raycast
- Charge: "2 km/s or ~33.3 m/tick (default, mods can change it per camera block)"; `camera.EnableRaycast = true`; `AvailableScanRange`, `CanScan(distance)`; charge is not saved (0 after load); raycast is expensive especially against voxels. [V-web] https://spaceengineers.wiki.gg/wiki/Scripting/Detecting_Things
- Definition fields [V-local, only on `LargeCameraBlock`]: `RaycastConeLimit` **45** (deg), `RaycastDistanceLimit` **-1** (unlimited), `RaycastTimeMultiplier` 2.0, `RequiredChargingInput` 0.001 MW. `SmallCameraBlock` has **none of these fields**, so it uses engine defaults, assumed identical (45 deg / unlimited / 2.0) [I]. The modding wiki does not define these fields. [V-web]
- A raycast beyond the cone limit fails; the offset overloads `Raycast(distance, pitch, yaw)` are limited to 45 deg off the camera axis [I from field name; the HANDOFF assumption "~45 deg cone" matches the data].
- Quirks: camera raycast with NaN distance bypassed the range limit (reported, fix promised in v201) [V-web https://support.keenswh.com/spaceengineers/pc/topic/unlimited-camera-raycast-via-nan-injection]; old report of planet false hits at very long range (v1.187, outdated).
- Cameras attach on Back face only with a tiny patch (HANDOFF 4.3).

## 6. IGC, runtime, whitelist

### 6.1 IGC
- Broadcast + unicast; tag + data + source; `IGC.SendBroadcastMessage(tag, data, TransmissionDistance)`. `TransmissionDistance`: `AntennaRelay` (default, needs antennas), `CurrentConstruct`, `ConnectedConstructs`. A broadcast listener must be registered on the tag; accepted messages leave the queue. [V-web] https://spaceengineers.wiki.gg/wiki/Scripting/Antenna_Communication_(IGC)
- Between **different constructs** you need antennas: sender needs radius >= distance to receiver; the receiving antenna can be short-range but "Enable broadcast must be on or messages will not be received". [V-web IGC page + https://spaceengineers.wiki.gg/wiki/Antenna]
- Antenna range: 0-50,000 m; power in kW = radius(m)/250 (so 2 km = 8 kW, 50 km = 200 kW). Antenna definitions in the user data carry no max radius field (beacons have `MaxBroadcastRadius 200000`); default radius not stated. [V-web / V-local] A merged or connector-docked drone shares a construct, so `CurrentConstruct`/`ConnectedConstructs` messages need no antenna; `_JOIN` after release does.
- Allowed message types: bool/byte/ints/floats/double/char/string, `ImmutableArray/List/...`, `MyTuple` with 1-6 type arguments, Vector2/3/4 (incl. `Vector3D`, `Vector3I`), Matrix/MatrixD, Quaternion, BoundingBox*, Color, Base6Directions enums, etc. **Max recursion 25.** No size or count limit is documented. [V-web] https://spaceengineers.wiki.gg/wiki/Scripting/IGC_Allowed_Message_Types . HANDOFF's `MyTuple<long,Vector3D,Vector3D,double,long>` and the 5-field `_SWARM` tuple are within the allowed set. Keep one tuple per message; no mutable `List<T>` (use `ImmutableArray`/`ImmutableList`).
- Not found: per-tick send/receive limits, queue length, max payload bytes. Treat as unknown; keep messages small and drain queues each tick.

### 6.2 Runtime limit
- Wiki: a "significant instruction" is entering any code block/scope; "the limit is simply a safeguard against infinite loops"; `Runtime.MaxInstructionCount`/`CurrentInstructionCount` and `MaxCallChainDepth`/`CurrentCallChainDepth` expose the numbers; "1 ms is very slow". [V-web] https://spaceengineers.wiki.gg/wiki/Scripting/The_Runtime
- Number: "the **50,000** entered code blocks limit (Script too complex)"; a 37x37x37 triple loop exceeds it. [V-web] https://spaceengineers.wiki.gg/wiki/Scripting/The_Grid_Terminal_System . The property value itself was not read from a running game: print `Runtime.MaxInstructionCount` on the PB screen (already in HANDOFF 4.2 plan). `MaxCallChainDepth` value not found.
- `UpdateFrequency` Update1/10/100; `LifetimeTicks` resets on world load/grid paste; `TimeSinceLastRun` zero on first run; `LastRunTimeMs` available. [V-web]

### 6.3 Whitelist (what the drone scripts may use)
https://spaceengineers.wiki.gg/wiki/Scripting/Whitelist_Overview [V-web]. Allowed: `System.Collections`, `System.Collections.Generic`, `System.Globalization`, `System.Text` (incl. `StringBuilder`, `RegularExpressions`), `System.Linq`, `System.Collections.Immutable`; types `Math`, `Random`, `DateTime`, `TimeSpan`, `Convert`, `Array`, `Action`, `Func`, `System.IO.Path`; only a few members of `Environment`/`Type`. Not whitelisted: `System.Threading`, general `System.Reflection`, most `System.IO`. The whitelist is per assembly, so a type from the wrong DLL is rejected. The page recommends MDK2 for realtime whitelist checking. The scripts' stub compile cannot check this; the real game or MDK is needed. HANDOFF's removal of `MyResourceSinkComponent` is consistent with a whitelist rejection [I].

## 7. Programmable block: missing script and paste failures

### 7.1 "Script missing after paste/spawn"
Ranked hypotheses, with evidence:
1. **World setting "In-game scripts" off** (needs Experimental mode for the option to appear): scripts "run only if in-game scripts are enabled in the World Settings"; hosting docs state "Without this option such scripts are removed when the world loads", and the Edit button is missing. [V-web] https://spaceengineers.wiki.gg/wiki/Programmable_Block , https://docs.emeraldhost.de/en/gameserver/space-engineers/enable-ingame-scripts . If the user tested in more than one world/save, this alone could explain intermittent loss. Ask which world.
2. **Projector-built PB** is not auto-compiled (Keen thread, section 1.3); text present, code idle. Recompile fixes.
3. **Compile failure** (whitelist/API mismatch): the PB shows an error but should keep the text [I]. HANDOFF already lists this; v1.8 not compiled against real API yet.
4. **Stale cache**: Starburst folder contains `bp.sbc` (3.07 MB) beside `bp.sbcB5` (0.6 MB) and `thumb.png` [V-local], so the game itself writes a binary cache. Whether the game prefers a stale `.sbcB5` over an edited `.sbc` is unconfirmed. Keep the "delete the old folder" rule (CLAUDE.md). [I]
5. **Wrong place in XML**: `Program` must be inside the PB's `MyObjectBuilder_MyProgrammableBlock` element, after `Enabled`; Starburst shows `<Program>` then `<Storage />` [V-local]. The text must be XML-escaped and free of control characters; one bad character invalidates the whole blueprint (paste fails rather than the script vanishing) [I].
6. There is **no "reset script on paste" option** that I could find. `LifetimeTicks` resets on paste, meaning the program restarts, and `Storage` is persisted by the game; Custom Data may be captured before `Save` runs. [V-web] https://spaceengineers.wiki.gg/wiki/Scripting/Handling_Configuration_and_Storage
- The wiki states a blueprint "stores ... PB scripts, configured toolbars and block groups". [V-web Blueprint page] So in principle F10 paste keeps the code.
- Keen forum shows no matching report of "blank PB after F10 paste" [V-web searches]. The 6 reports found are about disabled scripts, workshop import, and editor freezes.

### 7.2 Why blueprints with Sensor / Turret Controller fail to paste
No documented cause found online; evidence-based candidates, ranked:
1. **Incomplete object builder** for blocks that have a Toolbar or settings (Sensor, Timer, EC, Turret Controller). `sebp.py` comments already say Keen's source dereferences `Toolbar` during id remap; our earlier sensor omitted it. [I, from repo]
2. **TurretControlBlock needs many valid cross references**: Starburst's has `AzimuthId`, `ElevationId`, `CameraId`, `ToolIds`, `DirectionBlockId`, `IsCorrect`, `AllowAI`, `Flags` [V-local]. A turret controller whose ids point to no existing block (we have no rotors in a drone) may fail validation. Pairing it with a camera and guns only, set `IsCorrect=false`, ids absent/0. Also its Starburst shape contains the large `MultiTextPanelComponent` blocks; whether they are mandatory is unknown. Test T11.
3. **Known non-causes**: block ids are valid; DLC tag only informs (missing DLC blocks are dropped/"missing" rather than aborting, structural ones can break the grid in two) [V-web Blueprint page]. Starburst's `<DLC>` appears once at blueprint level. Duplicate EntityIds and overlapping blocks are ours to avoid (validator already checks).
4. Other generic causes seen online: missing mods/definitions, corrupted blueprint, unstable connection for DLC checks [V-web search snippets, low confidence].
- Always ask for `%AppData%\SpaceEngineers\SpaceEngineers_*.log` after a failed paste; it names the exception (HANDOFF 4.1).
- Size: blueprints list their size in MB; workshop thumbnails < 1 MB. No hard block-count limit found; the 13-grid Starburst blueprint is 3 MB and works. [V-web / V-local]
- `ShipBlueprint` Id collisions: not found as a failure mode online. The Id is `Subtype` of `MyObjectBuilder_ShipBlueprintDefinition`; Starburst uses a plain name. [V-local]; keep unique per package anyway [I].

## 8. Community designs and lessons

- **Projector drone / missile factories:** Keen threads describe a rotor + piston launch rig projecting a small drone with a pair of merge blocks, released by an Action Relay switching the merge block off (hit the "Cannot find blueprint" softlock); a missile factory via projector + merge (Beeblebom guide) and the PB-not-compiling report. [V-web] https://support.keenswh.com/spaceengineers/pc/topic/44353-error-with-projector-and-merge-block . Lessons: Keep Projection ON; do not grind/unpower the projector mid-cycle; recompile PBs after build.
- **Steam guide "Building Automatic Ship Printing Pad in Survival"** (https://steamcommunity.com/sharedfiles/filedetails/?id=1875791800) exists; fetch returned HTTP 429 so contents unverified.
- **Bumblebee Script** (vanilla, disposable drone swarm; one drone per script instance, experimental, no collision detection, marked obsolete); **Drone Master Script v3.4** (removed from Steam); **PAM** (mining drones); **Rdav's fleet command** (fighters in formation; unverified status) [V-web search results, not opened].
- **PTB-X01** (MIT, vanilla): single attack drone; approach by range bands (~50 m sneak, ~400 m cruise, ~1000 m full speed), fires when target within a 30 degree forward cone, retreats when main arm or camera lost. https://github.com/s-yurama/SpaceEngineers_Drone_PTB-X01 [V-web, README only]. Reusable idea: loss-of-capability retreat, range-banded speed, and a fire cone; our scripts already have analogues.
- **WHAM (Whip's Homing Advanced Missile Script)**: proportional-navigation homing, raycast homing class, camera proximity scan; companion fire-control "LAMP". Repo https://github.com/Whiplash141/SpaceEngineersScripts (folders Released/, Unpolished/, Classes/, Methods/) [V-web via search snippets; code not read]. Best reference for the Stinger's terminal guidance (ProNav instead of pure pursuit) [I].
- **Vanilla scriptless option for kamikaze**: AI Offensive **Intercept** mode with Target Prediction guidance and warheads (designed for "PBWs with warheads") [V-web]. Candidate for the scriptless Stinger.
- Wiki Scripts page lists **ProjectorInfo** (LCD of welded block counts) and Nanobot (mod, not vanilla) for printing; nothing combat-specific. [V-web] https://spaceengineers.wiki.gg/wiki/Scripts

## 9. What I could not verify

1. The merge face of `SmallShipSmallMergeBlock` beyond one wiki sentence (Up). Keen source for the merge block returned 404 and the SBC has no direction field.
2. Absolute merge holding force for `Strength` 0.005; required angular tolerance for snapping.
3. Whether a projected PB keeps `<Program>` and whether it compiles on completion (only an old, unreproduced Keen report).
4. Exact serialized XML for Timer, Sensor, AI blocks, Merge block, Warhead (no sample in Starburst; `sebp.py` shapes are from memory of Keen source, not re-checked here).
5. Why Sensor/Turret Controller blueprints failed to paste: no online source; candidates only.
6. Real value of `Runtime.MaxInstructionCount` (50,000 comes from a wiki sentence) and `MaxCallChainDepth`.
7. IGC payload/queue limits; default antenna radius.
8. Gyro pitch sign (Keen old source vs HANDOFF) and the override acceleration ramp frames; whether values above the angular cap are clamped.
9. Small camera raycast defaults (inherited, not listed in its definition).
10. Whether creative worlds in the user's save need ammo (true Creative vs Survival+creative tools); gatling auto-fill percentage at paste.
11. Welder weld speed (time per block) and the welder's exact sensing volume; thus no print-time estimate.
12. Whether EC/timer `SelectedBlocks`/toolbar references to not-yet-built blocks survive projector welding.
13. Steam guide for the printing pad (429), WHAM source, Whiplash `ApplyGyroOverride` source (404 on the file URL).
14. Whether the game prefers a stale `.sbcB5` to an edited `bp.sbc`.

## 10. Recommended in-game tests (cheap first)

| Id | Test | Decides |
|---|---|---|
| T1 | Place two `SmallShipSmallMergeBlock`s on two small grids with Up facing each other (and separately Right facing); see which pair turns yellow/merges. Or just look at the block model for the clamp. | Merge face (Up vs Right) |
| T2 | Save a small grid (Ctrl+B) containing one each of: Timer, Sensor, Custom Turret Controller (with camera + gun), Event Controller (with a selected event + actions), AI Offensive, AI Flight, AI Defensive, AI Basic, Merge Block, Projector (projection loaded, Keep on), Welder, Warhead, PB with a short script. Send `bp.sbc`. | Real XML shapes; paste-failure cause (section 7.2) |
| T3 | Two merge blocks merged; switch the drone-side one off, then the printer-side one off. | Which side can release; scriptless release |
| T4 | Merge a dynamic printer ship with a drone, then unmerge. Repeat with a static printer. | Static-grid trap |
| T5 | Project and weld a drone with a PB holding a script; check Edit window text, whether it runs without Recompile, and the block owner; toggle `GetOwnershipFromProjector`. | Q1/Q7 PB behaviour, ownership |
| T6 | Same as T5 but call `ApplyAction("Recompile")` from the printer PB while merged. | Mitigation for uncompiled PB |
| T7 | Projected drone containing an EC that references drone blocks (battery >= 90% -> own merge Off + AI On). Weld in random order. | Scriptless drone feasibility |
| T8 | In the user's world: place a gatling with empty inventory; fire. Report world type (Creative vs Survival + creative tools). | Ammo model |
| T9 | Small railgun: time trigger-to-impact and fire interval at full power. | Pre-shot delay, real recharge |
| T10 | Set one gyro `GyroOverride=true, Pitch=+1` (others 0) on a stationary drone; note whether the nose goes up or down; repeat for Yaw and Roll. | Pins the gyro sign convention |
| T11 | Paste a drone with a Turret Controller + camera + gun and no rotors; check `IsCorrect` and the log. | Turret controller paste failure |
| T12 | Print `Runtime.MaxInstructionCount`, `MaxCallChainDepth` on the PB screen; send an IGC test tuple between two antenna-equipped small grids at 1 km and 10 km with radius 1 km. | Runtime and IGC limits |
| T13 | Check world setting "In-game scripts" in each test world; paste with it off. | Cause 1 of missing scripts |
| T14 | Edit `bp.sbc` in a folder that has an old `bp.sbcB5`; paste. | Stale cache hypothesis |
