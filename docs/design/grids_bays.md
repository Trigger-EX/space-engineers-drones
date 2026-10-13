# grids_bays: drone bays on big ships (research and decision)

Key `grids_bays`. Read-only research; this file is the only repo change. Evidence labels: [data] repo game data or the owner's own game-written blueprints, [wiki]/[src] fetched pages, [rec] recollection with confidence, [sim] read from sim/ code.

## 0. Decision in one table

| Class | Bay | Drones | How it holds them | Launch | Recovery (phase 2) |
|---|---|---|---|---|---|
| Fighter (small) | none. Optional: one external Gnat clamp, not recommended | 0 (1) | - | merge off | - |
| Corvette (small) | CRADLE, integrated in the hull: 2 outrigger pylons, 1 Hornet-class H-slot each, drone nose forward, stern merge to a wall | 2 (Hornet, Wasp, Watcher, Harrier, Lancer, Aegis) | `SmallShipSmallMergeBlock` pairs, drones pasted as separate small CubeGrids already touching | Staff PB over IGC: LAUNCH block armed, host merge off, drone burns away | fly the drone nose-in to its own slot, merge re-clamps |
| Frigate (large) | Hangar + docked CRADLE POD: 4-slot small-grid pod on 2 connector pairs, 8-door hangar mouth | 4 | pod = same cradle module as the corvette, docked by `ConnectorMedium` to large `Connector` | Staff PB -> Pod PB (IGC `ConnectedConstructs`), doors open, release | same as corvette, inside the hangar |
| Dreadnought (large) | same pod, 2 hangars (port, starboard) | 8 (up to 16 with 4 hangars) | same | same, release in waves | same |

