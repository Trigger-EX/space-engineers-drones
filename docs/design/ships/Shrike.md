# Shrike (fighter, small grid)

Key `ship_shrike`. Module `tools/designs/shrike.py` (`MODELS = {"Shrike": fn(lite)}`, `DOCS`, `SCRIPTS = ["ShrikeAI.cs"]`), tests `tools/tests/test_shrike.py`.
Blueprints `Shrike` (Full) and `Shrike Lite`. Part of the four-ship family (fighter Shrike, corvette Corsair, frigate Warden, dreadnought Leviathan) of
`docs/design/systems_catalogue.md`; controls follow `docs/design/control_arch.md` (single-PB fighter, milestone M1: the ship the one-PB manual mode is tested on).
Evidence labels as in the other design notes: [data] repo game data / owner's blueprints, [calc] computed here, [rec] recollection, [unverified] never seen in a game file.

## 1. In one paragraph

A 17 m delta-wing dart: chisel nose carrying a spike (assault cannon) and two autocannons in a stepped "trident", cyan fighter canopy, two cheek gatlings,
a dorsal engine hump behind the canopy, a one-cell-thick delta wing on the deck plane with orange leading edges, two cowled engine pods through the wing
(main thruster aft, retro thruster forward, strafe thruster at the tip) and twin swept tail fins. Charcoal gunmetal hull, orange accents, glowing-orange
thruster blocks. One pilot (`DBSmallBlockFighterCockpit`), one Remote Control, **one Programmable Block** `[Helm] Shrike`, 8 large hydrogen thrusters on 3 full
tanks, 6 gyros, 5 fixed forward guns with magazines pre-loaded, no drone bay. Hand-flyable (cockpit toolbar 1-3 = guns) and scriptable.

## 2. Numbers (both variants build, validate, reload; `python3 -I tools/build_all.py --only Shrike --no-zip` PASS)

| | Shrike (Full) | Shrike Lite |
|---|---|---|
| blocks | 1,277 | 1,272 |
| dimensions (W x H x L) | 15.5 x 7.0 x 17.5 m (31 x 14 x 35 cells) | same |
| mass, blocks only (`_common.twr`) / loaded (+ magazines 1,790 kg incl. spare, uranium; hydrogen has no mass [rec]) | 59,277 kg / 61,067 kg | 58,703 kg / 60,493 kg |
| acceleration F / B / U / D / L / R, blocks only (`twr`) | 16.2 / 16.2 / 8.1 / 8.1 / 8.1 / 8.1 m/s2 | 16.4 / 16.4 / 8.2 / 8.2 / 8.2 / 8.2 |
| same, loaded | 15.7 / 15.7 / 7.9 / 7.9 / 7.9 / 7.9 | 15.9 / 15.9 / 7.9 / 7.9 / 7.9 / 7.9 |
| PCU (sum of block PCU, `defs_stats.json`) | 2,839 | 2,754 |
| HP (sum of component integrity) | 273 k (armour 34.2 t of the 59.3 t, 12.6 t of it heavy) | 271 k |
| centre of mass (loaded) | (0.25, 2.08, 9.03) m in grid coordinates, i.e. on the symmetry plane, 9.0 m behind the nose-tip cell edge (cell-edge coordinates x 0.50 / y 4.16 / z 18.06) | same |

Catalogue targets (4.1/4.2): 66 t, 16.1 m/s2 forward, 8.8 other axes, 3.3 k PCU, 5.3 k DPS. Hit: forward 15.7 loaded (-2 %), lateral / vertical 7.9
(-2 % of 8, -10 % of 8.8), mass 61 t (-7 %), PCU 2.8 k, DPS 5,267 (data). Brake (backward) is 15.7, twice the 8.8 of the catalogue because the retro
thrusters come in a symmetric pair (one per pod) and the pod tank feeds them: kept, it makes the dart a good "stop and turn" fighter.

## 3. Layout map

Coordinates: sebp cells of 0.5 m, `x` = Right, `y` = Up, `z` = Backward (the bow points to -z); the ship is mirror-symmetric about the middle of cell 0 (x -> -x),
block for block (the right half is the master, `mirror_hull()`; test `test_hull_is_mirror_symmetric_block_for_block`). Printed by `python3 -I tools/designs/shrike.py --plans`.
Column digits are |x| mod 10 (x = 0 is the centre pair of digits; rows of the plans are z). Legend: `#` armour, `w` window, `p` panel, `C` cockpit, `G` gun, `M` ammo container, `K` hydrogen tank, `T` thruster, `B` battery, `R` reactor, `y` gyro,
`P` PB, `r` remote control, `t` timer, `e` event controller, `i` AI block, `c` camera, `s` sensor, `a` antenna, `d` decoy.

