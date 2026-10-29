# Aegis Carrier (`tools/designs/aegis_carrier.py`): implementation plan

Status: **BUILT** (2026-10-20), nothing pasted in the game yet. The sections below are the plan; section 0a lists what the build changed and the measured numbers (they replace the
"(est.)" figures). Build: `python3 -I tools/build_all.py --no-zip --only "Aegis Carrier" "Aegis Carrier Wing"`, tests `tools/tests/test_aegis_carrier.py` (28), the zips are NOT rebuilt.

Owner request: a large-grid mega carrier named AEGIS, modelled on the player mothership of the game Starscape (reference images: a top view of a
near-circular, rounded-square disc in grey metal with panel seams, cyan light strips on the rim arcs, a raised central hub ring around a dark
circular launch well, two small cyan-lit pods north and south on the hub, flat bay doors on the left and right of the rim, and four boxy
engine/weapon pods at the diagonal corners standing outside the disc, each with a blue exhaust plume).

Templates reused by import, not copied: `tools/designs/leviathan.py` (ship scaffolding, hangars, conveyor router, figures), `tools/designs/pods.py`
(Cradle Pod, docking, drones), `tools/hulls.py` (envelope, hull builder, zones, greebles), `tools/designs/ballista.py` (`PD_SETTINGS`),
`tools/designs/_common.py` (scripts, gravity, walkways, cameras).

---

## 0. Decisions in one page

| item | decision |
|---|---|
| model key / blueprint folder | **`Aegis Carrier`** (module `aegis_carrier`). No clash: grep found no `Aegis Carrier`, `aegis_carrier` or `AEGISC` anywhere; the escort drone `Aegis` (extra.py, AegisAI.cs, AegisStaffAI.cs, `SUPPORT_STAFF['AEGIS']`, `Aegis Shipyard`) is not touched, and nothing in tools/ or sim/src matches model names by prefix (exact names only). |
| ship tag | **`AEGISC`** (`[Ship] Tag=AEGISC`; IGC `AEGISC.HB`, `.S2P`, `.P2S`, ...). Not `AEGIS`: that is the drone's type tag (`AEGIS_CMD`, `AEGIS_HOME`, in `[Wing] Types`, `SUPPORT_STAFF`, `SUPPORT_AUX`, TurretAI listen list). |
| size | disc **63 cells = 157.5 m** across (x and z), **22.5 m** thick at the core, **7.5 m** at the rim edge, hub ring **+5 m** on top; corner engine pods out to the diagonal radius of **110 m** (bbox 157.5 x 35 x 157.5 m incl. the bridge). Bigger than the Leviathan in footprint (24,800 m2 vs about 11,000 m2), a little lighter. |
| armour | about **10,500** blocks (prototype: 7,802 cubes, 1,026 Slope2 pairs, 360 slopes, 284 corners; hull builder accepted the envelope, mount check passed). |
| mass (est.) | about **15.6 kt** ship grid with pre-loads; COM about 1.2 m below the disc mid-plane. |
| thrust | **24** `LargeBlockLargeHydrogenThrust`, 6 per corner pod; **28.8 MN on every one of the six axes** (4 thrusters per direction), **1.85 m/s2** on every axis (est.). |
| gyros | **144** `LargeBlockGyro` (Leviathan v2.8 precedent): yaw **0.060**, pitch **0.117**, roll **0.121 rad/s2** (est.); 90 deg of yaw in 10.2 s. |
| drones | **24 slots = 6 docked 4-slot Cradle Pods** (separate CubeGrids): **4 pods in the central well** (16 slots, launch **up**, +Y) and **1 pod in each side bay** (8 slots, launch **port / starboard**). Hornet class (Hornet, Wasp, Harrier, Watcher, Lancer, Aegis drone). |
| weapons | 2 large railguns `[B1]` (bow), 4 artillery (pod tops), 4 assault (pod bottoms), 16 vanilla gatling turrets for point defence (8 dorsal, 8 ventral, `PD_SETTINGS`), 12 decoys. No rotor turrets (no Turret PB). |
| PBs | ship: `[Helm][SH] Aegis Helm` (ShrikeAI.cs), `[Staff][SH] Aegis Staff` (StaffAI.cs); each pod: `[Pod] Pod PB n` (CradleBayAI.cs). **No new PB** and no Helm change. One tiny StaffAI change (hangar sections 1..4 -> 1..8). |
| PCU (est.) | ship **~29.8 k**, + 6 empty pods 9.9 k = **~39.7 k** (fits a 50 k world); with 24 Hornets **~64 k** (needs a higher world limit: question Q1 for the owner). |
| models | `MODELS = {'Aegis Carrier': aegis_carrier}` (one entry, Full). The 24-drone ship `Aegis Carrier Wing` is a **test ship** (`aegis_carrier_test.py` in `TEST_MODULES`): built to out/Blueprints for the sim, never packaged (64 k PCU). |

---

## 0a. As built (what changed against the plan, measured numbers)

| item | as built |
|---|---|
| size | disc 63 cells = 157.5 m, bounding box 162.5 x 47.5 x 162.5 m with the corner pods and the cameras; **11,089 blocks** in the ship grid (3,658 heavy + 6,757 light armour, 22 windows, panels), +605 per pod |
| mass / thrust | **15,426 t** with ammunition and uranium (15,326 t blocks); **28.8 MN and 1.87 m/s2 on every one of the six axes** (4 thrusters per direction); thrust torque about the COM 0.2-0.7 % of the gyro torque |
| gyros | 144: 4,838 MN m, **0.062 yaw / 0.121 pitch / 0.122 roll rad/s2** (90 degrees in 10.1 / 7.2 / 7.2 s) |
| PCU | ship **30,050**, six empty pods 6 x 1,643 = 9,858, **39,908 in all**; with 24 Hornets about 64,000 (the `Aegis Carrier Wing` test ship) |
| power / fuel | 360 MW (1 large + 4 small reactors, 900 kg uranium), 36 MWh in 12 batteries, railgun recharge 76 MW, surplus 282 MW; 4 tanks = 60 ML, full forward burn 51.9 min (each pod fuels only its own thrusters), everything at 100 % 8.6 min |
| weapons | 2 rails (6 slugs each), 4 artillery, 4 assault, 16 `[PD]` gatlings with `PD_SETTINGS`, 12 decoys; sustained data DPS about 30,000 |
| conveyors | 112 junctions in four independent networks (one per engine pod, each reaching its 6 thrusters, 2 turrets, tank and locker) |
| drones | six pods, 24 slots; all six dock on both connector pairs to 2 mm; the four well pods stand nose up with their Up pointing away from the well axis; 24 Hornets fit (0.25 m clear of the hull in every pod) |

