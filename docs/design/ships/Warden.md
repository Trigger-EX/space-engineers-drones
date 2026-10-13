# Warden (`tools/designs/warden.py`)

Key `warden`. The large-grid frigate of the four-ship family (fighter Shrike, corvette Corsair, frigate Warden, dreadnought Leviathan): heavy gun deck, raised bridge tower, belly hangar with a docked,
empty 4-slot drone pod, four-engine stern. Models `Warden` and `Warden Lite` (ship + one docked `Cradle Pod`, a second CubeGrid); the pod alone is `Cradle Pod` (`docs/design/ships/Pod.md`).
Targets come from `docs/design/systems_catalogue.md` section 4.4; the hull is built with `tools/hulls.py`; control naming from `docs/design/control_arch.md`; bay from `docs/design/grids_bays.md`.
Nothing of this has been pasted in the game yet (section 11 lists what to look at first).

## 1. Numbers against the catalogue

> **Gyro boost (v2.8):** gyros 8 -> 24 (x3): 806 MN m, so the angular accelerations quoted below roughly triple, less the 205 t of extra gyros (mass 2,563 -> 2,768 t, +800 PCU). The Helm's Auto Max Turn calibration measures the new authority on the first slews; no script change.


| | catalogue 4.4 | Warden (Full) | note |
|---|---|---|---|
| size | 75 x 25 x 15 m (30 x 10 x 6 cells) | **85.0 x 27.5 x 40.0 m** (34 x 11 x 16 cells; hull 22.5 m wide, 12.5 m high deck; the 40 m is keel turrets to the mast) | longer and taller than the model hull: the hangar blister, the tower and the engines |
| mass | 2,147 t | **2,563 t blocks + 26.5 t ammunition = 2,589 t** (+21 %) | hangar blister and 8 doors (63 t), tower stem, flank armour belt, 101 junctions (53 t), 8 gyros; Lite 2,587 t |
| forward accel | 3.4 m/s2 (7.2 MN) | **4.03 m/s2** (10.44 MN = 1 large + 3 small hydrogen thrusters) (+19 %) | four engines at the stern as briefed |
| other axes | 1.5 m/s2 (3 small per axis) | **1.67 m/s2** (4 small per axis, 4.32 MN) (+11 %) | |
| angular accel | 0.15 rad/s2 (5 gyros) | about 0.16 rad/s2 yaw, 0.14 pitch (8 gyros, 269 MN m, solid-box inertia 1.7e9 / 1.9e9 kg m2; summing the real block masses gives 0.9e9 about yaw and pitch, i.e. up to 0.30 rad/s2, so the box figure is the pessimistic end) | 3 more gyros than the catalogue for the longer, heavier hull |
| PCU | 9,664 | **8,654 ship + 1,643 pod = 10,297** (+7 %); Lite 8,554 + 1,643 | doors 920, junctions 1,010, connectors 250 |
| armour HP | 8.8 M | 12.4 M (all blocks, 632 heavy + 354 light armour blocks, 20 windows) | |
| weapons | 2 rail, 2 artillery, 4 assault, 8 gatling, 6 decoys | the same | sustained data DPS 18,354 (catalogue 18.4 k) |
| generation / batteries | 6 x 15 MW / 8 x 3 MWh | the same: 90 MW, 24 MWh (96 MW) | |
| hydrogen | 2 tanks, 30 ML | the same, both full | full forward burn 7,230 L/s = **69 min**; at 25 % duty 277 min (catalogue need: 4.3 ML per 30 min of combat) |
| docks | 4 | 4 (one pod) | |
| thrusters | 16 | 24 (4 main + 20 small) | the catalogue's 3 per axis became 4 per axis for the heavier hull |
| junctions | 40 | 101 | 24 thrusters, 14 turrets, 2 tanks, 2 lockers on one network |

Deviations explained: mass +21 % (the hangar, the tower and the belt; a thinner skin would push the forward acceleration beyond the +20 % band, so the mass is kept) and the thrust that
comes from the four-engine stern. Everything else is within 20 %.

## 2. Silhouette and style

* Shape language (no two ships alike): a long **arrowhead bow** (3 cells wide at the tip, 9 at the forecastle, foredeck top one layer below the main deck) with **two railguns in channels
  either side of a central ridge**, their breeches on the orange **forecastle wall** (stepped decks); slab sides with an orange **flank stripe** and a lower **sponson belt** (x = +-5, hard-edged,
  orange) that carries the four assault turrets in notches that are recessed into the hull; a **raised bridge tower** (solid stem 5 wide, bridge level 7 wide that overhangs the stem by one
  cell, cyan glass front and sides, conn platform with four cameras and the antenna mast); a **ventral hangar blister** under the middle whose belly doors open on a free lane, framed by an
  orange hazard border; a **four-engine stern** (one large + three small hydrogen thrusters on an orange plate).
