# Kinetic Ram: heavy-armour impact drones (design plan)

Owner request: "design kinetic impact drones, with heavy armor designed to slam into enemy warships". This is a plan only. Nothing is built, simulated or
run in the game. Tags: **[data]** = `defs_stats.json`, **[src]** = Keen source as read in `docs/audit/physics_power.md` PP-07 (an old public snapshot,
**unverified in the game**), **[calc]** = arithmetic here, **[guess]** = assumption, to be settled by the in-game Ram Test (section 1.4).

## 0. Decisions in one table

| Question | Decision |
|---|---|
| First model | **Ram**: small grid, a 3x3-cell heavy-armour rod (6 layers of prow), ONE small-grid large ion thruster, point-and-burn, 9.3 t, 46.7 MJ at 100 m/s |
| Second model | **Ram Tandem** (Ram + 2 small warheads behind the prow, fired in the breach). Built after Ram Test R2. **Battering Ram** (large grid, 42 t) only if gate G2 says so |
| Armour | Heavy armour on the prow (all 6 front layers, 54 cubes). Light armour elsewhere. Everything except 2 cameras, the antenna and the merge stalk sits inside the prow's 3x3 shadow |
| Script | `scripts/RamAI.cs`, based on `StingerAI.cs` (13.8k minified). **One PB is enough** (estimate 30-40k minified). No Staff PB |
| Launch | Hand-paste, or a new **Ram Rack** printer (HiveMotherAI rack mode, as the Stinger Rack). It does not fit the Cradle Pod (16 cells long, the slot takes 9) |
| Sim | New scenario kind `ram`. Collision model: the existing energy model plus 2 calibrated additions (section 5.2). Few seeds |

## 1. Physics

### 1.1 What the game does on a grid-grid impact (all [src], unverified)
`MyGridPhysics.PerformDeformation` / `ApplyDeformation`, as read in PP-07:
- Above a separating speed of 2 m/s, each contact point gets a deformation offset `d = 3 * (0.3 + max(0, |v| - BreakImpulse/m) / 15)` m, capped at 20 m.
  Which body's `m` this is, and what `BreakImpulse` is, is **not established**.
- **Soft volume**: the deformation is applied around the contact point, **1.2 m wide on a small grid, 4 m on a large grid**, and `1.5 * d` deep.
- A block in that volume is **removed outright** (not damaged by HP) when `max(lowerRatioLimit, DeformationRatio) * localOffset > 0.7 * gridSize`.
  So the threshold is 0.35 m on a small grid and 1.75 m on a large one.
- A `GRID_DESTRUCTION` explosion of radius `min(max(gridSize, d), 10)` m is spawned. Its damage is unknown.
- This only happens with "destructible blocks" on (the default). Weapon damage ignores `DeformationRatio`. Collisions use it.

Local offset each block type must reach before it is removed ([data] DeformationRatio, [calc] threshold / DR):

| block | DR | removed when local offset > |
|---|---|---|
| small light cube (`SmallBlockArmorBlock`, 20 kg, 100 HP) | 0.32 | 1.09 m |
| small heavy cube (`SmallHeavyBlockArmorBlock`, 112 kg, 560 HP) | 0.16 | 2.19 m |
| large light cube (`LargeBlockArmorBlock`, 500 kg, 2,500 HP) | not in the file (engine default) | unknown [guess 0.45 -> 3.9 m] |
| large heavy cube (`LargeHeavyBlockArmorBlock`, 3,300 kg, 16,500 HP) | 0.40 | 4.38 m |

What follows from this (reasoning, to be checked by R1):
1. **Speed decides, not energy.** If `BreakImpulse/m` is small, 100 m/s gives d = 20 m (the cap) on both bodies, and every block inside the soft volume goes.
   A heavy prow therefore does **not** survive a 100 m/s impact any better than a light one. Its value is elsewhere:
   (a) **density**: 5.6x the mass per cell, so 5.6x the momentum to push through the breach;
   (b) **point-defence resistance**: 560 HP vs 100 HP per cell on the approach.
2. **Small vs large asymmetry.** For the same offset, a small-grid block needs 5x less (0.35 vs 1.75 m threshold). The large target's soft area is
   wider (4 m), though. Expect per contact: the Ram loses its whole front, and the warship loses one 4 m patch (1-4 large blocks per layer).
   How many layers deep is THE open number (R1).
