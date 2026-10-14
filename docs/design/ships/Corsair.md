# Corsair (corvette, small grid)

Key `ship_corsair`. Module `tools/designs/corsair.py` (`MODELS = {"Corsair": fn(lite), "Corsair Wing": fn(lite)}`, `DOCS`, `SCRIPTS = ["ShrikeAI.cs", "StaffAI.cs", "CradleBayAI.cs"]`;
M6: the Helm is `ShrikeAI.cs` and the Staff `StaffAI.cs`, the same scripts as on every other ship, there are no Corsair-specific scripts), tests `tools/tests/test_corsair.py`.
Blueprints `Corsair`, `Corsair Lite`, `Corsair Wing`, `Corsair Wing Lite`. Part of the family Shrike / Corsair / Warden / Leviathan (`systems_catalogue.md` 4.3, `control_arch.md`, `grids_bays.md` 5.3).
Evidence labels as elsewhere: [data] repo data, [calc] computed here, [unverified] never seen in a game file.

## 1. In one paragraph

> **Gyro boost (v2.8):** gyros 20 -> 40 (x2): about 18 MN m, so the angular accelerations quoted below roughly double (yaw about 0.7 rad/s2 before the extra mass). The Helm's Auto Max Turn calibration measures the new authority on the first slews; no script change.


A 33 m catamaran-hammerhead: a chisel nose and a hammer crossbar (two swept wings with assault cannons at the tips and autocannons inboard, orange leading edges), a dorsal
ridge carrying two small railguns, a stepped bridge tower with the cyan cockpit, a long charcoal centre hull, and two outrigger pylons (hydrogen tanks with brake / lift / dive
thrusters) that carry two open-fronted cradle pods with orange rims, fins and a 24-cell launch lane in front. Four main thrusters on four stacked stern tanks. Heavy armour on the
nose, belly, tower front, stern; light elsewhere; hollow with bulkheads. Hand-flyable (cockpit toolbar) and scripted (Helm + Staff PB).

## 2. Numbers (`python3 -I tools/build_all.py --only Corsair "Corsair Wing" --no-zip`: 4 PASS)

| | Corsair (Full) | Corsair Lite |
|---|---|---|
| blocks | 6,566 | 6,558 |
| dimensions W x H x L | 26.5 x 12.0 x 33.0 m (53 x 24 x 66 cells; H includes the belly turrets) | 25.5 x 10.0 x 33.0 m |
| mass, blocks / loaded (magazines, uranium) | 266.5 t / 268.1 t | 258.1 t / 258.9 t |
| acceleration F / B / U / D / L / R (`twr`) | 7.2 / 3.6 / 3.6 / 3.6 / 3.6 / 3.6 m/s2 | 7.4 / 3.7 x5 |
| PCU | 10,621 | 9,451 |
| HP (sum of integrity) | 1.26 M | 1.24 M |
| centre of mass (loaded) | on the centre plane, 2.6 m above the keel, 19.7 m from the nose-tip plane (cell z 45.5; recomputed in review, the earlier 22.5 m was wrong) | same |

Catalogue targets (4.1 / 4.3): 257 t, 8.2 m/s2 forward, 4.1 other axes, 12.1 k PCU, 8.1 k DPS. Result: mass +4 %, forward -12 %, other axes -12 %, PCU -12 %, sustained data DPS
about 8.1 k (2 rails 780 + 2 assault 1,333 + 2 autocannon 2,500 + 4 gatling turrets 2,813 + assault turret 667). All inside the 20 % band. Why lower thrust: one large thruster per
tank face (14 large, 1.92 MN forward, 0.96 MN per other axis), no small thrusters, because a small thruster on a tank face was never seen in a sample (see 5). Corsair Wing adds 2 Hornets
(2 x 4.4 t, +2 k PCU) that `twr` does not count: with them forward acceleration is about 6.7.

