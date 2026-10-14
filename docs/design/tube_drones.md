# Tube drones: kinetic ram drones (Tube 2x2, 2x4, 3x3, 4x4)

Status: **built (steps 1 to 5 and a reduced step 6 to 8, see section 8); not tested in the game.** Owner request (verbatim): *"make some tube drones that are designed to slam into the enemy at full speed, im thinking a
2x2, 2x4, 3x3, and a 4x4 variant"*. This file is the implementation plan; when the work lands, the "Results" section at the end gets the build lines and the
sim numbers, and the open questions get the owner's answers.

## 0. Decisions at a glance

| Question | Decision | Why (short) |
|---|---|---|
| What is a "tube" | A long box of armour with a **solid heavy-armour nose cap**, the systems packed inside, and the **whole rear face one bank of ion thrusters** (exhaust aft). Not an open pipe. | An open pipe puts no mass at the contact point and in the sim a target's blocks can slip into the empty core without a contact (section 3.2). Open question Q1. |
| Grid | **Small grid** | Every drone here is small grid (merge latch, racks, printers); small ion thrusters give 119 N/kg against 79 N/kg for large ones; a large-grid 4x4 would be a 100 t+ warship. Section 2. |
| Cross-sections | 2x2 (1 x 1 m), 2x4 (2 m wide x 1 m tall), 3x3 (1.5 x 1.5 m), 4x4 (2 x 2 m); 4 to 5 m long | Owner's numbers; lengths set so every variant accelerates at about 18-20 m/s2 (100 m/s in about 5.5 s, 250-280 m). |
| Kill mechanism | Kinetic only. **No warhead** in the four models. The script handles warheads if a player adds some (arms only in the terminal phase, far from home). | Owner asked for slamming; warheads would make the tubes a danger to their own rack/carrier and duplicate the Stinger. Open question Q3. |
| After an impact | **Re-ram** while it can still fly: extend to a run-up distance, turn, slam again. | A heavy nose survives one hit in the sim (section 3.3); one-shot would waste it. `Max Rams` in Custom Data (0 = unlimited, 1 = one-shot). |
| Script | **New `scripts/TubeRamAI.cs`, derived from `StingerAI.cs` v1.1**; StingerAI stays untouched. One PB, about 25-32k minified, no split. | Stinger hard-codes the "Stinger" block-name filter, is one-shot, warhead-centric, has no friendly avoidance, no impact detection and no re-attack; it is also pinned by the kamikaze baselines and the Stinger Rack. Section 4. |
| Finder | Like the Stinger: cued munitions (fleet / hive target feeds, `DRONE_CUE`, a player's `lock`), plus the nose camera keeping the lock. No AI blocks in v1. | The sim showed scripts-only camera search does not find small ships; AI blocks paste with "AI Behavior" off (in-game report 2026-10-13). Open question Q4. |
| Sim | The sim **can** score ramming (kinetic collision damage exists) but needs 3 small changes: fine-grid contact probe, ram metrics, "a rammer counts as armed". Opt-in scenario kind `ram`. | Section 3. |
| DLC | None. Every block below has `dlc: []` in `defs.json` / `defs_stats.json` (checked); `build_all.py` asserts it again. | |

## 1. Block facts used (from `defs.json` / `defs_stats.json`, small grid, all `dlc: []`)

| Block | Size (x y z) | Mass kg | Key stat | Mount faces (defs.json) |
|---|---|--:|---|---|
| `CubeBlock/SmallHeavyBlockArmorBlock` | 1 1 1 | 112 | 560 HP | all six |
| `CubeBlock/SmallBlockArmorBlock` (light) | 1 1 1 | 20 | 100 HP | all six |
| `Thrust/SmallBlockSmallThrust` (ion) | 1 1 2 | 121 | 14.4 kN, 0.2 MW | Back (full) + side mounts on the back-end cell only |
| `BatteryBlock/SmallBlockSmallBatteryBlock` | 1 1 1 | 146.4 | 0.2 MW out, 0.05 MWh | all (no mounts listed = full) |
| `BatteryBlock/SmallBlockBatteryBlock` | 3 2 3 | 1040.4 | 4 MW out, 1 MWh | all |
| `Gyro/SmallBlockGyro` | 1 1 1 | 613.6 | 448,000 | **Bottom only** |
| `MyProgrammableBlock/SmallProgrammableBlock` | 1 1 1 | 138.4 | | **Front only**, exclusion 3 (cannot sit on a camera) |
| `RemoteControl/SmallBlockRemoteControl` | 1 1 1 | 38.2 | | all six |
| `CameraBlock/SmallCameraBlock` | 1 1 1 | 40.6 | | **Back only** (0.4-0.6 patch), exclusion 3 (not on a PB front) |
| `RadioAntenna/SmallBlockRadioAntenna` | 1 1 2 | 84.2 | | Back + side faces (validator decides which cell) |
| `MergeBlock/SmallShipSmallMergeBlock` | 1 1 1 | 92.2 | clamp = local Up (`MERGE_CLAMP_FACE_SMALL`) | all six |
| `Warhead/SmallWarhead` (optional, not in the models) | 1 1 1 | 106.2 | 15 kHP blast, 4.49 m | all |

Rules that shape the layout: a gyro needs a block under its Bottom face; the PB's Front must touch a full block (armour, RC, battery), never a camera; a
camera's Back must touch a block, and then it can sit **flush** in the hull (`cam(bp, (0,y,z), "Left", ...)` with its back on the cell at x=1, the Hornet
outrigger pattern); a thruster with `fwd="Backward"` hangs on the layer in front of it by its Back face; `check_docking()` wants the merge block to be the
only block in the outermost layer along its clamp axis. Power: one small ion thruster draws 0.2 MW at full throttle, so the rear bank needs one small
battery per thruster (+1 for the rest), or one 4 MW `SmallBlockBatteryBlock` (it fits 3 wide x 2 tall; used where the cross-section allows it).

## 2. Grid size: small, and why

* **Speed is the same on both grids** (vanilla cap 100 m/s small and large: `SimSettings.SmallMaxSpeed = LargeMaxSpeed = 100`, Environment.sbc). What a ram
  drone needs is to *reach* that speed quickly and to turn: small ion thrusters give 119 N/kg of their own mass, large-grid ion 79 N/kg; small gyros on a
  4.5 m tube turn it 180 degrees in well under a second.