3. **Penetration needs repeated contacts.** Each tick the rod is still moving into the hole is another contact and another patch. So a long, dense rod
   whose rear keeps its speed digs deeper. More mass is nearly free here: the 100 m/s cap, not thrust, limits the speed (section 2).
4. **Head-on doubles the energy.** The cap is on world speed. Against a warship flying at us at 40 m/s (Pirate brain cruise) the closing speed is
   140 m/s (KE x2). A stern chase on a ship running at 40 m/s gives only 60 m/s (KE x0.36).
5. Warheads: the sim's model of the old `MyWarhead.ContactPointCallback` says armed warheads detonate on contact above 5 m/s (`WarheadContactDetonate`,
   off by default in the sim). Whether that still holds in the game, and whether an armed warhead destroyed by deformation explodes, is unknown (R2).

### 1.2 Known vs guess

| Item | Status |
|---|---|
| Masses, thrust, HP, DeformationRatio, PCU, DLC flags | [data], exact |
| 100 m/s cap, small and large | [data] `SmallShipMaxSpeed`/`LargeShipMaxSpeed` = 100 (audited) |
| Deformation formula above | [src], old snapshot. The meaning of `BreakImpulse/m` is unknown |
| Blocks removed per impact, hole depth, what is left of the Ram | **unknown**: R1 |
| Sim collision damage `0.0005 * 0.5 * mu * vn^2` over a 1.5 m patch, restitution 0.2 | placeholder [I] (PP-07). `systems_catalogue.md`: "do not tune ramming" until calibrated |
| Warhead contact / destroyed-warhead detonation | unknown: R2 |
| Small merge block (strength 0.005) holding 9.3 t on a rack | unknown: R4 |

### 1.3 Kinetic energy and run-up ([calc] from [data]; KE at a 100 m/s impact, run-up from rest at full thrust)

| design | mass | forward thrust | accel | 0 -> 100 m/s | KE @100 | KE @140 (head-on) | PCU |
|---|---|---|---|---|---|---|---|
| Stinger (existing, 2 warheads) | 2,037 kg | 14.4 kN | 7.1 m/s2 | 14.1 s / 707 m | 10.2 MJ | 20.0 MJ | 566 |
| Ram P2 (2 prow layers) | 5,305 kg | 172.8 kN | 32.6 | 3.1 s / 153 m | 26.5 MJ | 52 MJ | 494 |
| Ram P4 | 7,321 kg | 172.8 kN | 23.6 | 4.2 s / 212 m | 36.6 MJ | 72 MJ | 512 |
| **Ram (P6, recommended)** | **9,337 kg** | 172.8 kN | **18.5** | **5.4 s / 270 m** | **46.7 MJ** | 91.5 MJ | **530** |
| Ram Tandem (P6 + 2 warheads) | 9,510 kg | 172.8 kN | 18.2 | 5.5 s / 275 m | 47.6 MJ | 93 MJ | 628 |
| Battering Ram (large grid) | 42,236 kg | 691.2 kN | 16.4 | 6.1 s / 305 m | 211 MJ | 414 MJ | 345 |

Ingots: Ram P6 is 9.9 t iron, 605 nickel, 444 cobalt, 32 silicon, 12 gold, 5 platinum. The Battering Ram is 42.6 t iron, 2.35 t cobalt, 160 gold.
The sim's placeholder would put about 23,000 HP on EACH side of a Ram P6 hit on the 320 t Pirate (mu about 9.1 t): about 9 large light blocks' worth on the
warship, spread over 1-4 blocks. It is the right order of magnitude, but uncalibrated.
Targets for scale: Pirate 320 t (sim), Corsair 268 t, Ballista 1.1 kt, Warden 2.6 kt, Leviathan 14.1 kt, Tech Hunter **19.8 kt** (13,617 blocks: 3,703 heavy and 8,029 light large armour).
No ram kills a Tech Hunter by energy. A ram is a **component stripper**: what counts is which exterior blocks it removes (thrusters, turrets), so aiming matters (4.3).

### 1.4 In-game calibration: the Ram Test (style of `sprue_test.md`, one creative session, about 30 minutes)