Power [calc]: reactor 14.75 MW (`SmallBlockLargeGenerator`, 20 kg uranium = 20 MWh = 80 min at 15 MW), railgun recharge 2 x 3.6 MW = 7.2 MW (20 s each), 20 gyros 0.04 MW; batteries
4 x 1 MWh full, 16 MW. Lite has four `SmallBlockSmallGenerator` (2 MW) instead of the reactor (railgun recharge slows to about 55 s each; the 4 MWh covers it).
Fuel [calc]: ten `SmallHydrogenTank` full (500 kL each); a large thruster burns 386 L/s at full thrust, so each main/strafe thruster has 21.6 min of continuous full burn on its own tank;
the pylon tanks feed three thrusters each (7.2 min if all three burn at once, which never happens).
Ammunition (pre-loaded, capacity checked by `sebp`): railguns 6 slugs each (capacitor full), assault cannons 3 shells each, autocannons 3 clips = 48 rounds each, gatling turrets 5 boxes = 700 rounds each, assault turret 3 shells.

## 3. Layout map

Cells 0.5 m, `x` Right, `y` Up, `z` Backward (bow = low z); mirror-symmetric about the middle of cell 0, block for block (`mirror_hull()`). Side view at x = 0 (legend: `#` armour, `w` window,
`C` cockpit, `U` turret, `K` tank, `T` thruster, `R` reactor, `B` battery, `y` gyro, `P` PB, `r` RC, `t` timer, `e` event controller, `c` camera, `a` antenna, `G` gun, `M` cradle merge):
```
SIDE x=0 (columns z 0..70, rows y)
     0         1         2         3         4         5         6         7
     01234567890123456789012345678901234567890123456789012345678901234567890
y 20 .......................................................................
y 19 .......................................................................
y 18 .........................................CCC...........................
y 17 .........................................CCC....a.........UUUUUUU......
y 16 .........................................CCC....ac........UUUUUUU......
y 15 ........................................############......UUUUUUU......
y 14 ..............................######.##w#.#........###....UUUUUUU......
y 13 ..........................#####....#w#....#..........###..UUUUUUU......
y 12 ......................#####...#...........#...........##############...
y 11 ...................####.......#...........#...........#.......######...
y 10 ...............#####..........#...........#...........#.......######...
y  9 ............####..#...........#...........#...........#.......######...
y  8 ........#####.....#...........#...........#...........#.......######...
y  7 ......###.........#...........#...........#...........#.......######...
y  6 ......#...........#...........#...........#...........#.......######c..
y  5 .....c#...........#..........e#.###########...........#.......######...
y  4 ......####........#..........r#t#RRRyyyyyy#######.....#.......######...
y  3 .........#######..#..........P#P#RRRBBBBBBBBBBBB#.....#.......######...
y  2 ...............#######........#.#RRRBBBBBBBBBBBB#.....#.......######...
y  1 .....................###############################################...
y  0 ........................................c..............................
y -1 .......................................................................
y -2 .......................................................................
y -3 .......................................................................
y -4 .......................................................................

```
| z (cells) | what |
|---|---|
| 6-18 | nose tip, hammer crossbar (x -11..11, z 10-18) with 2 assault cannons (x +-9), 2 autocannons (x +-4) on its top, orange heavy bow |
| 22-36 | dorsal ridge (x -1..1) and the two railguns `[B1]` (x +-2, z 26-33, Back faces on the tower front) |
| 40-44 | two stacked mid tanks per side (flush with the flank) with a strafe thruster each (x +-7..9, moved here in review so the strafe thrust line passes 1.5 m from the centre of mass); centre corridor x = 0: Helm PB, Staff PB, Remote Control, Timer D, Event Controller |
| 33-47 | lower deck on the centre line: reactor (z 33-35), four batteries (z 36-47), 40 gyros on the battery tops (y 4, z 36-47) and beside them (x +-2, z 36-37) |
| 34-55 | bridge tower (3 steps, cockpit `SmallBlockCockpit` at x -1..1, y 16-18, z 41-43, glass slopes on the front), antenna, cameras |
| 53-67 | outrigger pylons x +-7..12: tank + brake thruster in front (z 53-55), dive thruster above, lift thruster below |
| 56-67 | cradle pods x +-13..25, bore x +-15..23, y 3-10, z 56-65; PD gatling turrets on the roofs and under the belly, outboard fins |
| 58-64 | assault turret on the aft deck (x -3..3) |
| 63-67 / 68-70 | four stacked stern tanks and the four main thrusters behind them |