PRINT bays (projector + welders + merge) stay an opt-in extra for small-grid hosts only, and are not shipped on any new warship. Reason: docs/audit/printers.md verdict (projected blocks fuse to the projector's grid) and 3.4k-12k PCU per printer.

Why this and not something else, in three lines:
1. The cradle needs no projector, no welder and no component stock. Every drone is already a separate grid at paste time, so the "FUSED" risk of the printers does not exist; the only merge risk left is T1 (which face clamps) and "full-surface contact makes a merge permanent" (wiki), which the existing printer geometry already avoids (`check_docking`).
2. A small-grid pod docked by connectors is exactly what the owner's own game saved: `Venture - Expedition End` holds a 432-block small ship locked to a large `Connector` with a small `ConnectorMedium` inside one blueprint [data, section 2.3].
3. One cradle module (small-grid geometry + one bay PB) serves the corvette (built in) and the frigate/dreadnought (docked). One generator function, one script, one test.

## 1. Grid-size rules (what the real game does)

| Question | Answer | Evidence |
|---|---|---|
| Small blocks placed on a large hull: one grid? | No. A small block placed on a large block is a separate small grid (floats off / is its own entity). It is not a documented way to build one rigid thing. Do not use it. | [wiki] How_to_connect_small_and_large_grid_blocks lists only connectors, rotor/hinge small heads, hitching two grids, timer auto-attach. Community posts found by search say the same. [rec 75%] for the exact physical coupling of the `EnableSmallToLargeConnections` flag (the grid XML has that field; Hydrogen Buggy writes `false`, others omit it): I do not rely on it. |
| Small rotor/hinge head on a large stator/hinge | Yes, permanent-ish. The head grid is a SUBGRID of the large ship. | [wiki] "considered a subgrid", small heads fit large stators. [data] Tech Hunter: `LargeStator` + `SmallRotor` ("Brain Stem"), `LargeHinge` + `SmallHingeHead` x 6 (small weapon pods of 45 blocks: 4 `SmallBlockAutocannon` each). Venture: `LargeStator` + `SmallRotor`. |
| Terminal / PB / `IsSameConstructAs` for subgrids | Rotor, piston, hinge, wheel subgrids: same construct, the GTS sees them, IGC `CurrentConstruct` covers them. | [wiki] Scripting/The_Grid_Terminal_System: construct = "any set of grids connected by rotors and pistons". |
| Connectors | A large `Connector` accepts small-grid connectors. GTS reaches the docked grid when the owner matches, but `IsSameConstructAs` ignores docked grids (not one construct). | [data] Venture: small `ConnectorMedium` <-> large `Connector`, both with `ConnectedEntityId`, `MasterToSlaveTransform`, `MasterToSlaveGrid`; `Small Grid 8000` / `drill test`: small `ConnectorSmall` <-> large `LargeBlockInsetConnectorSmall`. [wiki] Connector page: "temporarily connect Small Grids and Large Grids"; GTS page says connected grids are accessible with the same owner and that `IsSameConstructAs` "ignores anything docked". |
| Landing gear / magnetic plates | Lock across sizes (small gear to a large hull, large to small). Grids locked by gear are NOT in the GTS. No power or items pass. | [wiki] Landing_Gear page ("smaller ships can lock to the outer hull of a carrier"); GTS page excludes gear-locked grids. Samples contain `LargeBlockSmallMagneticPlate` and `SmallBlockSmallMagneticPlate` (non-DLC, 35 PCU) but no locked state. |
| Merge blocks | Same size only (small+small, large+large). After a merge the two are ONE grid (power and control shared, conveyors not). The merge is permanent if any other full block surface touches; only merge blocks plus thin edges touching = switchable. | [wiki] Merge_Block. |
| Rotors / pistons | Subgrids, see above. | [data] Pond Hopper: `SmallPistonBase` + `SmallPistonTop` x 4 on a small ship. |
| Projector vs grid size | Not established. Every projector in the owner's samples projects its own size (Large->Large, Small->Small). A large projector projecting a small blueprint: [rec 60%] works (there is a `Scale` field), but nothing here needs it. | [data] census above. |
| Welder / grinder across sizes | Not needed (no printing on big ships). | - |

Consequences for design: (i) a small-grid deck cannot be fused into a large hull, only attached as a subgrid (rotor/hinge) or docked (connector); (ii) a merge cradle only works between small grids, so the cradle must live on a small grid; (iii) docked small pods are separate constructs, which is a feature for script isolation (section 6.3).

## 2. What the owner's blueprints show

### 2.1 Tech Hunter (`pc/enemies/Tech Hunter/bp.sbc`)
- 34 CubeGrids (20 Large, 14 Small), 13,666 blocks in the main grid (Large, 215x97x72 m by task brief).
- CORRECTION to the brief: `Fighter Ac` is NOT a separate CubeGrid in a hangar. It is a `ProjectedGrids` entry inside a `SmallProjector` (small grid `Small Grid 6217`, 2 blocks: `SmallRotor` + `SmallProjector`) sitting on the `LargeStator` "Brain Stem" at (23,0,4) of the main grid. The projector is `Enabled=false`, `ProjectionOffset (2,-8,-5)`, `ProjectionRotation (0,0,1)`, `KeepProjection=false`, `ProjectionsRemaining=0`. The hologram is a Small grid of 524 blocks, bbox 13x9x22 cells (6.5 x 4.5 x 11 m), 28 `SmallBlockSmallHydrogenThrust`, 2 `SmallBlockAutocannon`, 8 `SmallBlockGyro`, conveyors. Nothing is locked in the XML.
- Hangar-ish parts: 2 `AirtightHangarDoor`, 1 `LargeBlockLandingGear` (`AutoLock=false`, `IsLocked=false`), 1 `Connector` (`IsParkingEnabled=true`), 5 `AirtightSlideDoor`, 6 `Projector`, 132 `ShipWelder` (a builder ship). No docked ship anywhere.
- The small grids are weapon/welder pods on `SmallHingeHead` of `LargeHinge` (45 blocks each, 4 autocannons or a projector), 6 single-block `SmallHingeHead` grids, and a timer-fed pod on a `SmallRotor` (14 `TimerBlockSmall`).
- Take-away: the owner's large ship hosts small-grid structures as hinge/rotor subgrids. This is the proven way to put small-grid blocks INSIDE a large-ship blueprint, and the blueprint stores them as separate `CubeGrid` elements.

### 2.2 Link XML shapes (copy these)
Rotor/stator (Tech Hunter, `Venture`): the stator carries `<TopBlockId>`; the head grid carries a `MotorRotor` block whose `<ParentEntityId>` is the stator's EntityId (checked in Venture: 131342597590583079).
```xml
<MyObjectBuilder_CubeBlock xsi:type="MyObjectBuilder_MotorStator">
  <SubtypeName>LargeStator</SubtypeName><EntityId>89648156706807416</EntityId>
  <Min x="23" y="0" z="4" /><BlockOrientation Forward="Left" Up="Backward" />
  ... <Enabled>true</Enabled>
  <TopBlockId>138535195605047362</TopBlockId><ShareInertiaTensor>false</ShareInertiaTensor>
  <SafetyDetach>5</SafetyDetach><RotorEntityId xsi:nil="true" /><WeldedEntityId xsi:nil="true" />
  <TargetVelocity>0</TargetVelocity><LimitsActive>false</LimitsActive><RotorLock>false</RotorLock>
  <Torque>3.36E+07</Torque><BrakingTorque>0</BrakingTorque>
</MyObjectBuilder_CubeBlock>
<!-- head grid: <GridSizeEnum>Small</GridSizeEnum>, own PositionAndOrientation, block: -->
<MyObjectBuilder_CubeBlock xsi:type="MyObjectBuilder_MotorRotor">
  <SubtypeName>SmallRotor</SubtypeName><EntityId>138535195605047362</EntityId>
  <ParentEntityId>(stator EntityId)</ParentEntityId><YieldLastComponent>false</YieldLastComponent>
</MyObjectBuilder_CubeBlock>
```
Connector pair (Venture, both sides carry the id of the other; one side `IsMaster=true` with the transform, the other `MasterToSlaveGrid xsi:nil`):
```xml
<ConnectedEntityId>97090944396047994</ConnectedEntityId>
<MasterToSlaveTransform><Position x="0.00728" y="0.00056" z="-1.8204" /> ...Forward/Up/Orientation... </MasterToSlaveTransform>
<MasterToSlaveGrid> <OrientationOffset>..</OrientationOffset><PositionOffset>..</PositionOffset>..</MasterToSlaveGrid>
<IsMaster>true</IsMaster><TradingEnabled>false</TradingEnabled><AutoUnlockTime>0</AutoUnlockTime>
<TimeOfConnection>237002</TimeOfConnection><IsParkingEnabled>true</IsParkingEnabled>
<IsPowerTransferOverrideEnabled>false</IsPowerTransferOverrideEnabled><IsApproaching>true</IsApproaching><IsConnecting>true</IsConnecting>
```
The 1.82 m is the centre-to-centre distance of a small `ConnectorMedium` locked to a large `Connector` [data]. An unlocked connector is just the block with `<Enabled>true</Enabled>`, `<IsParkingEnabled>`, `ConnectedEntityId` absent (as in Pod Chaser, Pertam Pod).
Merge block (Sudden Deth) has no state at all after `Enabled`: the lock is NOT saved, the game re-evaluates it at runtime:
```xml
<MyObjectBuilder_CubeBlock xsi:type="MyObjectBuilder_MergeBlock"><SubtypeName>SmallShipSmallMergeBlock</SubtypeName>...<Enabled>false</Enabled></MyObjectBuilder_CubeBlock>
```
Hangar door (Starburst): `xsi:type="MyObjectBuilder_AirtightHangarDoor"`, `<SubtypeName />` (empty), `Forward=Backward Up=Up`, tail `<AnyoneCanUse>false</AnyoneCanUse><Open>false</Open><CurrOpening>0</CurrOpening>`. defs: 1x3x1, 115 PCU, 7.86 t each, non-DLC. In Starburst they stand 1 cell wide and 3 cells tall, one per x step.

### 2.3 Multi-grid blueprints in the samples
| File | Grids | Small/large combination | Locks stored |
|---|---|---|---|
| `Venture - Expedition End` | 4: Large 713 blocks, Small 432, Small 5 (rotor head), Large 48 | the 432-block small ship is connector-docked to the Large grid by `ConnectorMedium` <-> `Connector`; another Large (48) is docked by `Connector` <-> `Connector`; a small rotor head hangs on a `LargeStator` | connector links with full transforms |
| `Small Grid 8000`, `drill test` | 2: Small fighter (38 blocks, `DBSmallBlockFighterCockpit`) + Large (5 blocks) | `ConnectorSmall` <-> `LargeBlockInsetConnectorSmall` locked; `SmallBlockSmallMagneticPlate` and `LargeBlockSmallMagneticPlate` present, both `LockMode=Unlocked`, `AutoLock=true` | connector link |
| `Starburst v2/v3/v4` | 10-13: Large carrier with 12-14 `LargeBlockInsetConnector`, 42 `AirtightHangarDoor`, hinge/piston arms, a small projector pod | connector pairs to separate Large grids | connector links |
| `Pond Hopper (Mk2)` | 3 / 5 small | piston tops as separate small grids | piston `TopBlockId` |
| `Hydrogen Buggy`, `Small Grid 8269` | 5 small | wheels (suspension) | - |
The blueprint file keeps all CubeGrids and all connector/rotor/piston links. Landing-gear and merge locks are never stored as locked in any sample (all gears `IsLocked=false`, merge has no state field). What happens when such a blueprint is pasted (are the grids placed with their relative positions and do connector links come back locked) is not seen by me in a game session. The Venture/`Small Grid 8000` files were written by the game after the player had docked, so a re-paste restoring the lock is [rec 70%], hence test C2.

## 3. Existing repo assets and what they imply

| Asset | State | Reuse |
|---|---|---|
| `scripts/HiveMotherAI.cs` (950 lines): bay = projector + welders + merge; states IDLE, BUILD, FINISH, CHARGE, ARM, RELEASE, COOLDOWN, DOCKED (rack), FAULT | designed around the projector; no-go verdict per docs/audit/printers.md | take: bay table by Custom Data (`[Bay N]`, `Drone Box Min/Max`, `Ignore Cells`), `FindDrone` (locate the drone PB/merge by cell box on the shared grid), `Release`/`Separate`/`Nudge`, ARM (writes `[Launch]` and runs `armed`), `DroneCharged` (battery gate), `FUSED` detection, Rack mode `launch` command, per-bay try/catch `BayFail`. Drop: projector, welder, stock, FINISH scan. |
| `tools/launch_block.cs` (LAUNCH v1.0 in 9 scripts: Hornet, Harrier, Gnat, Longbow, Wasp, Lancer, Aegis, Mantis, Basilisk) | state machine docked(1) -> burn(2) -> launched(3) | take-off needs nothing new for a cradle: separation of the merge pair triggers it (trigger 1), `armed` + `Away X/Y/Z` custom data (2), `launch` argument (3). Its state 1 -> re-docking arm is already in the block ("a merge pair locks"), so a drone that is flown back onto a cradle re-arms itself. |
| `tools/designs/printer_lib.py` | `BayDesign`, `solve_bay`, `exit_prism`, `merge_small`, `merge_clamp_dir`, `check_docking` | take the merge-pair geometry and exit-prism code; do not use welder/projector parts. |
| Rack mode (Stinger Rack) | "DOCKED after CHARGE, `launch` x3 after separation" | the cradle bay IS a rack-mode bay that never had to print. |
| Hornet-class merge | `SmallShipSmallMergeBlock` at the stern, clamp face Up = Backward (Hornet), Down for Gnat | docking face already validated by `check_docking`; clamp face still unverified in game (T1). |

Which drones can be cradled: only those with a merge block (MODELS.md, Merge=1): Hornet, Wasp, Watcher, Harrier (3.5x3.0x4.0 m = 7x6x8 cells), Lancer, Aegis (3.5x3.0x4.5 = 7x6x9), Midge Full (3.5x3.0x5.0 = 7x6x10), Gnat and Midge Lite (2.0x2.5x2.5 = 4x5x5). Longbow S / Mantis / all large drones have none (adding one to Longbow S or Mantis is a drone-side change, not planned).

## 4. Candidate comparison

Scores: H high, M medium, L low. "Generator" = tools/sebp.py today writes ONE CubeGrid at origin (`build_xml`, fixed Position 0,0,0, identity orientation). "Sim" = sim/ as is.

| | (a) CRADLE merge | (b) PRINT bay | (c1) pod docked in hangar | (c2) small deck as rotor subgrid | (d) drone connectors |
|---|---|---|---|---|---|
| Works in game, confidence | M-H (needs T1, C1) | L (printers.md no-go) | M-H (connector pairs proven by owner; pod PB isolated) | M (rotor subgrid proven by Tech Hunter; heavy deck on a small head wobbles [rec 65%]) | M-H for docking, but drones have no connector |
| Paste simplicity | one blueprint (several grids) | one blueprint | frigate blueprint with the pod docked, or two pastes | one blueprint | one blueprint |
| Script complexity | low: new `CradleBay` (about 8-12k chars) or HiveMother cut down | already exists, already doubtful | the same bay PB plus door control | same | high: drone dock-to-connector approach, LAUNCH block change |
| PCU per slot | 100 (merge) | 3.4k-12k per printer | 100 + pod overhead | 100 + 2 stators | 125 per drone connector + drone retrofit |
| Generator | needs a multi-grid writer (new, section 7) | has it (single grid + ProjectedGrids) | same new writer + connector links | same + stator/rotor links | same |
| Sim | needs a merge-across-bodies pass (section 7) | `printer-test` exists but cannot place projected blocks | connector not modelled; treat the pod as welded | rotor not modelled | no |
| Verdict | CORVETTE | opt-in, small-grid only | FRIGATE, DREADNOUGHT | rejected (wobble, no benefit) | rejected for phase 1; revisit for charge/rearm |

Note on (d): connectors would add power transfer (recharge) and conveyor items (ammo), which the merge cradle gets only partly (merge shares the electrical grid while merged, so the host charges drone batteries; no items). Drones have no conveyors or cargo (audit), so (d) gains little.

## 5. Recommended designs

### 5.1 The H-slot (cradle unit, small grid)
Cell = 0.5 m. Drone box 7x6x9 cells (covers Hornet/Wasp/Watcher/Harrier 8 long and Lancer/Aegis 9 long).
- Clamp axis = the drone's merge direction (Hornet: Backward). Host merge block `SmallShipSmallMergeBlock` one cell behind the drone's merge block, facing it (centres one cell apart, as in `DockLaunch.BuildDocked` and `PairMerges`).
- Host wall: 1-cell stalk (the host merge block only, no armour touching the drone's stern ring), then a 2-cell armour back wall.
- Free box around the drone: 1 cell on every side (so 9 x 8 x 9 interior), no block of the host may touch a drone surface except the two merge blocks (otherwise the merge is permanent, wiki). Check with `check_docking` for the drone and a new check on the host side.
- Launch lane: from the drone nose, 9 x 8 cells cross-section, 24 cells (12 m) long and fully open. The LAUNCH default `Clear Distance` is 8 m, drone length 4-4.5 m, margin 2 m. Nothing else (turret barrels, antennas, hull) in the prism.
- Exhaust: the thrusters that push the drone forward fire backwards into the wall. The wall keeps the stalk gap of 1 cell plus the merge pair; the drone's exhaust reserve zone is already enforced in `_common.keep_clear`. Whether ion flame damages the wall at the 4 s burn is untested (flame damage values exist in `defs_stats.json`, 0.1-1). Test C5.
- Slot pitch: 9 cells (4.5 m) sideways, 8 cells (4 m) up.
- Block cost: about 90 small armour blocks per slot, 1 merge (100 PCU, 92 kg).

### 5.2 Fighter (small grid)
No bay by default. A Gnat (4x5x5 cells, 1941 kg, belly merge) can physically hang in an external clamp under a fighter hull of 12 m or more, but: +672 PCU, +1.9 t, a Gnat has thrusters on the nose axis only, and the LAUNCH `Away` direction (merge to COM = belly-normal) is perpendicular to its thrust, so the burn depends on the fallback "thrust axis closest to Away" which ties between forward and backward (test C6). Verdict: omit. If the owner wants a token bay: one Gnat clamp, nose forward, bay PB (or the fighter's own PB) writes `[Launch] Away X/Y/Z` = the fighter's forward vector.

### 5.3 Corvette (small grid)
- 2 H-slots on two outrigger pylons, port and starboard, drones nose forward, back wall aft. Pylon outboard offset so the 9-cell-wide lane clears the hull and the beam weapons by 1 m: lane axis at least 3 m outside the hull skin.
- Optional (not in v1): 2 Gnat clamps on the dorsal deck, Away written by the bay.
- Drones: any of the 8 merge models above. Mix allowed. 2 x ~4.5 t.
- PCU: 2 merges 200 + Bay PB 100 + 2 backup timers 50 = 350 on top of the ship; drones 2 x ~1010 = 2020.
- Grid sharing: while merged, the drones are on the SAME grid as the corvette. See 6.3 (name tags). After release the grids split (the drone is lighter, so it becomes the small split).
- Recovery: see 5.6.

### 5.4 Frigate (large grid): hangar + docked CRADLE POD "P4"
- Pod P4 = 4 H-slots in one row: 36 cells wide (18 m) x 8 cells (4 m) high x 13 cells (6.5 m) deep, plus its own frame; mass roughly 5 t empty, 4 x 4.5 t drones; pod PCU about 400 (4 merges) + 250 (2 `ConnectorMedium`) + 100 (PB) + 100 (4 backup timers) + frame about 300 = about 1,150; with drones about 5,200.
- Docking: 2 `ConnectorMedium` (3x3x2 cells = 1.5 x 1.5 x 1.0 m, 685 kg, 125 PCU) on the pod's back wall, spaced across the width; host side 2 large `Connector` (1x1x1 large = 2.5 m cube, 3.6 t, 125 PCU) on the hangar rear wall, centre-to-centre 1.82 m [data]. Two pairs, not one, for stiffness. Option: `SmallBlockSmallMagneticPlate`/gear is not used (no power, and a gear-locked grid leaves the GTS).
- Hangar: interior 8 large cells wide (20 m) x 3 high (7.5 m) x 8 long (20 m): pod 3 cells deep at the aft wall, 5 cells (12.5 m) lane to the mouth. Mouth 8 cells x 3 cells = 8 `AirtightHangarDoor` (1x3x1 each, standing as in Starburst): 920 PCU, 63 t. Doors are optional ("open mouth" variant saves 63 t and 920 PCU; the hangar then needs only an armoured lip).
- Drone envelope: 4 drones in a row are 18 m wide and 3.5 m high; mouth 20 x 7.5 m leaves 1 m sideways, 2 m up/down.
- Pod orientation: nose to the mouth, gravity-free (space only; atmosphere is out of scope for these drones).
- Pod Interface to the frigate: GTS (same owner) lets the frigate's Staff PB see the pod; IGC uses `TransmissionDistance.ConnectedConstructs` (see 6.2). Both are tested in C3.
- Script: pod PB "CradleBay" (new, small, 8-12k chars) is the only thing that touches merge blocks. The Staff PB opens doors, then sends `release`.
- Fallback if connector auto-lock fails at paste: Staff PB calls `Connector.Connect()` on the 2 host connectors once at start, and the pod is also exported as a stand-alone blueprint (`Cradle Pod P4`) the owner can fly into the hangar by hand (the connector pulls in).

### 5.5 Dreadnought (large grid)
- 2 hangars (port and starboard), each the frigate hangar with a P4 pod: 8 drones. A 4-hangar layout gives 16 (PCU 16 x 1010 + 4 x 1,150 = about 21k for the bays: only for a PCU-unlimited world; set `Bays` per variant).
- Drones: Hornet class (mixed with Harrier, Wasp, Lancer, Aegis, Watcher), same as above. Gnat pods (4x5x5 cells, slot pitch 6 x 7 cells) possible as a P8 pod of 8 Gnat if a swarm is wanted; each Gnat 672 PCU. Large-grid drones (Basilisk, Longbow L, no merge blocks) are not covered; a future `LargeShipMergeBlock` (large, 558 kg, 100 PCU, non-DLC) cradle inside the dreadnought would need a merge block added to those hulls.
- Release in waves: 2 s apart per drone, 20 s between pods, so 8 drones leave in about 30 s.

### 5.6 Recovery (phase 2, do not block v1)
- The merge only clamps when the two clamp faces face each other within about a metre and are aligned [rec 60% on the alignment tolerance, wiki: yellow <1 m, snap about 0.1 m]. A drone returning to its slot must arrive along the lane axis. Its merge is at the STERN, so it must reverse into the slot (tail-in). Script cost: an approach controller "tail-in docking" in each drone script (about 150 lines, an extension of the existing `home` mode) and the bay publishing its slot pose (position + forward) over FLEET. Alternative: a Hornet with the merge at the nose would fly in nose-first, but that moves the merge relative to the guns.
- Cheap interim for the owner: manual. The pilot flies a drone with the ship's remote control, or leaves drones to be re-clamped on a stationary host. For a combat game: drones are expendable; the hangar `recall` can wait.

## 6. Script protocol

### 6.1 Who commands what
- Helm PB (flight/fire, Update1): never touches bays. Staff PB (Update10, fleet/IGC/bay manager, timers_pbs.md section 7): decides.
- Bay PB ("CradleBay" per pod or per corvette, Update10): owns the merge blocks, bay table, drone arming. Crashes cost only that pod.
- Staff -> Bay (unicast IGC, tag `<ShipTag>_BAY`, string):
  `release <n|all> [delay_s]`, `recall`, `status`, `hold`. Bay -> Staff: `bay <n> <state> <drone-id>`.
- Bay states: `EMPTY`, `DOCKED` (merge locked, drone found by cell box, drone PB `IsRunning`), `CHARGING` (drone batteries below `Charge Fraction`, default 0.9; Auto mode set), `READY`, `ARMING` (writes `[Launch]` with `Away X/Y/Z`, runs `armed`, 1-3 s), `RELEASED` (host merge off, drone merge off, `Nudge` of 0.2-0.5 s), `COOLDOWN` (merge re-enabled after 30 s so recovery works), `FAULT` (e.g. `FUSED`).
- Release call sequence (reused from HiveMotherAI `Release`): set drone batteries `Auto` -> `DPb.TryRun("armed")` -> host merge `Enabled=false` -> drone merge `Enabled=false` (the drone does it too) -> verify `Me.CubeGrid != drone grid` within 3 s else `FAULT FUSED`. The LAUNCH block takes over (trigger 1, no message needed).
- Hand launch: cockpit toolbar slot -> Staff PB `Run` with argument `release all` (sebp cannot write populated toolbars yet, timers_pbs.md 4.1); until then 1 `Timer Block` per slot with toolbar action "merge block On/Off -> Off" (Timer R(n) backup, already planned, 25 PCU each) wired by hand or by a generator fix.

**As built (M5, `scripts/CradleBayAI.cs`, `docs/design/control_arch.md` "M5 as built", `docs/design/ships/Pod.md` section 7):** the bay PB listens on control_arch D4's `<Tag>.S2P` and answers on `<Tag>.P2S` (not `<ShipTag>_BAY`); the states are EMPTY / DOCKED / RELEASING (CHARGING, READY, ARMING, RELEASED) / COOLDOWN / FAULT, the Corsair has its own `[Bay]` PB, and a release waits for charge, a clear lane, a quiet ship (turn < 0.1 rad/s, acceleration < 1 m/s2).

### 6.2 IGC range
`TransmissionDistance.CurrentConstruct` will NOT reach a connector-docked pod (docked = different construct, GTS page). Staff and Bay must use `ConnectedConstructs` [rec 85%, API enum exists] or both carry an antenna with a private tag. While the drones are still merged to a grid, they are in `CurrentConstruct`; after release they use the FLEET channel as today.

### 6.3 Same-grid contamination (important for the corvette and for any merged host)
While a drone is merged, its thrusters, gyros, PB, batteries, cameras are blocks of the HOST grid. A Helm that does `GetBlocksOfType<IMyThrust>` with only `CubeGrid == Me.CubeGrid` will adopt the drones' thrusters and overrides them. Rule for the new warships: every host block gets a name prefix (generator rule, e.g. `[CV]`), and Helm collects by prefix AND grid. Drone scripts already guard (LAUNCH waits about 1.7 s and only touches blocks with `CubeGrid == Me.CubeGrid`, so a drone that wakes while merged would do the same to the host; the drones are IDLE by default, so only the `launch`/`join` trigger can wake them, and only after the split). In the docked-pod variant this problem mostly disappears (the pod has its own grid for everything but the drones, and the connector separates constructs).
After a release the host's block lists change: Helm and Staff must re-run `GetBlocks` on `Me.CubeGrid` change (a drone-induced grid split fires nothing by itself).

## 7. Generator and sim support

### 7.1 tools/sebp.py (generator)
`Blueprint.build_xml` writes exactly one `<CubeGrid>` at Position (0,0,0), Forward (0,0,-1), Up (0,1,0), Orientation (0,0,0,1). Needed (about 120 lines):
1. `build_xml_multi(main, subs)` with `subs = [(Blueprint, offset_m(x,y,z), links)]`: a `<CubeGrid>` per sub with a unique `EntityId`, `PositionAndOrientation Position` = offset, identity orientation (axis-aligned grids avoid the quaternion), `GridSizeEnum`, own `CubeBlocks`; `<CubeGrids>` holds them all. `load_sbc` already reads every CubeGrid of a game file, so existing files can be inspected the other way.
2. Coordinate rule (identity orientation): a block at cell `c` of a grid with cell size `s` has its centre at `Position + c*s`. Drone/cradle alignment: `P_drone = P_host + (merge_host_cell + clamp_dir)*s - merge_drone_cell*s` for small+small (a multiple of 0.5 m; the lattices must stay aligned). Pod on a large hangar: put the pod position on the 0.5 m lattice relative to the large grid and compute the connector faces from the 1.82 m centre distance.
3. Ids: assign `EntityId`, rotor `TopBlockId`/`ParentEntityId`, connector `ConnectedEntityId` (leave unset = unlocked; add the locked shape only after C2).
4. Per-grid name: the grid `DisplayName` shows in the terminal (give pods readable names).
5. Drone block names: the drone blueprints keep their names (all drones in one blueprint are copies, so names repeat); the Bay PB finds a drone by its cell box (`Drone Box Min/Max` in Custom Data), not by name.
Existing safeguards (`validate`, DLC refusal, `check_docking`) run per grid.

### 7.2 Sim
- `BlueprintLoader` reads every CubeGrid (`bp.Grids`); `GridFactory.SpawnGrids` makes one `Body` per GridData with `ConstructId = grid index`, placing grids by their `PositionAndOrientation` relative to grid 0 [sim]. `PairMerges(body)` only pairs merge blocks INSIDE one Body, so two separate small CubeGrids with touching merge blocks stay two independent bodies and the merge never forms. `DockLaunch.BuildDocked` works around this by putting the dummy printer into the drone's own GridData.
- No rotor, piston, connector, landing gear or door API exists in `sim/src/Api` (grep), `IsSameConstructAs` = same body, and `ScenarioGen.EnemyBlueprint` reduces any multi-grid enemy to its largest grid.
- Minimal change (about 100 lines, loader/GridFactory only): a pre-pass in `SpawnGrids` that fuses GridData whose merge blocks are adjacent (same condition as `PairMerges`) into one Body keeping `ConstructId` per origin grid, and fuses connector- or stator-linked grids into one rigid Body (no API needed because scripts only see merge/thrust/gyro/battery). Then `sim/dock_launch_test.sh` can be extended with a `--cradle` case (real drone + 2nd GridData host) without code-built dummies. Door and connector behaviour stays untestable in the sim (in-game tests only).

## 8. Failure modes

| # | Failure | Effect | Mitigation |
|---|---|---|---|
| F1 | Clamp face is Right, not Up (T1) | no cradle merges, nothing docks | `MERGE_CLAMP_FACE_SMALL`, rebuild; run T1 first |
| F2 | Pasted separate grids do not auto-merge (no lock at paste) | drones float away when the ship accelerates | Bay PB toggles host merge `Enabled` off/on once at start; if still `IsConnected` false -> FAULT; C1 |
| F3 | Merge permanent (surface contact) or fused | drone never separates | geometry check; Bay PB `FUSED` check; grinder cleanup manual |
| F4 | Connector pod does not lock at paste / unlocks under thrust | pod drifts away | `Connect()` at start; 2 pairs; `IsParkingEnabled`; C2, C3 |
| F5 | IGC does not cross the connector | pod never gets `release` | `ConnectedConstructs`; fallback antenna; C4 |
| F6 | Helm drives docked drones' thrusters (merged corvette) | wrong flight | name-prefix rule, 6.3 |
| F7 | Launch burn flame damages the cradle or the neighbour | wall/merge destroyed | 1-cell gap, 2-cell wall, stagger 2 s, C5 |
| F8 | Gnat launch direction (tie forward/back) | drone flies into the hull | Away vector written by bay; omit Gnat on v1 |
| F9 | Hangar door opens too slowly / closes on a drone | delays/damage | open before ARM, close on `RELEASED` + 10 s; sensor optional |
| F10 | Heavy pod on connectors sags/wobbles | aim error, shear | 2 pairs; pod mass < 25 t; alternative: add `LargeBlockLandingGear`/plates (no power) |
| F11 | PCU/limits: 8 drones are 8k+ PCU | paste refused | `Bays` variant sizes; report PCU in MODELS.md |
| F12 | Drones launched with 30% battery | short endurance | CHARGING gate (merged grid shares power; 4 Hornet batteries 0.2 MWh each need minutes; set `Charge Fraction`) |
| F13 | Projected blocks (opt-in print bay) fuse | no release | not used on warships |

## 9. In-game tests (add to packages/tests.md)
C0/T1: which face of `SmallShipSmallMergeBlock` clamps (MergeFaceTest blueprint with three blocks facing Up/Right/Forward).
C1: paste a two-small-grid blueprint with touching merge blocks (Enabled): do they merge at paste time? Terminal shows one grid? Then `Enabled=false` from a PB: do they split and which side keeps the host name?
C2: paste a Large+Small blueprint with an UNLOCKED `ConnectorMedium` placed 1.82 m from a large `Connector`: does `Connect()` succeed? Does a game-saved locked blueprint (Venture) re-paste locked?
C3: after docking, from the large ship's PB: does `GridTerminalSystem` list the pod's blocks; `IsSameConstructAs`; with the pod thrusting the frigate at 1 m/s^2 for 30 s does it hold?
C4: IGC `CurrentConstruct` vs `ConnectedConstructs` across the connector; latency.
C5: launch burn from a slot (4 s, cos-weighted): damage to the wall, neighbour drone, the merge block; lane 12 m clear enough?
C6: Gnat belly clamp launch direction with and without `Away X/Y/Z`.
C7: Hornet recovery: tail-in at 0.5 m/s, does the merge re-lock, does the LAUNCH block re-arm?
C8: hangar door open/close time (8 doors) and `IMyDoor` API (OpenDoor/CloseDoor) from a PB.
C9: PCU and mass of a built P4 pod with 4 Hornets against the world limits.
C10: the Helm name-prefix rule: with 2 drones merged, does Helm still ignore their thrusters?
C11: small block placed on a large hull in the game: confirm it is a separate grid, and what the control panel shows (settles section 1 row 1).

## 10. Build order
1. T1, then sebp multi-grid writer, then the cradle module + test blueprint `Cradle Test` (1 slot) for C1/C5.
2. Bay PB (cut-down HiveMother) with the dock-launch sim extension (7.2).
3. Corvette pylons, then Pod P4 + frigate hangar (C2-C4, C8).
4. Recovery (5.6) only after launch works in game.
