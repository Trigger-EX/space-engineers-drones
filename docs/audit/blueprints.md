# Audit: blueprint generator and shipped blueprints (key `blueprints`)

Scope: `tools/sebp.py`, `tools/designs/*.py`, `tools/build_all.py`, `tools/extract_shapes.py`, `tools/shapes.json`, `tools/audit.py`, `defs.json`,
`defs_stats.json`, `docs/xml-notes.md`, `docs/welder-reach.md`, `packages/*`, and the built blueprints in `releases/Drones_v2.6_Full.zip`,
`Drones_v2.6_Lite.zip`, `Printers_v2.6_Full.zip` (unzipped to the scratchpad, read-only).

What was run (no simulation was run; nothing in the repo was modified except this file):
* `python3 -I tools/build_all.py --only Hornet Gnat "Hive Mother" --out <scratch> --no-zip` (6 PASS, 0 FAIL, 0 cross-check failures).
* `python3 -I tools/audit.py <bp.sbc>` on all 14 Drones Full, all 14 Drones Lite and all 16 Printers Full blueprints: every block attached, one connected piece per grid, 0 skipped.
* `python3 -I tools/designs/core.py` (builds + validates every core drone, Full and Lite): all PASS.
* My own independent checker (scratch `chk.py`): XML parse, unknown subtype, DLC list (defs.json), grid-size/block mismatch, duplicate EntityIds (incl. the projected grids inside printers), PCU sum, Program length / non-ASCII / control characters.
* Mount-point convention re-test against the 38 game-written blueprints in `ref/user-samples/` (new evidence since `docs/xml-notes.md` was written, see 2.1).
* Shape (tag set and order) diff of every generated block type against all of `ref/user-samples/*/bp.sbc`.

IMPORTANT data-source note: `ref/Starburst/bp.sbc` no longer exists. The user's real game-written blueprints are now in `ref/user-samples/<name>/bp.sbc` (41 files,
census in `docs/audit/user-samples-census.md`). Four of those ("Gnat", "Hornet Drone", "Longbow L", "Longbow S") are NOT game-written: they are old generator output
(no `ShowInInventory`/`NumberInGrid`, DLL-era shapes) that the user pasted/copied back; they are the files that contain the floating blocks (see 2.3). The other 38 are game-written.

## 1. Findings (details in section 3)

| id | severity | one line |
|---|---|---|
| blueprints-01 | high | Merge clamp face: best evidence says Up (wiki + a game-written sample); keep it, but nothing in the repo has run in game. |
| blueprints-02 | medium | Large turrets (Basilisk, Basilisk Seeker, Longbow L Seeker, also in the Lite variants) are written as a bare guessed shape; the real shape is now available in `ref/user-samples`. |
| blueprints-03 | medium | `packages/unknowns.md` says AI-block settings (AI Behavior switch, speed limit) cannot be pre-set; the real FlightMovement XML shows they can. Offensive/Defensive/Sensor/Decoy shapes are still unseen. |
| blueprints-04 | medium | Owner/BuiltBy is a hard-coded identity id (144115188075855915) that is per-world; on any other world blocks may be owned by a non-existent identity. |
| blueprints-05 | high (survival) / info (creative) | No ammunition in any weapon, no conveyors, merge blocks do not carry conveyors: a Survival drone cannot fire. Ammo can now be pre-loaded (real shapes exist). |
| blueprints-06 | high (risk) | Projection-built drone blocks join the projector's grid (Starburst evidence); merge-pair separation is the single largest assumption of the printers and matches the user's "printed ships do not fly off". |
| blueprints-07 | info | Mount-point convention is now fully confirmed (all 10 single-axis alternatives rejected on 20,128 blocks); `xml-notes.md` limitation paragraph is obsolete. |
| blueprints-08 | medium | Printed (welded) batteries: starting charge unknown (30 % definition value vs. blueprint value); drones may leave nearly empty. |
| blueprints-09 | low | Stale docs: PCU numbers in `unknowns.md`, "from source" shapes now seen, `audit.py --shape` default ref path crashes, Lite promise broken by turrets. |
| blueprints-10 | low | Script headroom: Basilisk 97,721 / 100,000 chars (2.3 %); fine today, any new feature breaks the build. |
| blueprints-11 | medium | Welder work-sphere centre still unknown; no new evidence exists in the repo; give a 3-step in-game measurement. |
| blueprints-12 | info | Schema fidelity details that differ from game-written files but are harmless (EnvironmentType, EnableSmallToLargeConnections, MaxStoredPower, BlockGroups). |

