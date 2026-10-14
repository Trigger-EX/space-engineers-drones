# Friendly avoidance (drones keep clear of non-enemy grids)

**Why.** In-game report 2026-10-14: Hornets in GUARD orbited a target; a Ballista (Shrike Helm, relationship left as spawned) sat in the orbit.
The Hornets never saw it and flew into it. Nothing in the drone scripts knew about grids that are not enemies: `Separation()` only repels hive
mates on the same IGC tag, `Valid()` throws away everything that is not a target, and the cameras never lock a friend. It was not the first
drone-on-drone crash (other wings, carriers, other tags have the same hole).

**Second cause (2026-10-14, Corsair spawned next to Hornets in GUARD).** The carrier link's `FLEET_HOME` is the carrier's *centre*; a drone in
GUARD (or with no target) flies to `Home()` and hovers there, so it rammed the hull. The movement code now holds on a sphere of
`FollowRadius() + 40` m round that centre, on the drone's own side (Hornet, Wasp, Lancer, Harrier, Aegis, Basilisk, Longbow, Mantis; Gnat's
FORM ring already sits on a sphere; RTB still flies to the centre to dock). With the carrier's beacon, the avoidance sphere is the real hull.
`avoid-test --mode transit --bx 0 --hx 0`: the drones stop 65 m from the centre. The in-game build the screenshot came from predates both fixes
if it was installed before the release zips are rebuilt.

**What.** Two shared blocks (`tools/blocks.json`, synced by `tools/blocks_sync.py`):

| Block | File | In | Does |
|---|---|---|---|
| `FRIENDLY AVOIDANCE` | `tools/fa_block.cs` | Hornet, Gnat, Longbow S/L, Lancer, Harrier, Aegis, Basilisk, Wasp Flight, **Shrike Helm** (2026-10-14) | beacon + sightings + steering (`FaWant`) |
| `FRIENDLY BEACON` | `tools/fb_block.cs` | Mantis, Wasp, Shrike Helm | beacon only (`FaBeacon`); the Helm keeps it besides the full block: 10 Hz, and also while parked / IDLE / ASSIST / flown by hand, when `FaWant` does not run |

* **Beacon**: every script with either block broadcasts `MyTuple<long id, Vector3D centre, Vector3D velocity, float radius>` (the grid's
  `WorldVolume`) on the IGC channel `DRONE_SPACE` (4 Hz from the avoidance block, 10 Hz from `BroadcastStatus` for the beacon block). Needs no
  tag, relationship or camera. Any other script (a future ship, a hand-written one) can join by sending the same tuple.
