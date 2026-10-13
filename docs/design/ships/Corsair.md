# Corsair (corvette, small grid)

Key `ship_corsair`. Module `tools/designs/corsair.py` (`MODELS = {"Corsair": fn(lite), "Corsair Wing": fn(lite)}`, `DOCS`, `SCRIPTS = ["CorsairHelmAI.cs", "CorsairStaffAI.cs"]`;
the scripts are written by other workers, the PBs carry an empty program until `scripts/<name>` exists), tests `tools/tests/test_corsair.py`.
Blueprints `Corsair`, `Corsair Lite`, `Corsair Wing`, `Corsair Wing Lite`. Part of the family Shrike / Corsair / Warden / Leviathan (`systems_catalogue.md` 4.3, `control_arch.md`, `grids_bays.md` 5.3).
Evidence labels as elsewhere: [data] repo data, [calc] computed here, [unverified] never seen in a game file.

## 1. In one paragraph

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
| 33-47 | lower deck on the centre line: reactor (z 33-35), four batteries (z 36-47), 20 gyros on the battery tops (y 4) |
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
| PBs | `[Helm] Corsair` (Custom Data: `[Ship]`, `[Battery 1..3]`, `[Cockpit]`), `[Staff] Corsair` (`[Staff] Ship Tag/Block Prefix`, `[Wing]`), `[Bay] Corsair` (M5: `[Pod] Tag`, `[Bay 1]`, `[Bay 2]`: the bay table moved here from the Staff data); scripts `CorsairHelmAI.cs`, `CorsairStaffAI.cs` if present, `CradleBayAI.cs` in the Bay PB |
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