## 2. What was checked and found CORRECT (coverage table)

### 2.1 Mount-point / face convention (sebp.FACE_MAP)
Method: load all 38 game-written blueprints (75 grids, 20,128 blocks, mixed small/large, rotors, pistons, turrets, doors, connectors, hinges, rails, wheels), run
`Blueprint.attachment`, and swap ONE face mapping at a time for each plausible alternative. Pieces must equal grids (75).

| variant | attached | pieces (true value 75) |
|---|--:|--:|
| shipped convention | 20128/20128 | 75 |
| Top: no Z flip | 20117 | 115 |
| Bottom: Z flip | 20111 | 122 |
| Front: no X mirror | 20105 | 113 |
| Back: X mirrored | 20110 | 104 |
| Left: u -> Z-u | 20103 | 135 |
| Right: u -> u | 20099 | 147 |
| Front v flipped | 20103 | 109 |
| Back v flipped | 20106 | 97 |
| Left v flipped | 20122 | 89 |
| Right v flipped | 20118 | 102 |

So the u AND v axis of all six faces are pinned by real data. (The old note in `xml-notes.md` that Starburst alone could not decide the Top/Bottom v sign is
now obsolete: on Red Ship alone Top-v and Bottom-v are not decided, but the whole set decides them.) The shipped blueprints are sensitive to this: under "Top no flip" 7 of 16 checked
blueprints split (Longbow L/S, Mantis, Basilisk...), so the validation is meaningful, not vacuous.

### 2.2 Everything below was checked and is consistent