* Family resemblance: charcoal gunmetal hull (`hsv 215 deg, .13, .36`), lighter plates on the tower, darker belly (`.24`), orange accent (`28 deg, .85, .95`) on bow, forecastle, flank stripe,
  sponson belt, mouth frame and engine plate, cyan bridge glass, thruster nozzles glow orange in the renders.
* Heavy armour (16.5 k HP, 3.3 t per block) on the bow shell, the whole belly and blister, the flank belt (y 3..4), the forecastle wall and the tower stem; light (2.5 k HP, 0.5 t) elsewhere:
  632 heavy, 354 light armour blocks. Slopes, corners and 2:1 ramps come from `hulls.build_hull` (mount faces checked); the hull is hollow with bulkheads at z = 16 and 25 and a floor over the blister.
* Symmetric about the centre column x = 0 (test `test_symmetric`: no unpaired cell). No floating block: every block attaches (`audit.py --types`: 100 %).
* Windows: only non-DLC `Window1x1Flat` / `Window1x1Slope` (the bridge front and sides, 20 blocks).

## 3. Layout (cells: x Right, y Up, z Backward, bow at z = 0, cell = 2.5 m; centre column x = 0)

Stations along z (1 cell = 2.5 m; the first number is the cell, the metres are from the bow tip):

| z | m | what |
|---|---|---|
| 0..11 | 0..30 | arrowhead bow, heavy; foredeck top y = 4; railgun channels z 4..11 (muzzles at z = 4, 3 cells of keep-clear ahead), central ridge, parapets; recessed ventral gatlings x +-3 at z 9..11; 4 lift thrusters at z = 9 / 29, flush strafe thrusters at z = 10, 11 |
| 12 | 30 | forecastle wall (orange), the rails' breech |
| 13..15 | 32.5..40 | fore compartment: 2 ammo lockers (3x3x3) either side of 6 reactors; on the deck: artillery P, gatling `Front`, artillery S (one row of three) |
| 16 | 40 | bulkhead; deck band (orange); 4 dive thrusters; brake thrusters on the sponson belt's front |
| 17..24 | 42.5..62.5 | hangar (8 cells = 20 m long, 3 wide, y 1..4; doors y = 0; connectors y = 5 at z = 18 and 23); flank bays (batteries z 17..19, 23; gyros z 20..22, 24); assault turrets z 17..19 and 22..24 on the sponsons; tower stem z 18..26, bridge z 19..25 |
| 25 | 62.5 | bulkhead (aft hangar wall) |
| 26..28 | 65..72.5 | engine room: 2 hydrogen tanks either side of a 1-cell service corridor; on the deck: three gatlings abreast (`Stern P/C/S`); ventral gatlings hang below at x +-3 |
| 29 | 72.5 | junction line (fuel bus) |
| 30 | 75 | stern wall (orange plate) |
| 31..33 | 77.5..85 | engines: large thruster centre, small port / starboard / top |

Levels (y): -1..1 blister (hangar mouth y = 0, doors) and hanging gatlings; 2 belly; 3..5 interior decks and the notches; 6 main deck; 7..8 tower stem (and the artillery / gatling turrets standing on the deck);
9..10 bridge; 11 conn platform; 12..14 antenna.

Deck plans (generated: `python3 -I tools/designs/warden.py --plans`; rows = z with the bow at the top, columns x -6..6; `#` armour, `w` window, `R` railgun, `T` turret, `t` small thruster, `E` n/a,
`G` gyro, `B` battery, `F` reactor, `K` tank, `C` ammo locker, `j` conveyor junction, `D` door, `N` connector, `P` PB or Remote Control, `S` seat, `X` decoy, `c` camera, `a` antenna, `n` sensor,
`i` AI block, `e` timer / event controller):

Keel layer y = 1 (blister walls, the hangar column x -1..1 empty, lift thrusters `t`, recessed / hanging turrets `T`, lane sensors `n`):

```
y = 1
   6543210123456   (columns x -6..6)
z 5 ......c......
z 6 .............
z 7 .............
z 8 .............
z 9 ..TTTt.tTTT..
z10 ..TTT...TTT..
z11 ..TTT...TTT..
z12 .............
z13 ...#######...
z14 ...#######...
z15 ...#.....#...
z16 ...#######...
z17 ...##...##...
z18 ...##...##...
z19 ...##...##...
z20 ...##...##...
z21 ..n##...##n..
z22 ...##...##...
z23 ...##...##...
z24 ...##...##...
z25 ...#######...
z26 ...#######...
z27 ...#######...
z28 ..TTTt.tTTT..
z29 ..TTT...TTT..
z30 ..TTT...TTT..
```

Engineering deck y = 3 (floor level inside the hull: lockers, reactors, batteries, gyros, tanks, junction network, flush strafe thrusters, the engines at z 31..33):