* **Sightings**: `Valid()` calls `FaSee(e)` first; every large or small grid that is not an `Enemies` relationship and not my target is kept
  12 s with position, velocity and hull radius (neutral, unowned, other players' friends, carriers).
* **Steering** (`FaWant`, after `KzWant`, before the speed cap), per obstacle with sphere = its radius + mine + 15 m margin:
  1. *barrier*: closing speed capped at `sqrt(2 * 4 m/s2 * gap)` (the drones manage 6.5 m/s2, so it can always stop);
  2. *ramp*: inside margin + 40 m a radial push up to 50 m/s;
  3. *sidestep*: if the straight line (actual or commanded velocity) passes inside the margin within 8 s, a lateral push.
* **Rocks** (2026-10-14): an asteroid reported with a hit point (a camera ray; every scan goes through `Valid()`) is kept as a 40 m sphere on that
  point for 60 s (one per 40 m cell, at most 150 obstacles) and gets the same barrier / ramp / sidestep. Sensors give no hit point, so only rays count.
  Not in the sim (it has no voxels): `avoid-test --rock` injects an asteroid report through the sensors (before: 5 m from the point; now 89 m).
  Aegis is at 99,989 of 100,000 characters. Wasp Split Flight (76 k) has the full block; the single-PB Mantis and Wasp only beacon (they have no room: moving a module such as launch, follow or display into a second PB would make it).
* **Not applied** in RTB and IDLE, to an obstacle whose sphere I am inside (launching, merged, docked), to my target, or to any current contact
  (IGC cannot tell friend from foe: an enemy running the same beacon must not be dodged).
* **No Custom Data keys**: several scripts are within 1-4 k of the 100,000-character limit; the constants are in the block.

**Not covered.** `MantisAI` (99,720) and `WaspAI` (99,197) have no room for the steering, so they are seen but do not dodge. `StingerAI` (warhead drones), `PeeperAI`, `SpotterAI` are untouched. A
friendly that neither beacons nor is ever seen by a camera or sensor (no antenna, outside every scan) is still invisible; fitting a sensor
to the drones would close that.

**Test.** `mono out/sim/SimHost.exe avoid-test --count 1 --bx 0 --mode transit [--beacon]`: a Hornet in GUARD flies a straight line to a HOME
beyond a parked friendly. Before: closest approach 5.1 m (FAIL); after: 47-51 m. `--mode hunt` is the orbit variant (does not collide
even before: the wingmate separation and the standoff keep the drones apart in that geometry). 10-seed regression, 48 scenarios (1v1 among
Aegis/Basilisk/Gnat/Harrier/Hornet/Lancer/Longbow S, Lancer+Mantis mixed, Gnat swarms): wins, timeouts, collisions and exceptions unchanged
within noise (collisions 0 and 0).

## READY mode (2026-10-14)
`ready` (PB argument, `<Tag>_CMD`, toolbar, `Start Mode=READY`, fleet / `wing ready`): GUARD with the home point fixed to where the drone is when ordered
(the carrier link's HOME and the satellite ring are ignored), so it holds still (dampeners), keeps the all-round camera scan, engages what comes within
`Guard Radius` of that spot (full combat movement, friendly avoidance included), and returns to the spot afterwards. Any other mode command ends it.
The status line shows `Mode   : READY`; the Storage field keeps it over a recompile. Gnat: FORM round the spot. The Shrike Helm keeps its own `react`.
The two-PB Wasp Split has it in the Flight PB only (the Staff screen shows GUARD).

## Wingmates of one ship class: three Ballistas (2026-10-14)
**Report.** Three default Ballistas pasted together all flew to the same spot and collided. **Cause.** The Helm (`ShrikeAI.cs`) had only the beacon: its wingmate
`Separation()` (hive mates on the same IGC tag) is sized for drones, 70 m range and 25 m/s push, against a 52 x 30 x 32 m hull (bounding sphere 29.5 m); three
Helms given the same target all steer for the same standoff point, so they arrive together and the push is far too weak. While the AI Flight blocks fly (no target,
assist on) nothing at all dodged, and every AI block picks the same enemy.
**Repro** (sim, real script + the blueprint's Custom Data; the sim does not model the AI blocks' flight, so only the script path is covered):
`mono out/sim/SimHost.exe wing-test --count 3 --spacing 120 --axis x --enemy Hornet --enemy-dist 2400 --cue --cmd hunt --seed N` (`sim/src/Run/WingTest.cs`: closest approach of
every pair, hull-on-hull collision events; a trailing Probe Foundry is counted apart, it is a sim artefact of the docked body). Before: 3 of 16 runs (spacing 70 / 120 m, both axes, seeds 1-4)
hull-on-hull collisions (first at 12-35 s, up to 120 k collision damage), 7 more under 45 m. After: 0 of 16, closest approach 92 m or more.
**Fix.** The Helm carries the full `FRIENDLY AVOIDANCE` block now (steering after `KzWant`, sightings in `Valid()`), so the radius of both hulls counts (sphere = radius + radius + 15 m: about 74 m
between two Ballistas) and the push is 50 m/s, not 25. Plus `FaYield()`: while the AI blocks fly and a friendly grid is in the ramp zone or on the straight line of the ship's velocity, the AI is switched off for 3 s and
`Control()` flies with the avoidance (unverified in the game). Helm size 81.2 k -> 83.8 k minified with this change (84,958 with the thrust-vector link merged in); the build limit is now 86,000 (CLAUDE.md: size is not a gate; past that move a module into the Staff PB).
