# Cradle Pod (`tools/designs/pods.py`)

Key `pod`. The 4-slot drone cradle that the Warden carries docked in its belly hangar and that the Leviathan will dock twice. One generator function, one model
(`Cradle Pod` / `Cradle Pod Lite`), one script (`CradleBayAI.cs`, to be written by the script workers: the Pod PB is empty until that file exists).
Concept and decision: `docs/design/grids_bays.md` sections 5.1 and 5.4. Nothing here has been pasted in the game yet (section 8).

## 1. What it is

A SMALL-grid frame (0.5 m cells) with a row of merge-block cradles. Each cradle is the HOST half of a `SmallShipSmallMergeBlock` pair: a Hornet-class drone, pasted as
its own CubeGrid with its stern merge block touching the pod's merge block, is held nose-out; separating the pair (Pod PB, or a backup timer) lets the drone's own LAUNCH
block fly it away. The pod docks to a large ship with two `ConnectorMedium` on its rear face against two large `Connector` blocks of the host (the owner's `Venture` file
holds exactly this pair, `docs/design/grids_bays.md` 2.3). It has its own Programmable Block, so a crash costs one pod, not the ship; it has no thrusters.

`Cradle Pod Lite` is the same blueprint under the Lite name (every block of the pod has a sample-derived shape, nothing to drop).

Frame axes: x = the row of slots (Right), y = Up, z = Backward; **the drones' noses point to -z, which is the launch direction**.

## 2. API (module docstring of `pods.py` is the reference; stable, append-only)

| call | does |
|---|---|
| `cradle_pod(n_slots=4, lite=False, name=None, tag='POD', ship_tag='WARDEN', script=None, uranium_kg=4.0, conn_spacing=None)` | builds the pod (2..8 slots); `bp.pod` = `PodInfo` with `.slots[k]` (`merge`, `merge_cell`, `drone_origin`, `box_min/max`, `free_min/max`, `away`), `.connectors`, `.conn_spacing`, `.pb`, `.timers`, `.batteries`, `.reactors`, `.dims_m`, `.rear`, `.lane` |
| `drone_pose(pod, k, drone_bp)` | pose of a drone grid clamped to slot k (`sebp_multi.Pose`, drone upright, nose -z, lattice aligned) |
| `attach_drone(pod, k, drone_bp, check=True)` | `pod.attach(drone, pose)` + `check_clearance` |
| `check_clearance(pod)` | raises if any pod block is within one cell (26-neighbourhood) of a drone cell, except host merge against drone merge |
| `dock_pod(host, pod, host_connectors, host_face='Forward', roll=None, lock=False, gap=PAIR_GAP, want_nose='Down')` | docks both connector pairs under a host (`roll=None` picks the turn about the connector axis whose pod nose points `want_nose`); pair B must meet pair A's pose to 2 mm; `lock=True` writes both pairs locked (Venture form) |
| `hangar_footprint(pod)` | what a host hangar must offer, in large cells: row 8, thickness 3, depth 3, connector spacing 5 cells, 12 m lane |
| `orient_attached(bp, xtype, sub, mn, toward=None, prefer=..., occ=None, **add_kwargs)` | places any block with the first of 24 orientations whose mount faces meet a neighbour (used by both ships for 1x1x1 devices) |
| `slot_report(pod)` | text table of the slots |

Host side (Leviathan): put two large `Connector` blocks (Forward = toward the pod) `conn_spacing / 5` large cells apart on the hangar ceiling, give the hangar the footprint above,
call `dock_pod(ship, pod, [conn_a, conn_b])`. For a second hangar build a second pod with a different `name` (block names stay unique per grid) and `ship_tag`.

```python
import pods
pod = pods.cradle_pod(4, name='Leviathan Cradle Pod P', ship_tag='LEVIATHAN')
pods.dock_pod(ship_bp, pod, [conn_a, conn_b])          # ship_bp.subgrids now holds the pod
```

## 3. Geometry (cells; 4 slots)

| item | value |
|---|---|
| frame | x -1..36, y -2..8, z 0..14 = 38 x 11 x 15 cells = **19.0 x 5.5 x 7.5 m** |
| slot pitch | 9 cells (4.5 m); drone box 7 x 6 x 9 (Hornet, Wasp, Watcher, Harrier 8 long; Lancer, Aegis 9 long); 2 empty cells between neighbouring drones |
| per slot k | drone merge cell `(9k+4, 3, 9)`, host merge cell `(9k+4, 3, 10)`, drone origin `(9k+4, 3, 6)`, drone box `(9k+1, 1, 1)..(9k+7, 6, 9)`, free box `(9k, 0, 0)..(9k+8, 7, 9)` |
| rear wall | z = 11..12: outer ring, centre bar (y = 3), posts between slots, 3 x 3 pads behind every merge block and connector (2 layers, 20 kg light blocks) |
| connectors | two `ConnectorMedium` (3 x 3 x 2) at x = 5 and 30, y centre 3, z 13..14, Forward = +z; spacing 25 cells = 12.5 m = 5 large cells |
| rails | nose ring (orange) at z = 0, rib at z = 6, four corner rails z 1..10; nothing else touches the drone free boxes |
| launch lane | the open space in front of the nose ring: 38 x 11 cells wide, 24 cells (12 m) long; the Pod PB's `Away` is (0, 0, -1) in pod cells |
| rear devices (z = 13) | on the centre bar between the connectors, then along the top and bottom rails: 2 batteries, Pod PB, antenna, 2 reactors, n timers, Remote Control |

