# Results summary (v2.0 + two tuning rounds)

Engine: `sim/` (C#, runs the real scripts; settings hash 86da6f4425ff003e). Full reports: `sim-baseline.md` (old engine), `sim-after-r1.md`,
`sim-r2-base-*.md` (round-2 baseline on the fixed engine), `sim-final.md` (final, 47,072 of 47,300 runs; stopped by a tool time limit).

Same-seed comparison, round-2 baseline -> final:

| group | timeouts | decisive wins |
|---|---|---|
| 1v1 (4,550) | 40.1% -> 23.0% | 58.0% -> 74.6% |
| 2v3 | 44.2% -> 24.4% | 55.4% -> 75.3% |
| mixed teams | 45.5% -> 24.3% | 54.5% -> 75.6% |
| pirate ship | 25.0% -> 24.0% | 74.7% -> 75.9% |
| Gnat swarms | 81.0% -> 71.1% | 19.0% -> 28.9% |

1v1 score by model (win = 1, draw/timeout = 0.5): Aegis 58->70, Longbow S 65->69, Hornet 54->65, Basilisk 60->64, Lancer 65->63, Harrier 64->61,
Longbow L 56->57, Mantis 59->56, Midge 46->42, Wasp 40->38, Watcher 29->23, Gnat 27->22, Stinger 27->21.

Caveats (read before trusting any number):
- AI Offensive/Flight blocks are a hand-written stand-in (2.5 km search from game data; 10 m/s speed limit is the wiki default, unverified). Much of the
  default-mode result depends on it. Alternative assumptions (thruster torque, gyro pitch sign, search 3.5 km, speed 100) were tried: gains are flat to slightly worse there.
- Scripts alone (AI blocks off) never found a small ship by camera ray in the sim. Camera cone/charge, gyro ramp, thrust-through-COM, explosion falloff, collision damage
  scale are unverified (see docs/research.md section 10 tests T1-T14, docs/sim-validation.md).
- Gnat, Stinger and Watcher gained nothing from round 2 (their tuning agent was stopped by an automated safeguard); they depend on a spotter/AI-equipped partner.
- Missing deliverables: standalone Stinger/Tick blueprint module and timer/Event-Controller printers (agents stopped by automated safeguards).
- Nothing here was run in the real game.

## Kamikaze defence (v2.4, `docs/kamikaze-defence.md`)
Scenario kind `kamikaze` (4 / 8 cued Stingers against every scripted combat model, 31 scenarios x 20 seeds = 620 runs, same seeds, on top of
v2.3): defenders still combat capable at the end 57% -> 70%, damage taken 9,158 -> 5,373 per fight, defender hull lost 40% -> 27%. Against
4 Stingers, a lone Hornet, Lancer, Mantis or Longbow S survives 75-85% of fights (was 35-55%). Unchanged: the Gnat (it only sees kamikazes
at 45 m), the Wasp (rockets too slow for them), and fights without kamikazes (1v1 regression in the doc).

## Running more simulation on your own PC
`releases/SE_SimPack_<rev>.zip` is a self-contained pack (precompiled simulator, blueprints, data, run plan). Unzip, double-click `run.bat`,
then `pack_results.bat`, and send `SE_results.zip`; ingest with `python3 -I tools/sim_ingest.py SE_results.zip --name pc1`.
Rebuild the pack after any script change: `bash sim/build.sh && python3 -I tools/build_all.py && python3 -I tools/make_simpack.py`.
Details in `pc/README_PC.md`; the experiment list is `pc/plans/default.plan`.
