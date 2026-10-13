# Leviathan (`tools/designs/leviathan.py`)

Key `leviathan`. The large-grid dreadnought of the four-ship family (fighter Shrike, corvette Corsair, frigate Warden, dreadnought Leviathan): a massive stepped wedge with an armoured dorsal citadel,
a forward main-battery spine of six railguns, eight artillery, twelve assault, twenty gatling and four rocket turrets, ten large manoeuvring thrusters, two flank hangars with a docked, empty 4-slot
drone pod each, and a wide engine block. Models `Leviathan` and `Leviathan Lite` (ship + two docked `Cradle Pod`s = three CubeGrids). Targets come from `docs/design/systems_catalogue.md`
section 4.5; the hull is built with `tools/hulls.py`; control naming from `docs/design/control_arch.md`; bays from `docs/design/grids_bays.md`; the pod builder is `tools/designs/pods.py`.
Nothing of this has been pasted in the game yet (section 12 lists what to look at first).

## 1. Numbers against the catalogue

| | catalogue 4.5 | Leviathan (Full) | note |
|---|---|---|---|
| size | 200 x 60 x 35 m (80 x 24 x 14 cells) | **222.5 x 72.5 x 42.5 m** (89 x 29 x 17 cells; hull 57.5 m wide, 72.5 m over the flank nacelles; 42.5 m from the ram keel to the top of the bridge) | +11 % / +21 % / +21 %: nacelles, ram keel, tower |
| blocks | 9,044 armour + systems | **8,374 blocks** in the ship grid (3,327 heavy + 4,458 light armour blocks, 20 windows, 73 panels) + 605 in each pod | Lite 8,359 |
| mass | 13.2 kt | **13,936 t blocks + 164 t ammunition and uranium = 14,100 t** (+7 %) | armour 12.0 kt = 86 % of the blocks (catalogue 83 %) |
| forward accel | 2.0 m/s2 (27 MN, 3 large + 5 small) | **2.04 m/s2** (28.8 MN = 4 large hydrogen engines; 2.07 empty) | four engines in a row at the stern |
| other axes | 1.1 m/s2 (2 large per axis) | **1.02 m/s2** (14.4 MN = 2 large per axis; -7 %) | 14 large thrusters in all (catalogue 18 mixed) |
| angular accel | 0.06 rad/s2 (86 gyros) | **0.038 yaw / 0.039 pitch / 0.49 roll rad/s2** (72 gyros, 2,419 MN m; inertia 6.4e10 / 6.1e10 / 4.9e9 kg m2 from the block masses, point-mass model) | -37 %: 14 fewer gyros than the catalogue (700 PCU, 180 t); 90 degrees of yaw take 12.9 s with full torque (flip and brake) |
| PCU | 34,575 | **30,006 ship + 2 x 1,643 pods = 33,292** (-4 %); Lite 29,560 + 3,286 | full table in section 10 |
| armour HP | 55.2 M | 68.5 M (sum over every block of the ship grid) | +24 % |
| weapons | 6 rail, 8 artillery, 12 assault, 20 gatling, 4 rocket, 12 decoys | the same | sustained data DPS 58,864 (catalogue 58.9 k); Lite: 24 gatlings, no rocket turret, 60.4 k |
| generation / batteries | 420 MW / 60 MWh (240 MW) | the same: 1 x 300 MW + 8 x 15 MW, 20 x 3 MWh | railgun recharge 228 MW, surplus 191 MW |
| hydrogen | 4 tanks, 60 ML | the same, all full | full forward burn 19,281 L/s = **51.9 min**; everything at 100 % 14.8 min; 25 % forward + 10 % others 104 min (catalogue need 17 ML per 30 min) |
| docks | 8 | 8 (two pods of 4) | each pod: 4 merge-block cradles, own PB |
| PBs | Helm, Gunnery, Staff, 2 x Pod | the same | `[Helm][SH]`, `[Gunnery][SH]`, `[Staff][SH]`, `[Pod]` x 2 |
| junctions | 200 conveyors | 271 (Lite 256) | 14 thrusters, 24 turrets, 4 tanks, 4 lockers on one network |

