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

## SATELLITE formation (v2.7)
Command `satellite` (also `sat`, `orbit`), `Start Mode=SATELLITE` (Gnat) or `[Follow] Formation (FOLLOW/SATELLITE)=SATELLITE`. `follow` / `escort` switch
back. All nine combat drone scripts (Hornet, Wasp, Lancer, Harrier, Aegis, Mantis, Longbow S/L, Basilisk) get it from the FOLLOW block (v1.1); the Gnat has its own
copy round its home point / carrier link (it has no on-foot owner tracking).

- **Shape**: the drones spread evenly round a **vertical circle centred on the owner**; the whole ring turns slowly (`Satellite Orbit Period (s)`, default 180 s,
  about 2 deg/s, 3.5 m/s at 100 m). `Satellite Ring (ACROSS/ALONG)`: ACROSS (default) is square to the owner's heading, a halo round the ship that covers every
  side; ALONG is side-on. The plane turns with the owner's (lagged) heading; with a parked owner every drone uses the same world axes, so they share one plane.
- **Distance**: `Satellite Distance (m)` (default 100, at least the owner's hull radius + 45 m). It grows with the number of ships so neighbours stay a wingmate
  separation apart (`max(SepRange x 1.1, 40) / (2 sin(pi/n))`; Hornets: 6 ships 100 m, 12 ships 149 m), capped at 600 m, and up to 2x while the owner manoeuvres hard.
  On foot: 20-40 m so the sensor keeps seeing you. 100 m keeps the ring inside the cameras' and turrets' mutual cover while leaving room for a 20-30 m hull.
- **Phase**: there is no shared clock in the game, so each drone averages the ring phase implied by the mates that sit on the ring (circular mean, 2 %/tick).
- **Threat**: unchanged FOLLOW combat. Targets within `Engage Radius` (800 m) of the owner are engaged by the whole hive, the drones leave their slots and fight under
  the `Leash`; when nothing is left they fly back to their slots, which have kept turning.
- **Gravity**: only the upper arc is used and the drones sway by a fifth of the slot spacing instead of orbiting.
- **Cost**: about 2.2 k characters of script. After that the Basilisk is 99,733 of 100,000 (Wasp 97.4 k), so the next feature needs code moved out. To pay for it
  `FollowLoad` was rewritten with two helpers and the unused `RktDist` was removed from the Wasp and Basilisk.

Verification (sim `follow-test --cmd satellite`, owner parked at the origin = `--path park`, 200 s, ring metrics after a 25 s settle):

| case | ring radius | plane deviation | orbit rate (expected 2.00) | result |
|---|---|---|---|---|
| Hornet x6 | 100 +/- 0.4 m | 1 m | 1.99 deg/s | ok |
| Hornet x12 | 149 +/- 0.4 m | 1 m | 1.99 deg/s | ok (closest mate 76 m) |
| Basilisk x4 | 125 +/- 1.9 m | 3 m | 1.99 deg/s | ok |
| Longbow L x4 | 112 +/- 2.6 m | 3 m | 1.85 deg/s | ok |
| Longbow S x4, Wasp x4 | 100 m | 1 m | 1.99 deg/s | ok |
| Gnat x6 | 101 m | 1 m | 1.98 deg/s | ok |
| Hornet x4 cruise (40 m/s, 90 deg turn, stop) | 100 m | ring follows the heading | | ok, nobody inside the hull |
| Hornet x3 on foot | 20 m | | | ok |
| Hornet x4 fight (enemy Hornet appears at 600 m at 30 s) | drones go out to 450-640 m, then re-form on the ring by 100 s | | | ok (the enemy survived in the plain FOLLOW run too) |

Not covered: the real game's handling of a moving, rotating slot (the drones track it within 1 m in the sim), mixed drone types sharing a ring (each type ranks and
phases its own ring; different types can cross), and the ring in a real gravity well.
Reproduce: `python3 -I tools/build_all.py --only Hornet --no-zip && bash sim/build.sh && (cd out/sim && mono SimHost.exe follow-test --model Hornet --count 6 --path park --cmd satellite --seconds 200)`.