```
y = 3
   6543210123456   (columns x -6..6)
z 0 .....###.....
z 1 .....###.....
z 2 .....###.....
z 3 .....###.....
z 4 ....##.##....
z 5 ....#...#....
z 6 ....#...#....
z 7 ....X...X....
z 8 ...##...##...
z 9 ...#jjjj##...
z10 ..tjj#jjjjt..
z11 ..tj##j##jt..
z12 ..###.j.###..
z13 ..#CCCFCCC#..
z14 ..XCCCFCCCX..
z15 ..#CCCFCCC#..
z16 .t#########t.
z17 .j#B#...#B#j.
z18 .##B#...#B##.
z19 .##B#...#B##.
z20 .##G#...#G##.
z21 .##G#...#G##.
z22 .##G#...#G##.
z23 .#jB#...#Bj#.
z24 .#jG#...#Gj#.
z25 .#jj######j#.
z26 .tjKKK.KKKjt.
z27 .tjKKK.KKKjt.
z28 ..#KKK.KKK#..
z29 ..#j.....j#..
z30 ..#########..
z31 .....ttt.....
z32 .....ttt.....
z33 .....ttt.....
```

Gun deck y = 5 (rails in their channels, hangar ceiling with the two connectors `N`, sponson turrets, tanks):

```
y = 5
   6543210123456   (columns x -6..6)
z 4 ....R...R....
z 5 ....R###R....
z 6 ....R###R....
z 7 ....R#.#R....
z 8 ...#R#.#R#...
z 9 ...#R#.#R#...
z10 ...#R#.#R#...
z11 ...#R#.#R#...
z12 ..####j####..
z13 ..#CCCjCCC#..
z14 ..#CCCjCCC#..
z15 ..#CCC.CCC#..
z16 ..#j#####j#..
z17 .TTT#####TTT.
z18 .TTT##N##TTT.
z19 .TTT#####TTT.
z20 ..#########..
z21 ..#########..
z22 .TTT#####TTT.
z23 .TTT##N##TTT.
z24 .TTT#####TTT.
z25 ..#########..
z26 ..#KKK.KKK#..
z27 ..#KKK.KKK#..
z28 ..#KKK.KKK#..
z29 ..#j..j..j#..
z30 ..#########..
z31 .....ttt.....
z32 .....ttt.....
z33 .....ttt.....
```

Main deck y = 6 (rails still visible, the deck row of junctions under the turrets, the open hangar roof):

```
y = 6
   6543210123456   (columns x -6..6)
z 4 ....R...R....
z 5 ....R...R....
z 6 ....R###R....
z 7 ....R###R....
z 8 ....R###R....
z 9 ....R###R....
z10 ....R###R....
z11 ....R###R....
z12 ..#########..
z13 ..#########..
z14 ..#jjjjjjj#..
z15 ..#j#j#j#j#..
z16 ..#j#j#j#j#..
z17 .TTT#####TTT.
z18 .TTT#####TTT.
z19 .TTT#.#.#TTT.
z20 ..###...###..
z21 ..###...###..
z22 .TTT#.#.#TTT.
z23 .TTT#####TTT.
z24 .TTT#.#.#TTT.
z25 ..###...###..
z26 ..#########..
z27 ..#########..
z28 ..#########..
z29 ..#j##j##j#..
z30 ..####j####..
z31 ......t......
```

Bridge level y = 9 (cockpit and gunnery seat at the front, Remote Control, timers and event controllers, AI blocks (Full), the two PBs at the back):

```
y = 9
   6543210123456   (columns x -6..6)
z13 ..TTTTTTTTT..
z14 ..TTTTTTTTT..
z15 ..TTTTTTTTT..
z16 .............
z17 .............
z18 .............
z19 ...#wwwww#...
z20 ...w#PSS#w...
z21 ...w#...#w...
z22 ...w#eee#w...
z23 ...w#i.i#w...
z24 ...w#PeP#w...
z25 ...#######...
z26 .............
z27 .............
z28 ..TTTTTTTTT..
z29 ..TTTTTTTTT..
z30 ..TTTTTTTTT..
```

## 4. Systems (block names and script-facing tags)

Every ship block is named `[SH] ...`; the PBs `[Helm][SH] Warden Helm` and `[Staff][SH] Warden Staff`. Pod blocks are `[POD] ...`, the pod PB `[Pod] Pod PB`.