| item | result |
|---|---|
| Block subtype spellings / `MyObjectBuilder_` prefixes | all 14+14+16 blueprints resolve every block through defs.json (`sebp.defkey`); 0 unknown. |
| Grid size vs block grid size | 0 mismatches (`add()` asserts; re-checked from the XML). A grid is Small or Large, blocks of the other size cannot be in it; no mixing anywhere. |
| DLC | 0 DLC blocks in any shipped blueprint (defs.json `dlc` empty for every block used, including AI blocks, Decoy, Sensor, Merge, SmallProjector; no `<DLC>` element written). Note the DLC variants of look-alikes exist (`SmallBlockSensorReskin` = Automatons, `TrussPillarDecoy` = Signal, `*GatlingTurretReskin` = Contact): not used. |
| EntityId uniqueness | no duplicates within any blueprint, grid id included, projected grids inside printers included. |
| BlockOrientation | only Base6 names, forward/up perpendicular (asserted); identity omitted like the game does. `Min` is the min corner of the rotated box (`Blueprint.ext`). |
| Field order / tag set per type | diff against all 41 samples: Battery, Camera, CargoContainer, CubeBlock, FlightMovement, Gyro, MergeBlock, MyProgrammableBlock, Projector, RadioAntenna, RemoteControl, ShipWelder, SmallGatlingGun, SmallMissileLauncher(+Reload), Thrust: OK. Not in any sample: Sensor, Decoy, OffensiveCombat, DefensiveCombat, LargeGatlingTurret. LargeMissileTurret/LargeGatlingTurret: see 02. |
| Timer toolbar shape | real `TimerBlock` (Pertam Pod) = `Toolbar{ToolbarType Character, SelectedSlot nil, Slots/Slot{Index, Item, Data xsi:type ToolbarItemTerminalGroup{Action, GridEntityId, BlockEntityId, GroupName}}, SlotsGamepad}, Delay, CurrentTime, IsCountingDown, Silent` after `Enabled`: identical to `HAND['TimerBlock']`. The hypothesis behind the old paste failures (unguarded Toolbar remap) is consistent with this; the v2.x Full drones (Sensor + 2 AI blocks) pasted and ran in the user's game (changelog v2.1 "In-game report"), so the always-write-an-empty-Toolbar fix is confirmed for Sensor. |
| MergeBlock XML shape | game-written (`Styrofoam Plane`, `Sudden Deth`): no ComponentContainer, no tail fields after `Enabled`; ours is the same. |
| ComponentContainer / ModStorage CustomData | real format (Starburst PBs): `ComponentData{TypeId MyModStorageComponentBase, Component xsi:type MyObjectBuilder_ModStorageComponent{Storage/dictionary/item{Key 74de02b3-27f9-4960-b1c4-27351f2b06d1, Value}}}`; generated printer PBs write exactly this (Hive Mother PB, 33,710 chars). Drones carry no CustomData by design. |
| Program text | all 13 scripted Full drones: valid XML text, 0 non-ASCII, 0 control chars, no CR; length (chars) 80,844 to 97,721; counting LF as CRLF the largest is 98,148 < 100,000. Wiki "The Anatomy of a Script": script limit 100,000 characters. |
| Projector XML | tail fields match the game-written `Starburst - v3` projector one to one (ProjectedGrids, ProjectionOffset, ProjectionRotation, KeepProjection, ShowOnlyBuildable, InstantBuildingEnabled, MaxNumberOfProjections 5, MaxNumberOfBlocks 200, ProjectionsRemaining, GetOwnershipFromProjector, Scale, MarkUnfinishedBlocks, MarkMissingBlocks). Largest embedded drone is 58 blocks, under `MaxNumberOfBlocks` 200. Ghost integrity value 1.52590219E-05 appears in the real sample. |
| Thruster direction | wiki "Finding a block's orientation": "Thrusters only have one reliable direction, that being Forward where the flame comes out" (pushes toward Back). `_common.thr`, `thrust_by_dir` (force toward OPP[fwd]) and the Hornet array (2 thrusters per direction, 6 directions) agree. |
| Gun muzzle / camera view | Forward for both; every Full drone has its guns and the main cameras Forward/Up; keep-clear zones (4 cells exhaust, 3 cells camera view) are enforced by `validate()`. |
| Masses | `defs_stats.json` component masses re-summed from the XML: Hornet 4451, Gnat 1941, Longbow L 110,181, Basilisk 115,787, Longbow S 6,044 kg = `releases/MODELS.md`. |
| Thrust / acceleration table | Small ion thruster force 14,400 N / 0.2 MW; large-grid small ion 345,600 N / 3.36 MW (defs_stats). Hornet 28.8 kN per direction / 4451 kg = 6.47 m/s2 (table 6.5). Longbow L 691.2 kN / 110,181 kg = 6.27 (6.3). OK. |
| PCU totals | my sum equals MODELS.md for every drone and printer (Hornet 1010, Wasp 1700, Basilisk 4257, Quad Hive 11,938). |
| Torque balance | thruster torque about the centre of mass at full thrust per direction vs gyro: Hornet worst 15.2 kN*m (Left/Right strafe) against one small gyro 448 kN*m (3.4 %); Longbow L worst 2.5 MN*m against 33.6 MN*m (7 %); Basilisk 2.9 MN*m (9 %). All controllable, no drone is thrust-unbalanced. |
| Power sizing | max realistic simultaneous burn = one direction per axis = 6 thrusters = 1.2 MW against 0.8 MW from 4 small batteries (67 % cap, as `unknowns.md` says); one-axis burn 0.4 MW from 0.2 MWh = 30 min. Longbow L 12 MWh / 48 MW against 40 MW. Battery-only is viable for minutes, not hours; there is no generator or solar panel on any drone. |
| Docking face | `check_docking` passes for every merge block: the merge block is the only block reaching the outer layer along its clamp axis and the clamp cell is free. |
| Validators on the zips | Drones Full 14/14, Drones Lite 14/14, Printers Full 16/16: 100 % attached, 1 piece per grid. |

### 2.3 The "floating block" failure
The four old generator files in `ref/user-samples` are the user's floating-block victims. The repo validator flags: Gnat 5 of 14 blocks (2 thrusters, 3 cameras),
Hornet Drone 7 of 44 (3 cameras, PB, gyro, antenna, top camera), Longbow L 1 of 49 (camera), Longbow S 4 of 50. Mechanism for the Hornet Drone: the camera at (0,1,-3) has mount points on its Back
face only; the block behind it is the PB, whose mount points are on its Front face only with an exclusion mask, so no pair of rectangles matches; the camera at (0,2,-2)
(Forward=Up, Up=Backward) has its Back face pointing Down onto the PB top face, which has no mount point. The gyro (Bottom face only) is surrounded by armour on its other sides. All of these
were created because the old generator treated "adjacent" as "attached". The current generator rejects every such block in `validate()` and the 44 shipped blueprints I validated have none; with the
convention now pinned by 20,128 real blocks (2.1), this cause is closed. Residual: the validator proves "every block has at least one mating neighbour and the grid is one connected piece",
which is what the game needs to keep a block on the grid; it does not prove the support chain of the build order, which `reach.support_report` covers for printers.

