# FOLLOW mode (v2.2)

## Why
Asked: does `guard` follow the owner? Only as a side effect. GUARD holds a home point, and a Mothership link on a ship replaces that
point with the ship's live centre. Measured in the simulator (`follow-test --cmd guard`, 3 Hornets, link ship cruising at 40 m/s):
the drones trailed **~500 m** behind while it moved. When it stopped they parked **27 m from its centre**, inside or touching a 20 m
hull (one drone spent 20 s inside the clearance). With the link gone they return to the old fixed spot. Nothing followed a player
on foot. The Gnat's `form` had the same lag (230-300 m at 40 m/s).

## How it works
Shared block `tools/follow_block.cs` (synced into the 8 scripted combat drones by `tools/follow_sync.py`; hooks listed in its header).
- **Owner**: whatever broadcasts the home point (`<Tag>_HOME` or `FLEET_HOME`, i.e. any Mothership link), or the owner's own character
  (sensor detection with Relationship Owner, sensor "Detect Owner" now on). The character wins while seen.
- **Velocity**: estimated from successive link fixes (works with old links; a jump > 150 m/s is ignored). On foot, the sensor velocity.
- **Slot**: ring at 60 deg off the owner's tail, upper half only in gravity. Radius: `Slot Distance` (60 m), at least hull radius
  + 30 m, wide enough that neighbours sit outside the wingmate separation range. Radius widens up to 2x while the owner manoeuvres
  hard (> 3 m/s2). On foot: `On Foot Distance` (20 m), at most 40 m so the sensor keeps seeing you, with a 15 m wingmate push
  instead of the normal separation. The ring turns with the owner's heading (about 3 s lag), so weaving does not swing it.
- **Hull**: radius from `<Tag>_HOMER` / `FLEET_HOMER` (all links since v1.1/v1.2), else `Owner Radius` (25 m). Paths through the
  hull detour round it. Near the hull a drone never closes in. On a collision course with the owner (closest approach within 8 s)
  it sidesteps.
- **Combat**: only targets within `Engage Radius` (800 m) of the owner. Past `Leash` (1200 m; 2200 m for Longbow/Mantis) it
  drops the fight and flies back. No AI-block search in FOLLOW.
- **Link loss**: coasts on the owner's last track for `Coast After Link Loss (s)` (3 s; 10 s on foot), then holds there.
- **Gnat**: `follow`/`escort` = `form`; the formation now gets the link ship's velocity fed forward and the radius from FLEET too.

## Verification (sim `follow-test`, 3 drones each, link on the FLEET channel, owner radius 20 m)
Cell: pass / worst drone's p95 distance to the owner / total time inside the hull clearance (radius + 5 m).
Paths: cruise 0-40 m/s with a 90 deg turn and a stop; weave +/-30 deg over 30 s with climbs; lost = link silent at 50 s (p95 is
large by design: the owner flies on, the drones must hold the coast point, checked within 150 m); foot = character walking 4 m/s,
seen only by sensors within 50 m; fight = enemy of the same model (HUNT) 600 m away at 30 s, drones crippled in the fight excluded;
hardweave = +/-60 deg over 20 s, about 13 m/s2 sideways, twice what the drones can do (stress: judged on the hull only).

| model | cruise | weave | lost | foot | fight | hardweave |
|---|---|---|---|---|---|---|
| Hornet | ok 64 m / 0.0 s | ok 75 m / 0.0 s | ok 1203 m / 0.0 s | ok 21 m / 0.0 s | ok 523 m / 0.0 s | ok 198 m / 0.0 s |
| Wasp | ok 64 m / 0.0 s | ok 75 m / 0.0 s | ok 1207 m / 0.0 s | ok 21 m / 0.0 s | ok 2061 m / 0.0 s | ok 179 m / 0.0 s |
| Lancer | ok 64 m / 0.0 s | ok 75 m / 0.0 s | ok 1199 m / 0.0 s | ok 21 m / 0.0 s | ok 1030 m / 0.8 s | ok 186 m / 0.0 s |
| Harrier | ok 62 m / 0.0 s | ok 76 m / 0.0 s | ok 1189 m / 0.0 s | ok 21 m / 0.0 s | ok 691 m / 0.0 s | **miss** 179 m / 0.9 s |
| Aegis | ok 64 m / 0.0 s | ok 76 m / 0.0 s | ok 1204 m / 0.0 s | ok 21 m / 0.0 s | ok 880 m / 0.0 s | ok 182 m / 0.0 s |
| Mantis | ok 62 m / 0.0 s | ok 75 m / 0.0 s | ok 1189 m / 0.0 s | ok 21 m / 0.0 s | ok 588 m / 0.0 s | ok 303 m / 0.0 s |
| Basilisk | ok 123 m / 0.0 s | ok 145 m / 0.0 s | ok 1244 m / 0.0 s | ok 23 m / 0.0 s | ok 615 m / 0.0 s | ok 291 m / 0.0 s |
| Longbow S | ok 63 m / 0.0 s | ok 76 m / 0.0 s | ok 1202 m / 0.0 s | ok 21 m / 0.0 s | ok 831 m / 0.0 s | ok 225 m / 0.0 s |
| Longbow L | ok 90 m / 0.0 s | **miss** 99 m / 0.0 s | ok 1222 m / 0.0 s | ok 25 m / 0.0 s | **miss** 137 m / 0.0 s | ok 236 m / 0.0 s |
| Watcher | ok 64 m / 0.0 s | ok 75 m / 0.0 s | ok 1203 m / 0.0 s | ok 21 m / 0.0 s | ok 997 m / 0.0 s | ok 203 m / 0.0 s |

Misses: Longbow L (110 t large grid) settles 100-114 m out instead of the test's 96 m tolerance (stable, nowhere near the hull).
Harrier, hard weave: one drone spent 0.9 s inside the clearance, with the owner turning twice as hard as the drone can.
Lancer fight: 0.8 s inside the clearance during the fight.
Gnat (4 drones, cruise): holds 100-109 m (its formation sphere plus pad) where v1.10 trailed 230-300 m.

Not covered by the sim: the in-game sensor's real behaviour with the owner's character (relationship, seated characters are
probably not detected), real gravity (the sim is space only), and physical collision with the owner (the owner is a virtual point).

Reproduce: `python3 -I tools/build_all.py --only Hornet --no-zip && sim/build.sh && (cd out/sim && mono SimHost.exe follow-test
--model Hornet --count 3 --path cruise)`; `--cmd guard` for the comparison, `--weave-deg 60 --weave-period 20 --stress` for the stress case.
