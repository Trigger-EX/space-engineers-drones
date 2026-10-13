# sim_methodology: the simulator as a scientific instrument

Auditor key `sim_methodology`. Read-only audit of scenario generation, determinism, scoring, enemy models and the validity of the headline
claims. Physics constants, weapon numbers and API details belong to the other auditors. Date 2026-10-13.

## 0. What was run (CLAUDE.md seed rule)

`out/Blueprints` does not exist in the checkout (out/ is ignored), so I unpacked `releases/Drones_v2.6_Full.zip` and
`Printers_v2.6_Full.zip` into a scratch repo root and copied `out/sim` there (`--repo <scratch>`); nothing under the repo's `out/` was touched.
All runs used the current `out/sim` build (settings hash `c458b657c6493767`).

| run | scale | why |
|---|---|---|
| `run --filter '^(1v1/Hornet-vs-Wasp\|pirate/Hornet-vs-Pirate)$' --seeds 5 --jobs 1` and again `--jobs 4` | 2 x 10 runs | determinism across thread counts: all 10 result lines identical (timing fields excluded) |
| the `verify` experiment of `pc/plans/default.plan` (4 scenarios x 20 seeds) | 80 runs | reproduce `pc/reference/verify-linux.runs.jsonl`: 80/80 identical (outcome, duration, shots), same settings hash |
| mirror fights Hornet, Aegis, Lancer, Harrier, Mantis, Longbow S, 40 seeds each; then Longbow S mirror +100 seeds (`--seed-start 40`) | 340 runs | team-side bias check (a mirror must split 50:50) |
| `selftest all` | 336 checks | do the self-tests pass; what do they test |

## 1. Coverage: checked and found correct

| item | result |
|---|---|
| Determinism per seed | Same seed gives identical results with 1 and 4 worker threads and equals the committed Linux reference (80/80). Per-PB `DetRandom` streams are saved and restored around every Main (`ScriptHost.cs:118-163`), engine RNG is SplitMix/xoshiro forked per subsystem, no `DateTime`, `Guid`, `GetHashCode` or unordered iteration in sim code (grep). `Dictionary`/`HashSet` are only used for membership (`Metrics._hitShots`) or in stable insertion order. Stopwatch values never reach scripts (`LastRunTimeMs` is the constant 0.05). |
| Seed independence | `ScenarioSeed = Mix(Mix(base, FNV(name)), index+1)` (`Scenario.cs:80-85`). Different scenarios and indexes get unrelated streams; every ship's placement/orientation comes from one stream, so side order carries no systematic information. |
| Result ordering and crash handling | `ParallelRunner` writes results in job order; a thrown engine exception becomes an `error` line (never dropped), counted in `errors` of summary.csv and `sim_report.py`. `SimHost run` exits 5 if any run errored. |
| Projectile time-step / tunnelling | Not an issue. Bullets and rockets are tested as swept segments (`Projectiles.cs:150-250`), block-exact DDA in the target's frame, target pose interpolated start..end of tick (`ProjectileSweepBodies`). An 800 m/s bullet moves 13.3 m per tick but the whole segment is tested. `SelfTestC` has "swept hit test hits exact-lead shots" and "without the sweep the tick quantisation costs hits". |
| Termination rule | `Termination` is a small pure state machine with unit tests (hold timer reset, mutual-kill, no-contest, 3 teams). 5 s hold, 6 Hz sampling. A never-armed Watcher does not make the other side win in 5 s (`TerminationFlags`). |
| Settings hash | FNV-1a of all public `SimSettings` fields (`Describe()`), stored per run and in meta.json; `sim_report.py` prints every distinct hash found. |
| Metrics arithmetic | `HitCountUnique` counts projectiles, so hit_rate <= 1; friendly-fire dedupe; summary.csv aggregates; `Pct` interpolation. Checked against `SelfTestD.TestMetricsEvents/TestJson`. |
| Documented counts | sim-final: 47,072 / 47,300 = 99.5 %; outcome sum 162+100+12,765+34,045 = 47,072; pirate group 3,372 = 3,600 - 228. Wasp-vs-Pirate has 72 runs and 2xWasp/3xWasp are absent. RESULTS "1v1 (4,550)" = 91 x 50. |
| lead-v1 "300 seeds" | Statement is true for that table (it quotes 300 per scenario); not re-run (Basilisk fights are slow). |

## 2. Findings