## 3. Findings in detail

### blueprints-01  Merge clamp face of SmallShipSmallMergeBlock (the biggest known risk) — recommendation: keep `Up`
Constant: `tools/designs/_common.py:MERGE_CLAMP_FACE_SMALL = "Up"` (1x3x3 block: Right). All printers and all printable drones depend on it (`merge_small`, `merge_clamp_dir`, `check_docking`).
Evidence, strongest first:
1. Wiki, Scripting/Finding_a_block's_orientation (fetched 2026-10-13, https://spaceengineers.wiki.gg/wiki/Scripting/Finding_a_block%27s_orientation): "the original 2 merge blocks merge towards their Right" and "the newer 1x1x1 merge block merges towards Up". Also consistent with defs.json: `SmallShipMergeBlock` is 1x3x3 with big mount faces Left/Right (its Right face is the big clamp plate), so "Right" for the old block is geometrically sensible. For the 1x1x1 block defs.json has six identical full mount faces, so it carries no direction.
2. NEW, circumstantial, from game-written files: both merge blocks in the user's own blueprints use `Forward=Left, Up=Down`. In `Styrofoam Plane` (block at Min (-1,3,1)) five of the block's six neighbour cells are occupied (armour panels on three sides, battery behind, cockpit above) and exactly one face is free: the block-local **Up** (grid Down). The local Right face (grid Backward) is covered by a battery. A player does not bury the clamp face under a battery, so the free face is the clamp: Up. (`Sudden Deth`: local Up also free; Right also free there, so it does not contradict.) Confidence this supports Up: moderate, it is how the owner built two merge ships with a working purpose, not a measurement.
3. I could not find any source for Right on the 1x1x1 block (`Merge_Block` wiki page states only "only the face with the clamps can merge", search found no counter claim). Keen's `MyShipMergeBlock` source returned 404 on raw.githubusercontent.com, as the earlier session found.
Verdict: Up, confidence about 85 % (recollection of the game's model: I do not remember the 1x1x1 block's clamp texture, so I do not add weight to memory). Recommendation: keep `Up`; run test T1 once before anything else because every printer result depends on it; the test is a 5 minute job: paste `P10_Merge` twice (`python3 -I tools/probe_blueprints.py`) or hand-place two blocks, one pair Up-to-Up, one Right-to-Right. If T1 shows Right, change one constant and rerun `tools/build_all.py` (no other code reads the face). Cheap hardening: ship a `MergeFaceTest` blueprint with one grid that has three small merge blocks facing Up / Right / Forward, so the owner can read the answer in one paste.
Related correctness check: the pairing logic in `merge_small`/`merge_clamp_dir` is self-consistent (printer and drone use the same constant and opposite directions), so a wrong constant would break docking on BOTH sides, not just one.