Changes against the plan:
* **Hub ring** radii 14.6 / 16 cells (plan 14 / 15.5) so the bridge cabin sits on the hub; the cabin is **9 cells wide** (x -4..4, interior 7 x 2 x 3, z -14..-10, interior z -13..-11): the plan's cabin hung 1 cell over the well edge.
* **Bridge layout** (mirror symmetric): AI Offensive (-3), RC (-2), Bridge Cockpit (0), Gunnery Seat (2), AI Flight (3) on z = -13; Helm (-3), EC Cockpit (-2), Timer (0), EC Core (2), Staff (3) on z = -11, EC Power on top of the timer; the walkway row z = -12 has a slide door in each side wall (x = +-4) and the gravity generator on the centre line.
* **Cameras**: the rim cells are Slope2 stairs, none is a flat cube. Six cameras stand on the flat outer faces of the engine pods in mirrored pairs (Front P/S, Rear P/S, Port, Starboard) plus Top (aft hub pod) and Belly: 8 in all. The two lane sensors stand under the belly near |x| = 26.
* **Antenna** at (0, 9, 11) on the aft hub pod (the front cell of that pod is a slope). **Greebles**: grid patterns only (the `lines` pattern broke the mirror symmetry).
* No `Aegis Carrier` walkway beyond the cabin doors; no TurretAI rotor turrets (as planned).
* `leviathan.py` refactors as planned (`_conveyors` terms / start / first / toward, `_decoys` seeds, `check_hangar` door_x / hangar_y / hangar_z); the Leviathan blueprint hash is identical before and after.
* **Smoothed hull (2026-10-27, owner: "rounded > angled > cubed, no rough cube edges")**: the planform is now an octagon whose norm grows by exactly one per grid row (`max(|x|, |z|, |x| + |z| - c)`), the rim bevel is 45 degrees (one cell of height per cell), the hub ring has one step (plateau y 6, shoulder y 5), the hull uses the round slope / corner blocks and no Slope2 pairs, the skin is the 26-neighbour skin, and each engine pod has a short root stub and chamfered vertical and horizontal edges. A circular contour or the regular-octagon norm left a tooth at every tread edge on the diagonals. No surface greebles (panels read as studs). Measured: 10,367 blocks in the build, about 29,850 PCU in the ship grid, 15.5 kt, thrust unchanged (1.9 m/s2 on all axes). The pod ammo lockers moved down one cell and the bow locker back by three cells, the lane sensors stand on the flat belly.
* **Square planform (2026-10-29, owner: the diagonal corners looked jagged)**: every diagonal contour of a cell grid is a staircase, so an octagonal or round disc left stepped cubes beside the sloped sides (rim bevel diagonals, hub shoulder, pod stubs). The disc is now a **square slab 59 cells (147 m) across** with a 45 degree rim bevel that runs out to a one-cell edge, a **square hub ring** (plateau y 6, one shoulder step y 5) around a square well, and the four engine pods stand in **notched corners** (pod box |x|, |z| 22..31, no stub). Only axis-aligned slopes remain; `fillets=True` adds the inverted corners in the concave edges. The four diagonal PD gatlings moved to (+-17..19, +-17..19). Measured: about 9,900 blocks, 13.2 kt, 2.2 m/s2 on all axes, about 29,400 PCU in the ship grid. The octagonal version is in the git history (commit 7d81b7d) if the rounder outline is preferred over the cleaner slopes.
* **Not run in this session:** the sim smoke test (no dotnet in the cloud container) and `tools/compile_check.sh` (no mcs); the only script change is the `[Hangar n]` loop bound 4 -> 8 in `StaffAI.cs`.

---

## 1. Frame and the shape recipe

Cells (2.5 m): x Right, y Up, z Backward (bow = -z), **the disc axis is the centre of cell (0, *, 0)**, mirror plane x -> -x through the centre of
cell 0 (`Volume.mirror('x', 0.5)`, as the Leviathan). The disc is also built mirror-symmetric in z; only the bridge (north), the aft pod (south)
and the bow railguns break the z symmetry. `test_symmetric` (x only) must hold for everything except the conveyor junctions.

### 1.1 Planform: rounded square (superellipse), no new primitive

```python
R, P = 31.5, 2.6                       # superellipse radius (cells) and exponent: 2 = circle, 2.6 = the rounded square of the reference
WELL_R, HUB_TOP_R, HUB_R = 9.0, 14.0, 15.5
def rho(x, z): return (abs(x) ** P + abs(z) ** P) ** (1 / P)     # planform norm, cell centres (cell 0 centre = axis)
def rc(x, z):  return math.hypot(x, z)                           # true circle for the hub and the well
def half_t(r): return 4 if r <= 26 else 3 if r <= 28 else 2 if r <= 30 else 1   # 2:1 rim bevel in steps of 2 cells (Slope2 pairs)
```

Everything is `Volume.from_function` / `Volume.box` / `.chamfer` / `.mirror` from `tools/hulls.py`; **no new primitive is needed**.
(Optional nicety, not required: a `hulls.superellipse(axis, start, stop, center, radius, p)` classmethod next to `Volume.cylinder`.)

| part | recipe | cells |
|---|---|---|
| disc | `from_function((-32,-4,-32), (33,7,33), f)`: inside `rho <= R`; y from `-half_t` to `+half_t`, except the hub: `rc <= 14` -> top y = 6, `14 < rc <= 15.5` -> top y = 5 (bottom stays -4); the **well** `rc <= 9.0 and y >= 4` is removed | x, z -31..31 |
| bay modules | `B((23,-2,-5), (32,3,6)).mirror('x', 0.5)`: the flat E/W door faces at x = +-31, 5 cells tall (y -2..2), 11 long (z -5..5) | |
| engine pods | `B((22,-3,22), (32,4,32)).chamfer('y', 2)`, then `.mirror('x', 0.5).mirror('z', 0.5)`: four 10 x 7 x 10 boxes (25 x 17.5 x 25 m) with chamfered vertical edges, overlapping the disc corner by 2-3 cells | x, z +-22..31, y -3..3 |
| bridge cabin (north hub pod) | `B((-3,7,-13), (4,10,-9))`: x -3..3, y 7..9, z -13..-10 (17.5 x 7.5 x 10 m) | on the hub top |
| aft pod (south hub pod) | `B((-2,7,10), (3,9,14))`: x -2..2, y 7..8, z 10..13 | on the hub top |

Profile (radius -> y range): core `rho <= 26` y -4..4 (22.5 m); rim steps y -3..3 (to 28), -2..2 (to 30), -1..1 (to 31.5, the 7.5 m edge);
hub ring y -4..6 (`rc` 9..14), step y -4..5 (`rc` 14..15.5); well floor at y = 3 (the well is 3 cells = 7.5 m deep, y 4..6, diameter 45 m).

`solid = disc | bay | pods | cabin | aftpod` -> **29,390 cells**, one piece, `symmetry_error('x', 0.5) == 0` (prototype).
Shell: `shell = solid.hollow(1) | B((-3,6,-13), (4,7,-9))` (the cabin floor), `shell &= solid`. **No bulkheads** (a 10-cell bulkhead grid in x and z
costs about 3,500 more blocks; the systems sit on the bottom skin, and the skin alone is one piece). Prototype: `build_hull` on this envelope with all
system cells reserved -> **10,498 armour blocks in 0.4 s**, mount-face check passed.

### 1.2 Reserved volumes and `build_hull`