```
SIDE x=0 (columns z, rows y)
     0         1         2         3    
     01234567890123456789012345678901234
y 10 .........................a.........
y  9 .................TTT.....ac........
y  8 ..........CCCCCC.TTT#########......
y  7 ..........CCCCCC.TTT.......###.....
y  6 ..........CCCCCCKKKKK......#.##....
y  5 GGGGGGGGG#CCCCCCKKKKK......#..#....
y  4 ...###MMM#######KKKKK.y..y.#..####.
y  3 ..c#..MMM..#....KKKKK.BBBBBB.....#c
y  2 ...###MMM..#Pr..KKKKK.BBBBBB..####.
y  1 .......##########TTT###########....
y  0 .........s..c....TTT...............
y -1 .................TTT...............
```
Deck plan, upper deck / wing plane (y = 5; the wing is one cell thick at y 4-5; guns, canopy, tank tops, hump):
```
PLAN y=5 (columns x, rows z)
     5432109876543210123456789012345
z  0 ...............G...............
z  1 ...............G...............
z  2 ...............G...............
z  3 ...............G...............
z  4 ...............G...............
z  5 ...............G...............
z  6 ...............G...............
z  7 .............##G##.............
z  8 .............##G##.............
z  9 .............#####.............
z 10 .............#CCC#.............
z 11 .............#CCC#.............
z 12 ...........#.#CCC#.#...........
z 13 ..........##.#CCC#.##..........
z 14 .........###.#CCC#.###.........
z 15 .....#######.#CCC#.#######.....
z 16 ....#KKKKK##.KKKKK.##KKKKK#....
z 17 ....#KKKKK##.KKKKK.##KKKKK#....
z 18 ....#KKKKK##.KKKKK.##KKKKK#....
z 19 ....#KKKKK##.KKKKK.##KKKKK#....
z 20 ...##KKKKK##.KKKKK.##KKKKK##...
z 21 ..##########.##.##.##########..
z 22 .###########.##.##.###########.
z 23 ############.##.##.############
z 24 ############.##.##.############
z 25 ############.##.##.############
z 26 ############.##.##.############
z 27 ..##########.#####.##########..
z 28 ......#########.#########......
z 29 ..........#####.#####..........
z 30 ............#######............
z 31 ...............................
z 32 ...............................
z 33 ...............................
z 34 ...............................
```
Lower deck (y = 2: nose container, avionics bay z 12-14, tanks, power bay z 22-29):
```
PLAN y=2 (columns x, rows z)
     5432109876543210123456789012345
z  0 ...............................
z  1 ...............................
z  2 ...............................
z  3 .............#####.............
z  4 .............#####.............
z  5 .............#####.............
z  6 .............#MMM#.............
z  7 ............##MMM##............
z  8 ............##MMM##............
z  9 ............##...##............
z 10 ............##...##............
z 11 ............#######............
z 12 ............##tPe##............
z 13 ......TTT...##.r.##...TTT......
z 14 ......TTT...##i.i##...TTT......
z 15 .....#TTT#..##...##..#TTT#.....
z 16 ....#KKKKK#.#KKKKK#.#KKKKK#....
z 17 ..TTTKKKKK#.#KKKKK#.#KKKKKTTT..
z 18 ..TTTKKKKK#.#KKKKK#.#KKKKKTTT..
z 19 ..TTTKKKKK#.#KKKKK#.#KKKKKTTT..
z 20 ....#KKKKK#.#KKKKK#.#KKKKK#....
z 21 .....#TTT#..##...##..#TTT#.....
z 22 ......TTT...##BBB##...TTT......
z 23 ......TTT...##BBB##...TTT......
z 24 ............##BBB##............
z 25 ............##BBB##............
z 26 ............##BBB##............
z 27 ............##BBB##............
z 28 ............##...##............
z 29 ............#.R.R.#............
z 30 ............#######............
z 31 .............#####.............
z 32 .............#####.............
z 33 .............#####.............
z 34 ...............................
```
Section through the cockpit (z = 12) and through the engine pods and core (z = 18):
```
SECTION z=12 (columns x, rows y)
     5432109876543210123456789012345
y 11 ...............................
y 10 ...............................
y  9 ...............................
y  8 ..............CCC..............
y  7 ..............CCC..............
y  6 .............GCCCG.............
y  5 ...........#.#CCC#.#...........
y  4 .........###w#####w###.........
y  3 ............#.....#............
y  2 ............##tPe##............
y  1 .............#####.............
y  0 ...............c...............
y -1 ...............................
y -2 ...............................

SECTION z=18 (columns x, rows y)
     5432109876543210123456789012345
y 11 ...............................
y 10 ...............................
y  9 ..............TTT..............
y  8 ..............TTT..............
y  7 ..............TTT..............
y  6 .....#####...KKKKK...#####.....
y  5 ....#KKKKK##.KKKKK.##KKKKK#....
y  4 ..TTTKKKKK###KKKKK###KKKKKTTT..
y  3 ..TTTKKKKK#.#KKKKK#.#KKKKKTTT..
y  2 ..TTTKKKKK#.#KKKKK#.#KKKKKTTT..
y  1 ....#KKKKK#..#TTT#..#KKKKK#....
y  0 .....#####....TTT....#####.....
y -1 ..............TTT..............
y -2 ...............................

```

