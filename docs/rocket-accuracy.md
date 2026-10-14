# Rocket accuracy (Wasp, Basilisk)

Trigger: owner's in-game test of Wasp Split showed many rockets missing tiny targets (190-720 m). Diagnosis in the sim (per-rocket probe, 20 seeds, Gnat and Watcher targets; probe not committed):

| Miss cause | Gnat | Watcher |
|---|---|---|
| Aim from the MIDPOINT of the two launchers (each rocket leaves its own launcher ~1.4 m off the axis) | 57 % | 4 % |
| Target not where predicted at launch (evasive, spinning) | 9 % | 44 % |
| Rocket entered the hull box but passed through empty cells / near the edge | 9 % | 26 % |
| 0.1 deg launcher deviation | 18 % | 8 % |
| Target manoeuvred after launch | 5 % | 13 % |

## Changes (scripts/WaspFlightAI.cs, scripts/BasiliskAI.cs; WaspAI.cs is the old single-PB script, not used by any design, untouched)
- `NextGunPos()`: aim from the launcher that fires next. Wasp vs Gnat rocket hit rate 0.15 -> 0.21 (30 seeds); Watcher 0.14 -> 0.16; Hornet/Harrier unchanged (~0.06, they are fast and armed). Basilisk: same or faster kills (20 seeds).
- Config `Minimum Fire Window (deg)` (was a hard 0.3 deg = 3 m at 600 m), default 0.1. In the sim it is not the binding limit (0.05 gives identical results), so it only matters if the real gyros hold tighter than the sim assumes.
- Config `Max Sideways Speed To Fire (0 off)`, default 0 = off. No launch while our speed across the barrel is above the value. Insurance against a wrong rocket velocity model; costs kills when on (limit 10: Watcher wins 22/30, Hornet hit rate 0.00; 25: little effect). Use it only if the in-game test below shows rockets do not inherit our velocity as the script assumes.

## Tried and rejected (sim, 30 seeds)
Shorter fire range / flight time for the unknown-stock case (450 m / 2.5 s, 350, 250, 1.5 s): hit rate barely moves (0.17) while kills take 2-3x longer and Gnat fights are lost. Jink gate K, aim margin: no effect. Salvo 1 / interval 2.5 s save ~25 % rockets per kill (matters only with limited ammo), not accuracy.

## BIGGEST UNKNOWN: the rocket velocity model (test it in game)
Docs disagree on whether rockets inherit the ship velocity. The script assumes CAPPED (`Rocket Velocity Model`). If the game differs, the sim says hits fall to ~0 % while the Wasp moves ~24 m/s across the barrel (orbit + jink). Test: hover stationary, then fly sideways at about 50 m/s and fire one rocket along the nose at a fixed point 400 m away; note where it goes. If it flies straight along the barrel from the muzzle regardless of your velocity set `Rocket Velocity Model=NONE`; if it keeps your sideways velocity added fully set `FULL`; if it curves onto the barrel line leave `CAPPED`. Report the result.

## Not done
Hold-still-before-firing behaviour; aiming at the hull centre against spinning targets; the evasive-target prediction error (44 % of Watcher misses) needs a better target model, not a gate.