Section through the cradles and pylon tanks (z = 60; the bores are the hollow squares, K = pylon tanks, U = turrets):
```
SECTION z=60 (columns x, rows y)
     65432109876543210987654321012345678901234567890123456
y 19 .....................................................
y 18 .....................................................
y 17 .....UUUUU.............UUUUUUU.............UUUUU.....
y 16 .....UUUUU.............UUUUUUU.............UUUUU.....
y 15 ..##.UUUUU.............UUUUUUU.............UUUUU.##..
y 14 ..##.UUUUU.............UUUUUUU.............UUUUU.##..
y 13 ..##.UUUUU.............UUUUUUU.............UUUUU.##..
y 12 ..###########........###########........###########..
y 11 .#############......##.........##......#############.
y 10 .##.........##......#...........#......##.........##.
y  9 .##.........##......#...........#......##.........##.
y  8 .##.........###KKKKK#...........#KKKKK###.........##.
y  7 .##.........#.#KKKKK#...........#KKKKK#.#.........##.
y  6 .##.........#.#KKKKK#...........#KKKKK#.#.........##.
y  5 .##.........#.#KKKKK#...........#KKKKK#.#.........##.
y  4 .##.........###KKKKK#...........#KKKKK###.........##.
y  3 .##.........##......#...........#......##.........##.
y  2 .#############......##.........##......#############.
y  1 ..###########........###########........###########..
y  0 .....UUUUU.................................UUUUU.....
y -1 .....UUUUU.................................UUUUU.....
y -2 .....UUUUU.................................UUUUU.....
y -3 .....UUUUU.................................UUUUU.....
y -4 .....UUUUU.................................UUUUU.....
```

## 4. Bays (grids_bays 5.1 / 5.3)

Each pod is an H-slot: bore interior 9 x 8 cells (x 15..23, y 3..10) and 10 deep (z 56..65: front margin cell, 8 drone cells, stalk layer); the 2-cell armour wall is z 66-67 and the host
`SmallShipSmallMergeBlock` stands alone on the stalk layer at (19, 6, 65) (left: (-19, 6, 65)), clamp face Forward (built with `merge_small`, `MERGE_CLAMP_FACE_SMALL`). The drone sits nose forward
(a Hornet is 7 x 6 x 8 cells: x 16..22, y 4..9, z 57..64, merge cell (19, 6, 64)), one free cell above, below, left, right; nothing of the host touches it. The launch lane is the same
9 x 8 prism for 24 more cells forward (z 32..55); `check_cradles()` asserts nothing is in bore or lane but the merge block (also registered as `bp.reserve`). Lane axis is 9.5 m from the centre line
(hull flank at 3 m), the hammer tip is 1 m from the lane edge. Bay 1 = port, Bay 2 = starboard; merge blocks `[SH] [Bay 1] Merge Port`, `[SH] [Bay 2] Merge Starboard` (kind `cradle`, not `merge`, because
`build_all` runs the drone-style `check_docking` on kind `merge`, which requires the merge block to be the outermost block of the whole grid; the host-side check is `check_cradles` / `check_wing`).
The `[Bay] Corsair` PB (third Programmable Block, M5; script `CradleBayAI.cs`; the design had the bay table in the Staff data, but the Staff script does not manage bays, so the table moved) holds `[Bay n]` with `Merge`, `Drone Box Min/Max` (host cells), `Drone Prefix`, `Away` (0,0,-1); the Staff reaches it with `<Tag>.S2P` (`release 1`, `release all`, `recall`). Hand launch: cockpit slots 5 and 6 switch the bay merge Off.
Drone exhaust fires into the wall 1 cell + merge away (test C5). Name rule: every host block starts `[SH] `, so Helm/Staff can ignore the merged drones' blocks (grids_bays 6.3).

