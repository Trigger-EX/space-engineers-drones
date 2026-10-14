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
| `FRIENDLY AVOIDANCE` | `tools/fa_block.cs` | Hornet, Gnat, Longbow S/L, Lancer, Harrier, Aegis, Basilisk | beacon + sightings + steering (`FaWant`) |
| `FRIENDLY BEACON` | `tools/fb_block.cs` | Mantis, Wasp, Shrike Helm | beacon only (`FaBeacon`) |

* **Beacon**: every script with either block broadcasts `MyTuple<long id, Vector3D centre, Vector3D velocity, float radius>` (the grid's
  `WorldVolume`) on the IGC channel `DRONE_SPACE` (4 Hz from the avoidance block, 10 Hz from `BroadcastStatus` for the beacon block). Needs no
  tag, relationship or camera. Any other script (a future ship, a hand-written one) can join by sending the same tuple.
* **Sightings**: `Valid()` calls `FaSee(e)` first; every large or small grid that is not an `Enemies` relationship and not my target is kept
  12 s with position, velocity and hull radius (neutral, unowned, other players' friends, carriers).
* **Steering** (`FaWant`, after `KzWant`, before the speed cap), per obstacle with sphere = its radius + mine + 15 m margin:
  1. *barrier*: closing speed capped at `sqrt(2 * 4 m/s2 * gap)` (the drones manage 6.5 m/s2, so it can always stop);
  2. *ramp*: inside margin + 40 m a radial push up to 50 m/s;
  3. *sidestep*: if the straight line (actual or commanded velocity) passes inside the margin within 8 s, a lateral push.
* **Not applied** in RTB and IDLE, to an obstacle whose sphere I am inside (launching, merged, docked), to my target, or to any current contact
  (IGC cannot tell friend from foe: an enemy running the same beacon must not be dodged).
* **No Custom Data keys**: several scripts are within 1-4 k of the 100,000-character limit; the constants are in the block.

**Not covered.** `MantisAI` (99,720) and `WaspAI` (99,197) have no room for the steering, so they are seen but do not dodge. The Shrike Helm only
beacons (budget raised 80,600 -> 80,900 for the 270 characters). `StingerAI` (warhead drones), `PeeperAI`, `SpotterAI` are untouched. A
friendly that neither beacons nor is ever seen by a camera or sensor (no antenna, outside every scan) is still invisible; fitting a sensor
to the drones would close that.

**Test.** `mono out/sim/SimHost.exe avoid-test --count 1 --bx 0 --mode transit [--beacon]`: a Hornet in GUARD flies a straight line to a HOME
beyond a parked friendly. Before: closest approach 5.1 m (FAIL); after: 47-51 m. `--mode hunt` is the orbit variant (does not collide
even before: the wingmate separation and the standoff keep the drones apart in that geometry). 10-seed regression, 48 scenarios (1v1 among
Aegis/Basilisk/Gnat/Harrier/Hornet/Lancer/Longbow S, Lancer+Mantis mixed, Gnat swarms): wins, timeouts, collisions and exceptions unchanged
within noise (collisions 0 and 0).