| zone (z, cells) | what is there |
|---|---|
| 0-8 nose | orange chisel nose (heavy armour). Assault cannon on the centre line (y 5, z 0-8, the long barrel sticks 3 cells out of the nose), two autocannons low on the flanks (x +-2, y 3, z 2-6, muzzles 1 cell proud of the nose), **medium cargo container** (x -1..1, y 2-4, z 6-8) between them under the assault cannon: the guns' rear cells touch the container (pattern of the owner's Pod Chaser, see 7.3), front camera between the autocannons |
| 9-16 cockpit | `DBSmallBlockFighterCockpit` (x -1..1, y 5-8, z 10-15) in a plate-coloured armour frame, two window rows on the flanks (y 4, z 12-16), cheek gatlings at x +-2, y 6, z 10-13 beside the canopy; avionics bay below (y 2, z 12-14: timer, PB, event controller, remote control, AI blocks); belly sensor (0, 0, 9) and down camera (0, 0, 12) |
| 15-21 centre | **core tank** (x -2..2, y 2-6, z 16-20) with the **dive** thruster on top (exhaust up, y 7-9) and the **lift** thruster below (exhaust down, y -1..1): the dorsal hump behind the canopy and a flush belly nozzle |
| wing, x 4..15 | one-cell delta (leading edge swept 47 degrees from z 11 at the root to z 24 at the tip), two cells thick at the root, orange leading edge, orange wing tips, an orange cross stripe per side; small canards at x 4-8, z 8-13; decoys (Full) on the tips |
| pods, x +-6..+-10 | tank (5 x 5 x 5, y 1-5, z 16-20) in a stepped cowl that passes through the wing and rises one cell above it; **main** thruster aft (z 21-23, exhaust to z 27), **retro** thruster forward (z 13-15, exhaust forward), **strafe** thruster at the tip (x +-11..13, exhaust sideways); orange nozzles |
| 22-29 power bay | two 1 MWh batteries (x -1..1, y 2-3, z 22-27) with the six gyros on top of them (y 4, z 22 and 25), two reactors (y 2, z 29), antenna and up camera on the spine, seven cameras all round |
| 26-33 tail | swept spine, twin fins on the wing roots (x +-4, y 6-12, orange tips) with left / right cameras on their outer faces, rear camera at the tail end |

Mass distribution: heavy 12.6 t (nose z < 8, belly z 3..17 on the fuselage, cockpit frame), light shell 20.9 t, thrusters 9.8 t, tanks 4.7 t, gyros 3.7 t,
batteries 2.1 t, cockpit 1.3 t, weapons 1.5 t, ammunition 1.8 t. Hull = hollow, 1-cell skin + bulkheads every 8 cells (z 11, 19, 27) for mass and for
one connected piece; the wing and fins are single plates.

## 4. Style