Connector spacing lattice: the host's cell is 2.5 m = 5 pod cells, so the spacing must be a multiple of 5 cells, an odd multiple for an even slot count and an even multiple for an
odd one (both connector centres must land on pod cells); `_conn_spacing(n)` picks the widest that fits the frame (n=2: 5, 3: 10, 4: 25, 5: 30, 6: 35, 8: 55; tested).

Drones that fit: every drone with a stern merge block, 9 cells long at most (MODELS.md Merge = 1): Hornet, Wasp, Watcher, Harrier, Lancer, Aegis (tested against all six). Not supported:
Midge Full (10 long), Gnat (merge on the belly), the large drones (no merge block).

## 4. Blocks, names, script-facing data

Every pod block carries `[POD]` (never `[SH]`, so the host's Helm and Staff never adopt a pod block): `[POD] Merge 1..n`, `[POD] Connector A/B`, `[POD] Battery 1/2`, `[POD] Reactor 1/2`,
`[POD] Timer R1..Rn`, `[POD] Antenna`, `[POD] Remote Control`; the PB is `[Pod] Pod PB`. Timer R(k) has delay 3 s and the toolbar `merge block k: OnOff_Off` (a seen action), the hand-launch
backup of `docs/design/control_arch.md` section 4.2 A3. The Pod PB's Custom Data is a proposal for the bay table the script reads (the HiveMother format: bay sections, drone box in pod cells):

```
[Pod]
Tag=WARDEN
Slots=4
Pulse=Update10

[Bay 1]
Merge=[POD] Merge 1
Drone Box Min=1,1,1
Drone Box Max=7,6,9
Away=0,0,-1
Timer=[POD] Timer R1
...
```

## 5. Numbers (4 slots, empty)

| | |
|---|---|
| blocks | 605 (588 light armour cubes, 4 merge, 2 connectors, 2 batteries, 2 reactors, 4 timers, PB, RC, antenna) |
| mass | 16.5 t (frame 11.8 t, connectors 1.4 t, batteries 2.1 t, reactors 0.6 t, rest 0.6 t); +4 Hornets about 18 t |
| PCU | 1,643 (armour 588, merges 400, connectors 250, PB 100, antenna 100, timers 100, reactors 50, batteries 30, RC 25); a Hornet adds about 1,010 |
| power | 2 x `SmallBlockBatteryBlock` full (2 MWh), 2 x `SmallBlockSmallGenerator` (1 MW) with 4 kg uranium each; docked, the host's power is expected to flow through the connectors (unverified, C3); merged drones are charged through the merge pair |
| tests | `tools/tests/test_warden.py` PodTests (slot counts 2..8, six drones clamp and clear, names, connector lattice) |

## 6. Why two connector pairs, no thrusters, a frame and not a box

Two pairs for stiffness (`grids_bays.md` F10); no thrusters because a pod only travels attached (a stand-alone `Cradle Pod` pasted in the game is a parking frame; fly the ship to it);
an open frame because every full armour wall costs 20 kg per cell and the drones need a free lane (the whole pod weighs 16.5 t, the doc estimate was about 5 t for a thinner frame
without the batteries and reactors).

## 7. Launch behaviour the pod relies on (unchanged from the grids_bays plan)

Staff PB -> IGC `release n` -> Pod PB: drone batteries Auto, `armed` to the drone PB, host merge off, drone merge off, verify the grids split; the drone's LAUNCH block does the burn along its own
nose. The host ship must not accelerate hard during a release; the Warden keeps the hangar doors open before the first `release`. Exhaust of the departing drone hits the rear wall 3 cells
behind its tail (C5 below).

## 8. Paste risks (in-game tests; nothing is verified yet)

| id | risk | what to look at | fallback |
|---|---|---|---|
| T1 | which face of `SmallShipSmallMergeBlock` clamps (`_common.MERGE_CLAMP_FACE_SMALL` = Up is from the wiki) | paste a drone against a slot: do the two merge blocks clamp (one grid in the terminal)? | change the constant, rebuild |
| C1 | pasted separate grids with touching merge blocks (Enabled) may not merge at paste time | terminal shows one grid or two after paste; `Enabled` off/on from the PB once | Pod PB toggles the host merge at start |
| C2 | the two connector pairs may paste unlocked (the file writes them unlocked, the Tech Hunter form; the locked form of the Venture file is `warden(lock=True)`) | hangar connectors `Connected`? | press Lock on both hangar connectors (ship `Connect()` from Staff at start) |
| C3 | docked = a different construct: does the ship's GTS list the pod? does it hold 1 m/s2 for 30 s? | Staff PB `GetBlocksOfType` count with and without the pod; thrust test | Pod PB talks over its own antenna |
| C4 | IGC `CurrentConstruct` does not reach a connector-docked pod | send `release` from Staff to Pod PB | `TransmissionDistance.ConnectedConstructs` or an antenna pair on a private tag |
| C5 | the launch burn of a drone against the rear wall (3 cells) and the neighbouring drone | damage after 4 launches | longer stalk (change `ZH`/`ZW`) |
| C7 | the clearances: 2 cells between drones, 0.5 m between the pod frame and the Warden hangar end walls (1.0 m beside), 0.07 m at the connector plane | any collision / grind noise while docking | `pods.check_clearance`, `warden.check_hangar` assert them at build time |

## 9. Files

`tools/designs/pods.py` (module, registered in `DRONE_MODULES` of `tools/designs/__init__.py`), `tools/tests/test_warden.py`, `tools/designs/_shiprender.py` (renders), this file.
Render: `python3 -I tools/designs/pods.py` builds both variants; the pod with four drones was rendered through `_shiprender.render_sheet`.