* A large-grid 2x2 tube is 5 x 5 m in section; heavy large armour is 3.3 t per cell, so a large 4x4 would be well over 100 t: a warship, not a drone,
  and not printable or rackable by anything in this project (all bays and racks are small-grid merge blocks).
* Sim view (section 3): collision damage scales with the reduced mass, so a large tube would hit harder, but the damage is shared by the blocks within
  1.5 m of the contact, which on a large target is one or two 2.5 m blocks whatever hits it. The gain from a huge rammer is mostly wasted in the model.
* Cost and attrition: tubes are expendable munitions; small grid keeps them cheap (about 3-12 t of mostly steel).

A large-grid "Tube L" can be added later from the same generator (`grid="Large"`); not part of this request (open question Q5).

## 3. What the simulator does with ramming today, and the minimal support needed

### 3.1 What exists (read in `sim/src/Core/Physics.cs`, `Combat/Damage.cs`, `Run/Metrics.cs`)
* `Physics.ResolveCollisions` runs every tick (no sub-steps): broadphase spheres, then **the block centres of the body with fewer blocks** ("probe") are
  tested against the occupied cells of the other ("host"). Deepest contact -> separating impulse with restitution 0.2 + positional correction.
* `SimSettings.CollisionDamage = true` (default): each body takes `CollisionDamageScale (0.0005) x 0.5 x reducedMass x vn^2` hit points, shared equally by
  its blocks within `CollisionDamagePatchM` (1.5 m) of the contact point. Teammates too. The scale is an **assumption [I]** (`docs/sim-design.md` row
  "Collision damage (7.7)"): 100 m/s between two 1.6 t walls = 2.2 kHP each. Damage to a block is capped at its HP; the rest of the share is lost.
* Events: `Collision` (Value = closing speed) and `CollisionDamage` (victim, attacker ship index). `Metrics` adds collision damage to the victim's
  `DamageTaken`/`CollisionDamageTaken`; blocks destroyed by it count as `BlocksKilled` for the attacker (via `RemoveBlock(b, attackerShip)`). It does **not**
  count as `DamageDealt` or hits.
* Termination: `BodyCapable` needs a weapon, turret or warhead. An unarmed ship is treated like the Watcher: its team stays "in the fight" while it can
  fly, but it never counts as armed. A tube alone on a team works (it wins when the enemy is beaten while it can still fly); a tube in a mixed team
  (Hornet + tubes) is ignored once the Hornet dies.

So **the sim can score a ram**, symmetric and coarse. Estimated hit points per ram at 100 m/s into a much heavier target (reduced mass ~ tube mass, lever
arms reduce it a little): `HP ~ 2.5 x tube mass in kg` on **each** body: 2x2 ~ 7.9k, 2x4 ~ 14.6k, 3x3 ~ 15.9k, 4x4 ~ 31k. On a large-grid target that
destroys about one 2.5 m block per ram (light large armour 2.5 kHP; heavy 16.5 kHP only falls to the 4x4); on a small target (Hornet, Gnat) the 1.5 m patch
covers most of the ship and a head-on hit (closing 150-200 m/s, damage x2.25-4) strips thrusters, cameras, RC and armour. On the tube's own side the share
lands on the nose cap and the first system layer: heavy cap cells (560 HP) survive a 100 m/s hit on the big tubes, the 2x2 loses its cap and part of its
first system layer. **None of this is verified in the game** (open question Q6 lists the in-game test).

### 3.2 Gaps that make ram results wrong
1. **A small tube passes through a large ship.** When the large target has fewer blocks than the tube it is the probe: its block centres sit on a 2.5 m
   lattice and must land inside the tube's 0.5 m cells. A 1 m wide 2x2 tube whose axis misses the lattice points never touches (about 84 % of lateral
   offsets for the 2x2). The finer grid has to probe the coarser one.
2. **Rams are not scored as dealt damage**, and an unarmed rammer is not "combat capable", so mixed teams end early and reports cannot show ram kills.
3. **Tunnelling at 200 m/s closing** (3.3 m per tick): only a risk when both bodies are short along the motion; the tubes are 4-5 m long, so a probe centre
   lands inside the other body at some tick. Covered by a self-test, fixed only if it fails (sub-stepped contact for pairs with `|vrel| dt > 0.5 cell`).
4. Out of scope, noted: rams always bounce (no penetration when the struck blocks break), no deformation, damage symmetric.

### 3.3 Minimal sim changes (step 7)
* `SimSettings.CollisionFineProbe = true` (new, default **true** as a bug fix; `--collision-fine-probe false` reproduces the old rule). In
  `Physics.Contact`: when `a.CellSize != c.CellSize` the probe is the body with the **smaller** cell size; equal cell sizes keep the fewer-blocks rule, so
  every small-vs-small and large-vs-large collision is bit-identical to today. Only mixed-size pairs change (e.g. a Gnat brushing a Basilisk): say so in
  `docs/sim-design.md` and in the changelog.
* `Metrics`: per ship `RamHits` (Collision events with an enemy ship at closing speed > 20 m/s, counted for both sides), `RamDamageDealt`
  (`CollisionDamage` events where ShipA is a non-friendly ship: add `e.Value` to ShipA), `RamMaxSpeed`. JSON fields `ram_hits`, `ram_damage`,
  `ram_max_speed` in `JsonOut` (write and parse) and two columns in the summary CSV.
* `Metrics.BodyCapable`: when no weapon/turret/warhead is found, a ship whose PB runs `TubeRamAI` (`sh.Pbs[k].ScriptName == "TubeRamAI"`) is armed if
  it has power, a working thruster, a functional PB and RC (the checks already made above). Then a tube is a combatant in any team.
* `ScenarioGen`: opt-in kind `ram`, enabled by `--ram "Tube 2x2,Tube 3x3,..."` (like `--kamikaze-extra`; add `"ram"` to `Program.Known`), **not** part of
  the default matrix, so `pc/plans/*.plan` and the published baselines are unchanged. For each listed tube T: targets {Hornet, Basilisk, Pirate} x {1, 3}
  tubes: `Build("ram", new[]{target}, Rep(T, k))` (tubes are team 1), `SwarmCueSeconds = 0.5` (the tubes get the kamikaze cue on `DRONE_CUE`, as the
  Stingers do). Add the four names to `FleetOnlyModels` so `--enemy-fleets "4xTube 2x2"` works against the owner's enemy blueprints. Names with an `x`
  parse correctly (`"2xTube 3x3"`: the count prefix is parsed, `"Tube 3x3"` alone fails the int parse and stays whole); assert it in a self-test.