### sim_methodology-01 (high): the default sim assumes the AI blocks work, which the user's in-game report contradicts

`SimSettings.cs:131,138` `ScriptAiBlocksInert=false`, `AiBehaviorNeedsSwitch=false`. The changelog (v2.1) records that in the real game the pasted AI
blocks have "AI Behavior off and do nothing"; the scripts try to flip a terminal bool whose id is unverified (`unknowns.md`). In the sim the
flying and aiming of every Full drone fight is done by the hand-written `VanillaAiBrain` (sim-validation 0: "flown mostly by the AI-block model";
sim-audit-1: 85-89 % of ship-runs never get a script target; `ai_on_s` 200 s of a 120-s fight). Measured effect of the real-game case:
drone-side wins vs pirate 211 (switch needed, found) / 44 (inert) of 720 vs 23 baseline (script-fix-notes.md 1307). Yet RESULTS.md, sim-final and
the pc plan (`robust-*` rows: torque, pitch sign, search 3500, speed 100) never vary `--script-ai-blocks-inert` or `--ai-behavior-needs-switch`,
so no published matrix describes the situation the user actually has in game. The stand-in's own numbers are [I]: 10 m/s speed limit, 300-600 m stay
band, fire cone 5 deg, perfect first-order lead, Kp/turn caps (`SimSettings.cs:140-150`).

Fix: add `robust-inert` and `robust-needswitch` to `pc/plans/default.plan`, make the pessimistic case the headline until T-tests settle the switch id, and state in
RESULTS.md that the ranking is conditional on AI blocks flying.

### sim_methodology-02 (high): the Pirate and the enemy ships are omniscient, predictable stand-ins; "beats the pirate" and hit-rate claims do not transfer

`PirateAi.cs`: `PirateBrain` picks `NearestEnemy(w, b, pos, 1e9)` every 30 ticks with no range, line of sight, sensor or stealth, cruises at 40 m/s,
then circles at 700 m (inside its own 800 m turret reach) at constant 25 m/s; evasion is off (`PirateEvasive=false`, and only 2.2 m/s^2 thrust if
turned on). All of these are `[I]` (`SimSettings.cs:114`). `TurretAi.cs`: turrets target the nearest enemy bound sphere, first-order lead with
the exact target velocity, 1.0 deg fire cone, slew from game data. Nothing says the game's NPC pirates fly like this (recollection, medium
confidence: vanilla encounter drones fly an autopilot approach and shoot with fixed guns / turrets, they do not hold a 700 m orbit). Consequences:
(a) docs/lead-v1.md hit rates (85-90 % rocket hits) are against a large, slow, constant-velocity orbiter and the doc itself says "the sim's Pirate barely
evades"; they do not support "LEAD v1 fixes misses against an evading AI ship" beyond the separate bench table; (b) pirate scenarios never time out
because the target is never found by the Pirate, only by the drone, so the pirate and drone-vs-drone groups have different failure modes
(sim-final pirate: timeouts concentrate on Gnat/Stinger/Longbow S at 29-90 %); (c) Tech Hunter study flies a 13,666-block warship with the same brain.

Fix: keep the Pirate, but label results "vs sim Pirate (omniscient, orbit 700 m)"; add an approach-and-ram / straight-pass variant and a weaker-perception
variant (`PirateSenseRange`), and run lead-v1 style A/B with `--pirate-evasive true` by default.

### sim_methodology-03 (medium): "timeout = 0.5" mixes "never met" with "stalemate"

`sim_report.py:14-15,161-165`. In the v2.0 baseline 56 % of timeouts had a closest approach > 2,500 m with no shot (sim-audit-1 section 1) because
`Scenario.StartDistanceMin/Max = 1500/3000` (`Scenario.cs:34`) and `VanillaSearchRange = 2500`. That scenario geometry is unchanged, so about 1/3 of
default AI-driven runs still cannot start. sim-final still has 12,765 timeouts (27 %); RESULTS.md reports "timeouts 40 -> 23 %" without the split. The model
"score" (win 1, draw/timeout 0.5) also gives each model free 0.5s against the three models that cannot win a 1v1 (Gnat, Stinger, Watcher: 0 % wins in every
row of the sim-final matrix), so "score" ranks "who beats the passive ones and does not time out" more than fighting strength; median ttk is conditioned on a win
(survivorship: fewer timeouts raise median ttk). Evidence is in the file already: `min_enemy_dist_m`, `shots`.