```python
res = H.build_hull(bp, solid, fill=shell, reserved=reserved, zones=_zones(),
                   skin_around=['hangar', 'doors', 'hconn', 'wconn', 'thr', 'rails'],
                   windows=lambda c: c[1] in (7, 8) and ((c[2] == -13 and abs(c[0]) <= 2) or (abs(c[0]) == 3 and -12 <= c[2] <= -10))
                                     or (c[1] == 7 and abs(c[0]) == 2 and 11 <= c[2] <= 12))
```

Reserved: side hangar interiors, door cells, hangar connectors, well connectors, every thruster, tank, locker, the two railguns (`rails`), the
belly / pod-bottom turrets that recess into the hull (none in this layout: every turret stands on the hull, so `turrets` cells are outside the envelope).

### 1.3 Colours, cyan strips, seams (zones, first match wins)

Starscape palette instead of the family charcoal (the owner asked for this ship's look); systems keep `leviathan.GUNC`.

| constant | hsv | use |
|---|---|---|
| `PLATE` | `H.hsv_mask(210, .06, .62)` | default: light grey metal plating (top and sides) |
| `PLATE_M` | `(215, .10, .45)` | rim bevel band, engine pods |
| `SEAM` | `(220, .16, .24)` | panel seams, belly, well interior (reads as the dark hole), nozzle frames |
| `CYAN` | `(186, .80, .95)` | light strips |
| `CYAN_D` | `(190, .75, .70)` | nozzle rings (the blue plumes) and hangar door frames |

Zones (`Zones(...)`, order matters):

1. `hub_strip` CYAN: `9 < rc <= 10.6 and y == 6 and abs(z) <= 4` (the two vertical cyan bars left and right of the well).
2. `rim_strips` CYAN: `28 < rho <= 30.5 and y >= 1` and the compass angle `atan2(x, -z)` within 14 deg of 0, 90, 180, 270 (N/E/S/W arcs; the E/W arcs
   run beside the bay modules); mirror to the belly (`y <= -1`) for a lit underside.
3. `nozzle_ring` CYAN_D, heavy: armour cells edge-adjacent to a thruster nozzle face (as `leviathan._nozzle_ring`, every group, not only main).
4. `hangar_frame` CYAN_D, heavy: `abs(x) == 31`, z -5..4, y in (-2, 2) or z in (-5, 4) (the door frame).
5. `well` SEAM, heavy: `rc <= 10.6 and y >= 3` (floor and inner wall of the well; heavy: the core sits under the floor).
6. `seams` SEAM: `y >= 4 and (x == 0 or z == 0) and rc > 15.5 and rho < 28` plus the ring seam `22 <= rho < 23` on the top (the quadrant cross of the reference).
7. `pods` PLATE_M, heavy: `abs(x) >= 22 and abs(z) >= 22`.
8. `rim_belt` PLATE_M, heavy: `rho >= 29`.
9. `bow_guard` heavy: `z <= -28 and abs(x) <= 6` (around the railgun muzzles).
10. `belly` SEAM, `flat=('Down',)` not used (the belly is bevelled like the top): `y <= -1` colour only.
11. default PLATE, light.

Prototype heavy / light split with this policy: **about 2,900 heavy, 6,900 light** (+ the slope pieces, same proportion).

**Light strips: paint only.** No light block has a known XML shape: `InteriorLight/SmallLight`, `LargeBlockLight_1corner`, `_2corner`,
`ReflectorLight/LargeBlockFrontLight` are DLC-free (`defs.json`), but none is in `tools/shapes.json` nor in `sebp.KNOWN_TYPES`, so sebp would
write a guessed shape (the conduit, troffer and floodlight blocks are DLC: never). Real glowing strips are an owner question (Q4): a game-written
sample with one `SmallLight` lets `tools/extract_shapes.py` add the shape; then a row of lights behind the CYAN band is a one-function add.

Greebles (`H.greeble`, `ArmorPanel`, 1 PCU each): hub top `pattern='grid', every=3, normals=('Up',), where=10.6 < rc <= 14 and y == 6`, colour SEAM
(the instrument blocks of the reference); disc top `pattern='lines', every=4` along the seams; engine pod sides `grid, every=3`, PLATE_M.
About 250 panels.

---

## 2. Engine / weapon pods, propulsion, gyros, power, fuel

### 2.1 Pod layout (one table, mirrored to the four corners)

Local coordinates per pod: `u = |x| - 22`, `w = |z| - 22` (both 0..9, growing outward), `y` as the ship. Mapping of a box `(u, y, w)` min corner and
size `(su, sy, sw)` to ship cells for the pod at signs `(sx, sz)`:
`x0 = 22 + u if sx > 0 else -(22 + u + su - 1)`, `z0 = 22 + w if sz > 0 else -(22 + w + sw - 1)`.
Fore pods are `sz = -1` (bow side), aft pods `sz = +1`.

| role | local min (u, y, w) | size | exhaust (local) | aft pod | fore pod |
|---|---|---|---|---|---|
| axial 1 | (2, -1, 7) | 3x3x3 | +w | **Main** (exhaust Backward, thrust Forward) | **Brake** (exhaust Forward) |
| axial 2 | (5, -1, 7) | 3x3x3 | +w | Main | Brake |
| lateral low | (7, -3, 2) | 3x3x3 | +u | exhaust outward: port pods Left (push Right), starboard pods Right | same |
| lateral high | (7, 1, 2) | 3x3x3 | +u | " | " |
| up | (1, 1, 1) | 3x3x3 | +y (thrust Down) | | |
| down | (1, -3, 1) | 3x3x3 | -y (thrust Up) | | |
| hydrogen tank | (4, -1, 2) | 3x3x3 | | `LargeHydrogenTank`, full | |
| ammo locker | (1, -1, 4) | 3x3x3 | | `LargeBlockLargeContainer` | |
| artillery turret | (4, 4, 5) | 3x3x3 | | on the pod top (y 3), `Up` | |
| assault turret | (4, -5, 5) | 3x2x3 | | under the pod bottom (y -3), `Down` | |

Verified in the prototype (scratch script): no two boxes overlap; every thruster, tank and locker cell lies inside the envelope (the inner-aft
chamfer forced axial 1 to u = 2); **every thruster's 7-cell lane over its whole nozzle face (`sebp.LARGE_H2_LANE`, `keep_clear(b, 7, 'exhaust', face=True)`)
is free of hull and systems**; the turrets stand on hull cells with their base cell. The up/down thrusters overlap the disc corner (reserved cells);
the disc is only y -2..2 there, so the up/down lanes are clear.

Names: `[SH] Thruster Main P/S n`, `Brake P/S n`, `Strafe <pod> hi/lo`, `Lift <pod>`, `Dive <pod>`, with `<pod>` = `NW`, `NE`, `SW`, `SE`;
`[SH] Hydrogen Tank NW..SE`, `[SH] Ammo Locker NW..SE`, `[SH] Artillery Turret n <pod>`, `[SH] Assault Turret n <pod>`. Roles for
`leviathan._terminals`: `thr_main`, `thr_brake`, `thr_side`, `thr_lift`, `thr_dive`, `tank`, `cargo`, `art`, `assault`.

### 2.2 Thrust per axis (balanced disc: equal on all six axes)

| direction | thrusters | force |
|---|---|---|
| Forward | 4 Main (2 per aft pod) | 28.8 MN |
| Backward | 4 Brake (2 per fore pod) | 28.8 MN |
| Left / Right | 4 each (2 per pod on the opposite side, the pods fore and aft of the COM) | 28.8 MN each |
| Up / Down | 4 each (one per pod) | 28.8 MN each |
| **total** | **24 large hydrogen thrusters** (288 PCU, 167 t) | |

Accelerations (est., 15.6 kt): **1.85 m/s2 every axis** (Leviathan: 2.04 forward, 1.02 others). Torque about the COM: the axial and lateral
pairs act at y = 0 (cell centre) and the COM is about 1.2 m lower: 28.8 MN x 1.2 m = 35 MN m = **0.7 %** of the gyro torque; the Up/Down
quartet is symmetric in x and z (COM z offset 0.5 m -> 0.3 %). `test_thrust_balance_about_the_centre_of_mass` (< 10 %) as the Leviathan.

### 2.3 Mass, inertia, gyros (prototype point-mass model; `numbers()` replaces them)

Mass about 15.6 kt (armour 12.5 kt, gyros 1.85 kt, turrets 0.22 kt, rest systems), COM (0, -1.2, -0.5) m, inertia yaw / pitch / roll
**8.0e10 / 4.15e10 / 4.0e10 kg m2** (a disc is twice as stiff in yaw as in pitch). 144 gyros x 33.6 MN m = 4,838 MN m ->
**0.060 / 0.117 / 0.121 rad/s2**, 90 degrees in 10.2 / 7.3 / 7.2 s. Placement: four banks of 6 x 6 on the bottom skin (y = -3, a gyro mounts
through its bottom face only, one layer), min corners `(10,-3,10)`, `(-15,-3,10)`, `(10,-3,-15)`, `(-15,-3,-15)` (x, z 10..15 per quadrant,
outside the well's core room), `[SH] Gyroscope 1..144`, `toward='Down'`.

### 2.4 Power, fuel, stores

| system | blocks | where |
|---|---|---|
| reactors | `LargeBlockLargeGenerator` (500 kg U) + 4 x `LargeBlockSmallGenerator` (100 kg U) = **360 MW** | core room under the well floor: large at (-1,-3,-1); small at (+-4,-3,-2), (+-4,-3,1) |
| batteries | 12 x `LargeBlockBatteryBlock` full = **36 MWh**, 144 MW | core room: x in {+-5, +-6}, y -3, z -1..1 |
| loads | railgun recharge 2 x 38 = 76 MW, about 1 MW else; hydrogen thrusters draw none; surplus about 283 MW | |
| hydrogen | 4 x `LargeHydrogenTank` (15 ML each, full), **one per pod, feeding only its own pod** | section 2.1 |
| endurance | full forward burn: each aft pod 2 x 4,820 L/s from its own 15 ML = **26 min**; a pod at 100 % on all six thrusters 8.6 min | |
| stores | 4 pod lockers (each: 100 artillery shells, 150 assault shells), 1 bow locker `[SH] Ammo Locker Bow` (x -1..1, y -3..-1, z -24..-22): 20 rail slugs, 100 gatling boxes | |
| gravity (Full) | `C.place_gravity(S, res, env, (0, 7, -11), (20.0, 10.0, 15.0), nm('Gravity Generator'))` | bridge cabin |

### 2.5 Conveyors: four short networks instead of one long one

One network per engine pod: terminals = the back-face cells of its six thrusters (the up/down pair share `(2, 0, 2)` local), the tank top, the locker
top, the artillery base (pod top skin) and the assault base (pod bottom skin); start = the tank top. About 20-30 junctions per pod, **~100 in all
(~1,000 PCU)** instead of ~300 for one network across 60 m of disc. Each pod is an independent fuel island (damage isolation); the price is that the
aft pods' tanks empty first on a long forward burn (Q5). The railguns are not fed (as the Leviathan: 6 slugs, reload by hand from the bow locker);
gatlings keep their own 3,360 rounds.

**Refactor needed in leviathan.py (no behaviour change for the Leviathan):** `_conveyors(S)` routes one network over all terminals, names
`Junction 1..n` and resets `S.conveyors`. Change it to

```python
def _conveyors(S, terms=None, start=None, first=1, toward=(0, 2, 82)):
    """terms: [(cell, role, label)] (default _terminals(S)); start: first network cell (default the first tank terminal);
    first: number of the first junction name; toward: sort key point of the routing order"""
```

with `S.conveyors` created once (`if not hasattr(S, 'conveyors'): S.conveyors = []`) and appended to, names `Junction {first + i}`, return the
network. The carrier calls it four times (`first` = 1 + junctions so far, `toward` = the pod centre). Prove the Leviathan unchanged: sha256 of
`out/Blueprints/Leviathan/bp.sbc` before and after (`build_all.py --no-zip --only Leviathan --force`), plus `test_leviathan.py`.
`C.link_conveyors(bp, [(c, n) for c, r, n in L._terminals(S)])` afterwards, as the Leviathan.

---

## 3. Bridge, sensing, control blocks

Bridge cabin (north hub pod) interior x -2..2, y 7..8, z -12..-10 (30 cells), cyan windows on the front (z = -13) and sides. Place every pair
mirrored in x (the symmetry test checks occupied cells, not block types):

| block | cell | name |
|---|---|---|
| `LargeBlockCockpitSeat` (main cockpit) | (0, 7, -12) | `[SH] Bridge Cockpit` |
| `LargeBlockCockpitSeat` | (2, 7, -11) | `[SH] Gunnery Seat` |
| `LargeBlockRemoteControl` | (-2, 7, -11) | `[SH] Remote Control` |
| `LargeProgrammableBlock` | (-2, 7, -10) | `[Helm][SH] Aegis Helm` (ShrikeAI.cs) |
| `LargeProgrammableBlock` | (2, 7, -10) | `[Staff][SH] Aegis Staff` (StaffAI.cs) |
| `TimerBlockLarge` | (0, 7, -10) | `[SH] Timer D (deadman)` (5 s, silent; toolbar Thrusters / Weapons Off) |
| 3 x `EventControllerLarge` | (-1, 8, -10), (0, 8, -10), (1, 8, -10) on top of the row (or y 7, z -12 at x +-1 and (0,8,-12)) | `[SH] EC Cockpit`, `EC Power`, `EC Core` (integrity of the 5 reactors, 0.4, inverted, toolbar Run Staff) |
| `LargeOffensiveCombat`, `LargeFlightMovement` (Full) | (-2, 7, -12), (2, 7, -12) | `[SH] AI Offensive [AI]`, `[SH] AI Flight [AI]` |

(EC cells: whichever mounts `orient_attached` accepts; `_bridge` tries the listed cells in order, as the Leviathan's turret-PB loop.)
`build_all` adds the `[Thrust]` PB by itself (`C.add_support_pbs`, ShrikeAI contains `TvAim(`), as for the Leviathan.

Sensing:

| block | cell, look | name |
|---|---|---|
| `LargeCameraBlock` x 6 (`C.cam`) | (0,0,-32) Forward; (0,0,32) Backward; (-32,0,8) Left; (32,0,8) Right; (0,9,12) Up (aft pod roof); (0,-5,18) Down | `[SH] Camera Front/Rear/Port/Starboard/Top/Belly` |
| `LargeBlockCompactRadioAntenna` 20 km | (0, 9, 10) on the aft pod roof, as the Leviathan's lying boom | `[SH] Antenna` |
| `LargeBlockSensor` x 2 (Full) | (+-32, -2, 0) under the door lanes | `[SH] Sensor Lane P/S` |
| `LargeDecoy` x 12 | flush in the skin, six mirrored pairs (seeds: (8,4,-24), (24,2,-12), (12,4,24), (30,0,14), (26,-3,26), (10,-4,4)) | `[SH] Decoy 1..12` |

`leviathan._decoys(S, res)` has its seeds hard-coded: add `seeds=None` (default = the Leviathan list) and call it with the carrier's.
Every ship block name starts `[SH] `; pods `[POD1]..[POD6]`; nothing in a pod carries `[SH]`.

---

## 4. Carrier function: launch well, side bays, six pods

### 4.1 Where the pods sit

| pod | tag / PB | First Bay | dock | nose (launch) | ship connectors (`Connector`, contact face) |
|---|---|---|---|---|---|
| 1 | `[POD1]`, `[Pod] Pod PB 1` | 1 | port side bay, upright | Left (-x) | (-26, 0, -3), (-26, 0, 2), face Left |
| 2 | `[POD2]`, `[Pod] Pod PB 2` | 5 | starboard side bay, upright | Right (+x) | (26, 0, -3), (26, 0, 2), face Right |
| 3 | `[POD3]`, `[Pod] Pod PB 3` | 9 | well, x = -5 | **Up** (+y) | (-5, 3, -3), (-5, 3, 2), face Up |
| 4 | `[POD4]`, `[Pod] Pod PB 4` | 13 | well, x = -2 | Up | (-2, 3, -3), (-2, 3, 2) |
| 5 | `[POD5]`, `[Pod] Pod PB 5` | 17 | well, x = 2 | Up | (2, 3, -3), (2, 3, 2) |
| 6 | `[POD6]`, `[Pod] Pod PB 6` | 21 | well, x = 5 | Up | (5, 3, -3), (5, 3, 2) |

Each pod: `pods.cradle_pod(4, name=f'Aegis Carrier {where} Cradle Pod', tag=f'POD{k}', ship_tag='AEGISC', first_bay=1 + 4 * (k - 1))`, PB renamed
`[Pod] Pod PB {k}` (as the Leviathan). Connector pairs are 5 cells (12.5 m) apart along z: the pod's `ConnectorMedium` spacing for 4 slots
(`pods._conn_spacing(4)` = 25 small cells).

**Side bays** reuse the Leviathan hangar geometry shifted 20 cells out: interior |x| 27..30, y -1..1, z -4..3 (10 x 7.5 x 20 m), connector wall
|x| = 26, eight `AirtightHangarDoor` per side at |x| = 31, y -1..1, z -4..3 (`fwd` = Left / Right, `Up`), groups `[Hangar 1]` (port) and `[Hangar 2]`
(starboard). Dock with `leviathan._dock_upright(bp, pod, conns, 'Left'/'Right', lock)`. Lane (12 m outboard of the door plane, y -1..1, z -4..3)
verified empty in the prototype.

**Launch well** (open to space, no doors, as the reference's dark hole): the well is `rc <= 9.0`, y 4..6 (45 m wide, 7.5 m deep); the four pods
stand **nose up** on floor connectors in the y = 3 skin, slot rows along z, pods 7.5 m / 10 m apart in x. A pod is 7.5 m tall, so its nose ring is
**flush with the hub top** (contact plane y edge 4 + 0.07 m, nose at edge 7.03). Why flush and not deep: the drone's take-off burn ends after
`[Launch] Clear Distance` = 8 m (launch-protocol s3, the drone script's default); a drone starts with its tail 4.5 m below the pod nose, so with the
nose flush its tail is 3.5 m above the rim when the burn ends. A deeper well would leave drones hovering inside the well (that would need a
`[Bay n] Clear Distance` key written by CradleBayAI into the drone's `[Launch]`; not needed with this geometry). Corner clearance: the outer pod's
corner is 7.46 cells from the axis against the well wall at >= 9.06: about 4 m.

Docking in the well: new helper `_dock_well(bp, pod, conns, out_sign, lock)` = `pods.dock_pod(bp, pod, hc, host_face='Forward', want_nose='Up')`
trying both connector pairings, keeping the one whose pod Up points to `(out_sign, 0, 0)` (mirror-looking pods), then
`bp.lock_connectors` on both pairs when `lock` (default True: the Venture locked form, as the Leviathan). The well connectors are
`S.fixed('conn', 'MyObjectBuilder_ShipConnector', 'Connector', (x, 3, z), 'Up', 'Forward', nm(f'Well Connector {k}{"AB"[i]}'), SEAM, kind='conn')`.

Build-time checks (assertions, also tests):
* `leviathan.check_hangar(bp, pod, s)` for the side pods: add keyword arguments `door_x=None, hangar_y=None, hangar_z=None` (defaults = the Leviathan
  constants, no behaviour change) and pass `door_x=32*2.5 ...`, `(-1, 1)`, `(-4, 3)`.
* new `check_well(bp, pod)`: no ship block within 0.3 m of a pod cell (the connector planes excepted, 0.07 m), the 12 m lane above the pod
  footprint (pod x/z extent, y edge 7 .. 7 + 4.8 cells) has no ship block, the pod nose points +y. Uses `leviathan._world_boxes`.
* `pods.assert_clean(bp)` (no DLC, no Prototech in any grid).

### 4.2 Launch protocol, Staff, pods

* Bay numbering is global (`First Bay`), so `release 9` is the first drone of well pod 3, `release all` launches all 24. `CradleBayAI.cs` needs no
  change (`MaxBays = 8` per pod, any `First Bay`; its lane gate only sees the pod grid, which is why the well and bay lanes are asserted at build time).
* `StaffAI.cs` reads `[Hangar n]` for n = 1..4 only (`Load()`: `for (int n = 1; n <= 4; n++)`). **Change 4 -> 8** (one character; StaffAI is
  53,359 of 80,000 minified). `[Hangar 3..6]` carry only `Pod=` (no `Group`: the well has no doors); every `[Hangar n] Group` still feeds the one door
  list, so a well-only release also opens the side doors (harmless; noted in section 9).
* `release all` is cut into one `release a-b <delay>` per answering pod, `Pod Gap` apart; the launch hands the Helm its mode back after the bays have
  let go **or after 90 s**. With six pods the default 20 s gap would put the last pod at 100 s: set **`[Staff] Pod Gap (s)=6`** -> pods start at
  0, 6, ..., 30 s, four drones 2 s apart each (CradleBayAI `Wave Gap`), all out by about 40 s. The well pods launch in parallel vertical lanes 7.5-10 m
  apart; the side pods sideways.

Staff Custom Data (`staff_cd`):

```ini
[Ship]
Tag=AEGISC

[Staff]
Block Prefix=[SH]
Cockpit Surface=1
Helm HUD Surface=0
Wing Surface=2
Turret Target Group=Weapons
Turret Target Names=Artillery,Assault
Pod Gap (s)=6

[Wing]
Types=HORNET,WASP,HARRIER,LANCER,AEGIS,WATCHER

[Hangar 1]
Group=[Hangar 1]
Pod=[Pod] Pod PB 1

[Hangar 2]
Group=[Hangar 2]
Pod=[Pod] Pod PB 2

[Hangar 3]
Pod=[Pod] Pod PB 3
; ... [Hangar 4], [Hangar 5], [Hangar 6] the same with Pod PB 4..6

[Harasser Defence]
Enabled=true
Max Fire Range (m)=1700
Jink Speed (m/s)=10
Away Speed (m/s)=15
```

Helm Custom Data: `f'[Ship]\nTag={TAG}\n' + leviathan.HELM_DATA` (the large-ship class tuning measured on the Leviathan: Standoff 800, Min Range 150,
no jinking, Orbit Speed 10, Combat Speed 40, `[Battery] Standoff By Weapons=false`). A carrier stand-off sweep (800 / 1,200 m) is a later sim task, not
part of the build.

### 4.3 Drones

`Aegis Carrier` ships the six pods **empty** (as the Leviathan). Owner adds up to 24 Hornet-class drones by pasting them against the pod merge blocks
(Midge and Gnat do not fit, Pod.md). The test ship `Aegis Carrier Wing` = `aegis_carrier(wing=4)`: `pods.load_hornets(pod, 4)` in all six pods
(24 Hornets, 31 grids), each pod's drones checked by `pods.check_clearance`, plus `check_well` / `check_hangar` with the drones attached
(0.25 m to the hull, as `test_locked_variant_with_eight_drones...`).

---

## 5. Weapons

| battery | blocks | where | names / groups |
|---|---|---|---|
| `[B1]` RAIL (the Helm's fixed battery) | 2 x `LargeRailgun`, capacitor full, 6 slugs each | x = +-3, y 0..1, z -31..-24 (muzzle flush with the bow rim, `keep_clear(b, 3, 'muzzle')`) | `[SH] [B1] Railgun 1/2`, group `B1` |
| artillery | 4 x `LargeCalibreTurret` (`fill_ammo`) | engine pod tops | `[SH] Artillery Turret n <pod>`, group `Artillery` |
| assault | 4 x `LargeBlockMediumCalibreTurret` | engine pod bottoms (`up='Down'`) | `[SH] Assault Turret n <pod>`, group `Assault` |
| point defence | 16 x `LargeGatlingTurret`, `settings=dict(PD_SETTINGS)` (`from ballista import PD_SETTINGS`: small grids and missiles only) | top deck y 5..7 and belly y -7..-5 at min corners (x, z) = (-1,-22) N, (-1,20) S, (20,-1) E, (-22,-1) W, (14,-16), (-16,-16), (14,14), (-16,14) | `[SH] [PD] Gatling n <place>`, group `PD` |
| decoys | 12 x `LargeDecoy` | skin | group `Decoys` |

Groups: `B1`, `Artillery`, `Assault`, `PD`, `Main Turrets` (8), `Turrets` (24), `Weapons` (26), `Thrusters` (24), `Decoys`, `[Hangar 1]`,
`[Hangar 2]`, `Gyros`, `Reactors`, `Batteries`, `Tanks`. Toolbars as `leviathan._groups_and_toolbars` (cockpit: B1 Shoot, Main Turrets, PD, Decoys,
`[Hangar 1]` open / close, Run Helm, Run Staff, Staff deploy / dock; gunnery seat with `[Hangar 2]`).
Sustained data DPS (Leviathan table): 2 x 817 + 4 x 2,667 + 4 x 1,263 + 16 x 792 = **about 30 k** (Leviathan 86 k with rotor turrets): the drones are
the carrier's main weapon. Power: only the rails draw (76 MW of 360 MW).
Optional later (not in this plan): four PD4L rotor turrets on the hub ring (+2.3 k PCU, `[TU] Turret PB`, TurretAI.cs 17.9 k).

---

## 6. Scripts and Programmable Blocks

| PB | grid | script | minified now | budget | change |
|---|---|---|---:|---:|---|
| `[Helm][SH] Aegis Helm` | ship | `ShrikeAI.cs` | 77,994 | 80,000 | **none** (class tuning in Custom Data only). It is 2,006 short of the budget: any carrier-specific Helm feature must go into another PB with an Opus plan (CLAUDE.md "Script size", `docs/design/flight_split.md` s10), never by trimming. |
| `[Staff][SH] Aegis Staff` | ship | `StaffAI.cs` | 53,359 | 80,000 | `[Hangar n]` loop 4 -> 8 (+0 characters) |
| `[Thrust] ... PB` | ship | `ThrustVectorAI.cs` | | | added by `build_all` (`add_support_pbs`), as on the Leviathan |
| `[Pod] Pod PB 1..6` | each pod | `CradleBayAI.cs` | 22,798 | 80,000 | none |
| drones | drone grids | their own | | | none |

No new PB is needed: nothing on the hot path changes and no script grows past 80 k. One writer per thruster/gyro override: the Helm (the Staff
only releases, the bays only touch pod merge blocks and the drones' own blocks).

---

## 7. PCU (est.) and world limits

| category | n | PCU |
|---|---:|---:|
| armour, windows, panels (hull ~10,500 + ~250 panels) | ~10,750 | ~10,750 |
| gyroscopes | 144 | 7,200 |
| gatling turrets | 16 | 3,600 |
| artillery + assault turrets | 8 | 2,200 |
| hangar doors | 16 | 1,840 |
| connectors (4 side + 8 well) | 12 | 1,500 |
| conveyor junctions | ~100 | ~1,000 |
| thrusters | 24 | 288 |
| PBs (Helm, Staff, Thrust), seats, RC, timer, ECs | | 655 |
| reactors, batteries, tanks, lockers, gravity | | 640 |
| railguns, cameras, antenna, decoys, sensors, AI | | 520 |
| **ship grid** | ~11,100 blocks | **~29,800** |
| 6 pods x 1,643 | | 9,858 |
| **empty carrier** | | **~39,700** (fits the owner's 50 k creative world) |
| + 24 Hornets x 1,010 | | **~63,900** |

Trim ladder for a 50 k world with drones (cumulative, each step keeps the ship flyable and armed): (1) 72 of 144 gyros (-3,600, yaw 0.03 rad/s2)
-> fills 13 slots; (2) drop the 8 ventral gatlings (-1,800) -> 15 slots; (3) undock the two side-bay pods and their doors / connectors
(-3,286 -1,840 -500, 8 slots fewer) -> 16 slots in the well, about 49.9 k with 16 Hornets. Above that the world limit must go up (Q1).

---

## 8. Integration checklist (implementer, in order)

1. **leviathan.py refactors, behaviour unchanged** (prove: Leviathan bp.sbc sha256 identical before / after, `test_leviathan.py` green):
   `_conveyors(S, terms=None, start=None, first=1, toward=(0, 2, 82))`; `_decoys(S, res, seeds=None)`; `check_hangar(bp, pod, s, min_gap=0.3, door_x=None, hangar_y=None, hangar_z=None)`.
2. **`tools/designs/aegis_carrier.py`** (module docstring in the Leviathan style: shape language, naming, models), imports:
   `from _common import *`, `import _common as C`, `import hulls as H`, `from hulls import Volume, Zone, Zones`, `import sebp`, `sebp_toolbar as TB`,
   `sebp_multi as MU`, `from sebp_inv import Item`, `import pods`, `from pods import orient_attached`, `import leviathan as L`, `from ballista import PD_SETTINGS`.
   Contents: constants (section 1, 2), `rho/rc/half_t`, `_disc() _bay_modules() _engine_pods() _hub_pods()`, `pod_sites()` (the section 2.1
   table expanded to 4 pods), `turret_sites()`, `thruster_sites()`, `_volumes()`, `_zones()`, `hull(bp)`, then `S = L.Ship(bp, res, vol)` and
   `_hangars(S)` (side bays + well connectors, `S.conns = {'P': [...], 'S': [...], 'W3': [...], ...}`, `S.doors = {-1: [...], 1: [...]}`),
   `_weapons(S)`, `_propulsion(S)` (`keep_clear(b, sebp.LARGE_H2_LANE, 'exhaust', face=True)`), `_interior(S)`, `_bridge(S)`, `_sensing(S)`,
   `L._decoys(S, res, seeds=...)`, `_networks(S)` (four `L._conveyors` calls), `_greebles(res)`, `res.check()`, `C.link_conveyors`,
   `_groups_and_toolbars(S)` (EC Core watches `R['reactor']`), `S.tmounts = []`, `S.tu_check = {}` (so `L.numbers` works), pods + docking + checks,
   `pods.assert_clean(bp)`, `return C.finalize(bp)`.
   `aegis_carrier(lite=False, with_pods=True, lock=True, name=None, wing=0)`: accepts only `lite=False` (`assert not lite`).
   `numbers(bp)` = `L.numbers(bp)` plus the per-axis thrust check; `pcu_table` = `L.pcu_table` (add the carrier role names).
   `DOCS = {'Aegis Carrier': ...}`, `MODEL_NOTES = {'Aegis Carrier': ...}` (world limit, paste only, pods are separate grids, drones are pasted
   against the merge blocks, `launch all` from the Staff), `SCRIPTS = ['ShrikeAI.cs', 'StaffAI.cs', 'CradleBayAI.cs']`,
   `MODELS = {'Aegis Carrier': aegis_carrier}`, `NO_LITE = set(MODELS)`.
   `__main__`: `--render DIR`, `--hull`, `--plans` (deck plans at y = -3, 0, 3, 5, 7 with `L.deck_plan(bp, y, xr=range(-32, 33))`), `--pcu`,
   default `C.run_module(MODELS, min_acc=0.5)` + `numbers` JSON.
3. **`tools/designs/aegis_carrier_test.py`**: `MODELS = {'Aegis Carrier Wing': lambda lite=False: aegis_carrier.aegis_carrier(name='Aegis Carrier Wing', wing=4)}`,
   `NO_LITE = set(MODELS)`, a one-line `DOCS`.
4. **`tools/designs/__init__.py`**: add `'aegis_carrier'` to `DRONE_MODULES` and `SHIP_MODULES` (after `'ballista'`), `'aegis_carrier_test'` to `TEST_MODULES`.
5. **`scripts/StaffAI.cs`**: `for (int n = 1; n <= 8; n++)` in `Load()`; update the header line `[Hangar n] Group, Pod` to say n = 1..8. Then
   `tools/compile_check.sh` (or the sim build) and `python3 -I tools/blocks_sync.py --check`.
6. **`tools/build_all.py`**: the Warships package intro strings (`PACKAGES`) list the ships: add "Aegis Carrier"; `SHIP_PB`: `StaffAI.cs` ->
   'Staff (Corsair, Warden, Leviathan, Aegis Carrier)', `CradleBayAI.cs` -> 'Bay / Pod PB (Corsair, each pod)' (unchanged).
   MODELS.md and the README model tables are generated from `DOCS` / `MODEL_NOTES` (no hand edit).
7. **Package texts** (no zip build): `packages/README_Warships.md.tmpl` (row: `**Aegis Carrier** | mega carrier, 157 m disc | large |
   [Helm][SH] Aegis Helm, [Staff][SH] Aegis Staff, [Pod] Pod PB 1..6 | six docked pods, 24 drone slots`, PCU note), `packages/README_All.md.tmpl`
   (ship list), `packages/tests.md` (`[ship]` steps from section 9), `packages/unknowns.md` (nose-up pods on floor connectors, per-pod fuel islands),
   `packages/changelog.md`.
8. **Docs**: this file -> design record after the build (fill sections with `numbers()`, the PCU table from `--pcu`, deck plans, renders);
   `docs/design/grids_bays.md`: a short section "vertical launch well" (pods nose up on floor connectors, flush noses, the 8 m Clear Distance reason);
   `docs/design/control_arch.md`: StaffAI hangars 1..8; `docs/handoff.md`: one status line.
9. **`docs/retrofit.md`**: under **Role-specific**: "StaffAI reads `[Hangar 1..8]` (was 1..4): only ships with more than four pods use it (Aegis Carrier)" and
   "Vertical launch well with flush pod noses: a carrier layout, not a general improvement". The Pending rows "Vanilla point-defence turrets written
   with small grids + missiles only" and "Exhaust lanes over the whole nozzle face" are already applied to this ship at birth: when the retrofit runs,
   list it as done.
10. **Tests**: new `tools/tests/test_aegis_carrier.py` (pattern `test_leviathan.py`, one cached build + one Wing build):
    names and models (`'Aegis Carrier'`, folder != `'Aegis'`, TAG `AEGISC`); no DLC / Prototech in any grid; `validate_all()` on every grid;
    `hull.check()`; symmetric in x except `kind == 'conv'`; size band 9,000-14,000 ship blocks; every host block `[SH]`, pod blocks `[POD1..6]`;
    PB names, scripts and Custom Data (`Tag=AEGISC`, six `[Hangar n]` with the right Pod PB, `Pod Gap (s)=6`); **StaffAI's hangar loop bound >= 6**
    (regex `n <= (\d+)` near `"Hangar "` in StaffAI.cs); groups and hangar door groups per side; counts (24 thrusters, 4 per direction, 144 gyros,
    16 PD with `PD_SETTINGS`, 4 artillery, 4 assault, 2 rails, 4 tanks, 12 decoys, 6 pods); every pre-load (ammo, uranium, hydrogen full,
    batteries full, capacitors full); **thrust equal on the six axes** (28.8 MN) and torque about the COM < 10 % of the gyro torque; every thruster's
    7-cell face lane clear and the rail muzzles clear; four networks, each reaching all its pod's terminals and exactly one tank, no junction next to
    a connector; all 6 pods dock on both connector pairs to 2 mm; side pods upright looking down their lane; well pods nose up, `check_well` passes;
    the Wing test ship (24 Hornets) writes, reloads and keeps 0.25 m clearance; PCU table adds up and the ship stays within 27-33 k; registered in
    `designs.SHIP_MODULES`.
    Extend: `tools/tests/test_hangar_doors.py` (load `('warden', 'leviathan', 'aegis_carrier')`, expect 3 ships);
    `tools/tests/test_m6.py` (`ship('Aegis Carrier')`, `check_keys`, and add it to the every-PB-has-its-script / Staff lists).
    `tools/tests/test_pb_budget.py` needs no change (it reads every script).

---

## 9. Build, validate, render, sim

```bash
# fast build (one model + the test ship, no zips)
python3 -I tools/build_all.py --no-zip --only "Aegis Carrier" "Aegis Carrier Wing"
# attachment audit of all grids (expect 100 % attached, each grid one piece)
python3 -I tools/audit.py --types "out/Blueprints/Aegis Carrier/bp.sbc"
# design figures, PCU table, deck plans, renders
python3 -I tools/designs/aegis_carrier.py                 # build + validate (C.run_module) + numbers JSON
python3 -I tools/designs/aegis_carrier.py --pcu
python3 -I tools/designs/aegis_carrier.py --plans
python3 -I tools/designs/aegis_carrier.py --render <scratchpad>/ship_renders/Aegis_Carrier   # _shiprender.render_sheet: iso, top, side, front, belly, section, deck, lane
# tests
python3 -I tools/tests/test_aegis_carrier.py
python3 -I tools/tests/test_leviathan.py
python3 -I tools/tests/test_hangar_doors.py
python3 -I tools/tests/test_m6.py
python3 -I tools/tests/test_pb_budget.py
tools/compile_check.sh && python3 -I tools/blocks_sync.py --check      # after the StaffAI change
python3 -I tools/tests/run_all.py                                      # once at the end
```

`build_all` itself validates (mount faces, one piece per grid, overlaps, reserve zones, DLC), writes the thumbnail and re-reads the file. Compare the
top render against the reference: rounded-square disc, raised hub ring, dark well, N/S hub pods, flat E/W doors, four corner pods.

**Sim smoke test, 3 seeds each, nothing more** (checks that it flies, fights and launches; not a measurement):

```bash
for k in 0 1 2; do SimHost.exe one --scenario "ship/Aegis Carrier-vs-Pirate" --seed-index $k --cap-seconds 300 --commands "Helm:hunt"; done
for k in 0 1 2; do SimHost.exe one --scenario "ship/Aegis Carrier Wing-vs-Pirate" --seed-index $k --cap-seconds 300 \
    --commands "Helm:hunt;3:Staff:launch all;60:Staff:wing satellite;80:Staff:wing hunt"; done
```

Look for: no override fight, the Helm turns the ship (yaw about 0.06 rad/s2), all 24 drones out by about 45 s with the roll call at 24, no drone
touching the well wall or the doors, the Helm back in HUNT after the launch. The sim holds the pods rigidly (`RigidLink`), has no conveyors or fuel,
and runs a ~11 k block hull at about 15-20 s of wall time per simulated minute. Afterwards: **the release zips are not rebuilt** (say so).

---

## 10. Risks, unknowns, questions for the owner

Risks (and the mitigation already in the plan):
1. **PCU**: 39.7 k empty, 64 k with 24 drones (section 7). Biggest owner decision (Q1).
2. **Nose-up pods on floor connectors** are new: the side bays are the proven Leviathan layout, the well docking is not (Pod.md C2/C3: the lock at paste,
   pods held under 1.85 m/s2). The blueprint writes the locked connector form; if a well pod does not lock, press Lock on its floor connectors.
3. **Drones leaving the well**: flush noses make the default 8 m burn enough; unverified in the game. Fallback: a `[Bay n] Clear Distance` key in
   CradleBayAI (small, off the hot path) if drones linger over the well.
4. **Per-pod fuel islands**: a destroyed tank strands its pod's six thrusters; on a long forward burn the aft tanks empty first (26 min). A ring network
   (one network over the four pods, about +200 junctions, +2 k PCU) is the alternative (Q5).
5. **Curved rim** = many slopes and corners on a staircase (prototype: 2,052 Slope2 pieces, 360 slopes, 284 corners); the hull builder accepted it,
   the look in the game is unseen (Q6).
6. **Paint, not light**: the cyan strips do not glow (section 1.3, Q4).
7. **Side doors open on any release** (the Staff keeps one door list): harmless, noted.
8. **ShrikeAI at 77,994 of 80,000**: no room for carrier-specific Helm logic; any such request triggers the add-a-PB rule with an Opus plan.
9. Sim performance with 31 grids; the integrity scan is skipped on hulls this size (as the Leviathan).
10. Unseen-shape blocks (Full): `LargeBlockSensor`, the two AI blocks, the gravity generator, the rocket-free turret set is all seen; `TimerBlockLarge`,
    `LargeBlockRemoteControl`, `LargeBlockSmallGenerator` use a seen sibling's shape (as the Leviathan).

Questions to ask the owner before or right after the first paste:
* **Q1** World PCU limit: is 64 k (all 24 slots filled) acceptable (raise the limit or block limits off), or should the default carry fewer pods?
* **Q2** Size: 157.5 m disc (about the Leviathan's 222 m length class in mass, twice its footprint) - bigger or smaller?
* **Q3** Accent colours: Starscape cyan everywhere (planned) or the family orange on the hangar frames?
* **Q4** Glowing strips: can the owner save a small game blueprint with one `SmallLight` (and one `LargeBlockLight_1corner`) so their XML shape can be extracted?
* **Q5** Fuel: four independent pod networks (planned) or one ring network?
* **Q6** After the first paste: proportions and the rim bevel against the reference images.

In-game first-paste checklist (owner):
1. Delete any old `Aegis Carrier` blueprint folder first (stale `bp.sbcB5`). Paste: 7 grids (ship + 6 pods), no DLC warning, PCU as section 7.
2. Terminal: 24 thrusters, 144 gyros, 16 doors, 4 tanks, `[Helm][SH] Aegis Helm`, `[Staff][SH] Aegis Staff`, six `[Pod] Pod PB n`.
3. Fuel: each tank 100 %; each pod's thrusters show hydrogen (else the junction network did not connect: grind and rebuild one junction per branch).
4. Fly by hand: all six directions push (1.85 m/s2), a 90 degree yaw takes about 10 s; dampeners stop the ship.
5. Pods: all six connector pairs Locked; the well pods stay docked at full thrust; the side bay doors open and close without touching a pod.
6. Paste one Hornet into a well pod slot, `Run Staff launch 9`: the drone rises out of the well and leaves; then one in a side pod (`launch 1`).
7. Turrets: PD gatlings ignore large grids (`PD_SETTINGS`), artillery / assault pull shells from their pod's locker.
8. Look: the top view against the Starscape images.