* Self-tests (group `combat`, next to the collision tests in `Combat/SelfTestFix.cs`): (a) a code-built small 2x2x8 heavy box at 100 m/s into a static
  large 5x5x2 light-armour wall, 20 lateral offsets: every one registers exactly one contact episode with closing speed ~100 and damage on both bodies
  with the fine probe (and documents how many miss with it off); (b) the same box head-on at 200 m/s closing into a small 3x2x4 body registers; (c)
  `RamDamageDealt` is credited to the rammer and not for teammates; (d) a lone tube is `CombatCapable`.

## 4. The script: `scripts/TubeRamAI.cs` (derived from StingerAI v1.1)

One PB holds everything (tracking, guidance, aim, thrust, impact detection): the hot path stays in one PB and it is the only writer of the gyro and
thruster overrides. Size estimate: Stinger 13.8k minified + FRIENDLY AVOIDANCE block ~5k + ram logic and status ~8-10k = **25-32k minified**, far under the
80k per-PB budget, so **no second PB** and none of the multi-PB link work. Add `"TubeRamAI.cs": 80000` to `SCRIPT_LIMITS` in `tools/designs/_common.py`
(the CLAUDE.md per-PB budget, enforced at build time). If it ever outgrows that (for example by adding the AI GATE hybrid search and the LAUNCH block),
follow CLAUDE.md "Script size": an Opus plan, move FLEET LINK / LAUNCH / display into a `[Staff]` PB over IGC, never cut the feature.

### 4.1 Kept from StingerAI
FLEET LINK block (unchanged, shared), target tuples from `ListenTags` (default list + `DRONE_CUE` + `TUBE`), `Valid()` (enemies only, large/small/neutral
switches), `Add()` track filter, `Nearest()`, `Docked()` + `AutoLaunch` on merge release, block re-scan on grid change, the `Aim()` gyro code **with its
sign convention unchanged** (confirmed in game, HANDOFF section 3), `Release()`, warhead arm/detonate (only active when warheads exist, see 4.4).

### 4.2 Changed or new
* **Block tag**: `Block Tag` key, default = the PB's own name without the trailing " PB" (`"Tube 3x3"`), replaces the hard-coded "Stinger" filter. Blocks
  must be on `Me.CubeGrid` and contain the tag. While docked (merge connected) the script never touches thrusters or gyros (rack safety, as Stinger).
* **Thrust axis**: all thrusters whose push is along the RC's Forward (the whole rear bank); there is no reverse thrust, it brakes by flipping.
* **Modes** `WAIT, HUNT, TERM, EXTEND, RTB, IDLE` (`RTB` and `IDLE` are the names the FRIENDLY AVOIDANCE block expects).
  * `HUNT` (approach): Stinger's velocity control, `want = dir(predicted intercept) x Max Speed`, then `want = FaWant(want, me)` (keeps off every friendly
    and neutral grid, including the carrier), point-and-burn with the full rear bank. Launch safety: speed capped at 30 m/s and FA always on until
    `Safe Distance From Launch` (150 m) from the release point.
  * `TERM` (terminal, entered when time-to-go < `Terminal Time` 2.5 s or range < 300 m): true proportional navigation at full throttle. Lateral command
    `aLat = N x Vc x (LOS rate x LOS)` (`Nav Gain` N = 3.5) minus lateral gravity; nose = `normalize(vHat x sqrt(aMax^2 - |aLat|^2) + aLat)`, so all thrust
    is used, the speed stays at the cap and the path bends onto the target. Below 90 % of Max Speed the nose leans further along the line of sight. The nose
    camera re-raycasts the predicted point whenever it has charge (lock refresh). FA steering is off in TERM (a ram must not sidestep its own target), but:
  * **Friendly guard** (every 0.25 s in TERM and once on entry): (1) the segment from me to the predicted impact point passes within `Friendly Clearance`
    (25 m) + radius of any FA obstacle that is not another tube (tubes broadcast `TUBE_RAM` so their ids are known), or (2) a nose-camera ray to 1.2x
    range hits a non-enemy grid before the target, or (3) the target is within `Friendly Clearance` of a friendly grid (a target docked to or touching our
    ship) -> **abort**: `EXTEND` sideways, retry after the run-up. Never `Valid()` a friendly; cue tuples are trusted only for position.
  * **Impact detection**: one-tick velocity change > `Impact Dv` (15 m/s), or range < 0.6 x target radius with the closing speed turned negative ->
    `_rams++`, status line `IMPACT n at v m/s`, then `EXTEND` if `Re-Attack` and `_rams < Max Rams` (0 = unlimited) and at least half the rear thrusters
    work; otherwise `HUNT` straight on (last run).
  * `EXTEND`: fly away from the target (along the current velocity, offset sideways by slot) until range > `Run Up Distance` (0 = auto:
    `1.3 x Vmax^2 / (2 aMax)`, clamped 200-800 m) or 10 s, then `HUNT`. Also used after an abort.
  * Cannot close (closing speed < 5 m/s for 15 s, a target running at the speed cap): stay in `HUNT`, status `CANNOT CLOSE`, keep the cue.
  * `RTB`: fly to the launch point or `_HOME` at 40 m/s, flip-and-burn stop inside 50 m, then `IDLE`.
* **Salvo spacing** (cheap, in v1): every tube broadcasts `TUBE_RAM` = `MyTuple<long grid, Vector3D pos, Vector3D vel, long targetId, double eta>` at
  4 Hz (antenna range); tubes on the same target take a lateral aim offset by slot (sorted grid ids, up to 0.4 x target radius) and the later ones add
  0.4 s to their ETA by easing off below the cap in `HUNT`, so a bounced tube is not hit by the next.
* **FRIENDLY AVOIDANCE** block (`tools/fa_block.cs`) included with its hooks (`FaSee(e)` first line of `Valid()`, `FaWant` in HUNT/EXTEND/RTB). It also
  makes the tube broadcast its own bounding sphere on `DRONE_SPACE`, so the other drones keep off the tubes.
* **Status screen** (PB text + Echo) must contain a `Target :` line (`none` when there is none): `Metrics.ScriptHasTarget` reads it. Lines: mode, target
  range / closing speed / tgo, speed, rams, thrusters working, contacts, instruction count and peak.
