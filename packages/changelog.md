### v2.7 (unreleased): no latch outside a drone's outline
- **Tube** (all 8 variants): the merge latch that hung under the tail is gone; every block now sits inside the W x H section. The tubes can no longer be printed or racked (nothing did that yet).
- **Gnat**: the merge block is set flush into the belly (it replaces the armour cube under the antenna) instead of hanging below it, so the Gnat stays inside its 2 x 2 section. The Hive printers still print it (`check_docking` has a flush mode; the bays are re-solved). Not tested in the game.

### v2.7 (unreleased): Tube ram drones
- **New models** `Tube 2x2`, `Tube 2x4`, `Tube 3x3`, `Tube 4x4` (`tools/designs/tube.py`, small grid, 3.2 to 12.4 t, 18 to 20 m/s2 forward): armoured box with a solid heavy-armour nose, one ion thruster per rear cell, merge latch, no weapon. Plan and results: `docs/design/tube_drones.md`.
- **New script** `scripts/TubeRamAI.cs` v1.0 (22 k minified, one PB; FLEET LINK + FRIENDLY AVOIDANCE blocks). Rams at full speed with proportional navigation, aborts a run when a friend is in the way, backs off and re-rams.
- The tubes carry AI Offensive + AI Flight blocks (`[AI]`); `TubeRamAI` has a SEARCH mode (AI GATE block + camera confirm rays, as the Gnat) so a launched tube finds a target alone.
- Recessed side thrusters on every tube (push up / down / left / right, nozzles flush with the hull, no reverse): `TubeRamAI` corrects its path with them instead of turning its flank to the target; the nose stays on the target in the terminal phase. Tubes are 0.5 m longer and 0.7 to 1.4 t heavier.
- **Warhead tubes** `Tube 2x2 W` / `2x4 W` / `3x3 W` / `4x4 W`: a full layer of small warheads right under the front heavy-armour layer (same size and mass); `TubeRamAI` arms them at the last moment (20 m from the hull or 0.3 s out) and fires them on impact or by the nose camera's contact fuse; one-shot. Sim: 2 to 7 times the damage of the plain tubes against a Basilisk.
- Sim: `CollisionFineProbe` (mixed grid sizes: the finer grid is the probe; small ships no longer slip through a large ship), a `TubeRamAI` ship counts as combat-capable, ad-hoc `ram/` scenarios are cued like `kamikaze`. Zips not rebuilt. Nothing about tube ramming is verified in the game.

### v2.7 (unreleased): Gnat split over two PBs
- `GnatSwarmAI.cs` (87.2k) is now the Flight half (76.4k: everything that aims, steers and fires, the hive channel, its full status screen); new `GnatStaffAI.cs` (22.4k: fleet link, take-off burn after a printer release, watchdog, fleet carrier radius `.HMR` and the fleet home once per fix) sits on the tail (`[Staff] Gnat PB`). No `[Thrust]` PB (point-and-burn). `docs/design/gnat_split.md`. Sim: combat bit-identical (1v1 vs Hornet, 10 seeds), dock-launch and printer tests pass. Release zips NOT rebuilt.

### v2.7 (unreleased): Basilisk split over two PBs
- `BasiliskAI.cs` (95.5k) is now the Flight half (82.1k: tracking, lead, rockets, aim, KZ, avoidance, AI gate, thrust link); new `BasiliskStaffAI.cs` (23k: fleet link, FOLLOW/SATELLITE, status screen, watchdog; no LAUNCH, the ship has no merge block) sits in the `[Staff] Basilisk PB` cell; the `[Thrust]` PB comes from build_all. Basilisk and Basilisk Seeker. `docs/design/basilisk_split.md`. Sim: combat bit-identical to the single-PB ship (1v1 vs Hornet, 10 seeds); follow-test and kill-PB runs as before. Release zips NOT rebuilt.

### v2.7 (unreleased): bays find a split drone's PB
- `HiveMotherAI.FindDrone` and `CradleBayAI.Mine/FindDrone` now accept `[Staff] <Prefix> PB` and `[Flight] <Prefix> PB` (a split drone) next to the plain `<Prefix> PB`, arm the Staff PB first (it holds LAUNCH and the `[Launch]` keys), then Flight, and never `[Thrust]` or a `[Spare]` PB. Before, every split Hornet/Watcher/Harrier/Lancer/Wasp/Aegis/Mantis/Longbow fell back to the JOIN message only. Sim: dock-launch Hornet/Harrier and `--cradle` 3 seeds pass except `Cradle Test: spinning host (expect: waits)`, which fails the same way without this change. Release zips NOT rebuilt.

### v2.7 (unreleased): extra PBs where a ship needs one
- Printed copies of Wasp, Aegis, Mantis and Longbow S now carry their `[Flight]` + `[Staff]` + `[Thrust]` PBs, and the Peeper its `[Thrust]` PB (the model function adds them; a printer projects that grid and build_all only touched the top-level blueprint).
- Gnat, Basilisk, Basilisk Seeker and Shrike carry one blank `[Spare]` PB reserved for the Staff half still to be split off their over-budget script (Ballista has had one since the first step). Plan, sizes and the CradleBay/HiveMother `FindDrone` bug: `docs/design/pb_headroom.md`. Release zips NOT rebuilt.