**Corsair Wing**: the same ship plus two Hornet (Lite: Hornet Lite) CubeGrids placed with `Blueprint.dock()` on the merge faces: identity orientation, positions on the 0.5 m lattice
(Hornet pose (+-9.5, 3.0, 30.5) m), merge blocks touching. `check_wing()` verifies every drone cell is inside its bore and no host block touches a drone cell except the merge pair.
**Paste risk C1** (grids_bays 9): merge locks are not stored in a blueprint; whether the touching enabled merge blocks lock at paste is untested.

## 5. Systems and tags the scripts rely on

| item | names / tags |
|---|---|
| PBs | `[Helm] Corsair` (`ShrikeAI.cs`; Custom Data `[Ship]`, `[Link] Ship Tag/Block Prefix`, `[Manual] HUD Surface`, `[Battery] Standoff By Weapons`; M6: the old `[Battery n]` and `[Cockpit]` sections were read by no script and are gone), `[Staff] Corsair` (`StaffAI.cs`; `[Staff] Ship Tag/Block Prefix/Cockpit Surface/Helm HUD Surface/Wing Surface`, `[Wing] Types`), `[Bay] Corsair` (`CradleBayAI.cs`; `[Pod] Tag`, `[Bay 1]`, `[Bay 2]`) |
| fire control | `[B1]` 2 railguns (RAIL), `[B2]` 2 assault cannons (SHELL), `[B3]` 2 autocannons (BALLISTIC), `[PD]` 4 gatling turrets (point defence, Full only), `Assault Turret`; terminal groups with the same names |
| groups | `[SH] Thrusters`, `[SH] Weapons`, `[SH] Gyros`, `[SH] Bay Merges` |
| control | `[SH] Cockpit [Helm Seat]` (main cockpit), `[SH] Remote Control`, `[SH] Timer D (deadman)` (5 s, silent; toolbar thrusters + weapons Off), `[SH] EC Cockpit (configure in game)` (placed, unconfigured: event number never seen, test K4) |
| cockpit toolbar | 1 railgun, 2 assault cannon, 3 autocannon, 4 thrusters On/Off, 5 / 6 bay merge Off, 7 PD On/Off; Full page 2 / 3: Helm and Staff commands as PB Run with argument ([unverified] shape, test K3) |
| sensing | 8 cameras (front, rear, left, right, up, down, two wing); Full: 2 sensors, 4 decoys; antenna 5 km |