* **Commands** written as `case "..."` labels so `build_all.script_reference` lists them: `launch` (alias `hunt`, the sim's default start command),
  `abort`, `idle` (alias `stop`), `rtb`, `lock` (nose-camera raycast lock on what it points at), `disarm`, `reload`. All of them already have a
  `COMMAND_DOC` entry in `tools/build_all.py`.
* **Custom Data** `[Tube]`: `IGC Tag=TUBE`, `Listen To IGC Tags`, `Block Tag`, `Auto Launch When Released=true`, `Max Target Distance=3000`,
  `Max Speed=100`, `Terminal Time=2.5`, `Nav Gain=3.5`, `Re-Attack=true`, `Max Rams=0`, `Run Up Distance=0`, `Impact Dv=15`,
  `Safe Distance From Launch=150`, `Friendly Clearance=25`, `Target Large Grids=true`, `Target Small Grids=true`, `Target Neutral/Unowned=false`,
  `Arm Warheads Within=200` (used only when warheads exist); `[Fleet]` from the FLEET LINK block. Every key gets a default on the first run.
* Error handling: `Main` -> `Tick` in try/catch, error text on the PB screen (house style).

### 4.3 Shared-block registration
* `tools/fleet_sync.py`: add `"TubeRamAI"` to `SCRIPTS`.
* `tools/blocks.json`: add `"TubeRamAI"` to the `FRIENDLY AVOIDANCE` script list.
* `python3 -I tools/blocks_sync.py --check` must pass (it also runs the legacy sync checks, which report unlisted carriers of a block).

### 4.4 Warheads (supported, not fitted)
If a player adds `SmallWarhead`s named with the block tag, the script arms them only in `TERM`, farther than `Safe Distance From Launch` from the release
point and within `Arm Warheads Within` of the target, and detonates on impact detection or at closest approach (Stinger logic). With no warhead the code is
inert. A fitted variant is open question Q3.

## 5. The blueprints: `tools/designs/tube.py`

### 5.1 Generator
`tube_frame(name, W, H, cap, systems, service, nose_cams)` builds one variant from a per-variant cell table (below), with `Blueprint(name, "Small",
seed=seed_for(name), legacy_shapes=True)` like Peeper/Hornet. Frame (core.py convention): x = Right (0..W-1), y = Up (0..H-1), z = Backward, **nose front
layer at z = 0**; 0.5 m cells.
1. Nose cap: `cap` full layers z = 0..cap-1 of `SmallHeavyBlockArmorBlock` (colour accent/RED stripe), with the nose camera(s) set **flush in the front
   layer**: `cam(bp, (x, y, 0), "Forward", "Up", f"{name} Camera Nose", 3)`, back on the heavy cell at z = 1.
2. System layers: the per-variant table (RC, PB, gyros, batteries, antenna). Every cell inside the W x H section that the table leaves empty gets
   **light armour** (`bp.armor`, HULL colour): the tube is a closed box, all blocks attach, nothing floats.
3. Service layer (last body layer): flush side cameras `Left`/`Right` (backs on the neighbouring inner cell), the antenna lying across the tube
   (`ant(bp, cell, "Right", "Up", ..., 2500)`, 2 cells along x), light armour elsewhere; the **merge latch** under it on the centre line:
   `bp.armor((cx, -1, zs), RED)`, `merge_small(bp, (cx, -2, zs), "Down", f"{name} Merge")` (Gnat pattern; the merge block is the lowest block).
4. Thruster bank: one `thr(bp, (x, y, zt), "Backward", "Up", f"{name} Ion Main {i}")` for **every** cell of the section, zt = first layer behind the body
   (2 cells long; `thr` reserves 4 cells of exhaust behind each).
5. `finalize(bp)`, return. No `add_support_pbs` effect (TubeRamAI has no `LkInit(`/`TvAim(`), no spare PB: exactly one PB.

Block names: every block `"<Model> <Part>"` (`Tube 3x3 PB`, `Tube 3x3 Remote Control`, `Tube 3x3 Gyroscope`, `Tube 3x3 Battery 2`, `Tube 3x3 Ion Main 7`,
`Tube 3x3 Camera Nose`, `Tube 3x3 Antenna`, `Tube 3x3 Merge`). PB Custom Data pre-set: `"[Tube]\nIGC Tag=TUBE\nBlock Tag=<Model>\n"`. Antenna 2500 m on,
batteries pre-charged (`bat()` does both).

### 5.2 Cell tables (cell = (x, y), "heavy" = heavy armour, "light" = light armour, blank = light)
Masses are sums of `defs_stats.json` masses; acceleration = rear thrust / mass, forward only. Check them against `report_line` after building.

**Tube 2x2** (W 2, H 2, 4 thrusters 57.6 kN; cap 2; body z 0..5, thrusters z 6..7; 4.0 m long; ~29 blocks, **~3.17 t, ~18 m/s2**, 5 small batteries = 1.0 MW)
| z | (0,0) | (1,0) | (0,1) | (1,1) |
|---|---|---|---|---|
| 0 | heavy | heavy | Camera Nose (Forward) | heavy |
| 1 | heavy | heavy | heavy | heavy |
| 2 | Remote Control | PB (Forward: front on heavy z1) | Gyroscope (bottom on RC) | Battery 1 |
| 3 | Battery 2 | Battery 3 | Battery 4 | Battery 5 |
| 4 | Camera Left (back on (1,0,4)) | light | light | Antenna (fwd Backward, z 4..5) |
| 5 | light | light | light | (antenna) |
| 6-7 | 4 x Ion Main | | | |
Latch: light (0,-1,4), merge (0,-2,4) clamp Down. (The 2x2 is too narrow for a crosswise antenna; it lies along z in the corner column.)

**Tube 2x4** ("2 tall x 4 wide": W 4, H 2, 8 thrusters 115.2 kN; cap 3; body z 0..6, thrusters z 7..8; 4.5 m; ~48 blocks, **~5.83 t, ~19.8 m/s2**, one 4 MW battery)
| z | row y=0 (x 0..3) | row y=1 (x 0..3) |
|---|---|---|
| 0 | heavy x4 | heavy, Camera Nose (1,1), heavy, heavy |
| 1-2 | heavy x4 | heavy x4 |
| 3-5 | Battery (SmallBlockBatteryBlock 3x2x3 at min (0,0,3): x 0..2, y 0..1, z 3..5) + column x=3: z3 RC, z4 PB (front on RC), z5 light | x=3: z3 Gyroscope (bottom on RC), z4 light, z5 light |
| 6 | Camera Left (0,0) back on (1,0); light (1,0), light (2,0); Camera Right (3,0) back on (2,0) | light (0,1); Antenna (1,1)-(2,1) fwd Right; light (3,1) |
| 7-8 | 8 x Ion Main | |
Latch: light (1,-1,6), merge (1,-2,6).

**Tube 3x3** (W 3, H 3, 9 thrusters 129.6 kN; cap 3; body z 0..6, thrusters z 7..8; 4.5 m; ~56 blocks, **~6.37 t, ~20.3 m/s2**, one 4 MW battery)
| z | rows y=0..1 | row y=2 (x 0..2) |
|---|---|---|
| 0 | heavy, Camera Nose at (1,1) | heavy x3 |
| 1-2 | heavy | heavy x3 |
| 3 | Battery 3x2x3 at min (0,0,3) (z 3..5) | RC, Gyroscope (bottom on battery), light |
| 4 | (battery) | PB (front on RC), light, light |
| 5 | (battery) | light x3 |
| 6 | y=0: light x3; y=1: Camera Left (0,1), light (1,1), Camera Right (2,1) (both backs on (1,1)) | light (0,2); Antenna (1,2)-(2,2) fwd Right |
| 7-8 | 9 x Ion Main | |
Latch: light (1,-1,6), merge (1,-2,6).

**Tube 4x4** (W 4, H 4, 16 thrusters 230.4 kN; cap 4; body z 0..7, thrusters z 8..9; 5.0 m; ~128 blocks, **~12.4 t, ~18.5 m/s2**, one 4 MW battery for 3.2 MW of thrust, 2 gyros)
| z | x 0..2, y 0..1 | column x=3, y 0..1 | rows y=2..3 (x 0..3) |
|---|---|---|---|
| 0 | heavy, Camera Nose 1 at (1,1) | heavy | heavy, Camera Nose 2 at (2,2) |
| 1-3 | heavy | heavy | heavy |
| 4 | Battery 3x2x3 at min (0,0,4) (z 4..6) | RC (3,0); Gyroscope 1 (3,1) on RC | Gyroscope 2 (0,2) on battery; PB (1,2) front on heavy (1,2,3); light x6 |
| 5-6 | (battery) | light | light (antenna is in z7) |
| 7 | light x4; Camera Left (0,1) back on (1,1); Camera Right (3,1) back on (2,1) | | light; Antenna (1,3)-(2,3) fwd Right (back on (0,3)) |
| 8-9 | 16 x Ion Main | | |
Latch: light (1,-1,7), merge (1,-2,7).

The validator (`bp.validate()`) is the judge of every mount: if the antenna or a camera does not attach where the table puts it, move it within the same
layer (another flush cell, or swap with a light cell) and keep the tests in 5.4 green. Do not add blocks outside the W x H section except the merge latch.

### 5.3 Registration and packaging
* `tools/designs/tube.py`: `MODELS = {"Tube 2x2": ..., "Tube 2x4": ..., "Tube 3x3": ..., "Tube 4x4": ...}` (callables take `lite` and ignore it),
  `NO_LITE = set(MODELS)` (Lite is retired; `build_all` already skips `NO_LITE` for Lite tasks), `REQUIRED_AXES = {m: ("Forward",) for m in MODELS}`,
  `DOCS` (one line each: size, mass, accel, "kinetic ram drone, no weapon, re-rams; needs a target cue or a `lock`") and `MODEL_NOTES` ("Thrust only
  forward: it brakes by flipping. Needs targets from the fleet / hive / carrier channels or a player's `lock`; on its own it sees only what its nose
  camera points at. Ram damage is not verified in the game."), `SCRIPTS = ['TubeRamAI.cs']`, `__main__` -> `C.run_module(MODELS, required_axes=REQUIRED_AXES,
  lites=(False,))`.
* `tools/designs/_common.py` (append-only API): `run_module(..., lites=(False, True))` new keyword, default unchanged; `SCRIPT_LIMITS["TubeRamAI.cs"] = 80000`.
* `tools/designs/__init__.py`: append `'tube'` to `DRONE_MODULES` (kind `drone`: built by `build_all`, packaged with the drones the next time the owner
  asks for zips).
* `tools/build_all.py`: the `models_md` intro sentence "Gnat-class ships thrust on F/B only by design" -> "... Gnat-class ships thrust on F/B, Tube drones
  on F only, by design". Nothing else (DOCS / MODEL_NOTES come from the module).
* Printers / racks: **not** in this request. The merge latch makes a later "Tube Rack" (HiveMother rack mode) possible; open question Q7.

### 5.4 Tests: `tools/tests/test_tube.py` (unittest, picked up by `tools/tests/run_all.py`)
For each of the four models (Full only):
1. builds, `bp.validate()` passes (mounts, one connected piece, no overlaps, reserve zones), `bp.dlc_used()` empty and no block with `dlc` in `defs_stats`.
2. `bp.name == model`; every block name starts with the model name and is unique; exactly one PB, named `"<Model> PB"`, whose program is the minified
   `TubeRamAI.cs` and whose Custom Data has `Block Tag=<Model>`; `C.add_support_pbs(bp)` adds nothing.
3. Envelope: every block except the merge latch lies inside x 0..W-1, y 0..H-1; length matches the table.
4. Nose: every cell of layers 0..cap-1 is heavy armour or a forward camera; at least one forward camera.
5. Thrust: all thrusters are `SmallBlockSmallThrust` with `fwd == "Backward"`, one per section cell, in the last two layers; `C.twr` gives Forward
   >= 15 m/s2 and 0 on every other axis.
6. Power: sum of battery max output >= 1.05 x sum of thruster max power (from `defs_stats.json`).
7. `C.check_docking` passes (merge clamp Down, lowest block).
8. Script budget: `len(script_text("TubeRamAI.cs")) <= 80000`.
Do **not** add the tubes to `golden_hashes.json` until the owner has pasted them.

## 6. Steps (in order)

1. Save this plan (done) and commit it.
2. `tools/designs/tube.py` + the `_common.py` and `__init__.py` edits (5.1-5.3). `python3 -I tools/designs/tube.py` -> four PASS lines; fix mounts
   until they pass; record mass / accel in section 8.
3. `scripts/TubeRamAI.cs` (section 4), FLEET LINK + FRIENDLY AVOIDANCE blocks inserted by `python3 -I tools/fleet_sync.py --apply` and
   `python3 -I tools/blocks_sync.py --apply` after registering (4.3). Checks: `tools/compile_check.sh scripts/TubeRamAI.cs`, `tools/minify_check.sh`,
   `python3 -I tools/blocks_sync.py --check`. Mono's `mcs` is needed (not installed in a fresh session: `apt-get install -y mono-complete`, or see
   `pc/ensure_mono.sh`).
4. `tools/tests/test_tube.py` (5.4); `python3 -I tools/tests/run_all.py --fast`, then without `--fast` once (it also builds Hornet and Gnat).
5. Blueprints for the sim (no zips): `python3 -I tools/build_all.py --no-zip --only "Tube 2x2" "Tube 2x4" "Tube 3x3" "Tube 4x4" Hornet Basilisk Gnat`.
   Must end `0 FAIL`.
6. Sim code (3.3): `Core/SimSettings.cs` (`CollisionFineProbe`), `Core/Physics.cs` (probe choice), `Run/Metrics.cs` (ram stats + rammer capability),
   `Run/JsonOut.cs` (fields), `Run/ScenarioGen.cs` (`ram` kind, `RamModels`, `FleetOnlyModels`), `Run/Program.cs` (`--ram` in `Known` and
   `ApplyGenOptions`), `Combat/SelfTestFix.cs` (ram self-tests), `docs/sim-design.md` (collision row: fine probe; metrics: ram fields). Note in the
   changelog that a new script changes the sim's provenance key (finish or restart interrupted `pc/` plan runs first).
7. `sim/build.sh`, then `mono out/sim/SimHost.exe selftest physics combat runner` must print `SELFTEST OK` (M4/M5/M6 failures noted in HANDOFF are
   pre-existing; do not run those groups for this check).
8. Sim check, small on purpose (CLAUDE.md "Simulations"):
   * smoke, 1 seed each: `one --scenario "ram/Hornet-vs-Tube 3x3" --ram "Tube 3x3" --echo-every 2 --echo-ship 1` (watch HUNT -> TERM -> IMPACT -> EXTEND
     -> HUNT and the `Target :` line), and the same for `Basilisk` and `Tube 2x2`.
   * run: `run --ram "Tube 2x2,Tube 2x4,Tube 3x3,Tube 4x4" --kind ram --seeds 5` (24 scenarios x 5 = 120 runs): enough to see that every variant reaches
     the target, rams (`ram_hits` > 0, `ram_max_speed` near 100-200) and scores (`ram_damage`), and to catch script exceptions or stuck tubes. No more seeds
     in a session; a direction (which size beats what) needs 20-50 seeds on the few scenarios that matter, and that run belongs in an optional
     `pc/plans/ram.plan` on the owner's PC with its own explicit `--seeds`.
   * one A/B for the probe fix, 5 seeds each: `run --ram "Tube 2x2" --filter "Basilisk-vs-Tube 2x2$" --seeds 5` with and without
     `--collision-fine-probe false` (expect contacts only with the fix).
   Say in one line what was run and why, as CLAUDE.md asks.
9. Docs: fill section 8 of this file (build lines, sim table, what the sim cannot show); `packages/tests.md`: `[drone]` in-game items (Q6 test);
   `packages/unknowns.md`: "ram damage in the game is unmeasured; the sim's collision damage scale is an assumption"; `packages/changelog.md`: a
   `### v2.7 (unreleased): Tube ram drones` entry (do not bump `VERSION`); one line in HANDOFF.md under "Later work".
10. Commit and push to `main` (CLAUDE.md). **Do not build or commit release zips**; tell the owner the zips are not rebuilt.

## 7. Open questions for the owner

* **Q1 Shape.** Solid-nosed armoured box with the systems inside (this plan), or a literally hollow open pipe? The plan's tube is a closed box; a hollow
  core could be an option of the generator (lighter, faster, weaker nose).
* **Q2 2x4 orientation.** Planned 4 wide x 2 tall (a flat blade). Or 2 wide x 4 tall?
* **Q3 Warheads.** Pure kinetic (plan), or also a fitted "W" variant with 2 small warheads behind the nose cap (impact-fused, arms only near the target)?
* **Q4 Finding targets.** v1 tubes are cued munitions (fleet / hive / carrier feeds, `lock`). Add AI Offensive + AI Flight blocks (AI GATE) so they can hunt
  alone? Costs ~310 kg per tube and the AI blocks' in-game behaviour is still in doubt.
* **Q5 Large grid.** Want a large-grid "Tube L" battering ram as well?
* **Q6 In-game ram test (the real arbiter).** Paste a Tube 2x2 and a Tube 4x4; ram a pasted Basilisk (large) and a Hornet (small) at full speed, `lock`
  then `launch`. Report: blocks destroyed on each side, did the tube survive and re-ram, game log if anything fails to paste.
* **Q7 Racks / printers.** Should a "Tube Rack" (rack mode, like the Stinger Rack) be made next?
* **Q8 Speed mods.** Vanilla 100 m/s assumed. If the server raises it, `Max Speed` in Custom Data follows.

## 8. Results

Built with the plan's defaults for the open questions (closed box, 2x4 = 4 wide x 2 tall, no warhead, small grid only, no rack). **Q4 was answered by the owner afterwards: the tubes carry AI Offensive + AI Flight blocks so they find targets alone** (see "AI blocks" below); the owner can still change any of them.

**Blueprints** (`python3 -I tools/designs/tube.py`, all PASS; no reverse thrust):

| Model | Blocks | Mass | Accel forward / each side | Size (m) |
|---|--:|--:|--:|---|
| Tube 2x2 | 33 | 4,177 kg | 13.8 / 3.4 m/s2 | 1.0 x 2.0 x 5.0 |
| Tube 2x4 | 56 | 7,073 kg | 16.3 / 4.1 m/s2 | 2.0 x 2.0 x 5.5 |
| Tube 3x3 | 70 | 7,326 kg | 17.7 / 2.0 m/s2 | 1.5 x 2.5 x 5.5 |
| Tube 4x4 | 152 | 14,129 kg | 16.3 / 2.0 m/s2 | 2.0 x 3.0 x 6.0 |

`build_all.py --no-zip --only "Tube 2x2" ...` : 4 PASS, 0 FAIL. Tests: `tools/tests/test_tube.py` (4 tests, OK). `blocks_sync.py --check` green.

**Side thrusters (owner request: "they have to turn their sides to a target to turn with only one thrust direction"):** every tube has two
extra layers in front of the service layer holding recessed side thrusters: small ion thrusters lying inside the hull with the nozzle flush
with the outer face, pushing up, down, left and right; never reverse (it still brakes by flipping). One per direction on the 2x2 and 3x3,
two per direction on the 2x4 and 4x4. Power covers the full main bank plus the two cross axes at once: the 2x2 got 2 more small batteries
(7, 1.4 MW), the 4x4 one small battery next to its 4 MW one. Cost: each tube is 0.5 m longer and 0.7 to 1.4 t heavier; forward
acceleration drops 2 to 3 m/s2 (2x2 now 13.8 m/s2, 100 m/s in about 7 s). `TubeRamAI` sorts the thrusters by push direction (`_side`):
`Burn()` gives the sideways part of the wanted push to the side thrusters while the nose turns; in TERM the proportional-navigation
correction goes to the side thrusters first (`SideScale`), and only what they cannot push tilts the nose, so the nose stays on the target.
Sim, 1 seed each (same scenarios as before, not enough for a direction): 6 Tube 2x2 vs a Basilisk 3 contacts / 11.2 kHP (before 1 / 3.7 kHP),
4 Tube 3x3 2 / 3.7 kHP (before 3 / 12.3 kHP), 4 Tube 4x4 1 / 2.6 kHP (before 1 / 7.6 kHP), 2 Tube 2x4 beat a Gnat; no script exceptions.

**Large-grid tubes `Tube L 1x1`, `Tube L 2x2` and their W versions (owner request: large grid, with and without warheads, 1x1 long and
2x2, armour panels on the sides allowed):** `tube_large()` in `tools/designs/tube.py`. Large-grid facts that shaped them: the large ion
thruster is 1 x 1 x 2 (4.4 t, 346 kN, 3.36 MW: 79 N/kg against 119 for the small one), the large gyro weighs 12.8 t, PB / gyro / compact
antenna mount on their Bottom face only (they face it forward onto the block in front), the large warhead blasts 22 m (small: 4.5 m).
* **Tube L 1x1**: a one-block column, 21 blocks long. Heavy nose (2 blocks), RC, PB, 3 batteries (36 MW), gyro, AI Offensive, AI Flight,
  compact antenna; four side thrusters each half sunk in the column (a 1-wide column cannot recess a 2-long thruster), nozzles flush with
  the panels; a cruciform rear bank of 5 (one thruster alone gave about 3 m/s2). Nose cameras above and below the nose, one block back:
  `Nose Camera Setback (m)=2.5` in its Custom Data, which the contact fuse adds. 107 t, 16.1 m/s2 forward, 3.2 sideways, 57.5 m overall.
* **Tube L 2x2**: flush nose camera, two heavy nose layers, PB / RC / 2 batteries, AI blocks, gyro, compact antenna, recessed side
  thrusters (as the small 2x2), 4 rear thrusters. 105 t, 13.1 m/s2 forward, 3.3 sideways, 27.5 m long.
* **Armour panels**: `LargeArmorPanelHeavy` (330 kg, 1,650 HP, no DLC) on every free side cell of the body, kept off nozzle exhaust and
  camera views. Why: in the sim the small tubes lost their systems to side fire before they reached a Basilisk.
* **W**: the second nose layer is large warheads (1 on the 1x1, 4 on the 2x2), same script logic (one-shot, last-moment arming).
* No merge latch (the racks and printers are small grid).

Sim, 5 seeds each, 1v1 against a Basilisk at 1.5 to 2.2 km, no target cue and no start command (Start Mode HUNT):

| Model | first target | rams | warheads fired | Basilisk damage avg | Basilisk blocks lost | tube blocks left |
|---|---|--:|--:|--:|--:|--:|
| Tube L 1x1 | 9.3 s | 9 | - | 24 kHP | 3.6 of 54 | 93 of 98 (won 1 of 5) |
| Tube L 1x1 W | 6.7 to 9.3 s | 5 | 5 | 240 kHP | 35 of 54 | 65 of 98 |
| Tube L 2x2 | 1.3 to 4 s | 7 | - | 24 kHP | 3.6 of 54 | 80 of 86 |
| Tube L 2x2 W | 2.7 to 5.3 s | 1 | 16 | 278 kHP | 43 of 54 | 20 of 86 |

The large tubes survive the approach (the small ones were shot apart); kinetic rams alone barely hurt a Basilisk in the sim's assumed
collision model, the W versions wreck it (they are lost with it). The sim is optimistic for large ships (`packages/unknowns.md`: large
thrusters and batteries die too slowly there). Nothing verified in the game.

**Detection, the Ballista way (owner report: "tube is not detecting enemies"), and Start Mode HUNT:** the Ballista finds enemies with its
AI Offensive block, not its cameras (`docs/design/ships/Ballista.md` 2: the cameras cover too little sky), and its Helm makes that block
usable. `TubeRamAI` now does the same:
* sets `UpdateTargetInterval` on its AI Offensive blocks to `AI Search Interval (s)=5` (default 30 s: the old 6 s AI windows often held no
  search at all);
* the blueprint presets `[AI Gate] AI Window (s)=20`, `Script Search Window (s)=5` (the Ballista's values; the old 6 / 8 s gave the script's
  spin more time than the AI);
* the first nose camera always casts the long 2,600 m confirm ray (before, a single nose camera cast only 1,300 m rays, so a target the AI
  held farther out was never confirmed);
* re-asserts the AI blocks' On state while they should fly, and re-picks (power-cycles the AI for one tick) after `AI Re-pick After (s)=20`
  when an enemy was seen in the last minute;
* screen line `1st contact <m> v <speed> <source>` (AI / CAM / SENSOR / LOCK / IGC), as on the Helm;
* `Start Mode (WAIT/HUNT)=HUNT` (owner request): once off its rack, or pasted free, it hunts by itself (tries every 2 s); `WAIT` keeps the
  old behaviour (`launch`, or a target in range when released).

Sim, 5 seeds each, enemy at 1.5 to 2.2 km, no target cue (`1v1`). Before: given an explicit `hunt`. After: no start command at all.

| Scenario | first target before | after | rams before | after |
|---|---|---|--:|--:|
| Tube 3x3 vs Basilisk | 26 to 80 s | 1.3 to 2.7 s | 0 | 5 |
| Tube 2x2 vs Hornet | 21 to 43 s | 2.7 to 5.3 s | 0 | 7 |

The sim's AI Offensive is a stand-in (it finds anything inside 2,500 m when it searches). Unverified in the game: whether the AI Offensive
block of an UNARMED grid locks a target and points the nose at it at all (the Ballista's has guns), and the AI Behavior switch (AI GATE
screen line `AI Behavior switch not found: turn AI Behavior on by hand` if the script cannot find it).

**Warhead variants `Tube 2x2 W`, `Tube 2x4 W`, `Tube 3x3 W`, `Tube 4x4 W` (owner request: warheads under the armour, fired on impact):**
the same tubes with the second nose layer (z = 1) replaced by a full layer of `SmallWarhead` blocks (4 / 8 / 9 / 16) right under the
front heavy-armour layer: same size, mass within 1 % (a warhead is 106 kg, heavy armour 112 kg). One-shot: Custom Data `Max Rams=1`.
`TubeRamAI` (only when it finds warheads with its Block Tag):
* arms them at the last moment in TERM: the gap to the target's hull under `Arm Warheads Within` (default now 20 m) or 0.3 s at the
  closing speed, never within `Safe Distance From Launch` of the release point. Arming earlier was a bug the sim showed: a Tube 4x4 W armed
  at 200 m, Basilisk rockets hit the armed warheads 148 m out and the tube blew itself up short of the hull (gunfire sets armed warheads off,
  in the sim and in the game);
* fires them on impact (the velocity jump) or by the contact fuse `Warhead Contact Fuse=true`: the nose camera sits flush in the front face,
  so its ray measures the gap to the hull; when that gap is under one tick of travel it fires (the impact check alone sees the hit a tick late);
* disarms them on an abort (friendly in the way) or when the tube falls back out of the terminal phase.
Sim (4 tubes or 6 Tube 2x2 vs one Basilisk; 3 seeds, the 3x3 / 4x4 rows 10 seeds): average damage dealt to the Basilisk

| Group | plain | W |
|---|--:|--:|
| 6x Tube 2x2 | 9.9 kHP (0 of 3 won) | 38.7 kHP (1 of 3 won) |
| 4x Tube 3x3 | 2.4 kHP | 17.2 kHP (1 of 10 won) |
| 4x Tube 4x4 | 5.6 kHP | 19.6 kHP |

A single Tube 4x4 or 4x4 W never reaches the Basilisk in 3 seeds (shot down at 200 to 280 m). Warhead layer at z = 1 vs the last cap layer:
10 seeds each, no clear difference (3x3 W 17.2 vs 21.5 kHP, 4x4 W 19.6 vs 15.4 kHP). No script exceptions in any run. In the game, unverified:
whether `Detonate()` on a freshly armed warhead fires at once, and whether the impact itself sets off armed warheads before the script runs.

**AI blocks (owner request after the first build):** every tube has an `AI Offensive [AI]` and an `AI Flight [AI]` block in place of two light-armour cells (about +270 kg). `TubeRamAI` gets a SEARCH mode: on `launch` with no target it hands the ship to the AI blocks (shared AI GATE block: switches AI Behavior on, time-shares with a script spin if the AI does nothing), the nose cameras confirm what the AI points at with boresight / spiral rays (Gnat pattern), and the first valid contact or IGC cue takes the ship back for HUNT. It returns to SEARCH when it loses its target. Sim (1 seed, start distance 800 to 1200 m because the sim's AI search radius is 2,500 m): the AI blocks switch on and find a Basilisk, HUNT starts; at the default 3,000 m spawn distance an AI-only ship never sees the enemy in the sim (same for the Hornet). The AI blocks' real in-game behaviour is still unverified (HANDOFF section 3).

**Script** `scripts/TubeRamAI.cs` v1.0: 27,608 characters minified (limit 80,000, one PB). Modes WAIT, HUNT, TERM (proportional navigation, friendly guard), EXTEND (re-ram), RTB, IDLE.
Not in v1: the `TUBE_RAM` salvo spacing broadcast of section 4.2 (tubes on one target do not yet space themselves; see the sim note below).

**Sim changes made:** `SimSettings.CollisionFineProbe` (default true) and its use in `Physics.Contact` (the finer grid is the probe in mixed-size pairs; equal sizes unchanged);
`Metrics.BodyCapable` counts a ship running `TubeRamAI` as capable while it has power, a thruster, a PB and an RC; `ScenarioGen.Custom` cues the `ram` kind like `kamikaze`.
**Not done:** the opt-in `--ram` scenario kind, the `ram_hits` / `ram_damage` / `ram_max_speed` metrics, the three ram self-tests (plan 3.3). Rams are read from `collisions`, `collision_damage` and `blocks_killed` in the normal output of an ad-hoc `one --scenario "ram/<target>-vs-<N>xTube 3x3"`.

**Sim runs (1 seed each, smoke only, to see that the script runs and rams; no exceptions in any):**
- Tube 3x3 vs Hornet: the Hornet kites at about 80 m/s, closing speed stays 14 m/s ("CANNOT CLOSE" territory), the tube is shot down without a contact. 3 tubes vs a Hornet: no ram.
- vs Basilisk (large, 4 missile launchers): 1 tube of any size is destroyed before contact (about 350 m out); 4x Tube 4x4: 1 contact, 7.5 kHP collision damage to the Basilisk (10 kHP to the tube); 6x Tube 2x2: 1 contact, 3.7 kHP; 4x Tube 3x3: 3 contacts, 12.3 kHP. The Basilisk wins all of them.
- vs Gnat: 2x Tube 2x4 vs a Gnat: 1 contact (0.4 kHP each side); the Gnat's gatling disables one tube.
- `selftest physics combat`: SelfTestC passes (135 checks); SelfTestA has 2 FAILs about the Hornet's mass and thrust-to-mass (Hornet blueprint build state, unrelated to the tube or collision changes).

**Reading it honestly:** with this sim's assumed collision scale a lone tube is a poor weapon against an alert armed ship: it arrives in 4 to 5 s from the first shots and the guns win. Tubes pay off in numbers and against slow or unarmed targets. A direction needs 20 to 50 seeds on the scenarios that matter after the `ram` kind exists.

**What the sim does not show:** deformation, penetration, asymmetric damage, the real collision-damage scale (an assumption [I]; nothing about ramming has run in the game, see Q6).