### blueprints-02  Large turrets are bare guessed shapes (Basilisk, Basilisk Seeker, Longbow L Seeker; Lite too)
Shipped blocks: `LargeGatlingTurret/` (Basilisk, Basilisk Seeker, Basilisk Lite; also the Escort Carrier Full printer) and `LargeMissileTurret/LargeCalibreTurret` (Longbow L Seeker Full and Lite). They fall to `GENERIC` in `sebp.shape_for` (no ComponentContainer, no fields after `Enabled`).
Real game shape (`ref/user-samples/Starburst - v3/bp.sbc`, 61 LargeMissileTurret instances over several samples): ComponentContainer with `MyInventoryBase` and `MyTimerComponent`, then `IsShooting, IsShootingFromTerminal, IsLargeTurret=true, MinFov, MaxFov, RemainingAmmo, Target, IsPotentialTarget, PreviousControlledEntityId, Rotation, Elevation, GunBase, EnableIdleRotation=true, PreviousIdleRotationState, TargetCharacters=false, TargetingGroup, Flags`.
Risk: (a) paste failure if the loader dereferences `GunBase` or the container; unknown, this is the same class of error as the old Sensor failure; (b) behaviour: missing `IsLargeTurret`, `EnableIdleRotation`, `Flags` become false/empty, so the turret may not rotate when idle or may have no target flags. The Seeker test ships depend on the turret targeting group.
`audit.py --shape` reports exactly this (not run on Starburst anymore, I ran my own diff: "missing Elevation, EnableIdleRotation, Flags, GunBase, IsLargeTurret, ..."). Also contradicts `packages/unknowns.md` "Lite = only shapes seen in a game-written ship": Basilisk Lite and Longbow L Seeker Lite contain the same unseen blocks. `LargeGatlingTurret` itself is in no sample at all (only the missile turret); a rendition from the missile turret would be a guess, ask for a Ctrl+B export of one.
Fix: extend `tools/extract_shapes.py` to read every file in `ref/user-samples` (it only reads one), regenerate `tools/shapes.json`, add `LargeMissileTurret/*`, `TimerBlock/*`, `FlightMovementBlock/*`, `BasicMissionBlock/*`, `MergeBlock/*`.