Rig (phase P3): `Ram Target`, a dynamic large-grid slab 10 x 5 x 4 cells (about 95 t). Left half: layers light / heavy / light / heavy from the front. Right half: heavy / light / heavy / light.
The slab has a battery, an antenna, and a PB running `RamTargetProbe.cs` at the back centre, behind both stacks. Every tick the PB counts each (zone, layer) cell with
`Me.CubeGrid.CubeExists` and sums `CurrentDamage` from `GetCubeBlock`. It broadcasts the zone centres on IGC `RAMTEST` and logs the result to its screen and to Custom Data `[Log]`.
The Ram runs `test <speed> <L|R> [range]`. It reads the zone centre from `RAMTEST`, backs to `range` (default 600 m) on the zone normal, and burns to `<speed>`.
It then coasts nose-on (dampeners off) and broadcasts mass, speed, and gyro / thruster / PB alive, every tick. The slab logs the last frame before contact and every frame after.
`Ram Test P2` (prow 2 layers) is the same Ram with a shorter prow.

| test | shots | question | result goes to |
|---|---|---|---|
| R1a | Ram at 30, 60, 100 m/s into L | blocks removed per layer vs speed. Does the Ram stop, bounce or pass through? Ram mass left | sim `CollisionDamageScale*`, `CollisionPassThrough` (5.2), gate G1 |
| R1b | Ram at 100 into R | heavy-first face: is the hole shallower? | sim DR handling, armour advice for our own warships |
| R1c | Ram Test P2 at 100 into L | does a longer / heavier rod dig deeper (P6 vs P2)? | prow length P (keep 6, or go to 4 if no gain) |
| R1d | paste `Ram Dense` (optional), read the Info tab mass with the artificial mass block on and off | does `VirtualMassSmall` (906 kg [data] in one cell, 8x heavy armour) count as real mass? | Ram Dense variant (7) |
| R2 | Ram Tandem: `test 100 L` with warhead delay 0, 2, 5 ticks, and `test 30 L nodet` (armed, script never detonates) | extra layers from a blast in the breach. Do armed warheads go off on contact or when crushed? | default `Tandem Delay Ticks`, arm timing |
| R3 | Stinger at 100 into L | baseline per PCU | doc table, owner choice |
| R4 | Ram on a Ram Rack, rack thrusting 3 m/s2 and turning | does the small merge hold 9.3 t? | rack limits, or a `SmallShipMergeBlock` (1x3x3, strength 0.015) latch |

**Gates**:
- **G1**: if R1a at 100 m/s removes at least 2 large layers in its patch, the pure Ram is worth shipping.
  If it removes 1 light layer or less, ship Ram Tandem as the default and keep Ram as a cheap option.
- **G2**: if R1c shows depth grows with rod mass, AND the owner wants anti-capital punch, build the Battering Ram (or Ram Dense if R1d passes).
- Re-run R1a after any game update (collision code changes between versions).

## 2. Variants and the recommendation

| | Ram (small) | Battering Ram (large) | Stinger (existing) |
|---|---|---|---|
| mass / KE @100 | 9.3 t / 46.7 MJ | 42.2 t / 211 MJ | 2.0 t / 10.2 MJ |
| run-up to cap | 5.4 s, 270 m | 6.1 s, 305 m | 14.1 s, 707 m |
| front HP vs PD | 9-cell face, 54 heavy cells = 30,240 HP | 2 heavy large faces, 66,000 HP | about 1,000 HP (warheads 486 each) |
| frontal area | 1.5 x 1.5 m | 2.5 x 5 m | 1 x 1 m |
| launch / print | small merge latch, Ram Rack printer, hive IGC, swarmable | large merge only; no large printer exists; welded by hand | Stinger Rack |
| cost | 10 t iron, 444 cobalt | 42.6 t iron, 2.35 t cobalt, 160 gold | small |
| deformation side | small grid: crushed easily (0.35 m threshold) | large vs large: equal thresholds, 4 m patches on both sides | blast only |

