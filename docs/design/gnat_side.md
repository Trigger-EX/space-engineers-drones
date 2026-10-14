# Gnat side-thruster experiment (2026-10-14)

Owner's request: "experiment with a gnat variant that has one side thruster, then one that has a side thruster instead of a backward thruster".

## Variants (test ships, `tools/designs/testships.py`; built into out/Blueprints, never packaged)
| Model | Thrusters | Blocks | Mass | Accel (m/s2) |
|---|---|--:|--:|---|
| Gnat (stock) | main + reverse | 19 | 2,290 kg | F6.3 B6.3 |
| Gnat Side | main + reverse + 1 side | 20 | 2,411 kg | F6.0 B6.0 D6.0 |
| Gnat Side NoRev | main + 1 side (reverse -> 2 armour) | 21 | 2,330 kg | F6.2 D6.2 |

The side thruster lies across the tail (`gnat_frame(side=...)` in `tools/designs/core.py`: cells x 1, y 0..1, z 3, flame Up, pushes Down), both cells
inside the 2 x 2 section (outline rule); the rear camera moves one cell back (hull 8 long). Mounts and keep-clear zones validate.

## Script (GnatSwarmAI v1.15, "SIDE THRUSTER")
Thrusters across the nose axis that push the same way become `_sideThr`. The gyro now also rolls (`Aim(..., rollTo)`):
* RUN: the nose stays on the lead point; the ship rolls the side push onto the sideways need and burns the part that lines up; the nose tilts only for the rest.
* VectorFly: turns whichever push (main, reverse, side) needs the smallest turn onto the need (hysteresis 0.25 rad) and burns the projections onto the
  main and side axes. Without side thrusters the old code path runs unchanged (stock Gnat behaves as before).
Minified 79,864 characters (budget ~80k: the next Gnat feature should move something into the Staff PB, CLAUDE.md).

## Sim results (`SimHost.exe one --scenario "1v1/<A>-vs-<B>" --seed-index K`, seeds 0..N-1, 4 in parallel)
Close start `--start-distance 600:1000 --face` (every run is a fight, so search luck is out):

| Scenario | Seeds | Wins | Hit rate (per fight) | Damage out / in |
|---|--:|--:|---|---|
| Gnat vs Hornet | 90 | 8 | 14.7 +- 1.1 % | 1,949 / 2,660 |
| Gnat Side vs Hornet | 90 | 9 | 15.0 +- 1.1 % | 2,404 / 2,436 |
| Gnat Side NoRev vs Hornet | 90 | 14 | 16.5 +- 1.5 % | 2,197 / 2,391 |
| 3x Gnat vs Hornet / Wasp | 30 + 30 | 30 / 30 | 14.6 / 19.6 % | TTK 49 / 36 s |
| 3x Gnat Side vs Hornet / Wasp | 30 + 30 | 28 / 30 | 15.9 / 17.8 % | TTK 43 / 42 s |
| 3x Gnat Side NoRev vs Hornet / Wasp | 30 + 30 | 30 / 29 | 16.7 / 21.0 % | TTK 53 / 36 s |

Head to head (30 seeds, close start): Gnat Side vs Gnat 7-5 (18 timeouts), Gnat Side NoRev vs Gnat 10-7 (13), mirror Gnat vs Gnat 6-10 (14).
Default start (1.5-3 km, 3 vs Hornet, 30 seeds): Gnat 16 wins, Side 5, Side NoRev 16; the AI blocks fly the search (AI on ~200-260 of 300 s), so this
measures finding the enemy, and the gap was not reproduced at close range.

## Reading
* A side thruster gives at most a small gain in the sim: hit rate and damage taken move the right way, but inside the noise at 90 seeds.
* Swapping the reverse thruster for a side thruster costs nothing measurable and is the best variant on most rows (14 vs 8 wins in 1v1 is ~1.4 sigma).
  Braking now turns the ship 90 deg (side) instead of 180 deg (flip) or uses the main thruster after a flip.
* Not tested in the game. Unknowns there: how the AI Flight block (search phase) uses a side thruster; the sim applies thrust at the centre of mass,
  like the game.
* Next if wanted: 100+ seeds on 1v1 vs Hornet only for Gnat vs Gnat Side NoRev, or an in-game test of Gnat Side NoRev.