| system | blocks | names / tags | where |
|---|---|---|---|
| cockpit | `LargeBlockCockpitSeat` (main cockpit), `CockpitOpen` gunnery seat | `[SH] Bridge Cockpit`, `[SH] Gunnery Seat` | bridge (0, 9, 20), (1, 9, 20) |
| script controller | `LargeBlockRemoteControl` | `[SH] Remote Control` | (-1, 9, 20) |
| PBs | 2 x `LargeProgrammableBlock` (+ the pod PB on the pod grid) | `[Helm][SH] Warden Helm` (`ShrikeAI.cs`), `[Staff][SH] Warden Staff` (`StaffAI.cs`); Custom Data `[Ship] Tag=WARDEN`; Helm `[Link] Block Prefix`, `[Shrike]` class tuning (section 13), `[Manual] HUD Surface`, `[Battery] Standoff By Weapons=false`; Staff `[Staff] Block Prefix/Cockpit Surface/Helm HUD Surface/Wing Surface/Turret Target Group/Turret Target Names`, `[Wing]`, `[Hangar 1]` (M6: the old `[Battery 1]` and `[Cockpit]` sections were read by no script) | (-1, 9, 24), (1, 9, 24) |
| main battery | 2 x `LargeRailgun`, capacitor full, 6 slugs each | `[SH] [B1] Railgun P/S`, group `B1` | channels x = -2 / 2, y 5..6, z 4..11 |
| artillery | 2 x `LargeCalibreTurret`, 6 shells each | `[SH] Artillery Turret P/S` | deck (-3 / 3, 7, 14) |
| assault | 4 x `LargeBlockMediumCalibreTurret`, 6 shells each | `[SH] Assault Turret 1..4 P/S` | sponson notches (+-4, 5, 18 and 23) |
| point defence | 8 x `LargeGatlingTurret`, 24 boxes (3,360 rounds) each | `[SH] [PD] Gatling 1..8 <place>`, group `PD` | deck front, deck stern P/C/S, belly bow P/S (recessed), belly aft P/S |
| groups | `B1` (2), `PD` (8), `Main Turrets` (6), `Weapons` (16), `Thrusters` (24), `Decoys` (6), `[Hangar 1]` (8 doors), `Gyros` (8), `Reactors` (6), `Batteries` (8) | | |
| decoys | 6 x `LargeDecoy` flush in the skin | `[SH] Decoy 1..6` | bow flanks, forecastle flanks, belly |
| engines | 1 x `LargeBlockLargeHydrogenThrust` + 3 x `LargeBlockSmallHydrogenThrust` aft | `[SH] Main Engine (large)`, `Main Engine P/S/T (small)` | z 31..33 |
| translation | 20 x `LargeBlockSmallHydrogenThrust` (4 lift, 4 dive, 4 brake, 8 strafe) | `[SH] Thruster Lift/Dive/Brake/Strafe ...` | belly z 9, 29; deck z 16; sponson fronts; flanks z 10, 11, 28, 29 |
| gyros | 24 x `LargeBlockGyro` (8 in the flank bays + 16 on the keel/engine-room floor) | `[SH] Gyroscope P1..4 / S1..4`, `K1..16` | flank bays, forward keel z 4-12, corridor z 29 |
| power | 6 x `LargeBlockSmallGenerator` (100 kg uranium each), 8 x `LargeBlockBatteryBlock` (full) | `[SH] Reactor 1..6`, `[SH] Battery P1..4 / S1..4` | fore compartment, flank bays |
| fuel | 2 x `LargeHydrogenTank` (full, 15 ML each) | `[SH] Hydrogen Tank P/S` | engine room |
| stores | 2 x `LargeBlockLargeContainer` with spare 10 slugs, 20 artillery, 30 assault shells, 100 gatling boxes each | `[SH] Ammo Locker P/S` | fore compartment |
| network | 101 x `LargeBlockConveyor` | `[SH] Junction 1..101` | section 7 |
| hangar | 8 x `AirtightHangarDoor`, 2 x `Connector` | `[SH] Hangar Door 1..8`, `[SH] Hangar Connector A/B` | y = 0, z 17..24; (0, 5, 18), (0, 5, 23) |
| timers / events | `TimerBlockLarge`, 3 x `EventControllerLarge` | `[SH] Timer D (deadman)` (5 s, silent; toolbar: groups Thrusters and Weapons Off), `[SH] EC Cockpit`, `[SH] EC Power`, `[SH] EC Core` (integrity of the 6 reactors, threshold 0.4, inverted, toolbar: Run Staff) | bridge |
| sensing | 5 x `LargeCameraBlock`, compact antenna (10 km), 2 x `LargeBlockSensor` (Full) | `[SH] Camera Front/Rear/Port/Starboard/Belly`, `[SH] Antenna`, `[SH] Sensor Lane P/S` | conn platform, belly, keel |
| AI (Full) | `LargeOffensiveCombat`, `LargeFlightMovement` | `[SH] AI Offensive [AI]`, `[SH] AI Flight [AI]` | bridge |

Cockpit toolbar (hand flight, page 1): slot 1 `B1` Shoot, 2 `Main Turrets` on/off, 3 `PD` on/off, 4 `Decoys` on/off, 5 `[Hangar 1]` Open_On, 6 Open_Off, 7 Run Helm, 8 Run Staff
(PB `Run` without argument is the one action never seen in a game file, section 11). Timer D, the cockpit toolbar and EC Core use only seen action ids except `Run`.

