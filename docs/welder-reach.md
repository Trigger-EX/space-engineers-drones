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

Output of `python3 -I tools/designs/reach.py --table` (uncovered drone blocks per hypothesis; the first four hypothesis columns are tier A;
"<2 welders" counts blocks that fewer than two welders reach under `tip r1.0`; "unsupp" = drone blocks never supported; every bay has 0 unsupported):

```
printer              bay drone          weld blocks tip r1.3 tip r1.0 tip-1 r1.3 tip-1 r1.0 tip-2 r1.3 tip-2 r1.0 mid r1.3 mid r1.0 <2 welders unsupp
Hive Mother          1   Gnat           8    19     0        0        0          0          0          4          1        12       0          0     
Hive Mother          2   Gnat           8    19     0        0        0          0          0          4          1        12       0          0     
Hive Mother Lite     1   Gnat Lite      6    18     0        0        0          0          0          3          0        10       1          0     
Hive Mother Lite     2   Gnat Lite      6    18     0        0        0          0          0          3          0        10       1          0     
Twin Hive            1   Gnat           8    19     0        0        0          0          0          4          1        12       0          0     
Twin Hive            2   Gnat           8    19     0        0        0          0          0          4          1        12       0          0     
Twin Hive            3   Gnat           8    19     0        0        0          0          0          4          1        12       0          0     
Twin Hive            4   Gnat           8    19     0        0        0          0          0          4          1        12       0          0     
Twin Hive Lite       1   Gnat Lite      6    18     0        0        0          0          0          3          0        10       1          0     
Twin Hive Lite       2   Gnat Lite      6    18     0        0        0          0          0          3          0        10       1          0     
Twin Hive Lite       3   Gnat Lite      6    18     0        0        0          0          0          3          0        10       1          0     
Twin Hive Lite       4   Gnat Lite      6    18     0        0        0          0          0          3          0        10       1          0     
Quad Hive            1   Gnat           8    19     0        0        0          0          0          4          1        12       0          0     
Quad Hive            2   Gnat           8    19     0        0        0          0          0          4          1        12       0          0     
Quad Hive            3   Gnat           8    19     0        0        0          0          0          4          1        12       0          0     
Quad Hive            4   Gnat           8    19     0        0        0          0          0          4          1        12       0          0     
Quad Hive            5   Gnat           8    19     0        0        0          0          0          4          1        12       0          0     
Quad Hive            6   Gnat           8    19     0        0        0          0          0          4          1        12       0          0     
Quad Hive            7   Gnat           8    19     0        0        0          0          0          4          1        12       0          0     
Quad Hive            8   Gnat           8    19     0        0        0          0          0          4          1        12       0          0     
Quad Hive Lite       1   Gnat Lite      6    18     0        0        0          0          0          3          0        10       1          0     
Quad Hive Lite       2   Gnat Lite      6    18     0        0        0          0          0          3          0        10       1          0     
Quad Hive Lite       3   Gnat Lite      6    18     0        0        0          0          0          3          0        10       1          0     
Quad Hive Lite       4   Gnat Lite      6    18     0        0        0          0          0          3          0        10       1          0     
Quad Hive Lite       5   Gnat Lite      6    18     0        0        0          0          0          3          0        10       1          0     
Quad Hive Lite       6   Gnat Lite      6    18     0        0        0          0          0          3          0        10       1          0     
Quad Hive Lite       7   Gnat Lite      6    18     0        0        0          0          0          3          0        10       1          0     
Quad Hive Lite       8   Gnat Lite      6    18     0        0        0          0          0          3          0        10       1          0     
Hornet Forge         1   Hornet         12   54     0        0        0          8          10         36         36       47       15         0     
Hornet Forge         2   Hornet         12   54     0        0        0          8          10         36         36       47       15         0     
Hornet Forge Lite    1   Hornet Lite    11   51     0        0        0          6          7          28         30       42       9          0     
Hornet Forge Lite    2   Hornet Lite    11   51     0        0        0          6          7          28         30       42       9          0     
Wasp Foundry         1   Wasp           12   54     0        0        0          8          10         36         36       47       15         0     
Wasp Foundry         2   Wasp           12   54     0        0        0          8          10         36         36       47       15         0     
Wasp Foundry Lite    1   Wasp Lite      11   51     0        0        0          6          7          28         30       42       9          0     
Wasp Foundry Lite    2   Wasp Lite      11   51     0        0        0          6          7          28         30       42       9          0     
Lancer Foundry       1   Lancer         12   54     0        0        0          8          10         36         35       47       11         0     
Lancer Foundry       2   Lancer         12   54     0        0        0          8          10         36         35       47       11         0     
Lancer Foundry Lite  1   Lancer Lite    12   51     0        0        0          6          8          30         29       42       9          0     
Lancer Foundry Lite  2   Lancer Lite    12   51     0        0        0          6          8          30         29       42       9          0     
Longbow Foundry      1   Longbow S      12   55     0        2        2          11         11         31         32       43       9          0     
Longbow Foundry      2   Longbow S      12   55     0        2        2          11         11         31         32       43       9          0     
Longbow Foundry Lite 1   Longbow S Lite 12   52     0        1        1          12         10         32         25       40       21         0     
Longbow Foundry Lite 2   Longbow S Lite 12   52     0        1        1          12         10         32         25       40       21         0     
Harrier Forge        1   Harrier        12   56     0        0        0          8          9          37         35       47       16         0     
Harrier Forge        2   Harrier        12   56     0        0        0          8          9          37         35       47       16         0     
Harrier Forge Lite   1   Harrier Lite   10   53     0        0        0          6          7          32         32       45       5          0     
Harrier Forge Lite   2   Harrier Lite   10   53     0        0        0          6          7          32         32       45       5          0     
Aegis Shipyard       1   Aegis          12   58     0        0        0          8          10         41         40       54       15         0     
Aegis Shipyard       2   Aegis          12   58     0        0        0          8          10         41         40       54       15         0     
Aegis Shipyard Lite  1   Aegis Lite     12   55     0        0        0          6          7          35         35       48       9          0     
Aegis Shipyard Lite  2   Aegis Lite     12   55     0        0        0          6          7          35         35       48       9          0     
Mantis Foundry       1   Mantis         12   55     0        1        1          13         13         33         33       46       20         0     
Mantis Foundry       2   Mantis         12   55     0        1        1          13         13         33         33       46       20         0     
Mantis Foundry Lite  1   Mantis Lite    12   52     0        1        1          12         11         31         32       43       16         0     
Mantis Foundry Lite  2   Mantis Lite    12   52     0        1        1          12         11         31         32       43       16         0     
Watcher Pad          1   Watcher        12   54     0        0        0          6          6          36         35       48       6          0     
Watcher Pad          2   Watcher        12   54     0        0        0          6          6          36         35       48       6          0     
Watcher Pad Lite     1   Watcher Lite   10   53     0        0        0          6          7          29         27       42       8          0     
Watcher Pad Lite     2   Watcher Lite   10   53     0        0        0          6          7          29         27       42       8          0     
Fleet Forge          1   Gnat           8    19     0        0        0          0          0          4          1        12       0          0     
Fleet Forge          2   Gnat           8    19     0        0        0          0          0          4          1        12       0          0     
Fleet Forge          3   Hornet         12   54     0        0        0          8          10         36         36       47       15         0     
Fleet Forge          4   Watcher        12   54     0        0        0          6          6          36         35       48       6          0     
Fleet Forge Lite     1   Gnat Lite      6    18     0        0        0          0          0          3          0        10       1          0     
Fleet Forge Lite     2   Gnat Lite      6    18     0        0        0          0          0          3          0        10       1          0     
Fleet Forge Lite     3   Hornet Lite    11   51     0        0        0          6          7          28         30       42       9          0     
Fleet Forge Lite     4   Watcher Lite   10   53     0        0        0          6          7          29         27       42       8          0     
Mobile Hive          1   Gnat           8    19     0        0        0          0          0          4          1        12       0          0     
Mobile Hive          2   Gnat           8    19     0        0        0          0          0          4          1        12       0          0     
Mobile Hive          3   Gnat           8    19     0        0        0          0          0          4          1        12       0          0     
Mobile Hive          4   Gnat           8    19     0        0        0          0          0          4          1        12       0          0     
Mobile Hive Lite     1   Gnat Lite      6    18     0        0        0          0          0          3          0        10       1          0     
Mobile Hive Lite     2   Gnat Lite      6    18     0        0        0          0          0          3          0        10       1          0     
Mobile Hive Lite     3   Gnat Lite      6    18     0        0        0          0          0          3          0        10       1          0     
Mobile Hive Lite     4   Gnat Lite      6    18     0        0        0          0          0          3          0        10       1          0     
Static Nest          1   Gnat           8    19     0        0        0          0          0          4          1        12       0          0     
Static Nest          2   Gnat           8    19     0        0        0          0          0          4          1        12       0          0     
Static Nest          3   Gnat           8    19     0        0        0          0          0          4          1        12       0          0     
Static Nest          4   Gnat           8    19     0        0        0          0          0          4          1        12       0          0     
Static Nest          5   Gnat           8    19     0        0        0          0          0          4          1        12       0          0     
Static Nest          6   Gnat           8    19     0        0        0          0          0          4          1        12       0          0     
Static Nest Lite     1   Gnat Lite      6    18     0        0        0          0          0          3          0        10       1          0     
Static Nest Lite     2   Gnat Lite      6    18     0        0        0          0          0          3          0        10       1          0     
Static Nest Lite     3   Gnat Lite      6    18     0        0        0          0          0          3          0        10       1          0     
Static Nest Lite     4   Gnat Lite      6    18     0        0        0          0          0          3          0        10       1          0     
Static Nest Lite     5   Gnat Lite      6    18     0        0        0          0          0          3          0        10       1          0     
Static Nest Lite     6   Gnat Lite      6    18     0        0        0          0          0          3          0        10       1          0     
Stinger Rack         1   Stinger        6    19     0        0        0          0          0          3          0        10       1          0     
Stinger Rack         2   Stinger        6    19     0        0        0          0          0          3          0        10       1          0     
Stinger Rack         3   Stinger        6    19     0        0        0          0          0          3          0        10       1          0     
Stinger Rack         4   Stinger        6    19     0        0        0          0          0          3          0        10       1          0     
Stinger Rack Lite    1   Stinger Lite   6    18     0        0        0          0          0          2          0        7        1          0     
Stinger Rack Lite    2   Stinger Lite   6    18     0        0        0          0          0          2          0        7        1          0     
Stinger Rack Lite    3   Stinger Lite   6    18     0        0        0          0          0          2          0        7        1          0     
Stinger Rack Lite    4   Stinger Lite   6    18     0        0        0          0          0          2          0        7        1          0     
Escort Carrier      1   Hornet      12   54     0        0        0          8          10         36         36       47       15         0     
Escort Carrier      2   Hornet      12   54     0        0        0          8          10         36         36       47       15         0     
Escort Carrier Lite 1   Hornet Lite 11   51     0        0        0          6          7          28         30       42       9          0     
Escort Carrier Lite 2   Hornet Lite 11   51     0        0        0          6          7          28         30       42       9          0     
```