Deviations explained: the size (nacelles for the manoeuvring thrusters and the pods' hangars, ram keel for the profile), the angular acceleration (gyros cost 50 PCU and 12.8 t each; 72 keep the
total inside the PCU band and the flagship slow, as the catalogue itself allows: "43 gyros give 0.03 rad/s2"), the thruster count (every axis gets whole large thrusters, 3x3x3 blocks recessed
in nacelles and flanks instead of the catalogue's mixed small ones). Everything else is within 20 %.

## 2. Silhouette and style

* Shape language (no two ships alike): a **massive stepped wedge**. Long **chisel arrowhead bow** (3 cells at the tip, 23 at the shoulders after 20 cells, 50 m) with a **ram keel** under it; on the bow deck
  the **forward main-battery spine**: six large railguns in six channels between seven armour ribs (13 cells = 32.5 m wide, 8 cells = 20 m long), their breeches in the orange **gunhouse wall**
  that rises in a sloped 2:1 front to the upper hull; behind it the hull steps up in three tiers (full-width hull with the **flank ledges** 3 cells wide, the 17-wide upper hull with its raised deck,
  the **armoured dorsal citadel** of three stepped heavy layers with orange bands) and ends in the **glazed bridge tower** (a 9 x 11 cell bridge on a 5 x 7 neck, cyan glass front and sides). Both flanks
  carry the artillery / assault / gatling turrets in a single rank on the ledges (recessed against the upper hull wall) and, in the middle, the two **flank hangars**: eight doors each in a
  hazard-orange frame. The stern is the **wide engine block**: raked top in steps of 3, four large nozzles in a row inside orange rings, 72.5 m over the nacelles, two swept dorsal fins.
* Family resemblance (Shrike / Corsair / Warden): charcoal gunmetal hull (`hsv 215 deg, .13, .36`), lighter plates on the upper tiers and the tower (`.52`), darker belly and engine block (`.24`),
  orange accent (`28 deg, .85, .95`) on the bow tip, the gunhouse slope, the rib tops, the deck-edge line along both flanks, the citadel bands, the hangar frames, the nozzle rings and the nacelle
  caps, cyan bridge glass, thruster nozzles glow orange in the renders.
* Heavy armour (16.5 k HP, 3.3 t per block) on the bow, the gunhouse and ribs, the whole citadel, the flank belt (|x| = 11, y 1..3), the keel strip (|x| <= 5) and the stern wall; light (2.5 k HP,
  0.5 t) elsewhere: **3,327 heavy, 4,458 light**. The hull is hollow with a 1-cell skin and light bulkheads every 10 cells (z = 10, 20, ... 80), an interior floor under the tanks and a bridge floor.
  Slopes, corners and 2:1 ramps come from `hulls.build_hull` (every mount face checked), 20 `Window1x1Slope/Flat` glass blocks on the bridge, 73 `ArmorPanel` deck seams (none in front of a railgun muzzle: the bow-deck grid skips the rail lanes).
* Symmetric about the centre column x = 0 (test `test_symmetric`: every block has its mirror image, except the conveyor junctions whose router picks a side). No floating block: every block of all three
  grids attaches (`audit.py --types`: 9,588 of 9,588 attached, each grid one piece).
* **Design iterations looked at** (renders in `scratchpad/ship_renders/Leviathan/v1 .. v6`, final set in `.../Leviathan/`): (v1) the demo-style hull with a plain nose: a brick, rejected; (v2) tiers, ribs and a
  raked engine block: the rails read, the fins looked like tombstones; (v3) colour zones: far too much orange on nacelles and stern; (v4) all systems placed: the ledge turret ranks made the
  silhouette, the nacelles needed a different colour; (v5) orange limited to the leading edges, hangar frames, nozzle rings; dark nacelles with orange caps; (v6) ram keel, raked nacelle fronts,
  a lying antenna boom instead of a mast (height 47.5 m -> 42.5 m), regular deck seams.

## 3. Layout (cells: x Right, y Up, z Backward, bow at z = 0, cell = 2.5 m; centre column x = 0)

Levels: belly y = 0 (ram keel y -2..-1), tier 1 y 0..4 (flank ledge top y = 5 for the turrets), upper hull y 5..7, citadel y 8..10, bridge neck y = 11, bridge y 12..14 (interior y = 13), stern block
raked from y = 4 (z 66..68) to y = 10 (z 84..87). Hull x -11..11 (the arrowhead from z = 0 to 20), nacelles x +-12..14 over z 56..87.

| zone | z | what |
|---|---|---|
| bow chisel | 0..19 | 3 -> 21 cells wide, 2..3 high on the bow deck (y 0..2), ram keel under z 2..22; Camera Front at (0, 3, 11) |
| main-battery spine | 12..19 | seven ribs x = -6, -4 ... 6 (y 3..4), six railguns x = -5, -3, -1, 1, 3, 5 (y 3..4, muzzle z = 12, breech z = 19) |
| gunhouse wall | 20 | upper hull starts (y 5..7, 17 wide), sloped 2:1 front from z = 14; ledges x = +-9..11 from here to the stern |
| forecastle ledges | 20..31 | four artillery turrets each side (z 20, 23, 26, 29); on the upper deck x = +-5..7: gatling (z 22) and rocket turret (z 28) |
| flank ledges | 32..64 | gatling z 32, assault 35, 38, gatling 41, assault 44, 47, Dive thruster 50, assault 53, 56, gatling 59, 62, rocket 65 (per side) |
| citadel | 36..65 | heavy stepped box y 8..10: x +-7 at the base, +-4 at the top, z 36..65 at the base, 41..60 at the top |
| flank hangars | 41..48 | interior |x| 7..10, y 1..3; doors |x| = 11; connectors |x| = 6 (z 42, 47) |
| bridge tower | 52..62 | neck y 11 (x +-2, z 54..60), bridge y 12..14 (x +-4, z 52..62); antenna boom z 63..65 |
| flank nacelles | 56..87 | x +-12..14, y 0..7, raked upper front; Brake thruster z 56..58, Strafe aft z 67..69 |
| stern block | 66..87 | raked top (steps of 3 cells from y = 4 to y = 10), gatlings on the steps z 72 and 78, fins x = +-4 (y 8..13), four engines at z 85..87 |
| keel / belly | all | six recessed gatling turrets flush with the belly: z 24, z 76 (x +-9..11) and the keel pair x -1..1 at z 28, 60 |

Interior compartments (between the bulkheads; floor = the belly skin y = 0 unless noted):

| z | compartment | contents |
|---|---|---|
| 11..19 | bow | empty |
| 21..29 | A | ammo lockers 1 (x -7..-5) and 2 (x 5..7) at z 22..24, y 1..3; junction trunk |
| 31..39 | B | lockers 3, 4 at z 33..35; recessed Strafe-fore thrusters (|x| 9..11, z 32..34, y 1..3) |
| 41..49 | C | the two hangars (|x| 7..10), the battery bank in the centre: 20 batteries x -2..2, y 1..2, z 43..44 |
| 51..59 | D | Lift thrusters (x 2..4 and -4..-2, y 0..2, z 50..52), the large reactor (x -1..1, z 54..56) and eight small reactors (x +-3, z 54..57); the tower stands above |
| 61..69 | E | gyro banks 1, 2 (6 x 3 each, x -9..-4 and 4..9, z 62..64), keel gatling 2 (z 60..62) |
| 71..79 | F | gyro banks 3, 4 (z 72..74), belly gatlings (z 76..78) |
| 81..86 | G | stern floor y = 3; four hydrogen tanks (x -10..-8, -5..-3, 3..5, 8..10; y 4..6; z 81..83); main engines z 85..87 |

Deck plans (`python3 -I tools/designs/leviathan.py --plans`; columns x -14..14, `#` armour, `w` glass, letters: R rail, T turret / thruster block (t = thruster), G gyro, B battery, F reactor, K tank,
C cargo, j junction, D door, N connector, P PB, S seat, a antenna, e timer / event controller, i AI, X decoy):

```
y = 1 (interior floor)                      y = 13 (bridge, z 52..65)
    43210987654321012345678901234               43210987654321012345678901234
z20 ...#######################...         z52 ..........#########..........
z22 ...#...CCC.........CCC...#...         z53 ..........w...S...w..........
z24 ...TTT#CCC.........CCC#TTT...         z54 ..........w.P...S.w..........   (P at z 54: Remote Control)
z28 ...#........#TTT#........#...         z57 ..........w...e...w..........
z33 ...ttt#CCC.........CCC#ttt...         z59 ..........w.P.P.P.w..........   Helm, Gunnery, Staff
z41 ...D....#...........#....D...         z60 ..........w#######w..........   (bulkhead z = 60)
z43 ...D....#...BBBBB...#....D...         z61 ..........w.e.e.e.w..........   EC Cockpit, EC Power, EC Core
z50 ...#######ttt###ttt#######...         z63 ..............a..............
z54 ...#.......F.FFF.F.......#...
z62 #....GGGGGG.#TTT#.GGGGGG....#
z72 #....GGGGGG.......GGGGGG....#
z85 (stern: tanks at y 4..6, engines z 85..87)
```

## 4. Systems (block names and script-facing tags)

Every ship block is named `[SH] ...`; the PBs `[Helm][SH] Leviathan Helm`, `[Gunnery][SH] Leviathan Gunnery`, `[Staff][SH] Leviathan Staff`. Pod blocks are `[POD1] ...` (port) and `[POD2] ...` (starboard),
the pod PBs `[Pod] Pod PB 1` and `[Pod] Pod PB 2`; nothing in a pod carries `[SH]` (grids_bays 6.3: Helm and Staff never adopt a drone or pod block).

| system | blocks | names / tags | where |
|---|---|---|---|
| seats | 2 x `LargeBlockCockpitSeat` | `[SH] Bridge Cockpit` (main cockpit), `[SH] Gunnery Seat` | bridge (0, 13, 53), (2, 13, 54) |
| script controller | `LargeBlockRemoteControl` | `[SH] Remote Control` | (-2, 13, 54) |
| PBs | 3 x `LargeProgrammableBlock` | `[Helm][SH] ...`, `[Gunnery][SH] ...`, `[Staff][SH] ...`; Custom Data `[Ship] Tag=LEVIATHAN`; Helm: `[Battery 1] Tag=B1 Class=RAIL Role=PRIMARY_CANDIDATE`, `[Cockpit]` surfaces; Gunnery: `[Turrets] Groups=Artillery,Assault,Rockets,PD`, `[Ammo]`; Staff: `[Wing]`, `[Hangar 1]`, `[Hangar 2]` (group + pod PB name) | (-2, 13, 59), (0, 13, 59), (2, 13, 59) |
| main battery | 6 x `LargeRailgun`, capacitor full, 6 slugs each | `[SH] [B1] Railgun 1..6`, group `B1` | ribs channels, y 3..4, z 12..19 |
| artillery | 8 x `LargeCalibreTurret`, 6 shells each | `[SH] Artillery Turret n P1..P4 / S1..S4`, group `Artillery` | forecastle ledges z 20, 23, 26, 29 |
| assault | 12 x `LargeBlockMediumCalibreTurret`, 6 shells each | `[SH] Assault Turret n P1..P6 / S1..S6`, group `Assault` | flank ledges z 35, 38, 44, 47, 53, 56 |
| point defence | 20 x `LargeGatlingTurret`, 24 boxes (3,360 rounds) each | `[SH] [PD] Gatling n <place>`, group `PD` | ledge z 32, 41, 59, 62; deck z 22; stern steps z 72, 78; belly z 24, 76; keel z 28, 60 |
| rockets | 4 x `LargeMissileTurret` (empty subtype), 6 rockets each (Full only; Lite has a gatling there) | `[SH] Rocket Turret n <place>`, group `Rockets` | ledge z 65, deck z 28 |
| groups | `B1` (6), `Artillery` (8), `Assault` (12), `Rockets` (4), `PD` (20), `Main Turrets` (20 = artillery + assault, Gunnery's), `Turrets` (44), `Weapons` (50), `Thrusters` (14), `Decoys` (12), `[Hangar 1]` (8 port doors), `[Hangar 2]` (8 starboard doors), `Gyros` (72), `Reactors` (9), `Batteries` (20), `Tanks` (4) | | |
| decoys | 12 x `LargeDecoy` flush in the skin, six mirrored pairs | `[SH] Decoy 1..12` | bow flank, flank, stern, citadel flank, belly |
| engines | 4 x `LargeBlockLargeHydrogenThrust`, exhaust aft | `[SH] Thruster Main Engine 1..4` | x -10, -5, 3, 8; y 4..6; z 85..87 |
| manoeuvring | 10 x `LargeBlockLargeHydrogenThrust`: 2 Brake, 4 Strafe, 2 Lift, 2 Dive | `[SH] Thruster Brake P/S`, `Strafe P/S fore/aft`, `Lift P/S`, `Dive P/S` | Brake in the nacelle fronts (z 56), Strafe fore in the flanks (z 32) and aft in the nacelles (z 67), Lift on the belly and Dive on the ledges at z 50 |
| gyros | 72 x `LargeBlockGyro` | `[SH] Gyroscope 1..72` | banks E, F |
| power | 1 x `LargeBlockLargeGenerator` (500 kg uranium), 8 x `LargeBlockSmallGenerator` (100 kg), 20 x `LargeBlockBatteryBlock` (full) | `[SH] Reactor Large`, `[SH] Reactor 1..8`, `[SH] Battery 1..20` | compartments D, C |
| fuel | 4 x `LargeHydrogenTank` (full, 15 ML each) | `[SH] Hydrogen Tank 1..4` | compartment G |
| stores | 4 x `LargeBlockLargeContainer`: spare 20 railgun slugs, 100 artillery, 150 assault shells, 300 gatling boxes, 60 rockets (Full) each | `[SH] Ammo Locker 1..4` | compartments A, B |
| network | 271 x `LargeBlockConveyor` | `[SH] Junction 1..271` | section 8 |
| hangars | 16 x `AirtightHangarDoor`, 4 x `Connector` | `[SH] Hangar P/S Door 1..8`, `[SH] Hangar P/S Connector A/B` | |x| = 11, y 1..3, z 41..48; |x| = 6, y 2, z 42 / 47 |
| timers / events | `TimerBlockLarge`, 3 x `EventControllerLarge` | `[SH] Timer D` (5 s, silent; toolbar: groups Thrusters and Weapons Off), `[SH] EC Cockpit`, `[SH] EC Power`, `[SH] EC Core` (integrity of the 9 reactors, threshold 0.4, inverted, toolbar: Run Staff) | bridge |
| sensing | 5 x `LargeCameraBlock`, `LargeBlockCompactRadioAntenna` (20 km), 2 x `LargeBlockSensor` (Full) | `[SH] Camera Front/Rear/Port/Starboard/Belly`, `[SH] Antenna`, `[SH] Sensor Lane P/S` | bow, stern face, flanks, belly; antenna boom behind the bridge; sensors under the lanes |
| AI (Full) | `LargeOffensiveCombat`, `LargeFlightMovement` | `[SH] AI Offensive [AI]`, `[SH] AI Flight [AI]` | bridge |

Cockpit toolbar (hand flight, page 1): `B1` Shoot, `Main Turrets` on/off, `PD` on/off, `Rockets` on/off (Full), `Decoys` on/off, `[Hangar 1]` Open_On, Open_Off, Run Helm, Run Staff. Gunnery seat:
`B1` Shoot, `Artillery`, `Assault`, `PD`, `Rockets` on/off, `[Hangar 2]` Open_On / Open_Off, Run Gunnery, Run Staff. PB `Run` without an argument is the one action never seen in a game file
(section 11). Timer D, the toolbars and EC Core use only seen action ids except `Run`.

Control hooks (control_arch): Helm collects weapons / thrusters / gyros by `[SH]` AND grid; Gunnery owns the turret groups (`Artillery`, `Assault`, `Rockets`, `PD`, `Main Turrets`); Staff finds the two
pod PBs by name and talks to them over `ConnectedConstructs`; the hangar groups are `[Hangar 1]` (port) and `[Hangar 2]` (starboard). Script file names embedded when the files exist in `scripts/`:
`HelmAI.cs`, `GunneryAI.cs`, `StaffAI.cs` (ship), `CradleBayAI.cs` (both pods); a missing file gives an EMPTY program (`SCRIPTS` in the module lists the names).

## 5. Weapons and point defence

| battery | weapons | reach | arcs |
|---|---|---|---|
| `[B1]` RAIL (Helm's primary candidate) | 6 x large railguns, fixed, parallel to the nose, 6 slugs each, capacitor full (38 MW recharge each, 59 s) | 2,000 m | forward only (muzzles at z = 12, 30 m from the tip) |
| artillery turrets | 8 x 6 shells (3 bursts of 2 each) | 2,000 m (turret terminal range 800 m until an AI block / pilot raises it) | forward and broadside, recessed on the forecastle ledges |
| assault turrets | 12 x 6 shells | 1,400 m | broadside from the flank ledges, clear of the tower and citadel |
| point defence `[PD]` | 20 gatlings | 800 m | dorsal ledges / deck / stern steps and ventral belly / keel: overlapping hemispheres |
| rockets | 4 x 6 | 800 m | splash on clustered drones |

Turrets are never batteries (control_arch 3.2); they are designators and automatic weapons. Rounds per gun with the pre-load: rail 6 (6 min at the 61 s cycle), artillery 6 (3 bursts, about 36 s), assault 6
(3 salvos), rocket 6 (about 7 s), gatling 3,360 (about 5 min); the four lockers hold 20 / 100 / 150 / 300 / 60 more of each each (rail, artillery, assault, gatling box, rocket): 116 slugs, 448 artillery shells,
672 assault shells, 1,680 gatling boxes, 264 rockets in the whole ship. Sustained data DPS (catalogue 2.6): rails 4,902 + artillery 21,334 + assault 15,158 + gatling 15,834 + rockets 1,636 = 58,864.

## 6. Power, thrust, fuel

* Electricity: 300 + 8 x 15 = 420 MW against 6 x 38 MW railgun recharge = 228 MW and about 0.9 MW for everything else; hydrogen thrusters draw no electricity. Surplus 191 MW; 20 batteries hold 60 MWh and
  deliver 240 MW: all six railguns recharging with every reactor dead for 15.8 min. Uranium 1,300 kg = 3.1 h at the full 420 MW (1 kg = 1 MWh), 5.7 h at the railgun load.
* Thrust (N), loaded accelerations (14,100 t): Forward 28.8 MN, 2.04 m/s2; Backward, Up, Down, Left, Right 14.4 MN each, 1.02 m/s2.
* Thrust balance about the centre of mass (point-mass model of every block and pre-load: COM x = 1.2 m (the mirror plane), y = 10.0 m, z = 128.8 m, i.e. cell z 51.5): the Lift, Dive pairs stand at z 50..52, the Strafe
  pairs 17 cells in front of and 17 behind the COM, so Up / Down thrust turns the ship by 0.1 % of the gyro torque, Left / Right by 1.5 % (roll 36 MN m), Backward by 0.7 %, Forward by 4.5 % (108 MN m of pitch: the
  engines sit 3.75 m above the COM). Nothing turns the ship noticeably under thrust (`test_thrust_balance_about_the_centre_of_mass` asserts < 10 %).
* Hydrogen: 60 ML in four tanks, one in front of each engine (z 81..83); full forward burn 19,281 L/s = 51.9 min; every thruster at 100 % is 67,484 L/s = 14.8 min; the catalogue's combat duty (25 % forward, 10 % others) 104 min.
* Ammunition and fuel masses are in the 164 t of pre-load (items are not in the block-mass accelerations of `build_all`, which therefore prints F2.1).

## 7. Armour and survivability

* HP by region (heavy = 16.5 k per block): bow, gunhouse, ribs and the citadel's three layers are all heavy (the citadel is 3 stepped layers: any shot from above or ahead meets heavy armour); the belly keel strip and
  the flank belt are heavy; the rest of the skin is light 2.5 k HP blocks. The interior (reactors, tanks, batteries) sits behind the 1-cell skin and, in the middle, under the citadel.
* The catalogue's point (small warheads, 15 k damage, cannot remove a heavy block) holds for the heavy parts; light parts (T2 deck, nacelle walls) fall to one warhead: 12 decoys in the skin draw turret fire first.

## 8. Fuel and ammunition network

Hydrogen thrusters get their fuel, and turrets their ammunition, **through the conveyor system**: without it the ship would not move. The generator knows no conveyor ports, so the layout is copied from the owner's
ships (measured on all game-written samples, `docs/design/ships/Warden.md` section 7): a `LargeBlockLargeHydrogenThrust` has a ported block (junction, tank, container, reactor) touching the centre of its
back face in 55 of 55 cases; the large gatling turret stands on a junction in 25 of 25, `LargeCalibreTurret` 18 of 25, `LargeBlockMediumCalibreTurret` 54 of 88 (the others on tubes / ducts); tanks and containers
are touched at a face centre; junction cubes touch each other (1,046 of 1,137 have a junction neighbour), so face-adjacent junctions form one network and need no tube.

`_conveyors()` builds ONE network of 271 junction cubes (524 kg, 10 PCU each): terminals = the cell behind every thruster's back-face centre (14), the cell under every artillery / assault / rocket turret
base (24), the tops of the four tanks and of the four lockers, routed by Dijkstra through free hull cavity (cost 1) and hidden interior armour cubes (cost 4, replaced), never adjacent to a hangar connector.
Gatling turrets keep their own 3,360 rounds and are not on the network; neither are the railguns (the owner's railguns are fed in 2 of 11 cases and their port is not known): **6 slugs each, reload by hand
from the lockers**. The tanks are not set to Stockpile. Risk: the paste may rebuild conveyor connectivity differently from the game's own saves (no `<ConveyorLines>` are written, see limits).

## 9. Hangars, pods and the launch lane

* Each hangar: interior |x| 7..10 (10 m), y 1..3 (7.5 m), z 41..48 (20 m); the eight `AirtightHangarDoor` (1 x 3 x 1, standing) close the flank at |x| = 11, between the frame cells of the orange mouth; the two
  large `Connector` blocks at |x| = 6 (z 42, 47: 12.5 m apart, the pod's `ConnectorMedium` spacing) face the hangar. The hull around is skinned (floor y = 0, ceiling y = 4, ends z = 40, 49).
* Each pod hangs on its two connector pairs: `[POD1]` (port) pose in the ship frame position (-23.57, 3.5, 120.0) m with its nose to port and upright, `[POD2]` (starboard) at (23.57, 3.5, 102.5) m nose to
  starboard; the faces are 0.07 m apart (the owner's Venture lock distance), both pairs meet to 2 mm (test `test_pods_dock_on_both_pairs`). The pod frame spans 19.0 x 5.5 x 7.5 m in a 20.0 x 7.5 x 10.0 m
  hangar: 0.5 m free at both ends, 1.0 m above and below, 2.4 m between the nose ring and the doors. `leviathan.check_hangar` asserts all of it at build time (0.3 m minimum gap, 0.07 m at the connector plane).
  The starboard pod's slot 1 is the forward one (its row runs aft), the port pod's slot 1 is the aft one (its row runs forward).
* **Launch lane** per side: straight out from the door plane (|x| = 30 m): 12 m deep, 20 m (the hangar length) by 7.5 m (y 2.5..10 m), nothing in it (the ledge turrets stand above y = 12.5 m, the belly
  turrets below y = 0, the sensors at y = 0; build-time assertion). Drone noses point down the lane; the pod's `Away` is (0, 0, -1) in pod cells = ship Left (port) / Right (starboard).
* Drones are not in the file: `Leviathan` = ship + two EMPTY pods. Add up to 4 per pod, Hornet-class (Hornet, Wasp, Harrier, Watcher 8 long; Lancer, Aegis 9 long): paste them so that each stern merge block touches
  a pod merge block, or build them with `pods.attach_drone(pod, k, drone)` (test `test_locked_variant_with_eight_drones_writes_reloads_and_fits` builds eight, clamps them, and checks 0.25 m clearance to the hull).
  Midge Full (10 long) and the Gnat (belly merge) do not fit.
* Locked variant: `leviathan.leviathan(lite, lock=True)` writes all four connector pairs in the Venture locked form (`sebp_multi`); unlocked is the default (the Tech Hunter form, the form that was seen to paste).
  No model is registered for it.

## 10. PCU

Full table (`python3 -I tools/designs/leviathan.py --pcu`; PCU per block from `defs_stats.json`):

| category | n | PCU |
|---|---:|---:|
| Armour, windows, panels (3,327 heavy + 4,458 light armour blocks, slopes and corners included, 20 windows, 73 panels) | 7,878 | 7,878 |
| Gatling turrets (`LargeGatlingTurret`, 225 each) | 20 | 4,500 |
| Assault turrets (275) | 12 | 3,300 |
| Artillery turrets (275) | 8 | 2,200 |
| Rocket turrets (275, Full only; Lite: gatlings, 225) | 4 | 1,100 |
| Railguns (80) | 6 | 480 |
| Gyroscopes (50) | 72 | 3,600 |
| Conveyor junctions (10; Lite 256) | 271 | 2,710 |
| Hangar doors (115) | 16 | 1,840 |
| Hangar connectors (125) | 4 | 500 |
| Thrusters (12) | 14 | 168 |
| Programmable blocks (100) | 3 | 300 |
| Seats (150) | 2 | 300 |
| Reactors (25) | 9 | 225 |
| Batteries (15) | 20 | 300 |
| Hydrogen tanks (25) | 4 | 100 |
| Ammunition lockers (10) | 4 | 40 |
| Cameras (25) | 5 | 125 |
| Antenna (100) | 1 | 100 |
| Remote Control (25) | 1 | 25 |
| Decoys (5) | 12 | 60 |
| Sensors (25, Full only) | 2 | 50 |
| AI blocks (25, Full only) | 2 | 50 |
| Timer (25) | 1 | 25 |
| Event controllers (10) | 3 | 30 |
| **ship grid** | **8,374** | **30,006** |
| each pod (605 small-grid blocks: 588 armour 588, 4 merge blocks 400, 2 `ConnectorMedium` 250, Pod PB 100, antenna 100, 4 backup timers 100, 2 reactors 50, 2 batteries 30, Remote Control 25) | 2 x 605 | 2 x 1,643 |
| **total, empty pods** | | **33,292** (catalogue estimate 34,575: -4 %) |

Lite: 29,560 + 3,286 = 32,846 (rocket turrets are gatlings: -200; no sensors, no AI blocks: -100). Each Hornet adds 1,010 PCU (Lite 935): eight drones 8,080, so **41.4 k** in all.

World limits and what to take out (each step lists the PCU it saves and what it costs; do them in this order; the ship stays flyable and armed at every step):

| world limit | state of the ship | PCU |
|---|---|---:|
| **50,000** (the owner's creative world is "50,000-ish") | everything: ship + two pods; 8 Hornets bring it to 41.4 k (83 %), 8.6 k spare | 33,292 / 41,376 |
| **34,000** | ship + pods, no drones | 33,292 (98 %) |
| **20,000** | trim, cumulative: (1) delete both pods, 4 connectors and 16 doors (-3,286 -500 -1,840; the hangars become open bays, no drones) -> 27,670; (2) delete the 4 rocket turrets (-1,100) -> 26,570; (3) delete 36 of the 72 gyros (-1,800; yaw 0.019 rad/s2, 90 degrees in 18 s) -> 24,770; (4) delete 10 of the 20 gatlings (-2,250; keep the keel pair, one per ledge end and the stern pair) -> 22,520; (5) delete 6 of the 12 assault turrets (-1,650; the odd slots) -> 20,870; (6) delete 4 of the 8 artillery turrets (-1,100; the aft two of each side) -> **19,766** | 19,766 |
| below 20,000 | further: delete the 2 sensors, the 2 AI blocks and 8 decoys (-190), the antenna (-100), 24 more gyros (-1,200) -> about 18,300 | 18,300 |

Delete the junction branch of every removed turret (10 PCU each, a few per turret) to recover another 150..250. Armour blocks are 1 PCU each: thinning the interior bulkheads (about 600 blocks) is possible
but removes mass and the attachment points of the interior; it is not part of the list.

## 11. Known limits

1. **Railguns have no feed** (section 8): 6 slugs each, reload by hand from the lockers. Artillery, assault and rocket turrets draw from the lockers through the junctions only if that network pastes working.
2. The two pods are empty (no drones); the Staff / Pod scripts and the pods' bay tables are proposals until the script workers write them; the pod docking is unverified in the game (Pod.md C1..C4).
3. Event Controllers: only EC Core has an event configured (integrity, a seen id); EC Cockpit and EC Power exist but their event numbers (cockpit occupied, stored power) are unverified, set them by hand.
4. `Run` toolbar slots (cockpits and EC Core) have no argument (the argument form is unseen).
5. Unseen XML shapes, Full variant only: `LargeBlockSensor` (from the game source), `LargeOffensiveCombat` and `LargeFlightMovement` (guesses), and the rocket turret (`LargeMissileTurret` with the empty
   subtype, which no sample contains; its shape is the assault turret's with the rocket magazine). Lite has none of them (the four rocket sites hold gatling turrets).
   `Reactor/LargeBlockSmallGenerator`, `RemoteControl/LargeBlockRemoteControl` and `TimerBlock/TimerBlockLarge` use the shape of a sibling subtype of the same type that was seen.
6. Terminal groups with multi-cell members (`B1`, the turret groups, `Weapons`, `Turrets`, `Thrusters`, the tanks, the reactors) store the block-local cell (0, 0, 0) of each member, because the game's own `Center`
   cell of those definitions is not in any sample (about 165 notes in `bp.notes` after `build_xml`); the game should resolve any cell of a block, unverified. If a group comes up short of members, re-add them by hand.
7. The generator writes no `<ConveyorLines>` (game saves store one `MyObjectBuilder_ConveyorLine` per adjacent ported pair). The junction network may therefore not connect until the game rebuilds the lines: after
   the first paste check that a thruster shows fuel from the tank; if not, grind and rebuild one junction per branch (or cut and paste the ship once).
8. No lights, beacon or LCD panels, no cargo conveyor to the pods. Space only: no gravity budget (the Up thrust of 14.4 MN is a tenth of the weight at 1 g). Slab sides and hard ledge edges make the silhouette
   boxy next to a hand-built ship; no `Armor2x1` or round blocks are used.
9. The angular acceleration is low on purpose (0.038 rad/s2): a 180 degree turn takes about 18 s with full torque. 72 -> 86 gyros would reach the catalogue's 0.045 rad/s2 for +700 PCU and +180 t.
10. The thumbnail written by `build_all` shows the ship grid only (the pods are other grids).
11. The mass model of `numbers()` is a point-mass sum (block centres): the inertia is within a few percent of a cell-resolved sum, the game uses the real block volumes.
12. Review notes: the Brake thrusters (x -14..-12 / 12..14, z 56..58) exhaust forward along the flanks, past the hangar doors (z 41..48): do not brake while drones leave through the doors. The Lite
    variant still uses three types that have a sample only for a sibling subtype (`LargeBlockSmallGenerator`, `LargeBlockRemoteControl`, `TimerBlockLarge`; same XML shape as the seen subtype).
    Measured by the review: thrust torque about the COM is 4.5 % of the gyro torque at worst (forward burn pitches the nose down; the main engines sit 3.75 m above the COM), every thruster exhaust
    path is clear of blocks for 8 cells, the six railgun lanes are free of blocks ahead of the muzzles.

## 12. In-game checks (first paste)

1. Delete any old `Leviathan` blueprint folder first (stale `bp.sbcB5` cache). Does it paste as three grids (ship + two pods) with no DLC warning; does the terminal list 16 doors, 72 gyros, 14 thrusters, 4 tanks?
2. **PCU and mass** inside the world's limits (section 10); gyro feel (0.038 rad/s2: a 90 degree turn in 13 s; accelerate then brake).
3. **Fuel**: tanks show 100 %; every thruster has hydrogen (terminal info "Hydrogen: ... filling"); fly: all five groups thrust. If not, the junction network is not connected: check one thruster's back cell and the
   tank terminal first (`[SH] Junction 1..271`).
4. **Ammo**: assault / artillery / rocket turrets pull from the lockers (take a shell out of one turret, watch it refill). Gatlings have their own 24 boxes. Railguns: load and capacitor full on paste
   (`RemainingAmmo`, `StoredPower`).
5. Hangars: `[Hangar 1]` and `[Hangar 2]` doors open and close, no panel hits a pod, the lane is clear, the pods stay docked under 1 m/s2 (Pod.md C3); connector status of the four hangar connectors (C2).
6. Pods: C1, C4 (Pod.md section 8); T1 with the first drone. Slot order: starboard slot 1 forward, port slot 1 aft.
7. Cockpit: seat view through the bridge glass, the toolbar slots, the Remote Control as script reference; the gunnery seat's toolbar.
8. Power: reactors on, batteries full, the railguns' capacitors full; recharge draw about 38 MW each (the surplus of 191 MW holds all six).
9. Proportions and colours against the renders; decoys are dark squares; hangar frames orange; nacelle fronts show the Brake thrusters' glow.

## 13. Files, commands, renders

* `tools/designs/leviathan.py` (this ship; `python3 -I tools/designs/leviathan.py [--render DIR] [--plans] [--pcu] [--hull]` prints both variants, the numbers JSON, renders, the deck plans or the PCU table),
  `tools/designs/pods.py` (the pod builder, written by the Warden designer), `tools/designs/_shiprender.py` (the renderer: slopes and corners drawn as wedges, turrets as base + head + barrel, thruster glow, cut-away
  section, deck cut and lane views), `tools/tests/test_leviathan.py` (29 tests: rules, naming, loadout, catalogue bands, thrust balance, conveyor network, hangar, locked variant with eight drones).
* `python3 -I tools/build_all.py --only 'Leviathan' --no-zip` builds both variants (about 30 s with two jobs), validates (mounts, one piece per grid, overlaps, DLC), re-reads the written file and checks the sub-grids;
  `python3 -I tools/tests/run_all.py` runs everything; `tools/audit.py --types out/Blueprints/Leviathan/bp.sbc` reports 100 % attached on all three grids.
  The full build and validation of one variant takes about 14 s (the hull builder 1 s, the conveyor router 8 s, the sebp validation 5 s).
* Renders for this design record: `scratchpad/ship_renders/Leviathan/` (`leviathan_iso_fl / iso_fr / iso_rear / belly / top / side / front / section / deck / lane .png`, `leviathan_sheet.png` is the contact sheet; `v1..v6`
  the design iterations).
* Shapes: sample-derived XML for every block (`sebp.shape_summary()`); see limit 5 for the exceptions.

M5: the starboard pod's PB has `[Pod] First Bay=5` (`cradle_pod(first_bay=5)`), so `release 5` is the first drone of the starboard pod and `release all` is sent as `release 1-4 0` and `release 5-8 20` (20 s pod gap); Helm/Staff Custom Data carry `Block Prefix=[SH]`.