### v2.7 (unreleased): SPRUE TEST (option-A printer test rig)
Why: the printer audit (`docs/audit/printers.md` printers-01) says a welded drone is fused to the printer grid, and option A cuts it loose by grinding an armour neck. This rig tests that in one creative-world session. Design, procedure and decision tree: `docs/design/sprue_test.md`; owner copy: `test_designs/Sprue Test/`.
- **New test ships** `Sprue Test` and `Sprue Test No Grinder` (`tools/designs/sprue_test.py`, kind `test`: built by `build_all.py --only ...`, never packaged): a 25-block printer (3 welders, a Small Ship Grinder, projector, battery, PB) that prints a 7-cell armour neck plus a dummy drone (battery, PB, one ion thruster). 733 PCU (633 without the grinder).
- **`scripts/SprueProbe.cs`** (26,569 characters minified) runs on both PBs: weld, charge, cut, hand, push, auto, stop, snap, reset. It reports fused or not, whether the grinder cuts its own grid, the welded PB and battery state, whether the payload leaves and whether the neck re-welds, on the PB screen and in Custom Data `[Log]`.
- **The ship-grinder XML is a guess** (the legacy small-welder form with the grinder type); the `No Grinder` model is the fallback (place the grinder by hand).
- **Source finding:** Keen's old public source says a ship grinder skips blocks of its own grid (`MyShipToolBase.CanInteractWithSelf` is false; only `MyShipWelder` overrides it). If that holds in the current game, a grinder on the printer cannot cut a neck that is welded onto the printer, and option A as written is dead; the rig measures it and carries a hand-cut control (grind N2 by hand, then `cut`) that shows whether a grinder works on another grid. The next design would be a grinder on a rotor/hinge head (A') or the projector on a rotor head (option B).
- **First game test (2026-10-14):** a PB welded from a projection compiles by itself, keeps its Custom Data and is owned by the printer's owner; it is on the printer's grid (fused). `GetCubeBlock` returned null for plain armour (12 of 12 cells), which made the first script version blind to the neck and its welders never stopped; SprueProbe now reads armour with `CubeExists` (`x` cells; confirmed in the game: 7 of 7 neck cells `x`, the weld ends by itself), has a settle timer and a BLIND fallback, and says when it cannot read the neck. `HiveMotherAI` FINISH scan and `CradleBayAI` lane check have the same blind spot (not fixed).
- **Third game run (2026-10-14): a ship grinder does not grind its own grid** (60 s of grinder time on the fused neck, nothing; after a hand cut made the neck another grid, it ground it): option A as written is dead, the grinder must sit on a rotor head (A').
- **`Sprue Test Close`** (third test ship, owner copy in `test_designs/Sprue Test/Blueprints/`): the grinder points its tip straight at the middle of the neck, one block of air in front, for the case that the first layout (grinder beside the neck) is out of the game's real working sphere. Same script (the position hint now names the expected tip direction); the sphere may touch the tips of Welder 1 and 3, guarded.
- The new script and blueprints change the simulator's provenance key (`sim/src/Run/Program.cs`): finish or restart interrupted `pc/` plan runs before using `--resume`.
- Tests: `tools/tests/test_sprue_test.py` (12). No simulation was run (the simulator models neither fused grids nor grinders). Release zips NOT rebuilt.

### v2.7 (unreleased): Ballista wing collisions
- Three Ballistas pasted together flew into each other. The Shrike Helm now has the full `FRIENDLY AVOIDANCE` steering (was beacon only) and the AI Flight blocks yield to it (`FaYield`); sim `wing-test`: hull collisions 3 of 16 runs -> 0, closest approach 24 m -> 92 m. Helm 83.8 k minified, 85.0 k with the thrust-vector link (limit 86,000). `docs/friendly-avoidance.md`. Release zips NOT rebuilt.
### v2.7 (unreleased): THREE PBs PER BLUEPRINT (headroom), Ballista first
- Every blueprint carries at least three Programmable Blocks, so a module can move off an over-full PB over IGC without a new blueprint (CLAUDE.md "Script size"). Ballista: Helm, Spotter and an empty `[SH] Ballista Spare PB` (no program) on the battery deck at (1, 4, 11), next to the Spotter. The spare's name starts with neither `[Staff]` (the Helm switches its Staff link on for that) nor contains `Hive Mother PB` (the Spotter would take it for the foundry). Release zips NOT rebuilt.


### v2.7 (unreleased): SATELLITE 400 m, READY mode, rocks
- **Satellite Distance default 400 m** (was 100) in every FOLLOW-block script, the Gnat and WaspStaff: a wider surveillance net and time to react to the ship moving. Custom Data keys already written in a PB keep their value.
- **`ready` mode** in all drones (Hornet, Wasp, Lancer, Harrier, Aegis, Mantis, Basilisk, Longbow, Gnat, Wasp Split Flight): hold this spot, scan all round, fight only when something comes, then return. `docs/friendly-avoidance.md`.
- **Rocks**: the friendly-avoidance block keeps asteroid ray hits as 40 m spheres (the sim has no voxels; tested with an injected report). The minifier now breaks lines at ~600 characters (was 200): about 300 characters back, which Aegis and Mantis needed.

### v2.7 (unreleased): FRIENDLY AVOIDANCE
Drones now keep clear of every grid that is not an enemy (`docs/friendly-avoidance.md`). Trigger: Hornets orbiting a target flew into a Ballista that was not on their tag, not a contact and never seen.
- Shared blocks `FRIENDLY AVOIDANCE` (`tools/fa_block.cs`: IGC beacon `DRONE_SPACE`, sightings from `Valid()`, barrier + ramp + sidestep in `FaWant`) in Hornet, Gnat, Longbow, Lancer, Harrier, Aegis, Basilisk; `FRIENDLY BEACON` (`tools/fb_block.cs`) in Mantis, Wasp and the Shrike Helm (they have no room to steer: 100,000 / 80,900 characters).
- GUARD / no-target drones hold on a sphere (`FollowRadius() + 40` m) round the carrier link's HOME instead of its centre (they flew into a freshly spawned Corsair).
- Sim: `SimHost.exe avoid-test` (straight run through a parked friendly: 5.1 m before, 47 m after). Release zips NOT rebuilt.

### v2.7 (unreleased): ROTOR + HINGE AI TURRETS on the four warships
New `scripts/TurretAI.cs` (v1.0, 17,316 of 100,000 characters) and `tools/designs/turrets.py`. Design, numbers, formulas, unknowns: `docs/design/turrets.md`.
- **The turret**: `LargeAdvancedStator` (azimuth) -> top grid with `LargeAdvancedRotor` + `LargeHinge` (elevation, +-90 degrees) -> small head grid with `SmallHingeHead`, four `SmallGatlingGun` and a camera: the chain
  of the six turrets in the owner's Tech Hunter, whose XML the game wrote (the head is that sample's pod with gatlings instead of autocannons). Small-grid ships get a two-gun `SmallAdvancedStatorSmall` / `SmallHinge`
  version (stator shapes never seen in a game file: Full only). A turret covers its whole outward half-space; the hinge axis is horizontal, the bore is the azimuth axis at angle 0.
