# LEAD v1.0: manoeuvre-aware lead for every armed drone (scripts v2.5)

**Why:** In-game report (2026-10-13): the Basilisk's rockets kept missing an enormous target that was doing basic evasive
manoeuvres. A rocket needs about 4 s to fly 800 m, and an AI ship on "evasive" flips its strafe every 2 to 5 s. The v2.4 solver
got three things wrong for that case:

1. **It extrapolated the acceleration over the whole flight.** The smoothed acceleration (0.8 old / 0.2 new per sighting, faded
   by a steadiness gate) lagged a strafing ship by about a second. It was then extrapolated as `0.5 a T^2` for all 4 s, by which
   time the ship had usually reversed.
2. **It aimed at hull points on the edge of the target.** ENGINES picks the rearmost quarter of the known hull points and SPREAD
   walks through all of them. With a 10 to 20 m expected miss, a shot aimed at the edge loses half its spread into empty space. Each
   hull point also carried its full depth, so a point 60 m down the hull of a turning ship swung sideways during the flight.
3. **It assumed rockets ignore the ship's velocity.** Keen's bug tracker ("Rocket doesn't move in a straight line", answered "by
   design: the rocket's maximum speed does not stack with the ship's velocity") says a rocket fired from a moving ship **curves**.
   That only happens if the rocket leaves with ship velocity + launch speed, accelerates along the barrel and has its total speed
   capped. The Basilisk flies alongside its target, so it often moves at 20 to 60 m/s itself.

## What changed

A shared block, `tools/lead_block.cs` ("LEAD v1.0"), is kept identical in every armed script by `python3 -I tools/lead_sync.py --apply`.
The hooks are three Track lines and each script's solver.

- **Fresh acceleration:** 0.8 of the newest velocity-difference sample, x0.9 (the Longbow's v2.2 filter, now used by everyone).
  The old smoothed acceleration and steadiness gate still drive the "target jinking" fire gates.
- **Manoeuvre horizon:** the track splits the target's motion into stretches of steady acceleration. A new one starts when the
  acceleration turns more than about 35 degrees away from the current one's mean. `LManT` is the running mean length. The solver
  holds the present acceleration for the expected rest of the manoeuvre, `max(ManT - age, 0.3 ManT, 2 (age - ManT))`, then flies
  the target on at constant velocity. The last term handles burns that outlast the usual length (an orbit, a chase, a long
  acceleration): without it, steady burns were under-led.
- **Aim point:**
  - The hull point counts only across the line of fire. Its depth along the line doesn't change what the shot hits.
  - It is turned with the target's measured spin (from successive orientations) for at most 1 s. A longer horizon lost hits when the
    target changed its turn rate.
  - It must leave **Aim Margin** x the predicted miss (`0.5 x |acceleration| x T^2`) of known hull around it. "Known hull" is the
    extent of all hull points along 4 directions across the line of fire. If no point qualifies, the most central one is used.
    Short, sure shots still pick engines. Long, uncertain ones go to the middle of the known hull.
- **Rockets (Basilisk, Wasp):** new `Rocket Velocity Model (CAPPED/NONE/FULL)`, default CAPPED. The closed-form flight path
  (`RktPath`) is within 1 m of a 60 Hz step-by-step flight. A Newton solve on the flight time replaces the fixed-point iteration.
  NONE is the v2.4 model. FULL (Galilean inheritance) was the worst choice in every bench case. The old `Projectiles Inherit Ship
  Velocity` key is no longer read by the rocket scripts.
- New Custom Data keys: `Aim Margin (predicted misses)` (default 1) in every armed script, and `Rocket Velocity Model` in Basilisk
  and Wasp. The PB screen's Lead line now shows the fresh acceleration and how long it is held (`acc 2.0/2.3 for 4.1 s`).

**In-game check for the rocket model:** fly sideways at 50 m/s or more and fire one rocket straight ahead.
- It **bends** onto the barrel line: CAPPED is right (the default).
- It flies **straight** along the barrel line: set `Rocket Velocity Model=NONE` in the Basilisk's and Wasp's Custom Data.
- It flies straight but **drifts sideways for its whole flight**: set FULL.

## Bench (tools/lead_bench/Bench.cs)

One shooter fires at one manoeuvring target. Sightings come every 0.2 s with exact position, velocity, orientation and one hull point
per ray. Every solver variant keeps its own track and fires at the same moments. Hit % per variant:

- Targets: huge = 30x24x120 m box; cat = twin hull with a gap; large = Pirate-sized 18x10x30 m; small = 3x2.5x6 m; tube = hollow
  small target.
- Manoeuvres:
  - strafe: 30 m/s across the line of sight, flipping every 2-5 s (rockets) or 0.5-2 s (guns).
  - strafe3d: as strafe, in random directions.
  - strafeturn: strafe while turning.
  - random: velocity changes every 1-4 s.
  - orbit: circles the shooter.
  - cruise: constant velocity.
  - burn: 8-15 s steady burns.
- Shooters:
  - Rockets: Basilisk-like at 750 m.
  - Gatling: Hornet-like at 350 m.
  - Railgun: Longbow-like at 1,000 m.
- Large target acceleration is 5 m/s²; small targets 15 m/s².

### Rockets: v2.4 vs v2.5, under both rocket physics

| target | manoeuvre | rockets don't inherit: v2.4 | v2.5 | rockets curve (CAPPED): v2.4 | v2.5 |
|---|---|---|---|---|---|
| huge | strafe | 48 | 65 | 43 | 66 |
| huge | random | 47 | 69 | 44 | 70 |
| huge | strafeturn | 44 | 65 | 40 | 65 |
| huge | orbit | 75 | 56 | 74 | 94 |
| huge | cruise | 93 | 84 | 86 | 93 |
| huge | burn | 83 | 67 | 48 | 91 |
| cat | strafe | 34 | 57 | 31 | 59 |
| cat | random | 38 | 55 | 37 | 53 |
| cat | strafeturn | 34 | 47 | 30 | 49 |
| cat | orbit | 62 | 52 | 71 | 90 |
| cat | cruise | 92 | 78 | 80 | 92 |
| cat | burn | 77 | 56 | 39 | 84 |
| large | strafe | 14 | 38 | 14 | 39 |
| large | random | 19 | 35 | 14 | 32 |
| large | strafeturn | 18 | 36 | 15 | 39 |
| large | orbit | 84 | 7 | 35 | 89 |
| large | cruise | 88 | 68 | 70 | 89 |
| large | burn | 63 | 18 | 20 | 78 |

Against evasive targets v2.5 gains 15 to 25 points under either physics. The rocket model matters when the Basilisk itself flies
fast (orbit, long burns, cruise). There, the wrong model costs a lot either way, so the in-game check above is worth doing. With
`NONE` set, the v2.5 solver keeps all the evasive-target gains: the "F hl1, truth-none solver" row in the bench averages 61 % vs
v2.4's 50 %.

### Guns: Hornet family (gatling), Longbow (railgun) and v2.5

| weapon | target | manoeuvre | Hornet-family v2.4 | Longbow v2.2 | v2.5 |
|---|---|---|---|---|---|
| gatling | small | strafe | 59 | 68 | 80 |
| gatling | small | random | 59 | 71 | 88 |
| gatling | small | burn | 71 | 72 | 94 |
| gatling | small | orbit | 66 | 67 | 86 |
| gatling | tube | strafe | 57 | 64 | 78 |
| gatling | tube | random | 58 | 71 | 88 |
| gatling | tube | burn | 70 | 69 | 92 |
| gatling | tube | orbit | 68 | 70 | 86 |
| gatling | large | strafe | 88 | 92 | 97 |
| gatling | large | random | 87 | 92 | 98 |
| gatling | large | burn | 87 | 88 | 99 |
| gatling | large | orbit | 89 | 88 | 95 |
| rail | small | strafe | 23 | 61 | 69 |
| rail | small | random | 34 | 64 | 72 |
| rail | small | orbit | 75 | 80 | 97 |
| rail | small | burn | 66 | 63 | 93 |
| rail | large | strafe | 81 | 91 | 97 |
| rail | large | random | 80 | 89 | 98 |
| rail | large | orbit | 88 | 90 | 98 |
| rail | large | burn | 91 | 89 | 99 |

### Variants that lost

- **Segment-mean acceleration** (the mean over the current manoeuvre): 2 to 5 points behind the fresh filter.
- **Singer model** (acceleration decaying with the manoeuvre time): worse than "hold, then coast" on 1D strafes.
- **Unlimited spin extrapolation:** lost up to 7 points when the target changes its turn rate.
- **Switching to the bounding-box centre when the predicted miss is large:** close to v2.5 on solid boxes. It is not shipped
  because the sim's Pirate (a hollow shell whose turrets and thrusters sit outside) is unbeatable with centre aim
  (docs/script-fix-notes.md R4: CENTER 0-11 % wins).
- **FULL rocket inheritance:** 12 to 43 % average, far below both other models.

## Simulator

- `--rocket-inherit 0|1|2` (`SimSettings.RocketInherit`). The default is now 1 (CAPPED). Selftest: a sideways launch at 60 m/s
  drifts 29.8 m and ends on the barrel line.
- `--pirate-evasive true` (and `--pirate-evasive-speed`): the Pirate strafes up and down across the line of sight at 30 m/s and
  flips every 2-5 s.

### Sim results so far (Basilisk vs the Pirate, 300 seeds per scenario, same engine, old vs new blueprints)

| | v2.4 (before) | v2.5, Aim Margin 0 | v2.5, Aim Margin 1 (shipped) |
|---|---|---|---|
| per-run rocket hit rate, 1 Basilisk vs Pirate | 85.5 % | 90.5 % | 89.2 % |
| mean score (win 1, timeout 0.5) over 1x / 2x / 3x Basilisk | 42.5 | 45.4 | 42.9 |
| 1x / 2x / 3x Basilisk win % | 10.0 / 35.3 / 43.7 | 18.3 / 40.7 / 54.3 | 12.7 / 37.3 / 46.7 |
| evasive Pirate, mean score | 28.4 | 33.6 | 32.3 |
| evasive Pirate, 1x / 2x / 3x Basilisk win % | 1.0 / 11.0 / 33.7 | | 9.7 / 21.7 / 40.7 |
| evasive Pirate, pooled rocket hit rate | 73-78 % | | 80-84 % |

(Aim Margin 0.5 and 2 scored 32.7 and 32.6 against the evasive Pirate and 42.9 and 42.8 against the plain one.)

- The pooled hit rate looks lower with the margin (79 % vs 85 %), but a handful of 300 s timeouts with thousands of shots dominate
  it. Paired per run, the difference is 1.3 points with a median of 0.
- The sim's Pirate barely evades: it has 2.2 m/s² of thrust. The bench's 5 m/s² strafing ships are the closer match to the
  in-game report.
- The full A/B matrix (every 1v1 pair and pirate scenario, plain and evasive, 100 seeds) still has to be run and added here.