**Build the Ram first.** It uses the systems that exist (small merge latch, rack printer, `_TGT`/FLEET/`DRONE_CUE` target feeds, the Stinger code path).
It needs no new XML shapes except `SmallBlockLargeThrust` (same `MyObjectBuilder_Thrust` type, first use in a pasted design: a small paste risk).
It reaches the cap in 270 m, so it can be launched inside a warship's turret envelope. Its prow is a PD sponge. Several Rams at the same breach dig deeper than one Battering Ram,
which is far heavier and has no launcher.
**Armour decision**: heavy on all 6 prow layers, for density and PD (1.1 point 1). Light armour for the rest of the skin: the side skin is exposed only off-axis, and
the extra mass is better spent as prow layers. The 3x3 rod puts battery, thruster, gyro, PB and RC in the prow's shadow, so they survive a head-on PD stream.
**Warhead decision**: the first model is pure kinetic, as asked. Ram Tandem (2 small warheads, 15,000 explosion damage each [data], 4.49 m radius)
follows if R2 shows they fire usefully inside the breach. A blast inside a hole is occluded far less than one on the skin (the sim ignores occlusion, so it cannot judge this).
**Block survival through impact**: nothing forward of the CoM is expected to survive a 100 m/s hit (1.1). The PB, gyro and thruster sit 4-12 cells behind the prow face. If R1a shows them alive,
the post-impact logic (4.6) uses them. If not, the Ram simply ends as debris, which is a valid end state.

## 3. Geometry (axes as `wasp.py`: x Right, y Up, z Backward, **nose -z**; cells are block Min, 0.5 m)

Rod cross-section x -1..1, y -1..1 (3 x 3). Length z -13..2 = 16 cells (8 m). Height with stubs y -2..3 (3 m).