Family livery (shared with Corsair / Warden / Leviathan): charcoal gunmetal hull `hsv(214, 0.14, 0.30)`, lighter plate `hsv(214, 0.10, 0.44)` around the cockpit
and the wing roots, dark steel belly, orange `hsv(24, 0.92, 0.96)` for the nose, the wing leading edges, wing tips, fin tips, spine stripe, pod rings and the
tail glow, burnt orange `hsv(20, 0.85, 0.70)` for the thruster blocks, cyan for glass (cockpit canopy, `SmallWindow1x1Flat` rows). Shape language: delta wing + twin
fins + nacelle engines; chisel nose, stepped decks (deck, dorsal hump, spine), recessed hardpoints, no loose boxes (every pod hangs on the wing and the core
tank; `validate()` proves one attached piece). Windows exist only where non-DLC window blocks do. Iterated 16 times with the repo's thumbnail code (hull style notes
below); renders: `python3 -I tools/designs/shrike.py --render DIR` writes `Shrike_top.png` / `_under.png` (four yaw views from above / below), `_nose`, `_pod`,
`_podunder`, `_tail` (zoomed) and `Shrike_ortho.png` (top / side / front silhouettes). Thumbnails draw every block as a full cube, slopes and panels look blocky there.

Iteration log (what the renders changed): v1 four-arm thruster crosses on the wings looked like a quadcopter -> v3 pods under the wing, vertical thrusters moved to a
core tank in the fuselage; v4-v6 first full systems, tail fins redone (the sheared fin staircased, now a swept plate on the wing root), stripes, windows; v7-v9 pod cowls
(bare tanks looked like boxes on a stick), COM tuning of the pods (PZ 22 -> 18) so the strafe and core thrusters sit on the COM; v10-v13 the cowl rises through the
wing (green tank tops showed in the wing), canards; v14-v16 the cowl steps toward the nozzles, panels, exact mirror symmetry (`mirror_hull`), cameras that really face out.

## 5. Systems, names and the tags the scripts rely on

Every functional block is named `[SH] <role>` (the host-ship prefix of control_arch 3.6 / grids_bays 6.3: Helm collects thrusters, gyros and weapons by prefix AND grid, so a
merged drone, if the owner ever clamps one on, is not adopted); the PB is `[Helm] Shrike` (S6: PBs are addressed by their `[Helm]` prefix).