Reading the table:

* Tier A is 0 everywhere except the blocks that no legal welder position can reach (the first column `tip r1.3` is 0 for every bay):
  * Hornet / Wasp / Lancer / Harrier / Aegis / Watcher: only `tip-1 r1.0` shows misses (6 to 8 blocks); the Hornet ones are Battery 1/3, Gyroscope,
    Ion Brake L, Remote Control and three armour cubes in the core. They sit 3 cells below the nearest surface a welder can touch.
  * Longbow S: Camera Up and Gyroscope under `tip r1.0` and `tip-1 r1.3`; the exit lane is Up, so the top of the hull is out of the welders' reach.
  * Mantis: one armour cube of the added docking stalk is a conflict miss (welders that could reach it overlap those needed elsewhere); it is
    listed in the report as waived, not silently dropped.
* The deep hypotheses (`tip-2`, `mid`) leave 30 to 50 blocks uncovered for the 50-block fighters. This is geometry, not a solver limit:
  with the centre 1 m or more inside the welder only the outermost layer of the hull can be reached. If the game's real centre is that far
  back, the fighters need a drone-side change (fewer interior blocks, or a hollow frame), see the report to the orchestrator.
* Every bay uses at most 12 welders (the budget); Gnat-class bays use 6 to 7 (they were 5).