Shapes not seen in a sample (Full only; Lite has none): `SmallBlockSensor` (from source), turrets `SmallGatlingTurret`, `SmallBlockMediumCalibreTurret`, reactor `SmallBlockLargeGenerator`, `SmallDecoy`
(seen-type: same block type seen with another subtype), PB Run toolbar slots with arguments. Lite = no turrets (so no point defence), no sensor, no decoys, small generators.
Hydrogen: every large thruster touches the tank that feeds it (Back face on the tank-face centre). That pattern was seen on large-grid ships; a small-grid thruster on a 5x5x5 tank was **not** seen
(the owner's small ships use converters and tubes): if a thruster shows no fuel, put a `SmallBlockConveyorConverter` + tube between it and the tank.
Ammunition limit: no conveyor feed (port positions are not in `defs.json`, no seen layout reproduced), so the guns have only their own 3-6 rounds; documented in `DOCS`.

## 6. Paste and load

Delete the old blueprint folder in the game's Blueprints directory first (stale `.sbcB5`), copy `Corsair` (or `Corsair Wing`), paste in space (no gravity: 14 large hydrogen thrusters, no lift budget).
Nothing to load: tanks, batteries, magazines, uranium are in the file. Check the terminal that PBs show the scripts.

## 7. Known limits and in-game checks

0. Thrust-line offsets from the centre of mass (review, [calc], 0.5 m cells): forward thrust line is 0.5 m above the COM (0.9 MNm pitch moment at full burn); strafe 1.5 m ahead of it (1.4 MNm yaw); brake 4.5 m, lift and dive 6.5 m behind it (about 6.2 MNm pitch, lift pitches the nose down, dive up). The 20 small gyros give 8.96 MNm, so pure lift or dive uses about 70 % of gyro authority and the scripts must counter-rotate; a hand-flown ship will pitch when lifting. Not rebalanced: the pylon thrusters need the pylon tank.

1. C1 (drones merge at paste), T1 (which merge face clamps), C5 (launch burn vs wall), C10 (Helm ignores merged drone blocks).
2. Thruster fuel by direct tank contact on a small grid (5). Strafe thrusters protrude 1.5 m from the flank; they and the dive thruster stand in the pylon gap.
3. K3/K4: toolbar PB Run with argument; Event Controller cockpit event.
4. Guns run dry quickly (3-6 rounds), no feed.
5. Renders (`/tmp/.../ship_renders/Corsair/it0..it2`, `python3 -I tools/designs/corsair.py --render DIR`) use the repo thumbnail code: slopes look like stairs. Lite has no point defence.
6. Mass is a hull-factor outcome of voxel hollowing; 6,566 blocks, 10.6 k PCU (21 % of a 50 k world).

## 8. Flight and fight (M6, simulator only)

Nothing here was measured in the game. Commands as in `Shrike.md` section 12; the wing runs use `Corsair Wing` with `--commands "Helm:hunt;3:Staff:launch all;20:Staff:wing satellite;40:Staff:wing hunt"`.

**Scripts and data.** Helm `ShrikeAI.cs`, Staff `StaffAI.cs` (22,834 characters), bay `CradleBayAI.cs` (17,007), the same files as every other ship; no Corsair-specific script exists or is needed.
Custom Data after M6 (section 5): `[Link] Ship Tag=SH`/`Block Prefix=[SH]`, `[Manual] HUD Surface=0`, `[Battery] Standoff By Weapons=true` on the Helm; `[Staff]` keys and `[Wing] Types` on the Staff; the bay table on the `[Bay]` PB.
The Helm chooses the stand-off from its batteries: with two small railguns (reach 1,190 m) it keeps **1,000 m**, outside the 800 m reach of a turret ship, so the Pirate hardly touches it (9 blocks lost, median).
A fixed 500 m stand-off kills the Pirate about twice as fast (4 seeds: 99-252 s against 185-415 s) but the same ship died against the Tech Hunter after 92 s (1 seed; at 1,000 m it survives 600 s): the weapon-based stand-off stays.

**Flight** (scripted pilot at full stick, dampeners on):

| | design | sim |
|---|---|---|
| forward / other axes | 7.2 / 3.6 m/s2 (blocks only) | 7.2 / 3.6 m/s2 (speed cap 100 m/s reached after 14 s) |
| braking (retro thrusters) | 3.6 m/s2 | from 40 m/s: 10.9 s, 217 m; from 100 m/s: 27.6 s, 1,388 m |
| yaw | 8.96 MN m of gyro torque | 0.34 rad/s2: 63 % of the 1.5 rad/s stick cap after 2.8 s; a 180 degree turn about 6 s; rotation stops 1.7 s after release |

**Fights** (5 seeds, cap 600 s vs the Pirate, 300 s otherwise; Wing = launch at 3 s):

| enemy | Corsair | Corsair Wing |
|---|---|---|
| Pirate (2.8 km) | **9/10 wins** (seeds 0-4: 5/5, kill 185-415 s, median 293; seeds 5-9: 4/5, 268-387 s, 1 timeout at 600 s), 9 blocks lost (median of seeds 0-4) | **5/5**, 106-216 s (median 170): the two Hornets halve the time |
| 3 Hornets (1,000 m) | 5/5, 84-264 s (median 152), 439 blocks lost | 4/5, 70-164 s (median 83), 1 timeout |
| 6 Gnats (1,000 m / 400 m) | stalemate (5 timeouts) / 1/5, 4 timeouts | stalemate / 2/5, 3 timeouts |
| Tech Hunter (13,666 blocks, cap 300 s) | 2 seeds, no kill: lost 105-165 blocks, dealt 268-300 k damage; the Hunter keeps 74 of 76 armed blocks (13,354-13,491 of 13,666 blocks) | 2 seeds, no kill: lost 139-199 blocks (Hornets included), dealt 98-481 k; the Hunter is down to 38-44 of 76 armed blocks (12,420-13,378 blocks left) |

Against the Tech Hunter in 600 s (3 seeds, no wing): 0 wins, the Corsair stays at 1,000 m, deals 630-680 k damage and takes 40-90 k; the Hunter keeps 13,300 of 13,666 blocks. It does not die; it does not lose either.

**Manual, dead-man, wing, instructions.** `selftest m6` (2 seeds each): the Helm holds all 40 gyros, 14 thrusters, 6 fixed guns, 5 turrets, 4 batteries, the one seat, 8 cameras and the dead-man timer, the Staff the same lists (no drone block adopted);
manual flight with a Pirate in the field: 0 override-fight ticks, fixed guns quiet, the turrets keep shooting (enemy 53 -> 22 blocks while the pilot flies), AUTO back 1.5 s after standing up; Helm PB killed at 15 s: the Staff releases
every override 1.1 s later, once, `HELM LOST`, Timer D fires; `launch all` at 4 s while hunting: both bays let go by 29 s, the Helm is handed HUNT back, the roll call counts 2, the drones sit 100 m from the ship 18 s after `wing satellite`.
Without `launch`, `release all` in HUNT waited until the 60 s drop ("ship accelerates 8.5 m/s2"): the Helm never lets the ship settle (that is why `launch` exists). Instructions (IL model, Pirate fight with launch):
Helm 17,904, Staff 6,150, bay 15,741, Hornet 17,245; block model 1,142 / 346 / 446 / 919.

**Lite.** `Corsair Lite` has no turrets, no AI blocks and no sensor: 0/3 against the Pirate (it loses 1,500 to 3,200 blocks and never fires, the camera sweep does not find the target). Fly it yourself (`lock`) or take the Full ship.

## 9. Rotor turrets (v2.7, Full only)

The two `SmallGatlingTurret` PD Roof turrets on the pod roofs are PD2S rotor turrets (`docs/design/turrets.md`): stator on the base-centre cell of the old 5 x 5 x 5 footprint, 2 `SmallGatlingGun` each,
85 % of the move/fire bins free; the belly gatlings and the assault turret stay vanilla. +800 PCU, +2.7 t, `[TU] Turret PB` (`SmallProgrammableBlock`, centre corridor) running `TurretAI.cs`.
The turret grids are sub-grids of the blueprint (a top and a head grid each): `check_wing` counts only the merge-block drones. The Lite ship has none (small-grid stators never seen in a game file).

## AI blocks (added after the Ballista blindness report)
The Full Corsair had no AI Offensive / AI Flight blocks, so its Helm searched with cameras alone (sim, Tech Hunter held at 900 m: no target in 90 s). Full now carries `[SH] AI Offensive [AI]` and `[SH] AI Flight [AI]` in the avionics corridor (same blocks as the Warden; the Helm's `AI Block Search Assist` default is on, search interval 5 s). Sim: target held from the start at 900 m. Lite still has none. Not run in the game; Warden / Leviathan / Shrike already have the pair and find the Hunter at 900 m in the sim.