| block (subtype) | n | name | cells (min) / notes |
|---|---|---|---|
| `SmallProgrammableBlock` | 1 | `[Helm] Shrike` | (0, 2, 12), `<Program>` = minified `scripts/ShrikeAI.cs` **if the file exists at build time**, otherwise an empty program (`_script()`); Custom Data: `[Battery 1] Tag=B1 Class=AUTO Role=PRIMARY_CANDIDATE`, `[Battery 2] Tag=B2 Class=AUTO Role=SECONDARY`, `[Cockpit] Helm Surface=0` (control_arch 3.2 / 2.8) |
| `SmallBlockRemoteControl` | 1 | `[SH] Remote Control` | (0, 2, 13), forward = bow: the script's reference controller (the sim knows only RCs) |
| `DBSmallBlockFighterCockpit` | 1 | `[SH] Cockpit [Helm Seat]` | (-1, 5, 10); `IsMainCockpit` true; toolbar: 1 assault cannon, 2 autocannon, 3 gatling (weapon items as in the owner's fighters), 4 `[SH] Thrusters` On/Off; Full also slots 10-17 = PB `Run` with `auto`, `manual`, `assist hud`, `hunt`, `rtb`, `lock`, `next`, `unlock` ([unverified], the argument form was never saved by a game; bp.notes flags them) |
| `SmallBlockGyro` | 6 | `[SH] Gyro 1..6` | on top of the two batteries (y 4, z 22 / 25), group `[SH] Gyros`; 6 x 448 kNm |
| `SmallBlockLargeHydrogenThrust` | 8 | `[SH] Thrust Main R/L`, `Retro R/L`, `Strafe R/L`, `Dive`, `Lift` | group `[SH] Thrusters`; see 6 |
| `SmallHydrogenTank` | 3 | `[SH] Hydrogen Tank R / L / Core` | 500,000 L each, `FilledRatio` 1 |
| `SmallBlockBatteryBlock` | 2 | `[SH] Battery 1, 2` | 1 MWh each, charged (`CurrentStoredPower` 1) |
| `SmallBlockSmallGenerator` | 2 | `[SH] Reactor L, R` | 0.5 MW each, 5 kg uranium each (= 5 MWh) |
| `SmallGatlingGun` (subtype empty) | 2 | `[SH] [B1] Gatling L, R` | (+-2, 6, 10); 4 x `NATO_25x184mm` (560 rounds) each |
| `SmallBlockAutocannon` | 2 | `[SH] [B1] Autocannon L, R` | (+-2, 3, 2); 3 x `AutocannonClip` (48 rounds) each |
| `SmallBlockMediumCalibreGun` | 1 | `[SH] [B2] Assault Cannon` | (0, 5, 0); 3 x `MediumCalibreAmmo` |
| `SmallBlockMediumContainer` | 1 | `[SH] Ammo Reserve` | (-1, 2, 6); 12 x `AutocannonClip` + 10 x `MediumCalibreAmmo` spare (1.1 t) |
| `SmallBlockRadioAntenna` | 1 | `[SH] Antenna` | on the spine (0, 9, 25), 5,000 m, ship name shown |
| `SmallCameraBlock` | 7 | `[SH] Camera Front / Rear / Left / Right / Up / Down / Front Top` | front between the autocannons, **front top (M6) on the nose ridge just ahead of the canopy (cell 0, 6, 9)**, rear at the tail, left / right on the fin faces, up on the spine, down under the cockpit; each with 3 free view cells (`keep_clear`). The second forward camera is there because ShrikeAI confirms what the AI blocks see with the forward cameras (one ray every 1.3 s per camera) and the nose camera is shot off first: with one forward camera the Shrike lost 4 of 5 duels against a single Hornet in the sim, with two 2 of 5 |
| `TimerBlockSmall` | 1 | `[SH] Timer D (deadman)` | 5 s, silent, **not counting**; toolbar: `OnOff_Off` on groups `[SH] Thrusters` and `[SH] Weapons` (control_arch 2.6: Helm re-arms it every second in AUTO and stops it in MANUAL; it only fires if Helm dies in AUTO) |
| `EventControllerSmall` | 1 | `[SH] EC Cockpit (configure in game)` | placed and **unconfigured**: the "cockpit occupied" event id was never seen in a file (test K4); in game set Cockpit occupied -> Timer D `Stop`, empty -> `Start` |
| Full only: `SmallBlockSensor`, `SmallOffensiveCombat`, `SmallFlightMovement`, 2 x `SmallDecoy` | | `[SH] Sensor`, `[SH] AI Offensive [AI]`, `[SH] AI Flight [AI]`, `[SH] Decoy L/R` | sensor under the nose, AI blocks in the avionics bay (Hornet naming `[AI]`; AI blocks must be set up by hand), decoys on the wing tips; their XML shapes are not seen ones (sensor from source, AI blocks guessed, decoy derived from `LargeDecoy`), hence Full only |
| groups | | `[B1]`, `[B2]` (battery tags, the same text as the terminal groups), `[SH] Weapons`, `[SH] Thrusters`, `[SH] Gyros` | `<BlockGroups>` written with each block's centre cell |

Weapon batteries (control_arch 3.2): **B1 = BALLISTIC** (2 autocannons + 2 gatlings, all 800 m/s, 800 m), **B2 = SHELL** (assault cannon, 500 m/s, 1,400 m): the
"2 batteries: gatling + one slow class" of the single-PB fighter. All five guns are fixed forward, parallel to the nose ("boresighted"), so the battery windows
share the primary's aim. All guns are within 1 m of the centre line; muzzles: assault cannon z 0, autocannons z 2 (1 m behind it), gatlings z 10 (5 m behind it).

## 6. Propulsion, fuel, power

| axis (push direction) | thrusters (exhaust direction) | thrust | accel loaded |
|---|---|---|---|
| Forward | `Main R`, `Main L` (exhaust aft) | 960 kN | 15.7 m/s2 |
| Backward | `Retro R`, `Retro L` (exhaust forward) | 960 kN | 15.7 |
| Up | `Lift` (exhaust down, centre line) | 480 kN | 7.9 |
| Down | `Dive` (exhaust up, centre line) | 480 kN | 7.9 |
| Left | `Strafe R` (exhaust right, right pod tip) | 480 kN | 7.9 |
| Right | `Strafe L` | 480 kN | 7.9 |

Parasitic torque of a full burn about the loaded COM [calc: sum of r x F]: forward / back 318 kNm (12 % of the six gyros' 2,688 kNm), strafe 190 kNm (7 %), lift / dive
105 kNm (4 %). Rotational inertia (point masses, 0.5 m cells) pitch 0.87, yaw 1.25, roll 0.51 x 10^6 kg*m2 -> gyro angular acceleration pitch 177, yaw 123, roll 302
deg/s2 [calc] (the catalogue guessed 115 deg/s2).

**Fuel network, what is built and what is not known.** The hydrogen thrusters are fed by direct contact: every thruster's Back (mount) face lies on the centre of a 5 x 5
tank face (`test_every_thruster_touches_a_hydrogen_tank`), the pattern of Red Ship / Starburst / Venture, where a `LargeBlockLargeHydrogenThrust` touches a `LargeHydrogenTank`
face by face [data: contact analysis over the 36 owner samples and Tech Hunter]. **Never seen: a small-grid thruster on a 5 x 5 x 5 `SmallHydrogenTank` face** (the owner's small-grid ships feed
their thrusters through `SmallBlockConveyorConverter` + duct tubes, which would need port positions that are not in `defs.json`). So: three independent systems,
each tank feeds only its own thrusters (right pod: Main R, Retro R, Strafe R; left pod likewise; core: Lift, Dive); there is no conveyor between them. If a thruster shows no
hydrogen in game, put a `SmallBlockConveyorConverter` + `ConveyorTubeDuctSmall` between it and the tank (the Pod Chaser / `temp` layout) and note which face; nothing else changes.

Fuel [calc, 642.7 L/s per MW of listed power, 0.6 MW per thruster -> 385.6 L/s at full thrust]: pod tank at 25 % main + 10 % retro + 10 % strafe = 174 L/s -> **48 min**;
core tank (lift + dive 10 %) 77 L/s -> 108 min; a main thruster at full burn empties its pod tank in 21.6 min. Total 1.5 ML (catalogue: 1.0 ML for 30 min at 25 % / 10 %: met three times
over, at the price of 1.6 t for the third tank, which exists to give the vertical axes a tank on the centre line).

Power: load about 0.05 MW (6 gyros 12 kW, RC 10 kW, 2 AI blocks 20 kW, cameras, sensor, thrusters ~0 for hydrogen) against 1.0 MW of reactors (2 x 0.5 MW) and 2 MWh / 8 MW of
batteries; no railgun, so no recharge draw: the batteries alone run it for ~40 h, the 5 kg of uranium per reactor for ~10 h at full output. There is nothing for the reactors to feed
but the batteries; they are there so a long patrol does not drain the 2 MWh.

## 7. Ammunition

### 7.1 What is loaded (capacity checks done by `bp.fill_ammo`, `max_items` = 0 for every gun in `test_weapons_and_their_ammunition`)

| gun | magazines in the gun | rounds | data DPS | firing time at the data rate |
|---|---|---|---|---|
| 2 x `SmallGatlingGun` | 4 x `NATO_25x184mm` | 560 each | 1,050 each (x 0.857 in game, audit W-02) | 48 s each |
| 2 x `SmallBlockAutocannon` | 3 x `AutocannonClip` | 48 each | 1,250 each | 19 s each |
| `SmallBlockMediumCalibreGun` | 3 x `MediumCalibreAmmo` | 3 shells (4,000 pool each) | 667 sustained, alpha 4,000 | 3 shots (reload 6 s) |

Sustained data DPS 5,267 (catalogue 5,267); reach 800 m (B1), 1,400 m (B2).

### 7.2 The limitation

The autocannons (3 clips) and the assault cannon (3 shells) cannot hold more than that in their own inventories: 19 s of autocannon fire and three assault shots. The spare magazines
(12 clips = 192 rounds, 10 shells) are in the nose container. This is the same limit as the catalogue 3.3 ("inventory alone cannot sustain fire from autocannons or assault cannons").

### 7.3 The container, the feed, and why it is stated as untested

The container touches both autocannons (their rear cell, side to side) and the assault cannon (its rear three cells on the container top). That is the Pod Chaser layout [data]: its
`SmallBlockMediumContainer` sits between two assault cannons touching their rear sides. Whether touching blocks feed each other (conveyor ports meeting) in the game was **not verified
here**; no conveyor tubes were added because no layout of a sample with a verified feed could be reproduced with confidence (a feed from a container needs the guns' and the container's
port faces to meet, and port positions are not in `defs.json`). README sentence in `DOCS['Shrike']`: the guns hold 3 magazines, the container touches them as in Pod Chaser, feeding is untested.
If the feed does not work the 1.1 t of spare magazines is just reload stock: take them from the container by hand (the pilot's inventory menu) when the guns run dry.

## 8. Lite and Full

`Shrike Lite` = the same hull, systems and names with only block types whose XML shape was seen in a game-written file (`bp.shape_summary()`: all `seen:`, test
`test_lite_has_only_seen_shapes`): **no** sensor, no AI Offensive / AI Flight, no decoys, no PB `Run` toolbar slots. Full adds those (sensor `from-source`, AI blocks `guess`,
decoys `seen-type`, PB Run slots [unverified]). Everything else, including the dead-man timer, the event controller (empty), groups and the cockpit weapon slots, is seen in both.

## 9. How to paste and what to load

1. Delete any old `Shrike` / `Shrike Lite` folders in the game's blueprint folder, then copy the new ones (stale `.sbcB5` cache).
2. In the Blueprints screen paste in creative (`Ctrl+V` or the projector); the blueprint stores its inventories (guns, container, reactors, tanks, batteries): nothing to load by hand. In a
   survival game the welded ship is empty: fill the tanks, reactors (uranium), the guns and the container.
3. Terminal: check the grid is **Shrike** (small), ownership, names `[SH] ...`; paste `ShrikeAI.cs` into `[Helm] Shrike` only if the blueprint was built before the script existed
   (rebuild with `tools/build_all.py` once `scripts/ShrikeAI.cs` is in the repo and the PB carries it), Recompile, `Run` with `manual` or `hunt`.
4. Power the batteries / reactors, turn the antenna on, set `IsMainCockpit` if another cockpit was added.

## 10. Known limits and the in-game checks to do (highest risk first)

1. **Fuel contact** (6): open the terminal on each of the 8 thrusters: the info panel must show hydrogen supply; fly a burn on each axis and watch the tank percentages fall (right pod, left pod, core).
2. **Container feed** (7.3): fire the autocannons dry, watch whether `Ammo Reserve` loses clips and the guns refill.
3. Cockpit view: the `DBSmallBlockFighterCockpit` canopy sits at x -1..1; the gatlings at x +-2 are beside the canopy, check they do not block the pilot's view.
4. Thrust torque: at full forward burn the nose-up torque is 12 % of the gyros [calc]; confirm there is no drift when the pilot lets go.
5. PB `Run` toolbar slots (Full): K3 of the test list in control_arch 7 decides whether the argument form saves; the timer's `OnOff_Off` group items are seen shapes.
6. Event controller: set the cockpit event by hand (K4).
7. Window blocks (10 `SmallWindow1x1Flat`): the glass position inside the cell is unverified (hull_builder.md 9).
8. The thumbnails draw full cubes; the real hull is smoother (Slope2 pairs); judge the look in game before changing the shape.
9. The sim has no hydrogen model and no cockpit: for sim runs the RC and the PB are what matters (acceleration is the `twr` number; hydrogen is free there).
10. As the nose magazines are spent the COM moves 0.16 m aft (z 18.06 -> 18.38 cells in edge coordinates, loaded -> empty); the strafe and core thrusters stay within 0.3 m of the COM either way.

## 11. Decisions (so the next change knows what it trades)

| decision | why | alternative and its price |
|---|---|---|
| 3 tanks, 8 large thrusters, direct tank contact | the only fuel-feed pattern seen on the owner's working ships without conveyor ports | catalogue's 13 thrusters (large + small per axis) need small thrusters fed by a conveyor |
| pods under the wing, vertical thrusters on a core tank in the fuselage | symmetric thrust with one vertical thruster per direction (8 m/s2 instead of 16) and no outrigger arms | pod-only crosses: vertical axes 16 m/s2, but 2 more large thrusters (+2.4 t) minus one tank (-1.6 t) and a quadcopter look |
| wing and fins one cell thick | a one-cell plate is the lightest valid hull piece (20 kg per cell); 2-thick wings add 4 t | thicker: stronger, F -1.5 m/s2 |
| cowl of light armour around the pod tanks | hides the 5 x 5 x 5 tank blocks; 4.8 t for both pods | bare tanks: -4.8 t (F 17.1 instead of 15.7 m/s2) and boxes on the wing |
| `mirror_hull()` | the builder picks Slope2 pairs by scan order, so a symmetric voxel hull came out with different slopes left and right | none, it is 40 lines |
| container between the guns | one reproducible layout from the samples | no reserve: -1.1 t |
| EC left unconfigured | event id unseen | wiring a guess would trigger the dead-man on the wrong event |

Mass trade: each tonne costs 0.26 m/s2 of forward acceleration (960 kN / 61 t); the armour budget (34 t) is the slack, `Zones` in `build_hull()` (heavy: nose, belly, cockpit frame) is the lever.

## 12. Flight and fight (M6, simulator only)

Nothing here was measured in the game. Commands are `SimHost.exe one --scenario ship/<Blueprint>-vs-<Enemy> --seed-index 0..4 --commands "Helm:hunt"` (warship scenarios start the PBs with the Custom Data the
blueprint carries; `selftest m6` is the automatic part). Drone enemies start 1,000 m away (`--start-distance 1000:1000`): the sim's Hornets and Gnats do not look farther, the Pirate (the sim's stand-in large ship,
260 blocks, turrets) starts at the default 2.8 km.

**Script and data.** `ShrikeAI.cs` is the only PB script: 79,990 of its 80,000 minified characters (the M6 changes cost 340: the sliced integrity scan, `Seat Exclude`). Custom Data after M6:
`[Manual] HUD Surface=0` and `[Battery] Standoff By Weapons=true`; the old `[Battery 1..2]` and `[Cockpit]` sections were read by no script and are gone. Second forward camera `Front Top` (section 5).

**Flight** (pilot at full stick in the seat, dampeners on; `meas.py`-style runs of the scripted pilot, 1 seed, the sim is deterministic here):

| | design | sim |
|---|---|---|
| forward acceleration | 16.2 m/s2 blocks only (15.7 loaded) | 16.2 m/s2 (80.9 m/s after 5 s; the sim caps speed at 100 m/s) |
| braking, dampeners, stick released | 15.7 m/s2 | from 40 m/s: 2.5 s, 50 m; from 100 m/s: 6.2 s, 308 m |
| yaw | gyro torque / inertia in the design record | reaches the sim's 1.5 rad/s stick-rate cap within 1 s (at least 1.5 rad/s2); rotation stops 1.2 s after the stick is released |

**Fights** (5 seeds each, cap 300 s):

| enemy | result | note |
|---|---|---|
| Pirate (2.8 km) | **8/10 wins** (seeds 0-4: 4/5, median kill 195 s, 99-296; seeds 5-9: 4/5, median 112 s, 98-200), 2 timeouts | 49 blocks lost (median); the Shrike shoots and the Pirate's turrets rarely reach a 17 m ship |
| 1 Hornet (1,000 m) | **2/5 wins** (3 losses) | with one forward camera 1/5, with the AI search off 0/5: the Shrike acquires a small target late (packages/unknowns.md) |
| 3 Hornets (1,000 m) | 0/5 | a single fighter loses to three (the old M3 result for every single-fighter hull): the Shrike fires under 10 shots |
| 6 Gnats (1,000 m / 400 m) | stalemate (5 timeouts) / 0/5 wins, 1 loss, 4 timeouts | the sim's Gnats do not close on a ship that does not find them |

**Manual, dead-man, instructions.** `selftest m6`: the Helm holds all 6 gyros, 8 thrusters and 5 guns, the one seat and the one dead-man timer; seat taken: MANUAL within 0.4 s, 0 override-fight ticks, fixed guns quiet, the cockpit
screen 0 says `MODE MANUAL ASSIST-HUD` with the lead pip, 1.5 s after standing up the Helm is back in HUNT and flies again. Helm PB killed at 15 s: Timer D (5 s) fires once and the thrusters are off (2/2 seeds; with one PB there is no
Staff to release the overrides: the Timer is the whole dead-man). Instructions (`--count-instructions`, vs the Pirate, 120 s): block model peak 1,629, **IL model peak 25,009** of the 50,000 (before the integrity scan was sliced:
71,079, i.e. a "Script too complex" if the game counts like the IL model: the scan read all 1,277 blocks in one tick).

**Lite.** `Shrike Lite` has no AI blocks and no sensor, so on its own it only sweeps with the cameras: 0/3 against the Pirate in 600 s without a shot fired (3 seeds). Use it with a pilot (`lock` gives the Helm a target) or take the Full ship.