- **Placed on**: Shrike (1, dorsal spine), Corsair (2, pod roofs, replacing the two roof gatling turrets), Warden (4: `Stern P/S`, `Belly Aft P/S`, replacing four gatling turrets), Leviathan (8: `Deck`,
  `Stern`, `Keel`, replacing eight gatling turrets; the Lite Leviathan 10). Every ship gets a PB `[TU] Turret PB` with its masks in the Custom Data. All turret blocks are named `[TU] ...`; every Helm Custom Data now
  has `[Link] Block Prefix=[SH]` (the Shrike's had none: without it the Helm would have fired the turret guns as fixed guns).
- **TurretAI**: aims by the closed-form pointing law (hinge axis = +-(azimuth axis x direction), verified against the real pose chain to 1e-6 rad), lead solver with the ship's velocity and a capped acceleration, line-of-sight
  feed-forward, rate = gain x error + feed-forward, slew limit; targets from the Helm's `.H2S`, the fleets' `_TGT` feeds and the ship's vanilla turrets / controllers; fire gate with hit cone, range, track age, a generated
  line-of-fire mask against the ship's own hull, a camera ray against friendlies; fire modes `free` / `helm` / `hold`; parking; a start-up sign check of both motors.
- **Generator**: `sebp.Blueprint.mount()`, Motor shapes in `shapes.json` (extracted from the Tech Hunter alone: `extract_shapes.py --merge-types`), `sebp_multi.top_pose` / `TOP_OFFSETS` (measured: 28 of the 29 stator/top pairs of the
  Tech Hunter reproduce within 2 cm / 0.02 rad), swing-envelope / line-of-fire / move masks, `check_mounts` in every ship generator.
- **Numbers** (generator, nothing measured in the game): PD4L 568 PCU and 4.0 t each, PD2S 400 PCU and 1.4 t; Warden +16 t and +2,272 PCU (-900 for the removed turrets), Leviathan +32 t and +4,544 PCU. The sustained gun DPS
  (catalogue figure) of the Warden goes 15.2 k -> 32.0 k, of the Leviathan 52.5 k -> 86.1 k; `numbers()` keeps the old figure as `dps_vanilla`.
- **Simulator**: no rotor model yet. `SimHost selftest m6` passes with the new blueprints (the Helm still holds exactly its own blocks), `ship/Warden-vs-Pirate` (1 seed) still wins in 82 s; the turret heads ride along inert.
  So **no combat result exists for these turrets**; K-T1..K-T12 in `packages/unknowns.md` list what only the game can answer.
- Tests: `tools/tests/test_turrets.py` (19), the Warden / Leviathan / Corsair / Shrike / m6 tests updated for the new blocks and names. `stubs.cs`: `IMyMotorStator` and friends.
### v2.8 (unreleased): BALLISTA railgun drone, probe foundry, Peeper probe, Spotter
- **Ballista fixes after the first game test** (zips not rebuilt): `AI Block Search Assist=true` (the cameras alone never found a target; sim 0/4 -> 4/4 kills); `[Battery] Salvo (AUTO/ON/OFF)` in ShrikeAI (default OFF, Ballista ON): all rails fire in one tick instead of one gun
  per slot; ShrikeAI no longer switches "AI Behavior" on when the assist is off (suspected cause of the ship flying off at paste, unverified); Helm budget 81,600 characters. Details `docs/design/ships/Ballista.md` 3a.
- **Ballista** (`tools/designs/ballista.py`, large grid, `Ballista` / `Ballista Lite`): 27 Large Railguns, 16 large hydrogen thrusters (31 m/s2 fore / aft, 16 m/s2 elsewhere), 12 gyros, no armour beyond a breech wall;
  about 7,200 PCU with its foundry (8,200 with a live probe). Runs the Warden Helm recipe (AUTO aim is BREACH on large grids). Design record `docs/design/ships/Ballista.md`.
- **Peeper** (`tools/designs/peeper.py`, `scripts/PeeperAI.cs`): 4.6 t unarmed probe with one ammunition-free small turret; orbits the Ballista's target, cycles the turret targeting group power / weapons / power /
  propulsion, reports vital blocks in the target's own frame.
- **Spotter** (`scripts/SpotterAI.cs`, second PB of the Ballista): per-target memory of every reported vital (merged, expired, saved in Storage), feeds the best one (power first) to the Helm, starts and stops the
  probe foundry when a big target is engaged, locks the foundry connectors.
- **Probe Foundry** (printer, `printers.py`): one-bay Peeper printer; the Ballista carries it docked on two connector pairs (`ballista.dock_foundry`, `check_foundry`).
- **ShrikeAI**: reads `<Tag>_AIM` = (target id, vital in the target frame) and makes it the BREACH point (+550 characters, 80,547 now). The Helm character budget is now 80,600 (was 80,000; hard cap 85,000): `SCRIPT_LIMITS`, `minify_check.sh`, `test_m6.py`.
- Tests: `tools/tests/test_ballista.py` (20). Nothing of this has run in the game or the simulator; the printed-drone separation risk (audit printers-01) applies to the foundry.

### v2.8 (unreleased): large-ship Helm (caught from behind by a capital ship) and locked pods
- **Locked connectors.** `leviathan()` and `warden()` now write the pod connectors LOCKED (`lock=True` is the default; `lock=False` gives the old unlocked form): the ships started unlocked and a pod with drones could break off the hull.
- **ShrikeAI (large grids only: Corsair, Warden, Leviathan; the Shrike fighter is unchanged)**, reported in the game: coming up behind the Leviathan with the Tech Hunter was game over, the idle ship only tipped back and forth, and the Hunter was seen only once it was inside turret range.
  - *Idle search*: a large ship turns steadily at half its Max Turn and nods +-20 degrees every 125 s (the fighter's +-34 degree, 16 s nod demanded more than 2x the dreadnought's angular acceleration, so it just rocked).
  - *Search*: every working camera scans, out to 1.8 km, and also in REACT (cameras only, no sweep). Honest limit: a camera ray costs its length in charge (2 km/s per camera), so a silent ship that is 5 degrees wide is found in minutes, not seconds; AI Block Search Assist=true found the Hunter ~10 s sooner in the sim at the price of an idle ship that wanders.
  - *Capital ships* (target radius >= 50 m; their turrets reach 800 m on their own): standoff = max(Standoff Range, `Capital Standoff Range` = 1000); in the rear half and inside that range the ship points its nose AWAY and burns its strongest thrusters until the range is open, 20 s are up, or the target is no longer behind, then turns round (40 s pause before it can flee again). `Capital Standoff Range=0` switches both off.
  - Braking distances use the thrust available in the direction of travel (the dreadnought brakes 1 m/s2 but accelerates 2 m/s2 forward); no random hit evasion on a large grid.
  - To fit 80,000 characters the Custom Data keys Aim Point, Breach Large Ships, Listen To IGC Tags, Friendly Fire Check, Guard Radius, Cruise Speed, Max Fire Range and Projectile Speed were dropped from the Helm (the `aim`/`breach` commands remain; the values keep their defaults).
- Simulator: `--face-away` (our ships start with their back to the nearest enemy).

### v2.8 (unreleased): printer timer chain and free take-off lanes
Request: could timer blocks release the drones from the printers automatically, and welders sometimes cover the front of the drone so it cannot take off. Details and limits: `docs/printer-release.md`.
- **Timer chain per bay** (`printer_lib.add_bay_timers`): `Timer Build / Release / Reclamp [Bay N]` on the pod shell, toolbars: welders off (block group `Bay N Welders`) -> printer merge block off -> merge block and welders on -> next build. Press `Start` on `Timer Build` once; the bay then cycles without any script. Delays are guesses (Build ~8 x the ideal weld time, Release 20 s, Reclamp 30 s). Not started by the blueprint. Untested in the game; it cannot split a drone that is fused to the printer grid (audit printers-01).
- **HiveMotherAI 2.3**: leaves a bay alone while one of its timers is counting down ("timer cycle running - script paused") and takes the bay back when they stop.
- **Nose lane** (`printer_lib.front_prism`): the drone's footprint swept 16 cells along its own Forward is kept free of welders, scaffold, projector and other bays. Before, 5 of 11 Hornet welders stood in front of the nose. The solver falls back to the old layout only when a tier-A block could not be reached otherwise. Hornet Lite: 12 welders instead of 11; Gnat unchanged.
- **Exit lane 14 -> 18 cells** (the launch burn runs 8 m; the Hornet Forge's bay 1 ran into bay 2's scaffold at 7.5 m) and **array bays now leave outwards** (the mirrored exit was only a tie-break and both Hornet Forge bays left to the right).
- `reach.py` verifies both lanes for every bay; `sebp.py`: a block dict can carry `sample_shape=True` to use the game-written shape (used for the printer timers, the legacy Timer shape is from Keen source).

### v2.7 (unreleased): WARSHIPS Shrike, Corsair, Warden, Leviathan (new package `Warships_v2.7_Full` / `_Lite`)
- **Helm default mode REACT** (`ShrikeAI.cs`, mode 7 in the `.H2S` flags, `StaffAI.cs` shows it): combat-ready but passive until an AI alarm, a mate's alarm, damage or a held target wakes it. `Start Mode` default is REACT (the key name is unchanged). Four rarely used Custom Data keys were dropped to make room (`Target Large/Small Grids`, `Aim Kp/Kd`, `RTB Battery Fraction`); the Helm is 79,977 of 80,000.
- **Cockpit buttons `deploy` / `dock`** (Staff) on Warden, Leviathan and Corsair; the cradle bay PB keeps a docked drone's thrusters off and switches them on at release.
- **Walkways and entry** (Warden, Leviathan; Shrike and Corsair are cockpit-only): slide doors (`LargeBlockSlideDoor`, seen in the Tech Hunter sample) in both sides of the tower stem lead to a one-cell passage and shaft to the bridge and, on the Warden, through the hangar roof; the Leviathan also has a door into each hangar. `carve_walkway` / `route_walkway` / `walk_check` in `designs/_common.py` (the build asserts a walk from the outside door to the bridge and hangars).
- **Gravity generator** (large `GravityGenerator`, 1 g toward the ship's down, field over the crew spaces) on Warden and Leviathan Full (the Lite variants keep no unseen shape). Large grid only: the small-grid Corsair cannot carry one. The object-builder fields (`FieldSize`, `GravityAcceleration`) were never seen in a game file.
- **Round slopes / corners** in the concave edges of the big Leviathan rooms (`chamfer_room`); the Warden's rooms are too cramped to take any.
- **`<ConveyorLines>`** are now written for every face-adjacent ported pair (junction cubes, thrusters on tanks / junctions, turret / tank / locker terminals), as the game's own saves do. Suspected cause of ships that could not hold forward: without the lines the hydrogen network may not connect on paste. Not verified in the game.

New zips with four ships that fly by hand (sit in the seat) and by themselves, plus the Cradle Pod and the `Corsair Wing`, `Warden Wing`, `Leviathan Wing` variants with Hornets in the cradles.
Three scripts: `ShrikeAI.cs` (the Helm of every ship: Hornet brain + MANUAL mode + weapon batteries + dead-man heartbeat; 79,990 of its 80,000 characters), `StaffAI.cs` (heartbeat watch that releases every
override when the Helm dies, wing orders and roll call, hangar doors, drone launch, display pages) and `CradleBayAI.cs` (the merge-block cradles). The usage guide is the README of the Warships zips.
The drone zips no longer contain the warships (they moved to the new zips); the All zip has everything. All ships were run in the simulator only: nothing has been pasted into the game yet.
- LongbowSniperAI / MantisAI: anti-rush. A big ship (radius 25 m+) charging the sniper (closing 25+ m/s, or running 40+ m/s pointed at it) within `Rush Detect Range` (4000 m): the drone flees along its heading at `Rush Flee Speed` (100 m/s, the game's cap; the old 90 m/s combat cap lost 10 m/s for good), and if it still comes inside `Rush Dodge Range` (500 m) the drone bursts across its path at `Rush Dodge Speed` (60 m/s), each drone in its own direction (grid id) round the charger's heading. Sim, 10 Longbow L from 3 km vs a Tech Hunter rushing at 100 m/s (10 seeds): closest approach 1,308 m (was 238 m), no damage taken (was 370k), all 10 alive. `--pirate-rush true` in the sim makes the pirate brain fly straight at its target.
- Staff 1.4: `launch [n|a-b|all]` (parks the Helm so the bays' ship-turns / ship-accelerates gates pass, releases, gives the Helm its mode back); `[Staff] Turret Target Group` / `Turret Target Names`
  (artillery and assault turrets aim at the targets' weapons: Leviathan vs Pirate, kill time median 143 s -> 89 s in the sim; replaces the planned Gunnery PB).
- ShrikeAI: the integrity scan (hit direction) is sliced, 150 blocks per call (a 1,300-block ship read all of them in one tick: 58,000 IL instructions of the 50,000 limit; peak is now 25,000);
  a seat named `Gunnery` no longer switches the Helm to MANUAL (`[Manual] Seat Exclude`).
- Designs: the Helm Custom Data of every ship now carries the keys the scripts read (the old `[Battery n]` / `[Cockpit]` sections were read by nobody); large ships: stand-off 700 / 800 m, jinking off,
  AI-block search off (the AI Flight block made the idle warship wander); Warden / Leviathan Timer D renamed `Timer D (deadman)` (the Helm finds it by that word: they had none); the Shrike has a second forward camera
  (with one the nose camera was shot off and the Shrike never saw a Hornet again); the empty Gunnery PB of the Leviathan is gone.
- Simulator: warship scenarios (`ship/<Blueprint>-vs-<Enemy>`) start with the Custom Data the blueprint carries and keep a connector-docked pod rigidly on its host; `selftest m6`.

### v2.7 (unreleased): SATELLITE formation
New operating mode: the drones spread round a slowly turning vertical ring centred on the owner (a ship running a Mothership link, you on foot, or the Gnat's carrier /
home point), break off and engage anything inside the Engage Radius, and fly back to their slots afterwards. Command `satellite` / `sat` / `orbit`; ring distance
(`Satellite Distance`, default 100 m, grows with the number of ships), orbit period and plane are in the `[Follow]` Custom Data section. Details, numbers and tests:
`docs/follow-mode.md`. FOLLOW block v1.1 (`tools/follow_block.cs`); the Gnat has its own copy. Size: the Basilisk is now 99,733 of the 100,000 characters.

### v2.6 (this package): BREACH aim against big ships; SEEKER test ships
- **Script length fix (important).** A Programmable Block takes at most 100,000 characters; since v2.2 every armed drone script
  was over it (Hornet 155,629, Basilisk 175,054 characters with comments), so a pasted drone's PB could not hold its script. The
  blueprints and the zips' `Scripts/` folder now carry minified scripts (comments and whitespace removed only; `tools/minify_cs.py`,
  proven identical by compiling both and comparing the IL, `tools/minify_check.sh`; largest now Basilisk 97,721). The commented
  originals are in `Scripts/Source/`, for reading only: they are too long to paste. The build refuses a script over the limit.
Question from the user: against large battleships (their *Tech Hunter*) the drones "can't figure out where vital components are and
just shoot the hull". Study: `docs/tech-hunter-study.md` (1,280 simulated fights, fleets of up to 10 Hornet / Gnat / Longbow S /
Longbow L / Basilisk).
- **BREACH** (shared block `tools/aim_block.cs` in every armed drone script, synced by `tools/aim_sync.py`): against a large grid of
  radius >= 25 m, AUTO holds one hull point (the known one nearest the target's centre, where the armour over the core is thinnest),
  keeps every round on it, and aims down the hole as it deepens; stalled or missing points are swapped. Sim vs Tech Hunter: railgun
  drones destroy about twice as many power blocks (Longbow L 0.65 -> 1.2 per drone in 10 min), at the cost of fewer exposed turrets.
  `aim breach`, `breach on|off`; Custom Data `Breach Large Ships (AUTO)`, `Breach Min Target Radius (m)`, `Breach Stall (s)`.
- **SEEKER (experimental, option 4)**: a turret on the drone named `[Seeker]` is set to `Seeker Group` (PowerSystems / Weapons /
  Propulsion); the script reads where it points and drills there. New test ships **Longbow L Seeker** (artillery turret, 2 km) and
  **Basilisk Seeker** (roof turret renamed); `scripts/SeekerProbe.cs` reports what any ship's turrets tell a script. In the sim it did
  not beat BREACH; the in-game API behaviour is the open question (test 23).
- Finding for fleet building: against a turret-heavy capital ship only Longbow L survives (2 km = artillery range); Hornets, Gnats and
  Basilisks die in under 70 s whatever they aim at. Nothing of 10 drones or fewer knocked the Tech Hunter out in 10 minutes.
- Simulator turret ranges as in the game: an AI turret on its own targets within its Range setting, at most 800 m for every
  large-grid turret (artillery and assault cannon included), 600 m small grid; a working AI Offensive/Defensive block on its grid
  unlocks the ammo range (artillery 2 km, assault cannon 1.4 km). Turn rates and elevation limits per turret type from the data
  (artillery 0.2 rad/s). Re-run vs Tech Hunter: Longbow S now survives too; with BREACH 10 of them destroy 4.2 power blocks (0.8 before).
- Simulator: turrets need power; only railguns have a capacitor (artillery / assault-cannon capacitors starved every turret of a big
  ship); turret targeting groups + Azimuth/Elevation API; enemy vitals in the results; `--enemy-fleets`.

### v2.5: foundry fixes (finish-welding, robust reach, automatic take-off, owner ids)
- **Finish-welding verification.** `HiveMotherAI.cs` 2.1 no longer leaves BUILD when the projector reports 0 blocks left (that only means "all placed", not "all welded"). A new FINISH state keeps every welder on until a full scan of the bay's drone box finds each block at full integrity and functional. Welders can no longer be switched off in BUILD/FINISH (the old stall "kick" that turned all welders off is gone; it now toggles one welder at a time). The LCD lists unfinished blocks (name, cell, integrity, distance to the nearest welder), reports off/damaged/unpowered welders and an empty `[Stock]`, and FINISH has a timeout that FAULTs with the list.
- **Robust welder reach.** The welder sphere centre is unknown (model dummy), so `reach.py` now checks 8 hypotheses (centre at the tip, 1, 2 or 3 cells inside the welder; radius 1.3 m and 1.0 m), a build-order support check and a foreign-bay check. Hornet-class bays now carry 10-12 welders (was 5-8), Gnat bays 7 (was 5). Exit directions were re-chosen per drone. Costs about +900 PCU and +2.7 t per Hornet-class bay. Per-bay Custom Data gains `Drone Box Min/Max`, `Drone Cell Count`, `Drone Occupied Cells`, `Ignore Cells`, an explicit `Exit Direction`, and a `[Launch]` section.
- **Automatic take-off.** A shared LAUNCH block (`tools/launch_block.cs`, synced by `tools/launch_sync.py`) is in the Hornet, Gnat, Longbow, Mantis, Wasp, Lancer, Harrier, Aegis and Basilisk scripts. A printed drone launches when its own PB sees the merge pair open, or on `[Launch] Armed=true`, `_JOIN`, FLEET `join`, or the arguments `armed` / `launch` / `disarm`. No single message is needed. It burns away from the printer for `Burst Seconds`, then goes home and into its start mode (default GUARD). The printer writes `[Launch]` into the drone PB before release, repeats `_JOIN` for 40 s and recompiles the drone PB.
- Known limit: Gnat only thrusts forward/back, so a lateral Away direction does not move it in the sim (see docs/launch-protocol.md). The Stinger script is not yet integrated with the LAUNCH block.
- Docs: `docs/foundry-notes.md`, `docs/welder-reach.md`, `docs/launch-protocol.md`, `docs/hive-protocol.md` section 9.
- **Owner / built-by ids.** Blueprints now carry Owner and BuiltBy (the in-game PB showed `Owner: Nobody` because none were written). Already-pasted v2.x ships: select all blocks in the terminal and Transfer to yourself.

### v2.4 : kamikaze defence
Simulated attacks by swarms of small warhead drones (4 and 8 Stingers cued by a carrier's radar, against every combat drone model) showed the
scripts writing the kamikazes off as wrecks. A Stinger correcting its course at the speed cap spins 1-2 rad/s, so the "tumbling for 4 s" rule
dropped it, ignored it for 120 s and told the whole hive it was finished, usually just before it arrived.
- **KAMIKAZE DEFENCE** (shared block in every combat drone script, `tools/kz_block.cs`, synced by `tools/kz_sync.py`; scripts 2.6 / Gnat 1.13
  / Longbow 2.4; details in `docs/kamikaze-defence.md`):
  - small contacts on a collision course with the drone or a hive mate are never written off as wrecks, and are dropped once spent;
  - they become the priority target;
  - the drone backs away along the threat axis while its guns stay on target, and breaks sideways inside 2 s of impact;
  - spare camera charge tracks the rest of the swarm and searches round known kamikazes for their pack;
  - the threats are shared with the hive, and Gnats skip their run-in against a kamikaze.
  New `[Kamikaze Defence]` section in Custom Data; PB screen line `!! KAMIKAZE x3 (2 at me) next 4.2 s 380 m`.
- Simulator, 620 kamikaze fights (20 seeds): defenders still fighting at the end 57% -> 70%, damage taken -41%. Against 4 Stingers, a lone
  Hornet goes 50 -> 80%, Lancer 45 -> 85%, Mantis 35 -> 75%, Longbow S 55 -> 80%. Fights without kamikazes are unchanged (1v1 regression in the doc).
- Simulator: scenario kind `kamikaze`, settings `SwarmCueSeconds` / `SwarmCueTeam`, `one --echo-every S`, report tool `tools/kz_report.py`.

### v2.3: lead aiming against manoeuvring targets
In-game report (2026-10-13): the Basilisk's rockets kept missing an enormous target that was doing basic evasive manoeuvres. A rocket
needs about 4 s to fly 800 m; an AI ship on "evasive" flips its strafe every 2-5 s. Full write-up and measurements: `docs/lead-v1.md`.
- **LEAD v1.0** (shared block in every armed drone script, `tools/lead_block.cs`, scripts v2.5 / Gnat 1.12 / Longbow 2.3):
  - the target's freshest acceleration, held only for as long as its manoeuvres usually last, then constant velocity (the old
    solver extrapolated a lagging, smoothed acceleration over the whole flight);
  - the hull aim point counts only across the line of fire, turns with the target's spin for up to 1 s, and keeps
    `Aim Margin (predicted misses)` x the predicted miss of known hull round it, so long, uncertain shots go to the middle of the
    known hull instead of its edge.
- Basilisk / Wasp: rockets modelled as leaving with the ship's velocity + launch speed and curving onto the barrel line
  (`Rocket Velocity Model` CAPPED / NONE / FULL; NONE is the v2.2 model). Closed-form rocket path, Newton solve on the flight time.
- Offline bench (`tools/lead_bench`), hits on manoeuvring targets: rockets 50 % -> 62 % (strafing 120 m ship 43 -> 66 %, strafing
  30 m ship 14 -> 39 %), gatling 70 % -> 90 %, railgun 77 % -> 90 %.
- Simulator: rockets inherit the shooter's velocity with the speed cap (`--rocket-inherit`, default 1), evasive Pirate
  (`--pirate-evasive true`).

### v2.2: FOLLOW mode
Question from the user: does `guard` follow the owner? Only as a side effect: a Mothership link's position replaces the guard spot, but
the drones fly to the ship's centre (into its hull for most drone types), lag behind a moving ship, and fall back to the old spot when
the link stops. Nothing followed a player on foot.
- **FOLLOW** (shared block in every scripted combat drone, `tools/follow_block.cs`, synced by `tools/follow_sync.py`): `follow` / `escort`
  command (also Start Mode and launch Mode `FOLLOW`). The owner is the ship running a Mothership link (any type, FLEET channel), or the
  owner's own character seen by the drone's sensor (on foot, ~50 m; wins while seen). Each drone flies its own slot on a ring behind the
  owner (upper half in gravity) outside its hull, with the owner's velocity fed forward (estimated from the link's position fixes, so old
  link scripts work), detours round the hull instead of through it, engages only targets within 800 m of the owner, breaks off past
  1.2 km (2.2 km for the snipers), and after a link loss coasts 3 s on the owner's track (10 s on foot) and holds there. On a
  collision course with the owner it sidesteps up to 8 s ahead, and the ring widens (up to 2x) while the owner manoeuvres hard.
  New `[Follow]` section in Custom Data. No AI-block search in FOLLOW (the AI Flight block would fly the drone away from you).
- Gnat 1.11: `follow` / `escort` = `form`; the formation now matches the link ship's velocity (keeps up with it), and also reads the
  ship radius from the FLEET channel.
- Mothership links (Hornet / Longbow v1.1, Gnat v1.2): send the ship's radius on `<Tag>_HOMER` and `<fleet>_HOMER`; `follow` relays.
- FLEET block: `follow` and `escort` added to the default `Allowed Commands`.
- Simulator: `follow-test` (moving owner on the fleet channel, or an owner character reported to the sensors; cruise, weave, link loss,
  on foot, fight).

### v2.1: first in-game report fixes
In-game report (2026-10-13): Full drones set to `guard` stood still; `hunt` drones waited about 20 s, then all flew off the same way
and never saw a pirate ship 700 m away. Cause: the AI blocks paste with **AI Behavior off**, so they do nothing, but the scripts handed
them the ship and switched their own camera search off while waiting for them. Reproduced in the simulator (AI blocks inert:
3 Hornets vs pirate at 700 m won 0 of 40, found the pirate in 1 of 40 runs).
- **AI GATE** (shared block in every scripted drone, `tools/ai_gate_block.cs`): the script tries to switch AI Behavior on itself
  (the PB screen says whether it could). Until the AI blocks have flown the ship once, they only get it in 6 s windows between 8 s
  of the script's own camera search (under fire the script searches); once they have, they are trusted as before (status `AI ok`).
  New `[AI Gate]` section in Custom Data.
- Side cameras scan out to 900 m whenever there is no target (was 120 m); short close-scan rays no longer drain the nose cameras
  below the long scan's range while under fire.
- Hit direction: the blocks that lose integrity tell which side was hit; the nose sweep and the side cameras search that side.
- HUNT search patrol: drones fan out (each starts its square at a different corner), keep sweeping their cameras on a leg instead of
  looking along the flight path, hold position at listening stops, and Lite drones (no AI blocks) patrol too.
- Simulator results (pirate ship 700 m away, 36 pirate scenarios x 20 seeds, every model): AI blocks doing nothing, `hunt`: drone
  side wins 23 -> 44 of 720, `guard`: 24 -> 47 of 720 (3 Hornets alone: found the pirate in 1 -> 15 of 40 fights); AI blocks that
  need the AI Behavior switch: 23 -> 211 of 720 (the script switches it on). With working AI blocks drone-vs-drone results are
  unchanged (1v1 without the Watcher, 1,560 fights: 77.6 % -> 77.4 % decisive). The unarmed Watcher (no AI blocks) now patrols in
  `hunt` like every drone without AI blocks, so armed drones catch it less often (Watcher fights 60 % -> 24 % decisive).
- Simulator: `--script-ai-blocks-inert` (AI blocks never act) and `--ai-behavior-needs-switch` (AI blocks act only once a script
  switches "ActivateBehavior" on); `GetProperties`/`GetActions` on terminal blocks.

### v2.0
- Every model exists as Full and **Lite** (Lite: only block XML shapes seen in a game-written ship, plus the merge block). Both variants build, validate and ship from one generator (`tools/build_all.py`).
- Blueprint generator rewritten around real data: mount-point validation (checked against the Starburst ship), real masses and costs from `defs_stats.json`, `<Toolbar>` written for Sensor/Timer/Turret Controller/Event Controller (probable paste-failure fix), Custom Data stored in ModStorage (the old `<CustomData>` element was ignored by the game), unique blueprint ids, stable (crc32) seeds, no `.sbcB5`.
- Weapon data corrected from the local game files: gatling 700 rpm, 800 m/s (was 400).
- Merge block clamp face corrected: small 1x1x1 block clamps toward Up (was Right); Hornet, Gnat and every printable drone got a clear docking face.
- New drones: **Wasp** (rockets), **Lancer** (autocannon), **Watcher** (unarmed scout), **Harrier** (interceptor), **Aegis** (escort / point defence), **Mantis** (railgun + gatling), **Basilisk** (large-grid rocket gunship), **Midge** (scriptless, experimental).
- New printers (projector + welders + merge block bays, `HiveMotherAI.cs` 2.0): Hive Mother, Twin Hive, Quad Hive, Hornet Forge, Wasp Foundry, Lancer Foundry, Longbow Foundry, Harrier Forge, Aegis Shipyard, Mantis Foundry, Watcher Pad, Fleet Forge, Escort Carrier, Mobile Hive, Static Nest, Stinger Rack.
- Hornet: rear camera moved one cell up to make room for the docking stalk. Gnat: merge block on a 2-cell stalk under the belly.
- Printers: every bay now has an exit lane. Before, Hornet-class bays (Hornet, Wasp, Lancer, Longbow S, Harrier, Aegis, Mantis, Watcher) had a welder on every side of the drone, so the finished drone could not leave. The drone's footprint swept 14 cells away from the merge block is kept free; Longbow S and Mantis leave sideways (outwards, away from the neighbour bay) and HiveMotherAI.cs reads the new per-bay `Exit Direction` Custom Data key to nudge the printer the right way.

### v1.x history
- **v1.0**: Hornet (gatling), Longbow S/L (railgun), Gnat (2-thruster swarm), mothership relays.
- **v1.1**: sensors added; nothing pasted.
- **v1.2**: sensor removed; Longbow stopped reading railgun power.
- **v1.3**: steadiness-gated lead, `Lead Scale`.
- **v1.4**: 5 extra cameras + all-around close scan; compact embedded scripts (size theory later disproven).
- **v1.5**: disabled-target detection, damage sensing and evasion, split focus, on-screen errors.
- **v1.6**: hull-point aim modes.
- **v1.7**: AI-block search hand-off (user places `[AI]` blocks).
- **v1.8** (uncompiled in-game): printer join / re-scan on grid change, Hornet scout mode.