Fix: report win, loss, engaged-timeout (min dist <= search range or shots > 0) and never-met separately; cap `StartDistanceMax` at 2,400 for AI-only matrices or
add the 1.5-2.4 km bucket; exclude never-met runs from "score".

### sim_methodology-04 (medium): no uncertainty estimates, and `compare` ranks by |delta| (winner's curse)

`tools/sim_report.py` has no CI, no paired test. `compare()` (line ~296) sorts all scenarios by the largest change in (A wins - B wins) and shows the top 60:
with 100 runs per cell a win rate has SE of about 4-5 points (difference of two cells about 7), so the top of that list is mostly noise.
RESULTS.md ranks models to 1 point ("Aegis 70, Longbow S 69") from 1,400 sides each with per-run SD of about 0.4 (SE about 1.1 points, opponents shared so the
differences are not independent). lead-v1 selects between 42.5 / 45.4 / 42.9 mean score and 10.0 / 18.3 / 12.7 % wins at n = 300 per scenario (SE about
2 points), i.e. the shipped setting is not distinguishable from either neighbour; the text relies on a pooled hit rate dominated by timeouts. Changelog/RESULTS statements such
as "Aegis 58 -> 70" are far outside noise; the 2-5 point statements are not.

Fix: add Wilson intervals to `scenario_row`, a paired bootstrap (same seed index) to `compare`, and mark rows whose |delta| < 2 SE.

### sim_methodology-05 (medium): the scripts were tuned on the same seeds they are reported on

RESULTS.md "Same-seed comparison, round-2 baseline -> final" and sim-final use seeds 0..99; script-fix-notes/changelog tuning rounds used the same seed range
and the same engine; the pc plan's `full-fresh` (seeds 100-299) is designed as the hold-out, but no pc results were ingested (`docs/pc/` does not exist) and
`robust-*` re-use seeds 0-49. Positions are paired but fights diverge chaotically, so pairing is of little help while selection on the same 100 initial
conditions still overfits. More important, tuning to the sim optimises against its AI stand-in (finding 01). Treat v2.x gains as in-sample and sim-conditional.

Fix: ingest `full-fresh`; for new tuning, tune on seeds 0-99, report seeds 1000+.

### sim_methodology-06 (medium): the win condition is harder for scripted drones than for brain ships

`Metrics.cs:381` `BodyCapable(..., needControl = !sh.HasBrain)`. A scripted drone is beaten when its PB, its remote control, power, all thrusters or all
weapons are gone. A Midge (vanilla AI), the Pirate and `--enemies` ships (`HasBrain=true`, `Scenario.cs:229,236,240`) are not required to keep a PB, a
controller or even their AI blocks: a Midge whose AI Offensive/Flight blocks are shot away is still "capable" and counts in the survivor flags. One
light block (the PB) therefore ends a scripted drone while a Midge or Pirate must lose weapons/thrust/power. This is defensible for the drones (no PB, no
flight) but it tilts every script-vs-Midge, script-vs-Pirate and script-vs-warship fight against the script side and was never mentioned in the docs.
Effect not measured; a one-line experiment is to give `HasBrain` ships the same "brain block alive" requirement.

### sim_methodology-07 (medium): provenance: what a result line is tied to

- `SimSettings.Hash()` covers only `SimSettings` fields. Scenario overrides (`--start-distance`, `--face`, `--commands`, `--custom-data`, `--enemies`,
  `--enemy-brain`, `--enemy-fleets`, `--unfair`) are not hashed or stored per run; the blueprint/script text, `defs_stats.json` and the sim code revision are not
  stored either. Two result files with the same hash can be different experiments, and the "lock" runs of sim-validation (500-700 m, facing) are identical to
  default runs in every field except the command line in meta.json.
- `--resume` appends under a possibly different configuration without comparing the hash, and `WriteMeta` rewrites meta.json on every start
  (`Program.cs:398`), so the stored command line/settings describe only the last session. `skip` contains the keys of `error` lines, so errored runs are never retried
  (`Program.cs:392-396`).
- Published numbers (RESULTS.md, sim-final: hash 86da6f4425ff003e) cannot be regenerated by the current code (hash `c458b657c6493767`, 80/80 reproduce the committed
  reference, which carries the new hash). The run directories are gone (`out/sim/results` does not exist).