### blueprints-03  AI-block settings can be pre-set; Offensive/Defensive/Sensor/Decoy remain guesses
`packages/unknowns.md`: "Even switched on, the AI blocks keep their default settings ... which a blueprint cannot pre-set"; `docs/xml-notes.md`: AI blocks "guessed". The game-written `SmallFlightMovement` (Venture - Expedition End) shows what a block really holds: `ComponentContainer` with `MyAutopilotComponent` (FlightMode, CollisionAvoidance, **AutopilotSpeedLimit**, Waypoints...) and `MyAiBlockComponent` with **`IsActivated`** (false in the sample; this is the "AI Behavior" switch the Full drones' scripts hunt for with a property name guess). `SmallBasicMission` also has a container (MyBasicMissionFollowHome/FollowPlayer/Autopilot + MyAiBlockComponent) and a tail field `SelectedMission`. Consequences:
* The v2.1 symptom (AI blocks paste inert) is exactly `IsActivated=false`; writing the two Flight components with `IsActivated=true` and `AutopilotSpeedLimit` (e.g. the ship max) removes the dependency on the script finding a terminal property called "...Behavior". Because "present container => only the serialized default components are created" (docs/xml-notes.md finding 3), the whole component list from the sample must be written, no subset.
* `FlightMovementBlock` tags are already OK versus the sample; the container is the missing piece.
* OffensiveCombat/DefensiveCombat have no sample; their component lists (attack pattern, selected weapons) cannot be copied. Ask the user for a Ctrl+B of one grid holding Offensive, Defensive, Sensor, Decoy and a Large Gatling Turret; that closes every remaining "guess" row in `confidence_table()`.

### blueprints-04  Owner/BuiltBy is a hard-coded identity id
`sebp.DEFAULT_OWNER = 144115188075855915` (the id in the user's Starburst). Identity ids are per world: the user's samples carry 915, 916, 917 and 918 for different worlds/NPC identities. The v2.x changelog says the in-game PB showed "Owner: Nobody" when no owner was written; with the id written, a world where the pasting player's identity is not 915 may get blocks owned by a non-existent identity (shows as unknown/"Nobody"), which would break owner-dependent behaviour (turrets/AI targeting friend/foe, PB `Me.OwnerId` check in the Hornet script, antenna relations). Recollection (confidence low, about 35 %) is that a paste re-owns only blocks with Owner 0 or the clipboard owner; I could not verify. Verification: paste a Hornet in a second world or a second account and read the PB owner; mitigation if it fails: omit Owner and keep BuiltBy (the script already prints "! PB has no owner: set ownership"), and tell the player to use Transfer ownership.

### blueprints-05  Ammunition: nothing is pre-loaded, nothing can be conveyed
Facts: no weapon in any shipped blueprint carries a magazine (`GunBase` empty, `Items` empty); drones have no cargo and no conveyor tubes; merge blocks do not carry conveyors (Merge_Block wiki as quoted in docs/research.md 1.x), and the printers deliberately have none either (stock sits in a cargo container touching a welder). So:
* Creative mode: Keen QA, https://support.keenswh.com/spaceengineers/pc/topic/28147-1-202-066-creative-weapon-blocks-do-not-retrieve-ammo : "Since it's creative mode, the weapon does not need any ammo or magazine in order to fire"; I found no source for a separate "infinite ammo" session setting and could not verify one. Survival with creative tools needs ammo.
* Survival (or Survival+creative tools): every drone is unarmed on spawn. A hand-loaded Hornet gatling holds floor(0.064 m3 / 0.016 m3) = 4 NATO magazines = 560 rounds, 48 s of fire at 700 rpm (`defs_stats.json`: inventory_max_volume 0.064, magazine_volume 16 L, capacity 140); a Wasp launcher 4 rockets (0.24 / 0.06 m3); the Longbow S railgun 6 slugs (0.048 / 0.008), no resupply path in flight. Printed drones will never have ammo.
* The "cannot write inventories" limitation in unknowns.md / xml-notes.md section 6 is obsolete: game-written samples with loaded weapons exist (`Pod Chaser`, `Death Attachment`, `Pond Hopper Mk2`, `Starburst - v2`): ComponentContainer `MyInventoryBase` `Items` plus `GunBase{RemainingAmmo, CurrentAmmoMagazineName, RemainingAmmosList/RemainingAmmoIns SubtypeName Amount, LastShootTime}`. Pre-loading is a generator change, not a research question.
* The sim default `SimSettings.InfiniteAmmo = true` therefore equals Creative only; for Survival the shipped blueprints do not match what the sim assumes. Recommendation: pre-load magazines (survival-valid and harmless in creative), or document loudly that drones are for creative worlds.

### blueprints-06  Projection-built blocks join the projector's grid (fused-grid risk)
Evidence from the user's own game: in `Starburst - v3`, the projector sits in "Small Grid 4752" and the welded copy of the projected drone (RC at (5,19,11), 2 thrusters, battery, antenna, gyro, camera) is in the SAME grid (43 blocks = 33 armour + projector + rotor + EC + the drone's 7). `printer_lib.py` says the same. In that sample the copy touched the printer's armour, so it would have joined the grid by contact anyway; the file cannot show whether a contact-free drone would be a separate grid. Recollection: `MyProjectorBase` builds into `CubeGrid` using projector-grid-local coordinates (confidence ~70 %); community practice with "projector + merge block" factories works because the projected merge block is aligned with a real merge block, which the wiki says makes it "supported". The changelog quotes the user (foundry screenshot 2026-10-13): "printed ships do not fly off". A drone whose merge block is welded in direct mount contact with the printer's merge block (all six faces of both blocks are full mount faces in defs.json) is a candidate for being the same grid from the first weld, in which case switching the merge pair off cannot release it. `HiveMotherAI` already detects the fused case ("FUSED" fault). Not fixable from the generator side with certainty. Test (P1): weld one bay, then in the terminal list the construct: is the drone a separate grid before the merge pair is switched off? If fused, one design option is to leave one empty cell between the two merge blocks (merge range is roughly a block) so the first weld has no mount contact; I did not test that in game.

### blueprints-07  Mount-point convention fully confirmed (informational)
See 2.1. Action: update `docs/xml-notes.md` section 2 "Limits (honest)" to cite the 38-file table, and make `tools/audit.py --controls` accept several files so the table can be reproduced (currently one file; my numbers came from a 20-line loop over `sebp.load_sbc`).

### blueprints-08  Starting battery charge of printed drones
Blueprints write `CurrentStoredPower` = full (0.05 MWh small battery, 3 MWh large; real file for comparison: Starburst writes 2.99999475 of 3, so the field is in MWh and our unit is right). `defs_stats.json` gives `initial_charge_ratio` 0.3 for a block built without a stored value. Whether a welded projected battery takes the 30 % definition value or the blueprint value is unknown (`unknowns.md` states 30 % unverified); recollection of the build path is that the projection clones the stored object builder (then 100 %), confidence 40 %. A drone at 30 % has 0.06 MWh = 9 min of 0.4 MW one-axis burn (Hornet). Test with the first printed drone: read battery charge right after release. `Max Charge Seconds` 25 s adds 0.2 MW x 25 s = 1.4 kWh (2.8 % of 0.05 MWh per battery), as the doc says.

### blueprints-09  Stale / inconsistent docs and tools
* `packages/unknowns.md` PCU: "Quad Hive (8,381 PCU) or Static Nest (6,244 PCU)"; MODELS.md and my sum give 11,938 and 8,900 (the Full-variant numbers changed after scaffolding).
* `docs/xml-notes.md` section 3: Timer, MergeBlock, FlightMovement, BasicMission are "from source / guessed"; all four are now in game-written samples and verified (Timer, Merge, FlightMovement identical; BasicMission has a container and `SelectedMission`).
* `python3 -I tools/audit.py --shape <bp>` crashes with FileNotFoundError: default reference path `ref/Starburst/bp.sbc` does not exist (now `ref/user-samples/Starburst/bp.sbc`). Same stale path in docs/xml-notes.md section 4 and `tools/extract_shapes.py` usage. A reference file is mandatory for the shape check to run; I passed merged tag sequences from all samples instead.
* `confidence_table()` and `packages/unknowns.md` list "large gatling turret" as the only unseen turret; `LargeMissileTurret` is seen (Starburst, Red Ship), see 02.
* `Blueprint.groups` exists but `build_xml` never writes `BlockGroups`; harmless because no script uses `GetBlockGroupWithName`/`GetBlockGroups` (grep over `scripts/*.cs`).
* `HANDOFF.md` section 4.3/4.4 and `docs/research.md` row 1 still say the merge face question is open; fine, they are history, but link to 01 for the current evidence.

### blueprints-10  Script size headroom
PB program lengths in the Full drones: Basilisk / Basilisk Seeker 97,721, Wasp 95,313, Mantis 94,623, Aegis 93,509, Longbow L/S 91,672, Lancer 90,660, Harrier 89,955, Hornet/Watcher 88,840, Gnat 80,844 (limit 100,000, wiki Scripting/The_Anatomy_of_a_Script). Build refuses over-limit scripts. Ok today, but the Basilisk has 2,279 characters of headroom, so the next feature needs a shared-block diet. The limit is in characters, there are no non-ASCII characters, so there is no byte/char ambiguity. Instruction-count limits are outside this audit (see the `il_profiler` report).

### blueprints-11  Welder work sphere centre (unchanged, still the printers' second unknown)
`docs/welder-reach.md` section 1 correctly marks the `detector_shiptool` dummy position as unknown; defs_stats confirms `radius_m` 1.3 (small) / 2.26 (large) and no SensorOffset. Nothing in `ref/user-samples` reveals the welder geometry (only large welders in Starburst v3/v4). The solver's hypothesis-robust coverage is sound. A 10 minute measurement would remove the guess: weld a projected 1x1x1 armour block placed 0.5, 1.0, 1.5, 2.0 m from the tip face of a small welder (and beside its flank), note which are built, repeat with a 3-block-deep target. That fixes the centre offset and whether a block is tested by its cell box or its centre.

### blueprints-12  Harmless deltas versus game-written files (no action needed unless a paste fails)
* Real `ShipBlueprint` has `<EnvironmentType>None</EnvironmentType>` (default, omitted by us); the main `CubeGrid` lacks `BlockGroups`, `CreatePhysics`, `EnableSmallToLargeConnections` (all defaults; the embedded projected grid has the latter two).
* Real batteries write `MaxStoredPower 0`; we write the definition value (overwritten from the definition at init).
* `OwnerSteamId` 0 vs the user's id in real files, `Points` 0: normal for hand-made files.
* `ColorMaskHSV` values use the game's offset encoding (S and V offsets) and match samples.
* Gatling guns in some samples carry `UseConveyorSystem`; ours do not (default false, drones have no conveyors).
If a future paste fails, bisect with `Blueprint(containers=False)` per `xml-notes.md` section 5.

## 4. Things I could not check
* Anything that needs the running game: merge face, grid fusion, welded-battery charge, AI behavior defaults, turret shape acceptance, owner re-assignment on paste, welder sphere.
* Keen source for `MyShipMergeBlock`, `MyProjectorBase`, `MyGridClipboard` (raw.githubusercontent paths return 404 here; the `github` MCP is bound to the project repository).
* The projection offset sign was taken from the repo's Starburst derivation (identity-orientation formula `cell = projector_cell - offset`); I did not re-derive it for non-identity projector orientations.
* Sensor, Decoy, Offensive/Defensive combat and LargeGatlingTurret XML: no ground truth exists in the repo.