Control hooks (control_arch): Helm collects weapons / thrusters / gyros by `[SH]` AND grid; Staff finds the pod PB by name and talks over `ConnectedConstructs`; the hangar group is `[Hangar 1]`. M5: `[Link] Block Prefix=[SH]` (Helm) and `[Staff] Block Prefix=[SH]` are in the PBs' Custom Data (the scripts take only blocks whose name starts with it); the Staff tries `ConnectedConstructs`, `AntennaRelay`, then a unicast to `[Hangar 1] Pod=` (see Pod.md section 7); `release all` opens the 8 doors first (sim: 2.7 s) and closes them 10 s after the last drone.

## 5. Weapons and point defence

| battery | weapons | reach | arcs |
|---|---|---|---|
| `[B1]` RAIL (Helm's primary candidate) | 2 x large railguns, fixed, parallel to the nose, 6 slugs each, capacitor full (38 MW recharge each, 59 s) | 2,000 m | forward only (muzzles at z = 4, flanking the bow) |
| artillery turrets | 2 x 6 shells | 2,000 m (turret terminal range 800 m until an AI block / pilot raises it) | dorsal, in front of the tower |
| assault turrets | 4 x 6 shells | 1,400 m | broadside, sponsons, clear of the tower |
| point defence `[PD]` | 8 gatlings | 800 m | 4 dorsal (front, stern x3) and 4 ventral (bow, aft pairs): hemispherical cover, gaps fore / aft |

Turrets are never batteries (control_arch 3.2); they are designators and automatic weapons. `LargeMissileTurret` blocks keep the targeting flags of the owner's sample (characters off).
Rounds per gun with the pre-load: rail 6 (6 min at the 61 s cycle), artillery 6 (3 bursts of 2, about 36 s), assault 6 (3 salvos), gatling 3,360 (about 5 min at 600-700 rpm); the lockers hold the same again
(rails 10 + artillery 20 + assault 30 + gatling 100 boxes each).

## 6. Power, thrust, fuel

* Electricity: 6 x 15 MW = 90 MW against 2 x 38 MW railgun recharge (76 MW) and about 0.14 MW of the rest; hydrogen thrusters draw no electricity. Surplus 14 MW; batteries 24 MWh / 96 MW carry both
  railguns for 19 min with every reactor dead. Uranium 600 kg = 6.7 h at 90 MW.
* Thrust (N): forward 10.44 MN, every other axis 4.32 MN (table in the numbers: `warden.numbers`). Loaded accelerations 4.03 / 1.67 m/s2.
* Centre of thrust against centre of mass (independent check, loaded, COM on the centreline, y 9.7 m, z 46.0 m from the grid origin): forward thrust acts 2.0 m above the COM (pitch torque 21 MN m = 8 % of
  the 269 MN m of the gyros), the Dive/Lift/Brake groups give 6..21 MN m of pitch (Dive 21: its 8 thrusters sit at z 16, 2.4 cells ahead of the COM), the strafe pairs 6.4 MN m yaw and 4.3 MN m roll
  (2 % of the gyros), sideways COT offsets are 0.02 m. All inside what the gyros and the dampeners absorb; nothing turns the ship noticeably under thrust.
* Hydrogen: 30 ML in two tanks; full forward burn 7,230 L/s = 69 min; the catalogue combat need is 4.3 ML per 30 min.
* Ammunition and fuel masses are in the 26.5 t of pre-load (items are not in the block-mass accel figures of `build_all`, which therefore prints F4.1).

## 7. Fuel and ammunition network

Hydrogen thrusters get their fuel, and turrets their ammunition, **through the conveyor system**: without it the ship would not move. The generator knows no conveyor ports, so the layout is copied from
the owner's ships, measured on all game-written samples (`ref/user-samples` + Tech Hunter):

* `LargeBlockSmallHydrogenThrust`: a `LargeBlockConveyor` (junction cube) is directly behind it in 1,117 of 1,117 cases; `LargeBlockLargeHydrogenThrust`: the cell touching the centre of its back face
  is a ported block in 55 of 55 (junction 25, hydrogen tank 12, cargo container 8, reactor 10);
* `LargeGatlingTurret`: stands on a junction in 25 of 25; `LargeCalibreTurret` 18 of 25 (the other 7 on tubes / ducts); `LargeBlockMediumCalibreTurret` 54 of 88 (30 on tubes / ducts, 4 on armour);
* tanks and cargo containers are touched by a junction at a face centre in 9 of 29 and 12 of 27 (the rest by tubes or by the thruster / turret itself);
* junction cubes touch each other: 1,046 of 1,137 have at least one junction neighbour (typical 2 to 4: solid lattices and chains), so face-adjacent junctions form one network and need no tube.

So `_conveyors()` builds ONE network of 101 junction cubes (524 kg, 10 PCU each): terminals = the cell behind every thruster's back face centre (24), the cell under every turret's base centre (14), the tanks'
rear face centres (2) and the lockers' top face centres (2), routed with Dijkstra through free hull cavity (cost 1) and hidden interior armour cubes (cost 4, replaced), never adjacent to the hangar
connectors or in the hangar column. What it looks like: a fuel bus along the stern service line (z = 29) and the engine room corridor, trunks along both flanks at y = 4 under the sponson belt (z 17..25),
a deck row under the three forward turrets and one under the three stern gatlings, spurs to every thruster.
**Not fed: the two railguns** (the owner's railguns are fed in 2 of 11 cases and their port is not known): 6 slugs each, reload by hand from the lockers. The `Hydrogen Tank`s are not Stockpile.
Risk: the paste may rebuild conveyor connectivity differently from the game's own saves; check in section 11.

## 8. Hangar, pod and the launch lane

* Hangar: interior x -1..1 (7.5 m), z 17..24 (20 m), y 1..4 (10 m, ceiling = the connector layer y = 5); the 8 `AirtightHangarDoor` (1 x 3 x 1, lying along x, hinged on the +x wall) form the floor of the
  blister at y = 0; the blister walls are 2 thick and carry an orange frame.
* The pod hangs from two large `Connector` blocks at (0, 5, 18) and (0, 5, 23) (Forward = Down, 12.5 m apart) by its two `ConnectorMedium`; pose of the pod grid in the ship frame: position
  (-1.5, 3.93, 42.5) m, nose = ship Down, slot row along z. The faces are 0.07 m apart (the owner's Venture lock distance); both pairs meet to 2 mm (test `test_pod_docks_on_both_pairs`).
* Inside: the pod frame spans 19.0 x 5.5 x 7.5 m in a 20.0 x 7.5 x 10.0 m hangar: 0.5 m free at both ends, 1.0 m at the sides, 2.5 m between the nose ring and the doors. `warden.check_hangar` asserts all of it
  at build time (0.3 m minimum, 0.07 m at the connector plane), plus the lane.
* **Launch lane**: straight down (-y) from the door plane: 7.5 m wide, the full 20 m hangar length, 12 m deep, nothing in it (the hanging gatlings are at z 28..30, the recessed ones at z 9..11, the sensors
  at x +-4; lift thrusters stand at z 9 and 28). Drone noses point down the lane; the pod's `Away` is (0, 0, -1) in pod cells = ship Down.
* Drones are not in the file: `Warden` = ship + EMPTY pod. Add up to four: paste Hornets so that each stern merge block touches a pod merge block (positions in `Pod.md`, or build them with `pods.attach_drone`).
* Locked variant: `warden.warden(lite, lock=True)` writes both connector pairs in the Venture locked form (`sebp_multi`), unlocked is the default (the Tech Hunter form, the form that was seen to paste).
  No `Warden` model is registered for it.

## 9. Paste and load

1. Delete any old `Warden` blueprint folder in the game's blueprint directory before copying the new one in (stale `bp.sbcB5` cache), `Warden` and `Warden Lite` and their ids differ.
2. Paste the ship in creative (it is a large grid, 1,208 blocks plus the pod's 605, PCU 10,297; each Hornet adds about 1,010). The pod comes with it, docked.
3. Ownership: the blocks carry the owner's save id and the game re-owns them to the pasting player.
4. Scripts: `ShrikeAI.cs` is the Helm, `StaffAI.cs` the Staff, `CradleBayAI.cs` the Pod PB (M6: there is no separate `HelmAI.cs`; the generator embeds them from `scripts/`; if the PBs
   have no program). Custom Data is pre-set (`[Ship] Tag=WARDEN`).
5. What is already loaded: every gun with its magazines (railguns 6 + capacitor full, gatlings 24 boxes, artillery / assault 6 shells), 100 kg uranium in each reactor, both tanks full, all batteries full,
   the lockers with spares. Nothing else needs loading; the drones (not included) are the only thing to add.

## 10. Known limits

1. Railguns have no feed (section 7); artillery and assault turrets draw from the lockers through the junctions only if that network pastes working.
2. The pod is empty in `Warden` (use `Warden Wing` for four Hornets already clamped); the scripts are the shared ones (section 13) and everything about the pod across the connectors is unverified (Pod.md C1..C4).
3. Event Controllers: only EC Core has an event configured (integrity, a seen id); EC Cockpit and EC Power exist but their event numbers (cockpit occupied, stored power) are unverified, set them by hand.
4. `Run` toolbar slots (cockpit 7, 8 and EC Core) have no argument (the argument form is unseen).
5. No lights, beacon, LCD or cargo conveyors to the pod. Large grid, space only (no gravity budget).
6. Slab sides and hard sponson edges make the silhouette boxy next to a hand-built ship; no `Armor2x1` or round blocks are used.
7. The thumbnail written by `build_all` shows the ship grid only (the pod is a second grid).
8. Terminal groups with multi-cell weapons (`B1`, `PD`, `Main Turrets`, `Weapons`) store the block-local cell (0, 0, 0) of each member, because the game's own `Center` cell of the large rail / turret definitions is not in any sample (32 notes in `bp.notes`); the game should resolve any cell of a block, unverified. If a group comes up short of members, re-add them by hand.
9. The generator writes no `<ConveyorLines>` (game saves store one `MyObjectBuilder_ConveyorLine` per adjacent ported pair, `docs/xml-notes.md`). The junction network may therefore not connect until the game rebuilds the lines: after the first paste, check that a thruster shows fuel from the tank; if not, grind and rebuild one junction per branch (or cut and paste the ship once). Not emitted because it needs a change inside sebp's grid writer; a later tool task.

## 11. In-game checks (first paste)

1. Does it paste as two grids (ship + pod) with no DLC warning; does the terminal list 8 doors, 24 gyros, 24 thrusters?
2. **Fuel**: tanks show 100 %; every thruster has hydrogen (terminal info "Hydrogen: ... filling"); fly: all five groups thrust. If not, the junction network is not connected: check one thruster's back cell and
   the tank terminal first (`[SH] Junction 1..101`).
3. **Ammo**: assault / artillery turrets pull from the lockers (take a shell out of one turret, watch it refill). Gatlings have their own 24 boxes.
4. Railguns: load and capacitor full on paste? (`RemainingAmmo`, `StoredPower`.)
5. Hangar: `[Hangar 1]` doors open and close, no panel hits the pod, the lane is clear, the pod stays docked under 1 m/s2 (C3 in `Pod.md`); connector status of the two hangar connectors (C2).
6. Pod: C1, C4 (`Pod.md` section 8); T1 with the first drone.
7. PCU and mass in the world's limits; gyro feel (0.16 rad/s2 yaw: a 180 degree turn takes about 9 s with full torque, accelerate then brake).
8. Cockpit: seat view through the bridge glass, the toolbar slots, the Remote Control as script reference.
9. Beam, hull and tower proportions against the renders; the orange colour of the sponson belts; decoys are dark squares.

## 12. Files, commands, renders

* `tools/designs/warden.py` (this ship; `python3 -I tools/designs/warden.py [--render DIR] [--plans]` prints both variants, the numbers JSON, renders or deck plans), `tools/designs/pods.py`,
  `tools/designs/_shiprender.py` (the renderer: slopes and corners drawn as wedges, turrets as base+head+barrel, thruster nozzle glow, cut-away section and lane views),
  `tools/tests/test_warden.py` (25 tests: rules, naming, loadout, catalogue bands, conveyor network, hangar, locked variant, pod).
* `python3 -I tools/build_all.py --only 'Warden' 'Cradle Pod' --no-zip` builds, validates (mounts, one piece, overlaps, DLC), re-reads the written file and checks the second grid;
  `python3 -I tools/tests/run_all.py` runs everything; `tools/audit.py --types out/Blueprints/Warden/bp.sbc` reports 100 % attached on both grids.
* Renders for this design record: `scratchpad/ship_renders/Warden/` (iso front-left / front-right / rear / belly, top, side, front, section, deck, lane; `warden_sheet.png` is the contact sheet).
  Four design iterations were looked at: (1) hull only with the demo style (octagonal bow, hollow with generated partitions), (2) the arrowhead bow, forecastle step, rails in channels, blister,
  (3) systems placed, T-bridge, recessed assault turrets, colour zones and the orange hangar frame, (4) the fuel / ammunition network, hard-edged sponson belts, 8 gyros.
* Shapes: sample-derived XML for every block (`sebp.shape_summary()`); of the type-level ones only `Reactor/LargeBlockSmallGenerator`, `RemoteControl/LargeBlockRemoteControl` and
  `TimerBlock/TimerBlockLarge` come from a sibling subtype of the same type. The Full variant adds the unseen shapes `SensorBlock/LargeBlockSensor` (from the source), `OffensiveCombatBlock` and
  `FlightMovementBlock` (guesses); Lite has none of them.

## 13. Flight and fight (M6, simulator only)

Nothing here was measured in the game; large-grid damage in the sim is optimistic (docs/audit: large thrusters and batteries die 1.6-5.9 times too slowly), hydrogen is unlimited, there is no conveyor model, the Pirate and the
Tech Hunter are stand-ins flown by the sim's Pirate brain, and the docked pod is held rigidly to the hull (`RigidLink`, no pod mass). Commands as in `Shrike.md` section 12; `Warden Wing` is the Warden with four Hornets in the pod.

**Does the Warden need its own Helm script?** No. `ShrikeAI.cs` flies it unchanged with the class numbers below in its Custom Data; the audit points were: thrusters and gyros (the script reads mass and per-direction
thrust from the Remote Control and the thrusters, and writes overrides to the 24 thrusters and 24 gyros: fine), `MaxTurn` calibration (the script measures the angular acceleration while the command saturates and sets
`0.85 sqrt(2 a 0.6 rad)`: **0.51 rad/s** against the analytic 0.85 sqrt(2 x 0.30 x 0.6) = 0.51, after the first slew from a random heading), stand-off for 2 km railguns (the two railguns are 2 of 16 weapons; the
turrets decide, see below), turret-only mode (on: `Batt B1* RAILx2 ... TO`, the fixed rails refuse targets they cannot hit while the turrets work).

**What the sim said about the settings** (Warden vs Pirate, same seeds, cap 400-600 s):

| setting | result |
|---|---|
| nothing set (stand-off from the weapons = 1,000 m, the two rails dominate) | 3 seeds: 1 win (304 s), 2 timeouts at 600 s: at 1,000 m only the rails (one slug per 30 s each) and the artillery reach, the 800 m assault and gatling turrets do not |
| `Standoff Range=700`, `Standoff By Weapons=false` | 3/3 wins (128-184 s); 450 m: 3/3 (155-243 s) |
| jinking on / off at 700 m, 8 seeds each | 8/8 both; median 90 s / 114 s (sd about 90 s: no difference; off because a 12 m/s random change is more than the 1.7 m/s2 strafe thrust can follow, it only burns hydrogen) |
| AI Block Search Assist on / off, 6 seeds each | 6/6 both; median 101 / 100 s, longest 360 s / 150 s. On, the idle ship wanders (5-8 m/s, 1 rad/s yaw) while the AI block searches, which also keeps the bays' gates shut: off |
| artillery + assault turrets on target group `Weapons` (Staff `Turret Target Group`) | 6/6, 53-86 s (median 76) against median 100 s without |

Class data in the Helm Custom Data: `Standoff Range=700`, `Min Range=150`, `Evasive Jinking=false`, `Orbit Speed=10`, `Combat Speed=40`, `AI Block Search Assist=false`, `[Battery] Standoff By Weapons=false`.

**Flight** (scripted pilot at full stick, dampeners on):

| | design | sim |
|---|---|---|
| forward / other axes | 4.03 / 1.67 m/s2 | 4.07 / 1.7 m/s2 |
| braking, stick released | 1.7 m/s2 | from 40.7 m/s: 23.9 s, 492 m (analytic 23.5 s, 470 m) |
| yaw | 0.16 rad/s2 (solid-box inertia; the block masses give up to 0.30) | **0.30 rad/s2**: a 90 degree turn about 4.6 s, 180 degrees about 6.5 s (the doc's "9 s" was the pessimistic figure); rotation stops 4.4 s after the stick is released |

**Fights** (5 seeds each, cap 600 s vs the Pirate, 300 s otherwise):

| enemy | Warden | Warden Wing (`launch all` at 3 s, ring at 20 s, `wing hunt` at 40 s) |
|---|---|---|
| Pirate (2.8 km) | **10/10 wins** (seeds 0-4: 53-86 s, median 79; seeds 5-9: 51-77 s, median 65), about 25 blocks lost | **5/5**, 55-81 s (median 65) |
| 3 Hornets (1,000 m) | 5/5, 65-106 s (median 92) | 5/5, 28-76 s (median 44) |
| 6 Gnats (1,000 m) | stalemate (nobody fires) | stalemate |
| 6 Gnats (400 m) | 4/5 wins (135-264 s), 1 loss (a precise Gnat swarm disabled the ship at 116 s with 76 blocks gone) | 5/5 (50-131 s) |
| Tech Hunter (13,666 blocks, cap 300 s) | 1 seed: **out-gunned** - lost 418 blocks (23 %) in 300 s, dealt 559 k; the Hunter is down to 36 of 76 armed blocks (12,375 of 13,666 blocks), nobody dead | 1 seed: lost 352 blocks, dealt 1,410 k; the Hunter down to 42 of 76 armed blocks (11,693 blocks), nobody dead |

**Manual, dead-man, wing, instructions.** `selftest m6` (2 seeds each): the Helm and the Staff hold exactly the ship's own 24 gyros, 24 thrusters, 2 fixed guns, 14 turrets, 8 batteries, the bridge seat (the `Gunnery Seat` and the Remote Control do not count: a gunner
in the open seat does not switch the Helm to MANUAL), 5 cameras and the `(deadman)` timer, nothing of the pod; manual flight with an idle Hornet 650 m away: 0 override-fight ticks, fixed guns quiet, the turrets kill it (54 -> 34-40 blocks),
AUTO back after 1.5 s; Helm PB killed at 15 s: all overrides released 1.08 s later, once, `HELM LOST`, Timer D fires. `launch all` at 4 s while hunting: the Helm goes idle, the doors open, all four Hornets have left 25 s after the command, the Helm is handed HUNT back, the roll call counts
4, the ring is 98-102 m from the ship. Without `launch`, `release all` waited for ever ("ship turns 0.45 rad/s", "ship accelerates 2.8 m/s2": the Helm sweeps for targets at 0.2-0.3 rad/s and orbits at 10 m/s). Instructions (IL model, with launch): Helm 27,358, Staff 11,812, pod
bay 26,267 (the lane check); block model 1,852 / 650 / 616.