Fix: store `git rev-parse HEAD`, `sha1(scripts+blueprints)`, the override list and an "experiment id" in every run line; refuse `--resume` on mismatch; retry errors.

### sim_methodology-08 (medium): RESULTS.md is partial and stale relative to the v2.6 package

`docs/RESULTS.md` headline is v2.0 plus two tuning rounds (47,072 of 47,300 runs, "stopped by a tool time limit"). Because jobs are scenario-major and flushed in order, the
missing 228 runs are all at the end of the pirate group (Wasp x1 72/100, Wasp x2, x3 absent), so the pirate rows ("74.7 -> 75.9 % decisive") are an unbalanced sample
and omit the model with the lowest pirate win rate. Scripts changed afterwards in v2.3 (LEAD), v2.4 (KAMIKAZE), v2.5 (LAUNCH), v2.6 (BREACH), with only partial
regression runs (1v1 "regression in the doc"). Table of model scores in RESULTS.md is therefore not the score of the shipped v2.6 scripts. In my 20-seed spot check the Aegis-vs-Hornet
cell (9:8:3 win/win/timeout) is compatible with sim-final (63 % / 26 %), so there is no sign of a large change, but nobody has re-measured.

Fix: rerun the 1v1 matrix and pirate group with v2.6 scripts (`full-fresh`), regenerate RESULTS.md from `sim_report.py`.

### sim_methodology-09 (medium): "0 script exceptions" does not show the drones work in game

Defaults: `InfiniteAmmo=true` (so `BodyCapable` ignores ammo), `CountInstructions=false` and `EnforceInstructionLimit=false` (so a script that would die with "too complex"
runs forever), `FakeLastRunTimeMs=0.05`, no sim-speed drop with many PBs, `PowerEnabled` but no fuel for hydrogen. sim-validation, RESULTS and the pc plan report
"0 exceptions / 0 errors" as if it were a viability check. The instruction counter exists (`il_profiler` audit: model "blocks" gives <= 6.2 % of 50,000) but is not part of any plan.
unknowns.md states no ammunition is pre-loaded: in a world without infinite ammo every drone starts unarmed, a case that no matrix covers.

Fix: add one plan line with `--count-instructions true --enforce-instruction-limit true --infinite-ammo false` on a few scenarios.

### sim_methodology-10 (low): possible side bias and ordering effects

Team 0 is always the first-named side, its PBs run first each tick (`ScriptHost.RunDue`, attach order = ship index), and its ships are spawned first (separation test). Mirror
checks: 340 runs over seven mirrors, A:B wins = 119:137 (p about 0.3); individually Aegis 15:22, Harrier 19:13, Hornet 15:16, Lancer 19:17, Mantis 12:14 are fine, but Longbow
S-vs-Longbow S gives 34:55 over 140 seeds (about 2.2 sigma, p about 0.03 unadjusted, one of seven tests). Probably chance, but cheap to settle with 300 more seeds. Since the matrix has
only one order per pair (i <= j), any real side bias would be confounded with the model alphabet.

### sim_methodology-11 (low): shared script statics and whole-CustomData override

(a) `LongbowSniperAI.cs:127,1367`, `MantisAI.cs:136` write the `static double AccFastW/AccFastK` from per-PB Custom Data (`AccFastW = Cfg(...)`). In the sim all PBs of a script
share one loaded assembly (`ScriptRegistry`), so statics are shared by every ship and every parallel run; in the game each PB has its own assembly. Harmless today (all
use the default), but a `--custom-data` sweep of those keys, or Longbow vs Longbow with different data, races and leaks between runs.
(b) `--custom-data` replaces the PB Custom Data entirely and for every scripted ship of both teams (`Program.cs:248-259`, `Scenario.cs` step 3): the "script keeps its defaults for
keys not given" only holds if the script writes nothing else it needs; in a Hornet mirror the sweep also modifies the opponent, which the plan avoids only by its filter.

### sim_methodology-12 (low): `new Random()` is independent per PB in the sim

