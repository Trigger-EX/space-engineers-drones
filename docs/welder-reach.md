# Welder reach and print order (foundry geometry)

Owner: `tools/designs/reach.py` (verification), `printer_lib.py` (solver), `printers.py` (layouts).
Run the report: `python3 -I tools/designs/reach.py [--table] ["Printer Name" ...]`.

## 1. What is known and what is assumed about the welder sphere

| Fact | Status | Source |
|---|---|---|
| `SmallShipWelder` is 3x3x6 cells (1.5 x 1.5 x 3.0 m) | known | `ref/CubeBlocks/CubeBlocks_Tools.sbc` |
| `SensorRadius` = 1.3 m, **no** `SensorOffset` element (default 0) | known | same file; the drills do carry offsets (0.8 / 2.8), the welders do not |
| Mount points exist on Left/Right/Top/Bottom (rear half and centre column) and Back, none on the Front | known | same file, so the working end ("tip") is the block Forward end |
| The model must contain a dummy whose name contains `detector_shiptool`; it "defines the center of the work sphere" | known | wiki `Modding/Reference/Blocks/ShipWelder` |
| Sphere = dummy position shifted `SensorOffset` along Forward; offset is scaled by the dummy size, so it is not always metres (a welder with offset 1 gave about 2.5 m) | known | wiki `ShipWelder_Definition`; Keen forum topic 187-2 "ship tool SensorOffset is not in meters" |
| Wiki player page: the welder range "is spherical and centered roughly on the welding tip" | known, vague | wiki `Welder_Block` |
| **Where the `detector_shiptool` dummy sits in `Welder.mwm`** | **NOT known** | no source states it, the .mwm is not in the repo |
| Whether the game tests a projected block against the sphere by its whole cell box (this model) or by its centre | assumed (box, as for `GetBlocksInsideSphere`) | not verified in game |

Consequence: the old model (centre = tip face, radius 1.3 m minus 0.2 m) was one guess. In the real game some blocks stayed unwelded, so the
guess was wrong or too tight. The design is now made **robust to the unknown centre** instead of tuned to a new guess.

## 2. The hypothesis family (`reach.FAMILY`)

Sphere centre on the welder axis, `backoff` cells (0.5 m each) inside the welder behind the tip face, times two radii:

| name | centre | radius |
|---|---|---|
| tip r1.3 / tip r1.0 | tip face | 1.3 m / 1.0 m (0.3 m margin) |
| tip-1 r1.3 / r1.0 | 1 cell (0.5 m) inside | same |
| tip-2 r1.3 / r1.0 | 2 cells (1.0 m) inside | same |
| mid r1.3 / r1.0 | 3 cells (1.5 m) inside = middle of the body | same |

A block counts as reached when its cell box (any cell of a multi-cell block) intersects the sphere. The design requirement:

* **Tier A** (`tip`, `tip-1`, both radii): every drone block is reached by at least one welder under each of these four hypotheses, except
  blocks that no legal welder position can reach (listed below as "impossible"). A tier A miss fails `reach.py` and `build_all`.
* **Tier B** (`tip-2`, `mid`, both radii) and **redundancy** (tip r1.0 reached by TWO welders): best effort inside a budget of
  12 welders per bay; shortfalls are printed in the table and are not failures.

Why not "every block under every hypothesis": geometry forbids it. With the centre in the middle of the body and a 1.0 m radius, only blocks
touching the side of a welder are reached; with the centre 1 m inside, only the outermost layer of the drone. A drone block three cells deep
inside the hull is 1.5 m from any surface a welder can touch, so it is out of reach for every centre more than about 1 cell behind the tip.
The solver computes, per (block, hypothesis), whether ANY legal welder position (not overlapping the drone, its exit lane, clear zones, no
mount-point contact with the drone) reaches it ("impossible" otherwise) and covers everything else.

## 3. Build order (support propagation)

The welder builds a projected block only when it is supported: attached by mount points (`sebp.attachment`, the game's
`CheckMountPointsForSide` rule) to a block that already exists. `reach.support_report` starts with all printer blocks (including the printer
merge block), adds a drone block when it is attached to an existing one and repeats; a drone block that is never added fails the design.
The result per bay is the number of rounds (depth of the support chain, 7 to 9 for the fighters).

## 4. Layout rules the solver follows

* welders never overlap the drone, its 1-cell skin (unless the welder tip touches without a mount point), the clear zones or the exit lane;
* the exit direction and the weld mode (air gap / tip contact, camera zones) are chosen per drone to leave the fewest unreachable pairs;
* a welder of one bay must stay out of reach of another bay's drone under every margin-free hypothesis (`bays_conflict`, `foreign_reach`);
* welders are pruned only when neither coverage nor the redundancy count drops.

## 5. Custom Data written for HiveMotherAI.cs (per bay)

`Drone Box Min`, `Drone Box Max` (printer grid cells, translation T included), `Drone Cell Count` (unique drone blocks), `Drone Occupied Cells`,
`Ignore Cells` (`x,y,z;...` printer cells inside the box or next to the drone merge block), `Exit Direction` (always explicit; the older
long-named key is unchanged), and a `[Launch]` section with `Auto=true`, `Burst Seconds=4`.

## 6. Results

(See the table appended by the last verification run below.)