| z range | y -1..0 (lower 2 rows) | y = 1 (top row) | outside the rod |
|---|---|---|---|
| -13..-8 | **prow: 54 x `SmallHeavyBlockArmorBlock`** (all 9 cells per layer, P = 6) | (part of the prow) | none |
| -7..-5 | `SmallBlockBatteryBlock` 3x2x3 (1 MWh, 4 MW: the large ion needs 2.4 MW) | (0,1,-7) PB, Forward = -z (front face mounts on the prow's back); (-1,1,-7) Remote Control; (0,1,-6) Gyro (Bottom face on a neighbour, placed with `pods.orient_attached`); 6 light cubes | (0,2,-7) camera Forward on seats (0,2,-6),(0,2,-5); (0,-2,-7) camera Forward on seats (0,-2,-6),(0,-2,-5) |
| -4..-1 | `SmallBlockLargeThrust` 3x2x4, Forward = Backward (exhaust +z), `keep_clear` 4 -> z 0..3 at y -1..0 | 12 light cubes (spine) | (0,2,-3) antenna 1x1x2 standing Up (as the Hornet) |
| 0..2 | exhaust lane (empty) | stalk (0,1,0),(0,1,1) light, `merge_small((0,1,2), "Backward")` | none |

```
side view x=0   z: -13 ........ -8 | -7 -6 -5 | -4 -3 -2 -1 | 0  1  2
y= 3                                              A
y= 2                               C  s  s        A
y= 1               H  H  H  H  H  H | P  G  a |  a  a  a  a | s  s  M
y= 0               H  H  H  H  H  H | B  B  B |  T  T  T  T | >  >  >  (exhaust)
y=-1               H  H  H  H  H  H | B  B  B |  T  T  T  T | >  >  >
y=-2                               C  s  s
```

- **CoM** [calc]: z = -8.4 cells (the back layer of the prow, 2.5 m behind the nose face). y = +0.07 cells (3 cm above the axis), x about 0.
  The impact line (the centre of the prow face, along -z) passes within 3 cm of the CoM, so an end-on hit adds almost no spin.
  The thrust line is irrelevant: the game applies thrust at the CoM (audit).
- Mass 9,337 kg, 87 blocks, PCU 530. Pitch inertia about 6e4 kg m2 [calc, rough]: one small gyro flips it 180 degrees in about 1.5 s [guess, sim checks].
- Mount rules (HANDOFF 4.3): PB front only (into the prow), gyro bottom only (`orient_attached`), cameras back only (on armour seats), antenna back end, merge clamp per `MERGE_CLAMP_FACE_SMALL`.
  The battery and large ion faces are checked by the validator. If one does not seat, swap the neighbour to a light cube.
- Validator calls in `ram.py`: `thr()` / `cam()` (they add `keep_clear` 4 / 3). `bp.validate()` (ids, overlaps, mount attachment, one piece, clearances).
  `C.finalize`, `C.report_line` (checks docking via `check_docking`).
  Module asserts:
  - `check_shadow`: every block except the 2 cameras, their 4 seats and the antenna lies inside x, y in [-1, 1].
  - `check_prow`: layers z -13..-8 are all heavy cubes, and nothing lies at z < -13 except camera view zones.
  - `check_com`: the CoM is within 0.25 m of the axis.
  - `check_no_dlc`: every block has `dlc == []` in `defs_stats.json`.
  - `run_module(MODELS, required_axes={"Ram": ("Forward",)})`: point-and-burn, like the Gnat.
- Ram Tandem: (-1,1,-6) and (1,1,-6) become `SmallWarhead` (named `Ram Warhead 1/2`), 1 cell behind the prow, in the shadow. Ram Test P2: `ram(prow=2)`.
- Lite: retired (CLAUDE.md). `ram(lite)` ignores `lite`.

## 4. Script and AI: `scripts/RamAI.cs` (fork of `StingerAI.cs` v1.1)

### 4.1 PB plan
One PB, Update1: track, lead, aim, gyro, thrust, impact detection and post-impact in one hot path. It keeps FLEET LINK (`tools/fleet_sync.py`) and the Stinger's launch / merge logic.
Estimate 30-40k minified, far below the 80k budget. **No Staff PB.** If it ever passes 80k, the CLAUDE.md procedure applies: an Opus plan first,
then the test mode, display and FLEET LINK move to `[Staff] Ram PB` over `CurrentConstruct` IGC. The hot path and the only override writer stay in Flight.

### 4.2 States
`WAIT` (docked or no target) -> `RUN` (midcourse) -> `TERMINAL` (t_go < `Align Time` + 1 s) -> `IMPACT` (30-tick assessment) -> `RERAM` / `INERT` / (Tandem) `DETONATE`. Also `ABORT` and `TEST`.

### 4.3 Targeting and aim point
- Sources: the Stinger's (sensor, nose camera rays, `_TGT` of the listed tags, FLEET, `DRONE_CUE`), plus the two forward cameras.
  Large grids only by default (`Target Small Grids=false`). Enemies only (`Valid()`).
- Hull points: port the Hornet / Longbow store (raycast `HitPosition` kept in the target's own frame from `MyDetectedEntityInfo.Orientation`). That way a turning ship keeps the aim point.
- `Aim Point` modes:
  - `Hull` (default): the hull point nearest the line from me to the target centre. This guarantees a hit, not a graze.
  - `Stern`: the hull points with the largest target-local +z. Main thrusters usually sit there.
  - `Center`.
  - `Breach`: the impact point another Ram reported for this target.
  Mode choice is an A/B in the sim (5.3).
- **Breach sharing**: after an impact, a Ram broadcasts `RAM_BREACH` `MyTuple<long tgtId, Vector3D breachLocal, Vector3D normalLocal, double time, long sender>` (target frame).
  Later Rams in `Breach` mode aim there once their own camera lock gives them the target's orientation. Repeated hits dig deeper (1.1 point 3).

### 4.4 Guidance
- Midcourse (the Stinger law): `tgo = dist / max(20, closing)`, predicted point `p + v*tgo + a*tgo^2/2`, want = 100 m/s toward it, `need = (want - v)*Gain`.
  Point and burn: the nose follows `need`, and thrust runs only within 37 degrees (the Stinger gate).
- Terminal, at t_go < `Align Time` + 1 s: a zero-effort-miss PN law `a_perp = N * ZEM_perp / tgo^2` (N = `PN Gain` 3.5), executed by tilting the nose up to 15 degrees off `v_rel`
  (at the cap, lateral accel = 18.5 * sin(tilt), so 4.8 m/s2 at 15 degrees).
- In the last `Align Time` (1.0 s), the nose is held on `v_rel` (my velocity minus the target's), so the prow face leads. Thrust stays at 100%.
- Pre-impact speed: the Ram is always at full thrust. Because the cap needs 270 m of run-up, a launch closer than about 300 m hits at lower speed (screen warning).
  Geometry preference: when the target is closing on me, keep the head-on line (1.1 point 4). Do not loop round for a stern hit unless the aim mode is `Stern`.

### 4.5 Point defence
The prow is the shield, so the Ram flies nose-on along the line of sight (true for most of the run).
Optional weave (`Weave=false` by default; sim A/B): +/-12 degrees at 0.5 Hz between 800 m and 300 m, straight inside 300 m.
Kamikaze-defence scripts of enemy drones key on a radius of 3.5 m or less. The Ram's bounding radius is about 4.2 m [calc].

### 4.6 Impact detection and after
- **Imminent**: a forward camera ray shows a hull distance below `v_rel` * 0.3 s (the Stinger's hull ray). Ram Tandem arms here. It never arms within 150 m of the launch point.
- **Impact**: any of the following.
  - The measured velocity change in one tick exceeds 3 m/s more than the thrust can explain (18.5 m/s2 = 0.31 m/s per tick).
  - `CalculateShipMass().PhysicalMass` drops more than 3% in a tick.
  - `Me.CubeGrid` changed (the grid split).
- After impact, the Ram broadcasts `RAM_BREACH`. Tandem: `Detonate()` after `Tandem Delay Ticks` (default 2, from R2).
- Pure kinetic: assess for 30 ticks.
  - If the gyro and thruster are working and mass is at least `Re-Ram Min Mass Fraction` (0.5) of the launch mass: **RERAM**. Flip, burn out to `Re-Ram Back-off` (300 m) along
    the breach normal, turn, and ram the same breach (about 12 s per cycle [calc]). At most `Max Re-Rams` (2).
  - Else **INERT**: overrides released, thrust 0, dampeners on, `_TGT` share of the target kept alive for others. No self-destruct: the pure Ram has none.
- `abort`: flip and burn to a stop, then hold. A Ram never commits (t_go < 3 s) while a hive or FLEET friendly position is within 25 m of the final path.

### 4.7 Config `[Ram]` (written on first run)
`IGC Tag=RAM`, `Listen To IGC Tags` (Stinger list), `Max Target Distance=4000`, `Aim Point=Hull`, `Max Speed=100`, `Gain=2.5`, `PN Gain=3.5`, `Align Time=1.0`,
`Weave=false`, `Weave Amplitude=12`, `Weave From=800`, `Weave Until=300`, `Re-Ram=true`, `Max Re-Rams=2`, `Re-Ram Min Mass Fraction=0.5`, `Re-Ram Back-off=300`,
`Arm Warheads Before Impact (s)=0.3`, `Tandem Delay Ticks=2`, `Never Arm Within This Of Launch Point=150`, `Target Large Grids=true`, `Target Small Grids=false`.
Commands: `launch | abort | test <speed> <L|R> [range] [nodet] | reload`. Screen: the state, `tgo`, closing speed, aim mode, impact speed and mass left.

## 5. Integration

### 5.1 Files
| file | change |
|---|---|
| `tools/designs/ram.py` | new: `ram(lite=False, prow=6, warheads=False, dense=False)`. `MODELS = {"Ram", "Ram Tandem"}`. Module asserts (section 3). `__main__` = `run_module` |
| `tools/designs/ram_test.py` | new, kind test: `Ram Target`, `Ram Test P2` (and `Ram Dense` when R1d is wanted) |
| `tools/designs/__init__.py` | `'ram'` into `DRONE_MODULES`, `'ram_test'` into `TEST_MODULES` |
| `tools/designs/printers.py` | `DRONES['Ram'] = ('ram', 'Ram', 'RAM', False)`. Spec `Ram Rack` (`groups=[('Ram', 2, (2, 1))]`, `rack=True`, `launch_tag='RAM'`). `build()` passes `launch_tag` to `PL.hive_ini` |
| `scripts/RamAI.cs`, `scripts/RamTargetProbe.cs` | new |
| `tools/fleet_sync.py` | `"RamAI"` into `SCRIPTS` |
| `tools/build_all.py` | command list for `RamAI.cs` (as `StingerAI.cs` at line 243) |
| `tools/tests/test_ram.py`, `golden_hashes.json` | build + asserts, minified size < 80k, no DLC, docking, mass 9.0-9.6 t, accel >= 15 m/s2. `run_all.py --update-golden` after the design is final |
| `sim/src/Run/ScenarioGen.cs` | `Ram()` kind `ram`. `"Ram"` into `FleetOnlyModels`. `DefaultCommands`: `case "Ram"` -> `launch` at 0.5 s. `--ram-extra` (as `--kamikaze-extra`). `SwarmCueSeconds = 0.5`, Rams on team 1 (the `SwarmCueTeam` default) |
| `sim/src/Core/Physics.cs`, `SimSettings.cs` | collision additions (5.2) |
| `sim/src/Run/Metrics.cs` + `tools/ram_report.py` | per Ram: impact yes/no, impact speed, nose-vs-`v_rel` angle, killed before impact, target blocks removed, target thrusters / turrets lost |
| `pc/plans/ram.plan` | owner-PC runs (5.3) |
| `docs/hive-protocol.md` (`RAM_*`, `RAM_BREACH`), `packages/changelog.md`, `HANDOFF.md` section 7 list, `packages/unknowns.md` (R1-R4) | docs |

### 5.2 Ramming in the simulator
Today the sim resolves contacts with a separating impulse (restitution 0.2), plus a placeholder energy damage split over blocks within 1.5 m (`Physics.cs` `Contact()`).
A Ram there bounces off after one contact, and the damage scale is a guess. The minimum model, all behind settings whose defaults reproduce today's results exactly:
1. **`CollisionPassThrough`** (0..1, default 0). Compute the damage first. If the target block at the deepest contact was destroyed in this tick, scale the normal impulse by
   `1 - CollisionPassThrough`. The Ram then keeps moving and makes the next contact a tick later: the multi-contact penetration of 1.1 point 3.
2. **`CollisionDamageScaleSmall` / `CollisionDamageScaleLarge`** (default = `CollisionDamageScale`): the scale applied to each body by its grid size, so the R1 split (Ram crushed, warship dented) can be fitted.
   Optional `CollisionUseDeformationRatio` (default false) divides a block's share by its DR / 0.32.
3. **Sub-stepping**: when a body moves faster than 50 m/s, test it for contact at 2 sub-steps of the tick (1.67 m per tick against a 2.5 m cell is borderline).
4. A self-test `selftest ram` checks three things.
   - A 3x3x6 heavy rod at 100 m/s into a static 5x5x4 large slab removes at least 1 block on each side, and with pass-through goes deeper than without.
   - With all defaults, `TestCollision` and the golden numbers are unchanged.
   - `WarheadContactDetonate` still works.
5. Later, if R1 shows depth behaviour these cannot fit: the full PP-07 depth model (`CollisionModel=deform`), with its own plan.
Until R1 is in, report sim ram **damage** as "uncalibrated". Only hit rate, impact speed and angle, and PD survival are trusted.

### 5.3 Scenarios and seed counts (CLAUDE.md: few seeds)
- `ram/Pirate-vs-Ram`, `ram/Pirate-vs-2xRam`, `ram/Pirate-vs-4xRam`, and `ram/Pirate-vs-4xStinger` (cost baseline: 2,264 vs 2,120 PCU).
- `--ram-extra Corsair` (an own warship with PD, blueprint Custom Data plus rigid subgrids, as the Ballista).
- Tech Hunter: `run --enemies enemies --kind enemy --enemy-fleets "Ram;4xRam;4xStinger"`.
- Claude session runs: smoke `one --scenario "ram/Pirate-vs-Ram" --trace` (1 seed) and `run --kind ram --seeds 3`.
  A/B tests (aim `Hull` vs `Stern` vs `Breach`, weave on/off, PN gain 3 vs 4) at **20 seeds** on `ram/Pirate-vs-4xRam` only.
  Tech Hunter check at 10 seeds. Nothing larger.
- `pc/plans/ram.plan` (owner PC, explicit counts): `ram-main | run --kind ram --seeds 50 --resume`, `ram-th | run --enemies enemies --kind enemy --enemy-fleets "Ram;2xRam;4xRam;8xRam;4xStinger;8xStinger" --seeds 50 --resume`,
  and the same two with `--collision-pass-through <R1 value>`.

## 6. Phases (each sized for one Sonnet session; no zips; say "zips not rebuilt")

| # | work | verification |
|---|---|---|
| P1 | `tools/designs/ram.py` (Ram, Ram Tandem) + `tools/tests/test_ram.py` + register in `DRONE_MODULES` (placeholder PB script: StingerAI.cs until P2) | `python3 -I tools/designs/ram.py` (PASS lines), `python3 -I tools/tests/test_ram.py`, `python3 -I tools/tests/run_all.py` |
| P2 | `scripts/RamAI.cs` from StingerAI (sections 4.2-4.7, incl. `test` mode); swap it into `ram.py`; `fleet_sync` | `tools/compile_check.sh`, `python3 -I tools/fleet_sync.py --check`, `python3 -I tools/build_all.py --no-zip --only "Ram" "Ram Tandem"` |
| P3 | `tools/designs/ram_test.py` + `scripts/RamTargetProbe.cs` + test sheet (section 1.4) for the owner in `test_designs/Ram Test/` | `python3 -I tools/build_all.py --no-zip --only "Ram Target" "Ram Test P2"`, `python3 -I tools/tests/run_all.py`; owner runs R1-R4 |
| P4 | sim: kind `ram`, `FleetOnlyModels`, `DefaultCommands`, settings 5.2 (1-3), `selftest ram`, metrics, `tools/ram_report.py` | `bash sim/build.sh && mono out/sim/SimHost.exe selftest all` (only pre-existing failures), `one --scenario "ram/Pirate-vs-Ram" --trace out/sim/ram.csv`, `run --kind ram --seeds 3` |
| P5 | calibrate from the owner's R1 numbers (fit the scales and pass-through to R1a/R1b counts), then the A/B tests of 5.3 | `run --filter "^ram/Pirate-vs-4xRam$" --seeds 20 ...` per variant; `python3 -I tools/ram_report.py A B` |
| P6 | `Ram Rack` printer, `pc/plans/ram.plan`, Tandem defaults from R2, docs (`hive-protocol`, changelog, unknowns, HANDOFF) | `python3 -I tools/build_all.py --no-zip --only "Ram Rack"`, `python3 -I tools/tests/run_all.py` |
| P7 (gate G2 only) | Battering Ram (large: 2 + 2 heavy prow, `LargeBlockGyro`, `LargeBlockBatteryBlock`, 2 `LargeBlockSmallThrust`, compact antenna) or `Ram Dense` (artificial-mass core). Needs its own launcher plan | same as P1/P2 |

## 7. Variants held back
- **Ram Dense**: a 1x1x4 `VirtualMassSmall` core (906 kg/cell [data], switched off) behind 2 heavy layers: about 11 t in the same 16-cell rod.
  It costs 14 gold and 220 cobalt per block. Only if R1d shows its 906 kg counts as real mass, and R1c shows a heavier rod digs deeper.
- **Hydrogen Ram**: `SmallBlockSmallHydrogenThrust` (98.4 kN, 334 kg [data]) gives 3x the thrust per kg, but needs a tank, conveyors and a fuel fill on the rack.
  The sim does not consume hydrogen. Not worth it while the 100 m/s cap, not acceleration, limits the Ram.
- **Salvo** (time on target, several Rams arriving within 1 s from different bearings to split PD): `RAM_SWARM` t_go exchange. After P5, if the A/B shows PD kills Rams.

## 8. Open questions for the owner
1. Ship the pure kinetic Ram as asked, with Ram Tandem as the second model? Or make Tandem the default if R1 is weak (gate G1)?
2. Launch platform: hand-paste plus the Ram Rack printer? Or should the Warden / Leviathan carry Rams? That needs a 17-cell-deep Cradle Pod slot (`pods.cradle_pod` today: 9).
3. World settings: is block destruction / deformation on? Any server or world collision-damage multiplier? Experimental mode as before?
4. Is 9.3 t, 10 t iron and 444 cobalt per Ram acceptable? Or should Ram P4 (7.3 t) be the default?
5. Can you run the Ram Test (R1-R4, about 30 minutes in creative) once P3 is built? The sim ram damage stays uncalibrated until then.
6. Is the artificial-mass ballast (Ram Dense) fair game, or is it out like DLC blocks?
7. Add the `ram` kind to the default full matrix (`All()`, as `kamikaze` is)? That changes the job counts of the existing PC plans.
8. Do you want the large Battering Ram at all, given it needs a large-grid launcher?