`DetRandom` hands every PB a distinct stream. On .NET Framework (the runtime the PB sandbox used when last verified) `new Random()` is seeded from `Environment.TickCount`
(15 ms resolution), so PBs compiled in the same frame (a pasted fleet, a world load) get identical sequences. Scripts use `_rng`, `_kzRng`, `_evRng` for patrol corners,
evasion and kamikaze rays (Hornet `:181,974,2220`). Recollection, medium confidence on the runtime (SE has been moving to newer .NET; unknown for the current build).
If it applies, fleets that appear to move in lockstep (the user's "all flew off the same way") are a real effect the sim cannot show. Fix: add a `--random-sync true`
mode that seeds all PBs of one world identically and re-run `follow`/`hunt` fan-out.

### sim_methodology-13 (low): self-tests prove consistency, not fidelity, and some are silent

336 checks pass (`selftest all`). They compare the code with analytic values the same authors derived (rocket drift 29.8 m, gatling spread law, damage*(1-d/r), 500 ms railgun delay), and with
game data (`TurretMaxRange 800`, rates). That catches regressions, not wrong assumptions. Weak spots: the pirate/vanilla brains are checked only for "closes", "fires",
"stays at 150-800 m"; nothing tests the brain-vs-script capability asymmetry (06), the scoring rules or `FleetSearch.Score`. `RunScenarioTests` prints "skip: out/Blueprints missing"
and returns 0 (counted as pass), while `SelfTestA` crashes with an unhandled `FileNotFoundException` for the same missing folder; both need the ignored `out/Blueprints` and
`out/dist/MODELS.md`. Determinism is tested only sequentially on one thread (not 1 vs N threads or across runtimes).

### sim_methodology-14 (low): swallowed errors and small bookkeeping defects

`Metrics.Finish` wraps the final `Sample` in `catch (Exception) { }` (a crash there leaves stale capability). `FleetSearch` drops error runs after counting them (`_errors++`).
`JsonOut.WriteSummaryCsv` folds teams >= 1 into column B and averages `avg_duration_s` over error runs (duration 0). `Program.LoadRuns` skips unreadable lines with a stderr note
(a corrupt line silently shrinks n). `sim_report.py` win/loss percentages divide by n including errors. None currently matters (0 errors in all published runs).

### sim_methodology-15 (low): search and small-n studies

`FleetSearch` ranks by `mean - max(sd,5)/sqrt(n)` among thousands of candidates with an adaptive (UCB, mutation) allocation: with that many comparisons a one-standard-error penalty still
selects lucky fleets. The Tech Hunter study uses 10 seeds per fleet (820 + 180 + 200 + 80 runs) and then states "twice as many power blocks" (0.65 -> 1.2 per drone; 0.8 -> 4.2 with 10 drones): at
n = 10 on a zero-heavy count, those are directional only. The enemy there is also a 13,666-block hull flown by the Pirate brain without its rotor pods or scripts, fuel never running out.

### sim_methodology-16 (info): residual fidelity notes on the time step

- Body-to-body contact is discrete (`Physics.ResolveCollisions`, probe block centres inside host cells, no sweep). At 100-200 m/s closing (1.7-3.3 m/tick) a small-grid hull (0.5 m cells) can be
  skipped. Stinger kamikazes are detonated by the script on distance, so the kamikaze results are not affected; ramming damage between ships is.
- Rockets are not entities: bullets cannot shoot them down (the Aegis point-defence behaviour has nothing to shoot at in the sim).
- Shooter body immunity is 0.2 s (`ProjectileShooterIgnoreS`), i.e. 160 m of an 800 m/s bullet's flight.
- `OnTarget` time-on-target uses the target's bounding sphere, so it overstates time on target for slender hulls.

## 3. Places where a documented conclusion exceeds the sim's fidelity

1. RESULTS.md model ranking and "decisive wins 58 -> 74.6 %": conditional on AI blocks working (01), on starting geometry that makes 1/3 of runs a no-contact timeout (03), on in-sample seeds (05) and on v2.0-era scripts (08).
2. "Pirate ship" rows, LEAD v1 hit rates, Basilisk-vs-Pirate A/B (lead-v1 table): against an omniscient, constant-velocity, 320 t stand-in (02).
3. Tech Hunter / BREACH / SEEKER conclusions: 10 seeds per fleet, stand-in flight, no game verification of `SetTargetingGroup` behaviour (also acknowledged by unknowns.md) (02, 15).
4. Anything about script robustness ("0 exceptions", "fits the limit"): ammo, instruction limit, sim speed not modelled by default (09).
5. "Scripts alone never find a small ship": true in the sim's camera model; the same docs say it is the main driver of all results, so everything not flown by the stand-in depends on camera cone/charge numbers that are `unverified` in RESULTS.md caveats.
